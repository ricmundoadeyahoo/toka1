namespace Toka.BuildingBlocks.Contracts.Events;

/// <summary>
/// Catálogo de eventos (routing keys del exchange topic "toka.events").
/// Convención: {bounded-context}.{agregado}.{hecho-en-pasado}
/// </summary>
public static class EventTypes
{
    // Identity (Auth Service)
    public const string UserRegistered = "identity.user.registered";
    public const string LoginSucceeded = "identity.login.succeeded";
    public const string LoginFailed = "identity.login.failed";
    public const string AccountLocked = "identity.account.locked";
    public const string LoggedOut = "identity.logout.completed";

    // Users (User Service)
    public const string UserUpdated = "users.user.updated";
    public const string UserDeactivated = "users.user.deactivated";
    public const string UserActivated = "users.user.activated";

    // Roles (Role Service)
    public const string RoleCreated = "roles.role.created";
    public const string RoleDeleted = "roles.role.deleted";
    public const string RolePermissionsChanged = "roles.role.permissions-changed";
    public const string RoleAssigned = "roles.assignment.granted";
    public const string RoleRevoked = "roles.assignment.revoked";

    // AI (AI Service)
    public const string AiQueryCompleted = "ai.query.completed";
    public const string AiKnowledgeIngested = "ai.knowledge.ingested";
}
