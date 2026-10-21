using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Toka.Ai.Application;
using Toka.Ai.Domain;
using Toka.Ai.Infrastructure.Llm;

namespace Toka.Ai.UnitTests;

public class LlmClientTests
{
    private sealed class CapturingHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public string? LastBody { get; private set; }
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private const string DeepSeekResponse = """
        {"id":"x","model":"deepseek-flash","choices":[{"index":0,"finish_reason":"tool_calls","message":{"role":"assistant","content":"",
          "tool_calls":[{"id":"call_abc","type":"function","function":{"name":"estadisticas_usuarios","arguments":"{}"}}]}}],
         "usage":{"prompt_tokens":1200,"completion_tokens":30,"total_tokens":1230,"prompt_cache_hit_tokens":1000,"prompt_cache_miss_tokens":200}}
        """;

    private static LlmRequest Request() => new([LlmMessage.OfSystem("s"), LlmMessage.OfUser("hola")], null);

    [Fact]
    public void Parse_lee_tool_calls_y_tokens_en_cache_de_DeepSeek()
    {
        using var doc = JsonDocument.Parse(DeepSeekResponse);
        var r = OpenAiChatClient.Parse(doc.RootElement, Request(), "fallback");

        Assert.Equal("deepseek-flash", r.Model);
        Assert.Equal("tool_calls", r.FinishReason);
        var call = Assert.Single(r.ToolCalls);
        Assert.Equal("call_abc", call.Id);
        Assert.Equal("estadisticas_usuarios", call.Name);
        Assert.Equal(new TokenUsage(1200, 30, 0, 1000), r.Usage);
    }

    [Fact]
    public void Parse_lee_cached_tokens_formato_OpenAI_y_estima_si_no_hay_usage()
    {
        using var openAi = JsonDocument.Parse("""
            {"choices":[{"message":{"content":"ok"}}],"usage":{"prompt_tokens":100,"completion_tokens":5,"prompt_tokens_details":{"cached_tokens":64}}}
            """);
        Assert.Equal(64, OpenAiChatClient.Parse(openAi.RootElement, Request(), "m").Usage.CachedPromptTokens);

        using var noUsage = JsonDocument.Parse("""{"choices":[{"message":{"content":"respuesta de prueba"}}]}""");
        var r = OpenAiChatClient.Parse(noUsage.RootElement, Request(), "modelo-default");
        Assert.True(r.Usage.PromptTokens > 0);
        Assert.True(r.Usage.CompletionTokens > 0);
        Assert.Equal("modelo-default", r.Model);
    }

    [Fact]
    public void Mensaje_con_tool_calls_envia_content_null()
    {
        var array = OpenAiChatClient.BuildMessages(
        [
            LlmMessage.OfAssistant(null, [new LlmToolCall("c1", "buscar_usuarios", "{\"texto\":\"ana\"}")]),
            LlmMessage.OfTool("c1", "{}")
        ]);
        Assert.Null(array[0]!["content"]);
        Assert.Equal("function", array[0]!["tool_calls"]![0]!["type"]!.GetValue<string>());
        Assert.Equal("c1", array[1]!["tool_call_id"]!.GetValue<string>());
    }

    [Fact]
    public void ExtraBody_agrega_parametros_del_proveedor()
    {
        var body = new JsonObject { ["model"] = "deepseek-flash" };
        OpenAiHttp.MergeExtraBody(body, """{"thinking":{"type":"disabled"}}""");
        Assert.Equal("disabled", body["thinking"]!["type"]!.GetValue<string>());

        OpenAiHttp.MergeExtraBody(body, null); // sin cambios
        Assert.Throws<InvalidOperationException>(() => OpenAiHttp.MergeExtraBody(body, "[1]"));
    }

    [Theory]
    [InlineData("https://api.deepseek.com", null, OpenAiEndpointOptions.DeepSeekDisableThinking)]
    [InlineData("https://api.deepseek.com", "none", null)]
    [InlineData("https://api.openai.com/v1", null, null)]
    [InlineData("https://api.openai.com/v1", "{\"x\":1}", "{\"x\":1}")]
    public void Extra_body_automatico_por_proveedor(string baseUrl, string? configured, string? expected) =>
        Assert.Equal(expected, new OpenAiEndpointOptions { BaseUrl = baseUrl, ExtraBodyJson = configured }.EffectiveExtraBodyJson());

    [Theory]
    [InlineData("", false)]
    [InlineData("sk-REEMPLAZA-CON-TU-KEY", false)]
    [InlineData("changeme", false)]
    [InlineData("sk-123", true)]
    public void Detecta_api_key_no_configurada(string key, bool usable) =>
        Assert.Equal(usable, OpenAiHttp.HasUsableKey(new OpenAiEndpointOptions { ApiKey = key }));

    [Fact]
    public async Task ChatAsync_envia_modelo_tools_extra_body_y_bearer()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK, DeepSeekResponse);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.deepseek.com/") };
        var options = Options.Create(new LlmOptions { Chat = new() { BaseUrl = "https://api.deepseek.com", ApiKey = "sk-test", Model = "deepseek-flash" } });
        var client = new OpenAiChatClient(http, options, NullLogger<OpenAiChatClient>.Instance);

        var tools = new[] { new ToolDefinition("estadisticas_usuarios", "d", """{"type":"object","properties":{}}""") };
        var response = await client.ChatAsync(new LlmRequest([LlmMessage.OfUser("¿cuántos?")], tools), default);

        Assert.Equal("https://api.deepseek.com/chat/completions", handler.LastRequest!.RequestUri!.ToString());
        Assert.Equal("sk-test", handler.LastRequest.Headers.Authorization!.Parameter);
        using var sent = JsonDocument.Parse(handler.LastBody!);
        Assert.Equal("deepseek-flash", sent.RootElement.GetProperty("model").GetString());
        Assert.Equal("disabled", sent.RootElement.GetProperty("thinking").GetProperty("type").GetString());
        Assert.Equal("estadisticas_usuarios", sent.RootElement.GetProperty("tools")[0].GetProperty("function").GetProperty("name").GetString());
        Assert.Single(response.ToolCalls);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, typeof(LlmRateLimitedException))]
    [InlineData(HttpStatusCode.PaymentRequired, typeof(LlmUnavailableException))]
    [InlineData(HttpStatusCode.Unauthorized, typeof(LlmUnavailableException))]
    [InlineData(HttpStatusCode.InternalServerError, typeof(LlmUnavailableException))]
    public async Task ChatAsync_traduce_errores_del_proveedor(HttpStatusCode status, Type expected)
    {
        var http = new HttpClient(new CapturingHandler(status, "{}")) { BaseAddress = new Uri("https://api.deepseek.com/") };
        var client = new OpenAiChatClient(http, Options.Create(new LlmOptions { Chat = new() { BaseUrl = "https://api.deepseek.com", ApiKey = "sk-test", Model = "m" } }),
            NullLogger<OpenAiChatClient>.Instance);
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => client.ChatAsync(Request(), default));
        Assert.IsType(expected, ex);
    }

    [Fact]
    public async Task Sin_api_key_falla_con_mensaje_claro_sin_llamar_al_proveedor()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK, DeepSeekResponse);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.deepseek.com/") };
        var client = new OpenAiChatClient(http, Options.Create(new LlmOptions { Chat = new() { BaseUrl = "https://api.deepseek.com", ApiKey = "", Model = "m" } }),
            NullLogger<OpenAiChatClient>.Instance);
        var ex = await Assert.ThrowsAsync<LlmUnavailableException>(() => client.ChatAsync(Request(), default));
        Assert.Contains("API key", ex.Message);
        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public async Task Embeddings_ordena_por_indice_y_cuenta_tokens()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK,
            """{"data":[{"index":1,"embedding":[0.3,0.4]},{"index":0,"embedding":[0.1,0.2]}],"usage":{"prompt_tokens":7}}""");
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://ollama:11434/v1/") };
        var client = new OpenAiEmbeddingClient(http, Options.Create(new LlmOptions()), NullLogger<OpenAiEmbeddingClient>.Instance);

        var result = await client.EmbedAsync(["a", "b"], default);

        Assert.Equal("http://ollama:11434/v1/embeddings", handler.LastRequest!.RequestUri!.ToString());
        Assert.Equal(new[] { 0.1f, 0.2f }, result.Vectors[0]);
        Assert.Equal(7, result.Tokens);
        Assert.Equal("nomic-embed-text", client.EmbeddingModel);
    }

    [Fact]
    public void Costo_con_cache_hit_de_DeepSeek()
    {
        // 1,000 tokens en caché × 0.006 + 200 sin caché × 0.30 + 30 de salida × 1.20  (USD por millón)
        var pricing = new ModelPricing(0.30m, 1.20m, 0, 0.006m);
        Assert.Equal(0.000102m, pricing.Calculate(new TokenUsage(1200, 30, 0, 1000)));
        // Sin precio de caché, todo se cobra a precio de entrada normal
        Assert.Equal(0.000396m, new ModelPricing(0.30m, 1.20m).Calculate(new TokenUsage(1200, 30, 0, 1000)));
    }
}
