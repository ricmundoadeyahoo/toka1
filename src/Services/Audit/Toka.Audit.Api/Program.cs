using Toka.Audit.Application;
using Toka.Audit.Infrastructure;
using Toka.BuildingBlocks.Contracts.Security;
using Toka.BuildingBlocks.Infrastructure.Hosting;
using Toka.BuildingBlocks.Infrastructure.Http;

var builder = WebApplication.CreateBuilder(args);

builder.AddTokaPlatform("audit-service");
builder.Services.AddAuditApplication();
builder.Services.AddAuditInfrastructure(builder.Configuration);

var app = builder.Build();
app.UseTokaPlatform();
await app.Services.InitializeAuditStoreAsync();

var audit = app.MapGroup("/api/audit").WithTags("Audit").RequireAuthorization(Permissions.AuditRead);

audit.MapGet("", async (string? eventType, string? category, string? actorId, string? subjectId, bool? success,
        DateTimeOffset? from, DateTimeOffset? to, int? page, int? pageSize, AuditUseCases useCases, CancellationToken ct) =>
    (await useCases.SearchAsync(eventType, category, actorId, subjectId, success, from, to, page, pageSize, ct)).ToHttpResult());

audit.MapGet("/summary", async (int? hours, AuditUseCases useCases, CancellationToken ct) =>
    (await useCases.SummaryAsync(hours, ct)).ToHttpResult());

app.Run();
