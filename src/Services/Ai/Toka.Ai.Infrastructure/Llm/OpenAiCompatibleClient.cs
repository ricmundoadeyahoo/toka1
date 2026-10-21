using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;
using Polly.Timeout;
using Toka.Ai.Application;
using Toka.Ai.Application.Rag;
using Toka.Ai.Domain;

namespace Toka.Ai.Infrastructure.Llm;

/// <summary>Un endpoint con protocolo OpenAI (chat o embeddings).</summary>
public sealed class OpenAiEndpointOptions
{
    /// <summary>Ej.: https://api.deepseek.com · https://api.openai.com/v1 · http://ollama:11434/v1</summary>
    public string BaseUrl { get; set; } = string.Empty;
    /// <summary>API key (inyectar por variable de entorno; nunca en el repositorio).</summary>
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    /// <summary>
    /// JSON con parámetros extra específicos del proveedor que se agregan al cuerpo de la petición.
    /// Vacío = automático: si el host es DeepSeek se envía {"thinking":{"type":"disabled"}} (el modo thinking viene
    /// activo por defecto y eleva costo/latencia); para otros proveedores no se envía nada (OpenAI rechaza
    /// parámetros desconocidos). "none" = no enviar nada nunca.
    /// </summary>
    public string? ExtraBodyJson { get; set; }

    internal const string DeepSeekDisableThinking = """{"thinking":{"type":"disabled"}}""";

    /// <summary>Parámetros extra efectivos según la regla anterior.</summary>
    public string? EffectiveExtraBodyJson()
    {
        if (string.Equals(ExtraBodyJson?.Trim(), "none", StringComparison.OrdinalIgnoreCase)) return null;
        if (!string.IsNullOrWhiteSpace(ExtraBodyJson)) return ExtraBodyJson;
        return Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) && uri.Host.EndsWith("deepseek.com", StringComparison.OrdinalIgnoreCase)
            ? DeepSeekDisableThinking
            : null;
    }
}

public sealed class LlmOptions
{
    public const string SectionName = "Llm";

    /// <summary>Modelo de chat / function calling. Por defecto DeepSeek.</summary>
    public OpenAiEndpointOptions Chat { get; set; } = new()
    {
        BaseUrl = "https://api.deepseek.com",
        Model = "deepseek-flash"
    };

    /// <summary>Modelo de embeddings para el RAG. DeepSeek no ofrece embeddings: por defecto Ollama local (solo embeddings).</summary>
    public OpenAiEndpointOptions Embeddings { get; set; } = new()
    {
        BaseUrl = "http://ollama:11434/v1",
        ApiKey = "ollama",
        Model = "nomic-embed-text"
    };

    /// <summary>Máximo de llamadas simultáneas al proveedor (protege la cuota RPM/TPM).</summary>
    public int MaxConcurrency { get; set; } = 4;
    /// <summary>Tiempo máximo esperando turno antes de responder 429.</summary>
    public int QueueTimeoutSeconds { get; set; } = 30;
    public int AttemptTimeoutSeconds { get; set; } = 60;
}

/// <summary>
/// Transporte compartido: cola de concurrencia, autenticación, traducción de errores (429 → LlmRateLimitedException;
/// caída/timeout/circuit breaker → LlmUnavailableException). La resiliencia (retry con backoff y respeto de
/// Retry-After, circuit breaker, timeouts) la aporta el pipeline de Microsoft.Extensions.Http.Resilience.
/// </summary>
internal static class OpenAiHttp
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal static bool HasUsableKey(OpenAiEndpointOptions endpoint) =>
        !string.IsNullOrWhiteSpace(endpoint.ApiKey)
        && !endpoint.ApiKey.Contains("REEMPLAZA", StringComparison.OrdinalIgnoreCase)
        && !endpoint.ApiKey.Equals("changeme", StringComparison.OrdinalIgnoreCase);

    internal static void Configure(HttpClient http, OpenAiEndpointOptions endpoint)
    {
        if (HasUsableKey(endpoint))
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", endpoint.ApiKey);
    }

    internal static void MergeExtraBody(JsonObject body, string? extraBodyJson)
    {
        if (string.IsNullOrWhiteSpace(extraBodyJson)) return;
        if (JsonNode.Parse(extraBodyJson) is not JsonObject extra)
            throw new InvalidOperationException("ExtraBodyJson debe ser un objeto JSON.");
        foreach (var (key, value) in extra.ToList())
        {
            extra.Remove(key);
            body[key] = value;
        }
    }

    internal static async Task<JsonDocument> PostAsync(
        HttpClient http, SemaphoreSlim gate, OpenAiEndpointOptions endpoint, int queueTimeoutSeconds,
        string path, JsonObject body, ILogger logger, CancellationToken ct)
    {
        if (!HasUsableKey(endpoint))
            throw new LlmUnavailableException($"No hay API key configurada para {endpoint.BaseUrl} (revisa LLM_API_KEY en .env).");

        if (!await gate.WaitAsync(TimeSpan.FromSeconds(queueTimeoutSeconds), ct))
            throw new LlmRateLimitedException("Cola de inferencia saturada", TimeSpan.FromSeconds(5));
        try
        {
            using var response = await http.PostAsJsonAsync(path, body, JsonOptions, ct);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                throw new LlmRateLimitedException("El proveedor LLM respondió 429", response.Headers.RetryAfter?.Delta);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.PaymentRequired or HttpStatusCode.Forbidden)
            {
                logger.LogError("El proveedor LLM rechazó la credencial o el saldo ({Status}) en {Path}", (int)response.StatusCode, path);
                throw new LlmUnavailableException($"El proveedor LLM rechazó la petición ({(int)response.StatusCode}): revisa API key y saldo.");
            }
            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(ct);
                logger.LogError("LLM {Path} respondió {Status}: {Detail}", path, (int)response.StatusCode, detail.Length > 500 ? detail[..500] : detail);
                throw new LlmUnavailableException($"El proveedor LLM respondió {(int)response.StatusCode}");
            }
            var stream = await response.Content.ReadAsStreamAsync(ct);
            return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        }
        catch (Exception ex) when (ex is BrokenCircuitException or TimeoutRejectedException or HttpRequestException)
        {
            logger.LogError(ex, "LLM no disponible en {Path}", path);
            throw new LlmUnavailableException("No fue posible contactar al modelo de IA", ex);
        }
        finally
        {
            gate.Release();
        }
    }
}

/// <summary>
/// Chat Completions con function calling sobre cualquier proveedor compatible con OpenAI
/// (DeepSeek por defecto; OpenAI, Azure OpenAI v1 u Ollama cambiando configuración).
/// </summary>
public sealed class OpenAiChatClient : ILlmClient
{
    private static SemaphoreSlim? _gate;
    private static readonly object GateLock = new();

    private readonly HttpClient _http;
    private readonly LlmOptions _options;
    private readonly ILogger<OpenAiChatClient> _logger;

    public OpenAiChatClient(HttpClient http, IOptions<LlmOptions> options, ILogger<OpenAiChatClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
        lock (GateLock) _gate ??= new SemaphoreSlim(Math.Max(1, _options.MaxConcurrency));
        OpenAiHttp.Configure(_http, _options.Chat);
    }

    public string ChatModel => _options.Chat.Model;

    public async Task<LlmResponse> ChatAsync(LlmRequest request, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["model"] = _options.Chat.Model,
            ["messages"] = BuildMessages(request.Messages),
            ["temperature"] = request.Temperature,
            ["max_tokens"] = request.MaxTokens,
            ["stream"] = false
        };
        if (request.Tools is { Count: > 0 })
            body["tools"] = BuildTools(request.Tools);
        OpenAiHttp.MergeExtraBody(body, _options.Chat.EffectiveExtraBodyJson());

        using var doc = await OpenAiHttp.PostAsync(_http, _gate!, _options.Chat, _options.QueueTimeoutSeconds, "chat/completions", body, _logger, ct);
        return Parse(doc.RootElement, request, _options.Chat.Model);
    }

    /// <summary>Interpreta la respuesta (expuesto para pruebas unitarias).</summary>
    internal static LlmResponse Parse(JsonElement root, LlmRequest request, string defaultModel)
    {
        var choice = root.GetProperty("choices")[0];
        var message = choice.GetProperty("message");

        var content = message.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
        var toolCalls = new List<LlmToolCall>();
        if (message.TryGetProperty("tool_calls", out var calls) && calls.ValueKind == JsonValueKind.Array)
        {
            var i = 0;
            foreach (var call in calls.EnumerateArray())
            {
                var function = call.GetProperty("function");
                var args = function.TryGetProperty("arguments", out var a)
                    ? a.ValueKind == JsonValueKind.String ? a.GetString() ?? "{}" : a.GetRawText()
                    : "{}";
                var id = call.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String
                    ? idProp.GetString()!
                    : $"call_{i}";
                toolCalls.Add(new LlmToolCall(id, function.GetProperty("name").GetString() ?? string.Empty, args));
                i++;
            }
        }

        int promptTokens = 0, completionTokens = 0, cachedTokens = 0;
        if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
        {
            promptTokens = ReadInt(usage, "prompt_tokens");
            completionTokens = ReadInt(usage, "completion_tokens");
            // DeepSeek: prompt_cache_hit_tokens · OpenAI: prompt_tokens_details.cached_tokens
            cachedTokens = ReadInt(usage, "prompt_cache_hit_tokens");
            if (cachedTokens == 0 && usage.TryGetProperty("prompt_tokens_details", out var details) && details.ValueKind == JsonValueKind.Object)
                cachedTokens = ReadInt(details, "cached_tokens");
        }
        // Fallback de estimación si el proveedor no reporta uso.
        if (promptTokens == 0) promptTokens = request.Messages.Sum(m => TextChunker.EstimateTokens(m.Content));
        if (completionTokens == 0) completionTokens = TextChunker.EstimateTokens(content);

        var finish = choice.TryGetProperty("finish_reason", out var f) && f.ValueKind == JsonValueKind.String ? f.GetString() : null;
        var model = root.TryGetProperty("model", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString()! : defaultModel;
        return new LlmResponse(content, toolCalls, new TokenUsage(promptTokens, completionTokens, 0, Math.Min(cachedTokens, promptTokens)), model, finish);
    }

    private static int ReadInt(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : 0;

    internal static JsonArray BuildMessages(IEnumerable<LlmMessage> messages)
    {
        var array = new JsonArray();
        foreach (var msg in messages)
        {
            var hasToolCalls = msg.ToolCalls is { Count: > 0 };
            // Con tool_calls el content puede ir null (algunos proveedores rechazan "" junto a tool_calls).
            var node = new JsonObject
            {
                ["role"] = msg.Role,
                ["content"] = hasToolCalls && string.IsNullOrEmpty(msg.Content) ? null : msg.Content ?? string.Empty
            };
            if (msg.ToolCallId is not null) node["tool_call_id"] = msg.ToolCallId;
            if (hasToolCalls)
            {
                var calls = new JsonArray();
                foreach (var call in msg.ToolCalls!)
                {
                    calls.Add(new JsonObject
                    {
                        ["id"] = call.Id,
                        ["type"] = "function",
                        ["function"] = new JsonObject { ["name"] = call.Name, ["arguments"] = call.ArgumentsJson }
                    });
                }
                node["tool_calls"] = calls;
            }
            array.Add(node);
        }
        return array;
    }

    private static JsonArray BuildTools(IEnumerable<ToolDefinition> tools)
    {
        var array = new JsonArray();
        foreach (var tool in tools)
        {
            array.Add(new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = tool.Name,
                    ["description"] = tool.Description,
                    ["parameters"] = JsonNode.Parse(tool.ParametersJsonSchema)
                }
            });
        }
        return array;
    }
}

/// <summary>Embeddings con protocolo OpenAI (/embeddings). Por defecto Ollama local; alternativa OpenAI.</summary>
public sealed class OpenAiEmbeddingClient : IEmbeddingClient
{
    private static SemaphoreSlim? _gate;
    private static readonly object GateLock = new();

    private readonly HttpClient _http;
    private readonly LlmOptions _options;
    private readonly ILogger<OpenAiEmbeddingClient> _logger;

    public OpenAiEmbeddingClient(HttpClient http, IOptions<LlmOptions> options, ILogger<OpenAiEmbeddingClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
        lock (GateLock) _gate ??= new SemaphoreSlim(Math.Max(1, _options.MaxConcurrency));
        OpenAiHttp.Configure(_http, _options.Embeddings);
    }

    public string EmbeddingModel => _options.Embeddings.Model;

    public async Task<EmbeddingResult> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["model"] = _options.Embeddings.Model,
            ["input"] = new JsonArray(inputs.Select(i => (JsonNode?)JsonValue.Create(i)).ToArray())
        };
        OpenAiHttp.MergeExtraBody(body, _options.Embeddings.EffectiveExtraBodyJson());

        using var doc = await OpenAiHttp.PostAsync(_http, _gate!, _options.Embeddings, _options.QueueTimeoutSeconds, "embeddings", body, _logger, ct);
        return Parse(doc.RootElement, inputs);
    }

    internal static EmbeddingResult Parse(JsonElement root, IReadOnlyList<string> inputs)
    {
        var vectors = root.GetProperty("data").EnumerateArray()
            .OrderBy(d => d.TryGetProperty("index", out var idx) ? idx.GetInt32() : 0)
            .Select(d => d.GetProperty("embedding").EnumerateArray().Select(v => v.GetSingle()).ToArray())
            .ToList();

        var tokens = root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object
                     && usage.TryGetProperty("prompt_tokens", out var pt) && pt.ValueKind == JsonValueKind.Number
            ? pt.GetInt32()
            : 0;
        if (tokens == 0) tokens = inputs.Sum(TextChunker.EstimateTokens);
        return new EmbeddingResult(vectors, tokens);
    }
}
