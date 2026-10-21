using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Toka.BuildingBlocks.Contracts.Security;
using Toka.BuildingBlocks.Infrastructure.Hosting;
using Toka.BuildingBlocks.Infrastructure.Messaging;
using Toka.Roles.Application;
using Toka.Roles.Domain;

namespace Toka.Roles.Infrastructure;

public sealed class RolesDbContext(DbContextOptions<RolesDbContext> options) : DbContext(options)
{
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserRoleAssignment> Assignments => Set<UserRoleAssignment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("roles");
        modelBuilder.Entity<Role>(e =>
        {
            e.ToTable("roles");
            e.HasKey(r => r.Id);
            e.Property(r => r.Id).ValueGeneratedNever();
            e.Property(r => r.Name).HasMaxLength(40).IsRequired();
            e.HasIndex(r => r.Name).IsUnique();
            e.Property(r => r.Description).HasMaxLength(200);
            e.Property(r => r.Permissions).HasColumnType("text[]"); // array nativo de Postgres
        });
        modelBuilder.Entity<UserRoleAssignment>(e =>
        {
            e.ToTable("user_roles");
            e.HasKey(a => new { a.UserId, a.RoleId });
            e.HasOne(a => a.Role).WithMany().HasForeignKey(a => a.RoleId).OnDelete(DeleteBehavior.Restrict);
            e.Property(a => a.AssignedBy).HasMaxLength(254);
            e.HasIndex(a => a.UserId);
        });
    }
}

public sealed class RoleRepository(RolesDbContext db) : IRoleRepository
{
    public async Task<IReadOnlyList<Role>> ListAsync(CancellationToken ct) =>
        await db.Roles.AsNoTracking().OrderBy(r => r.Name).ToListAsync(ct);

    public Task<Role?> GetByIdAsync(Guid id, CancellationToken ct) => db.Roles.FirstOrDefaultAsync(r => r.Id == id, ct);

    public Task<Role?> GetByNameAsync(string name, CancellationToken ct) =>
        db.Roles.FirstOrDefaultAsync(r => r.Name.ToLower() == name.ToLower(), ct);

    public async Task<IReadOnlyList<Role>> GetRolesForUserAsync(Guid userId, CancellationToken ct) =>
        await db.Assignments.AsNoTracking().Where(a => a.UserId == userId).Select(a => a.Role!).ToListAsync(ct);

    public async Task<IReadOnlyList<Guid>> GetUserIdsWithRoleAsync(Guid roleId, CancellationToken ct) =>
        await db.Assignments.Where(a => a.RoleId == roleId).Select(a => a.UserId).ToListAsync(ct);

    public Task<UserRoleAssignment?> GetAssignmentAsync(Guid userId, Guid roleId, CancellationToken ct) =>
        db.Assignments.FirstOrDefaultAsync(a => a.UserId == userId && a.RoleId == roleId, ct);

    public Task<int> CountUsersWithRoleAsync(Guid roleId, CancellationToken ct) =>
        db.Assignments.CountAsync(a => a.RoleId == roleId, ct);

    public async Task AddRoleAsync(Role role, CancellationToken ct) => await db.Roles.AddAsync(role, ct);
    public void RemoveRole(Role role) => db.Roles.Remove(role);

    public async Task AddAssignmentAsync(UserRoleAssignment assignment, CancellationToken ct)
    {
        // El Role ya está trackeado o existe en BD: se adjunta para no intentar insertarlo de nuevo.
        if (assignment.Role is not null && db.Entry(assignment.Role).State == EntityState.Detached)
            db.Attach(assignment.Role);
        await db.Assignments.AddAsync(assignment, ct);
    }

    public void RemoveAssignment(UserRoleAssignment assignment) => db.Assignments.Remove(assignment);

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}

public static class DependencyInjection
{
    public static IServiceCollection AddRolesInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<RolesDbContext>(o =>
            o.UseNpgsql(configuration.GetConnectionString("RolesDb"), npgsql => npgsql.EnableRetryOnFailure(5)));
        services.AddHealthChecks().AddDbContextCheck<RolesDbContext>("postgres");
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddEventConsumer<AssignDefaultRoleHandler>("role-service.registrations");
        return services;
    }

    /// <summary>Seed de roles de sistema y del administrador inicial.</summary>
    public static async Task InitializeRolesDatabaseAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("RolesDbInit");
        var db = scope.ServiceProvider.GetRequiredService<RolesDbContext>();

        await StartupRetry.RunAsync(async () =>
        {
            await db.Database.EnsureCreatedAsync();
            var now = DateTimeOffset.UtcNow;
            var seeds = new[]
            {
                (SystemRoles.Admin, "Acceso total a la plataforma", Permissions.All.ToArray()),
                (SystemRoles.Auditor, "Lectura de usuarios, roles y auditoría", new[] { Permissions.UsersRead, Permissions.RolesRead, Permissions.AuditRead, Permissions.AiUse }),
                (SystemRoles.User, "Usuario estándar", new[] { Permissions.AiUse })
            };
            foreach (var (name, description, permissions) in seeds)
            {
                if (!await db.Roles.AnyAsync(r => r.Name == name))
                    db.Roles.Add(Role.Create(Guid.NewGuid(), name, description, permissions, isSystem: true, now));
            }
            await db.SaveChangesAsync();

            var adminRole = await db.Roles.FirstAsync(r => r.Name == SystemRoles.Admin);
            if (!await db.Assignments.AnyAsync(a => a.UserId == SeedData.AdminUserId && a.RoleId == adminRole.Id))
            {
                db.Assignments.Add(UserRoleAssignment.Create(SeedData.AdminUserId, adminRole, "seed", now));
                await db.SaveChangesAsync();
            }
        }, logger, "Inicialización de base de datos Roles");
    }
}
