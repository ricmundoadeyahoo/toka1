using System.Security.Claims;
using Toka.BuildingBlocks.Contracts.Security;
using Toka.BuildingBlocks.Infrastructure.Hosting;
using Toka.BuildingBlocks.Infrastructure.Http;
using Toka.BuildingBlocks.Infrastructure.Security;
using Toka.User.Application;
using Toka.User.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.AddTokaPlatform("user-service");
builder.Services.AddUserApplication();
builder.Services.AddUserInfrastructure(builder.Configuration);

var app = builder.Build();
app.UseTokaPlatform();
await app.Services.InitializeUserDatabaseAsync();

var users = app.MapGroup("/api/users").WithTags("Users").RequireAuthorization();

users.MapGet("", async (string? search, string? status, string? department, int? page, int? pageSize, UserQueries queries, CancellationToken ct) =>
        (await queries.SearchAsync(search, status, department, page, pageSize, ct)).ToHttpResult())
    .RequireAuthorization(Permissions.UsersRead);

users.MapGet("/stats", async (UserQueries queries, CancellationToken ct) => Results.Ok(await queries.GetStatsAsync(ct)))
    .RequireAuthorization(Permissions.UsersRead);

users.MapGet("/me", async (ClaimsPrincipal user, UserQueries queries, CancellationToken ct) =>
    user.GetUserId() is { } id
        ? (await queries.GetByIdAsync(id, ToActor(user), ct)).ToHttpResult()
        : Results.Unauthorized());

users.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, UserQueries queries, CancellationToken ct) =>
    (await queries.GetByIdAsync(id, ToActor(user), ct)).ToHttpResult());

users.MapPut("/{id:guid}", async (Guid id, UpdateUserRequest body, ClaimsPrincipal user, UserCommands commands, CancellationToken ct) =>
    (await commands.UpdateAsync(new UpdateUserCommand(id, body.FullName, body.Department, body.JobTitle, ToActor(user)), ct)).ToHttpResult());

users.MapPost("/{id:guid}/deactivate", async (Guid id, ClaimsPrincipal user, UserCommands commands, CancellationToken ct) =>
        (await commands.DeactivateAsync(id, ToActor(user), ct)).ToHttpResult())
    .RequireAuthorization(Permissions.UsersWrite);

users.MapPost("/{id:guid}/activate", async (Guid id, ClaimsPrincipal user, UserCommands commands, CancellationToken ct) =>
        (await commands.ActivateAsync(id, ToActor(user), ct)).ToHttpResult())
    .RequireAuthorization(Permissions.UsersWrite);

app.Run();

static Actor ToActor(ClaimsPrincipal user) =>
    new(user.GetUserId(), user.GetEmail(), user.HasPermission(Permissions.UsersWrite),
        user.HasPermission(Permissions.UsersRead) || user.HasScope(Scopes.UsersRead));

internal sealed record UpdateUserRequest(string FullName, string? Department, string? JobTitle);
