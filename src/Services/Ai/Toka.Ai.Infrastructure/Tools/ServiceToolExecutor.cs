using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Toka.Ai.Application;

namespace Toka.Ai.Infrastructure.Tools;

/// <summary>
/// Ejecuta las herramientas del agente llamando por REST a User/Role/Audit Service.
/// Usa TOKEN RELAY: reenvía el JWT del usuario, por lo que cada microservicio aplica sus propias reglas
/// de autorización (el agente nunca tiene más privilegios que quien pregunta).
/// Las respuestas se compactan (solo campos útiles) para ahorrar tokens.
/// </summary>
public sealed class ServiceToolExecutor(IHttpClientFactory httpClientFactory, ILogger<ServiceToolExecutor> logger) : IAgentToolExecutor
{
    public const string UsersClient = "users";
    public const string RolesClient = "roles";
    public const string AuditClient = "audit";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = false };

    public async Task<string> ExecuteAsync(string toolName, string argumentsJson, AgentContext context, CancellationToken ct)
    {
        var args = ParseArguments(argumentsJson);
        logger.LogInformation("Ejecutando herramienta {Tool}", toolName);

        return toolName switch
        {
            "buscar_usuarios" => await SearchUsersAsync(GetString(args, "texto"), context, ct),
            "estadisticas_usuarios" => await GetAsync(UsersClient, "/api/users/stats", context, raw => raw, ct),
            "roles_de_usuario" => Guid.TryParse(GetString(args, "userId"), out var userId)
                ? await GetAsync(RolesClient, $"/api/roles/users/{userId}", context, raw => raw, ct)
                : "ERROR: userId debe ser un GUID válido. Usa primero buscar_usuarios para obtenerlo.",
            "resumen_auditoria" => await GetAsync(AuditClient, $"/api/audit/summary?hours={Math.Clamp(GetInt(args, "horas") ?? 24, 1, 720)}", context, CompactAuditSummary, ct),
            "eventos_auditoria" => await GetAsync(AuditClient, BuildAuditQuery(args), context, CompactAuditList, ct),
            _ => $"ERROR: la herramienta '{toolName}' no existe."
        };
    }

    private Task<string> SearchUsersAsync(string? text, AgentContext context, CancellationToken ct)
    {
        var term = (text ?? string.Empty).Trim();
        if (term.Length is < 2 or > 100) return Task.FromResult("ERROR: el texto de búsqueda debe tener entre 2 y 100 caracteres.");
        return GetAsync(UsersClient, $"/api/users?search={Uri.EscapeDataString(term)}&pageSize=10", context, raw =>
        {
            using var doc = JsonDocument.Parse(raw);
            var items = doc.RootElement.GetProperty("items").EnumerateArray().Select(u => new
            {
                id = u.GetProperty("id").GetString(),
                email = u.GetProperty("email").GetString(),
                nombre = u.GetProperty("fullName").GetString(),
                departamento = u.TryGetProperty("department", out var d) ? d.GetString() : null,
                estado = u.GetProperty("status").GetString()
            });
            return JsonSerializer.Serialize(new { total = doc.RootElement.GetProperty("totalCount").GetInt64(), usuarios = items }, JsonOptions);
        }, ct);
    }

    private static string BuildAuditQuery(Dictionary<string, JsonElement> args)
    {
        var query = "/api/audit?pageSize=10";
        var type = GetString(args, "tipo");
        var userId = GetString(args, "usuarioId");
        if (!string.IsNullOrWhiteSpace(type)) query += $"&eventType={Uri.EscapeDataString(type.Trim())}";
        if (!string.IsNullOrWhiteSpace(userId)) query += $"&subjectId={Uri.EscapeDataString(userId.Trim())}";
        return query;
    }

    private static string CompactAuditSummary(string raw)
    {
        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;
        return JsonSerializer.Serialize(new
        {
            desde = root.GetProperty("from").GetString(),
            hasta = root.GetProperty("to").GetString(),
            total = root.GetProperty("total").GetInt64(),
            fallos = root.GetProperty("failures").GetInt64(),
            porTipo = JsonSerializer.Deserialize<Dictionary<string, long>>(root.GetProperty("byEventType").GetRawText()),
            fallosRecientes = root.GetProperty("recentFailures").EnumerateArray().Take(5).Select(CompactEntry).ToList()
        }, JsonOptions);
    }

    private static string CompactAuditList(string raw)
    {
        using var doc = JsonDocument.Parse(raw);
        return JsonSerializer.Serialize(new
        {
            total = doc.RootElement.GetProperty("totalCount").GetInt64(),
            eventos = doc.RootElement.GetProperty("items").EnumerateArray().Select(CompactEntry).ToList()
        }, JsonOptions);
    }

    private static object CompactEntry(JsonElement e) => new
    {
        tipo = e.GetProperty("eventType").GetString(),
        fecha = e.GetProperty("occurredAt").GetString(),
        actor = e.TryGetProperty("actorEmail", out var a) ? a.GetString() : null,
        sujeto = e.TryGetProperty("subjectId", out var s) ? s.GetString() : null,
        exito = e.GetProperty("success").GetBoolean()
    };

    private async Task<string> GetAsync(string clientName, string path, AgentContext context, Func<string, string> map, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(context.BearerToken)) return "ERROR: no hay credenciales del usuario para consultar el servicio.";

        var client = httpClientFactory.CreateClient(clientName);
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.BearerToken);
        using var response = await client.SendAsync(request, ct);

        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
            return "SIN_PERMISO: el usuario actual no tiene permisos para consultar esta información.";
        if (response.StatusCode == HttpStatusCode.NotFound) return "NO_ENCONTRADO: no existe el recurso solicitado.";
        if (!response.IsSuccessStatusCode) return $"ERROR: el servicio respondió {(int)response.StatusCode}.";

        var raw = await response.Content.ReadAsStringAsync(ct);
        try
        {
            return map(raw);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Respuesta inesperada de {Client}{Path}", clientName, path);
            return raw;
        }
    }

    internal static Dictionary<string, JsonElement> ParseArguments(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                ? doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        }
    }

    // Los modelos pequeños a veces envían números como texto o al revés: lectura tolerante.
    internal static string? GetString(Dictionary<string, JsonElement> args, string name) =>
        args.TryGetValue(name, out var v) ? v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            _ => null
        } : null;

    internal static int? GetInt(Dictionary<string, JsonElement> args, string name) =>
        args.TryGetValue(name, out var v) ? v.ValueKind switch
        {
            JsonValueKind.Number when v.TryGetInt32(out var n) => (int?)n,
            JsonValueKind.String when int.TryParse(v.GetString(), out var s) => (int?)s,
            _ => null
        } : null;
}
