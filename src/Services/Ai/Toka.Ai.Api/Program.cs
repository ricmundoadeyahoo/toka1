using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;
using Toka.Ai.Application;
using Toka.Ai.Application.Evaluation;
using Toka.Ai.Application.UseCases;
using Toka.Ai.Infrastructure;
using Toka.Ai.Infrastructure.Llm;
using Toka.BuildingBlocks.Contracts.Security;
using Toka.BuildingBlocks.Infrastructure.Hosting;
using Toka.BuildingBlocks.Infrastructure.Http;
using Toka.BuildingBlocks.Infrastructure.Security;

var builder = WebApplication.CreateBuilder(args);

builder.AddTokaPlatform("ai-service");
builder.Services.AddAiApplication();
builder.Services.AddAiInfrastructure(builder.Configuration);

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // Control de costo/abuso: N consultas por minuto por usuario autenticado.
    var perMinute = builder.Configuration.GetValue("Agent:RequestsPerMinutePerUser", 10);
    options.AddPolicy("ai-per-user", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.GetUserId()?.ToString() ?? context.Connection.RemoteIpAddress?.ToString() ?? "anon",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = perMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "60";
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            title = "ai.rate_limited",
            detail = $"Límite de {perMinute} consultas por minuto alcanzado. Intenta más tarde.",
            status = 429
        }, ct);
    };
});

var app = builder.Build();
app.UseTokaPlatform();
app.UseRateLimiter();
await app.Services.InitializeAiStoreAsync();

var ai = app.MapGroup("/api/ai").WithTags("AI").RequireAuthorization();

ai.MapPost("/ask", async (AskRequest body, HttpContext http, AskAgentUseCase useCase, CancellationToken ct) =>
        (await useCase.ExecuteAsync(new AskCommand(body.Question ?? string.Empty, body.UseCache ?? true, ToContext(http)), ct)).ToHttpResult())
    .RequireAuthorization(Permissions.AiUse)
    .RequireRateLimiting("ai-per-user");

ai.MapGet("/knowledge/status", async (IVectorStore store, IOptions<LlmOptions> llm, CancellationToken ct) => Results.Ok(new
    {
        chunks = await store.CountAsync(ct),
        chatModel = llm.Value.Chat.Model,
        chatProvider = new Uri(llm.Value.Chat.BaseUrl).Host,
        embeddingModel = llm.Value.Embeddings.Model,
        embeddingProvider = new Uri(llm.Value.Embeddings.BaseUrl).Host,
        promptVersion = Toka.Ai.Application.Prompts.PromptLibrary.PromptVersion
    }))
    .RequireAuthorization(Permissions.AiUse);

ai.MapPost("/knowledge/reindex", async (IngestKnowledgeUseCase useCase, CancellationToken ct) => Results.Ok(await useCase.IngestAllAsync(ct)))
    .RequireAuthorization(Permissions.AiAdmin);

ai.MapPost("/knowledge/documents", async (AddDocumentRequest body, IngestKnowledgeUseCase useCase, CancellationToken ct) =>
        (await useCase.IngestDocumentAsync(body.Title, body.Content, ct)).ToHttpResult())
    .RequireAuthorization(Permissions.AiAdmin);

ai.MapGet("/metrics/summary", async (int? last, IMetricsRepository repository, CancellationToken ct) =>
        Results.Ok(MetricsSummaryCalculator.Calculate(await repository.GetRecentAsync(Math.Clamp(last ?? 200, 1, 1000), ct))))
    .RequireAuthorization(Permissions.AiAdmin);

ai.MapGet("/metrics/recent", async (int? limit, IMetricsRepository repository, CancellationToken ct) =>
        Results.Ok(await repository.GetRecentAsync(Math.Clamp(limit ?? 20, 1, 100), ct)))
    .RequireAuthorization(Permissions.AiAdmin);

ai.MapPost("/evaluate", async (int? maxCases, HttpContext http, EvaluateAgentUseCase useCase, CancellationToken ct) =>
        Results.Ok(await useCase.ExecuteAsync(ToContext(http), maxCases, ct)))
    .RequireAuthorization(Permissions.AiAdmin)
    .RequireRateLimiting("ai-per-user");

app.Run();

static AgentContext ToContext(HttpContext http) => new(
    http.User.GetUserId()?.ToString(),
    http.User.GetEmail(),
    http.GetBearerToken(),
    http.User.Claims.Where(c => c.Type == TokaClaimTypes.Permission).Select(c => c.Value).ToList());

internal sealed record AskRequest(string? Question, bool? UseCache);
internal sealed record AddDocumentRequest(string Title, string Content);
