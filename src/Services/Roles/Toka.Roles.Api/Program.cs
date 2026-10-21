using System.Security.Claims;
using Toka.BuildingBlocks.Contracts.Security;
using Toka.BuildingBlocks.Infrastructure.Hosting;
using Toka.BuildingBlocks.Infrastructure.Http;
using Toka.BuildingBlocks.Infrastructure.Security;
using Toka.Roles.Application;
using Toka.Roles.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.AddTokaPlatform("role-service");
builder.Services.AddRolesApplication();
builder.Services.AddRolesInfrastructure(builder.Configuration);

var app = builder.Build();
app.UseTokaPlatform();
await app.Services.InitializeRolesDatabaseAsync();

var roles = app.MapGroup("/api/roles").WithTags("Roles").RequireAuthorization();

roles.MapGet("", async (RoleUseCases useCases, CancellationToken ct) => Results.Ok(await useCases.ListAsync(ct)))
    .RequireAuthorization(Permissions.RolesRead);

roles.MapGet("/permissions", () => Results.Ok(Permissions.All))
    .RequireAuthorization(Permissions.RolesRead);

roles.MapPost("", async (CreateRoleRequest body, ClaimsPrincipal user, RoleUseCases useCases, CancellationToken ct) =>
        (await useCases.CreateAsync(new CreateRoleCommand(body.Name, body.Description, body.Permissions ?? new List<string>()), ToActor(user), ct))
        .ToHttpResult(r => Results.Created($"/api/roles/{r.Id}", r)))
    .RequireAuthorization(Permissions.RolesManage);

roles.MapPut("/{id:guid}/permissions", async (Guid id, UpdatePermissionsRequest body, ClaimsPrincipal user, RoleUseCases useCases, CancellationToken ct) =>
        (await useCases.UpdatePermissionsAsync(id, body.Permissions ?? new List<string>(), ToActor(user), ct)).ToHttpResult())
    .RequireAuthorization(Permissions.RolesManage);

roles.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal user, RoleUseCases useCases, CancellationToken ct) =>
        (await useCases.DeleteAsync(id, ToActor(user), ct)).ToHttpResult())
    .RequireAuthorization(Permissions.RolesManage);

// Consultado por Auth Service (token de servicio con scope roles.read), por administradores o por el propio usuario.
roles.MapGet("/users/{userId:guid}", async (Guid userId, ClaimsPrincipal user, RoleUseCases useCases, CancellationToken ct) =>
{
    var allowed = user.HasPermission(Permissions.RolesRead) || user.HasScope(Scopes.RolesRead) || user.GetUserId() == userId;
    return allowed ? Results.Ok(await useCases.GetUserRolesAsync(userId, ct)) : Results.Forbid();
});

roles.MapPost("/users/{userId:guid}/assign", async (Guid userId, AssignRoleRequest body, ClaimsPrincipal user, RoleUseCases useCases, CancellationToken ct) =>
        (await useCases.AssignAsync(userId, body.RoleName, ToActor(user), ct)).ToHttpResult())
    .RequireAuthorization(Permissions.RolesManage);

roles.MapDelete("/users/{userId:guid}/{roleName}", async (Guid userId, string roleName, ClaimsPrincipal user, RoleUseCases useCases, CancellationToken ct) =>
        (await useCases.RevokeAsync(userId, roleName, ToActor(user), ct)).ToHttpResult())
    .RequireAuthorization(Permissions.RolesManage);

app.Run();

static RoleActor ToActor(ClaimsPrincipal user) => new(user.GetUserId(), user.GetEmail());

internal sealed record CreateRoleRequest(string Name, string? Description, List<string>? Permissions);
internal sealed record UpdatePermissionsRequest(List<string>? Permissions);
internal sealed record AssignRoleRequest(string RoleName);
