using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Toka.BuildingBlocks.Contracts.Security;
using Toka.BuildingBlocks.Infrastructure.Hosting;
using Toka.BuildingBlocks.Infrastructure.Messaging;
using Toka.User.Application;
using Toka.User.Domain;

namespace Toka.User.Infrastructure;

public sealed class UserDbContext(DbContextOptions<UserDbContext> options) : DbContext(options)
{
    public DbSet<UserProfile> Users => Set<UserProfile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("users");
        modelBuilder.Entity<UserProfile>(e =>
        {
            e.ToTable("profiles");
            e.HasKey(u => u.Id);
            e.Property(u => u.Id).ValueGeneratedNever();
            e.Property(u => u.Email).HasMaxLength(254).IsRequired();
            e.HasIndex(u => u.Email).IsUnique();
            e.Property(u => u.FullName).HasMaxLength(UserProfile.MaxNameLength).IsRequired();
            e.Property(u => u.Department).HasMaxLength(UserProfile.MaxDepartmentLength);
            e.Property(u => u.JobTitle).HasMaxLength(UserProfile.MaxJobTitleLength);
            e.Property(u => u.Status).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(u => u.Status);
            e.Ignore(u => u.IsActive);
        });
    }
}

public sealed class UserRepository(UserDbContext db) : IUserRepository
{
    public Task<UserProfile?> GetByIdAsync(Guid id, CancellationToken ct) => db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

    public Task<bool> ExistsAsync(Guid id, CancellationToken ct) => db.Users.AnyAsync(u => u.Id == id, ct);

    public async Task<(IReadOnlyList<UserProfile> Items, long Total)> SearchAsync(UserSearchCriteria criteria, CancellationToken ct)
    {
        var query = db.Users.AsNoTracking().AsQueryable();
        if (!string.IsNullOrEmpty(criteria.Search))
        {
            // ILIKE parametrizado; se escapan comodines para que el usuario no inyecte patrones.
            var pattern = $"%{EscapeLike(criteria.Search)}%";
            query = query.Where(u => EF.Functions.ILike(u.Email, pattern, "\\") || EF.Functions.ILike(u.FullName, pattern, "\\"));
        }
        if (criteria.Status is not null) query = query.Where(u => u.Status == criteria.Status);
        if (!string.IsNullOrEmpty(criteria.Department)) query = query.Where(u => u.Department == criteria.Department);

        var total = await query.LongCountAsync(ct);
        var items = await query
            .OrderBy(u => u.FullName)
            .Skip((criteria.Page - 1) * criteria.PageSize)
            .Take(criteria.PageSize)
            .ToListAsync(ct);
        return (items, total);
    }

    public async Task<UserStatsDto> GetStatsAsync(CancellationToken ct)
    {
        var byStatus = await db.Users.GroupBy(u => u.Status).Select(g => new { g.Key, Count = g.LongCount() }).ToListAsync(ct);
        var byDepartment = await db.Users
            .GroupBy(u => u.Department ?? "Sin departamento")
            .Select(g => new { g.Key, Count = g.LongCount() })
            .ToListAsync(ct);

        var active = byStatus.Where(s => s.Key == UserStatus.Active).Sum(s => s.Count);
        var deactivated = byStatus.Where(s => s.Key == UserStatus.Deactivated).Sum(s => s.Count);
        return new UserStatsDto(active + deactivated, active, deactivated, byDepartment.ToDictionary(d => d.Key, d => d.Count));
    }

    public async Task AddAsync(UserProfile profile, CancellationToken ct) => await db.Users.AddAsync(profile, ct);

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}

public static class DependencyInjection
{
    public static IServiceCollection AddUserInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<UserDbContext>(o =>
            o.UseNpgsql(configuration.GetConnectionString("UsersDb"), npgsql => npgsql.EnableRetryOnFailure(5)));
        services.AddHealthChecks().AddDbContextCheck<UserDbContext>("postgres");
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddEventConsumer<UserRegisteredHandler>("user-service.registrations");
        return services;
    }

    public static async Task InitializeUserDatabaseAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("UserDbInit");
        var db = scope.ServiceProvider.GetRequiredService<UserDbContext>();

        await StartupRetry.RunAsync(async () =>
        {
            await db.Database.EnsureCreatedAsync();
            if (!await db.Users.AnyAsync(u => u.Id == SeedData.AdminUserId))
            {
                var admin = UserProfile.Create(SeedData.AdminUserId, SeedData.AdminEmail, SeedData.AdminFullName, "Tecnología", DateTimeOffset.UtcNow);
                admin.UpdateProfile(admin.FullName, admin.Department, "Administrador de plataforma", DateTimeOffset.UtcNow);
                db.Users.Add(admin);
                await db.SaveChangesAsync();
            }
        }, logger, "Inicialización de base de datos Users");
    }
}
