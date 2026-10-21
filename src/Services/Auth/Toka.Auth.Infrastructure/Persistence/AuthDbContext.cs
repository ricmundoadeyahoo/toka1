using Microsoft.EntityFrameworkCore;
using Toka.Auth.Application;
using Toka.Auth.Domain;

namespace Toka.Auth.Infrastructure.Persistence;

public sealed class AuthDbContext(DbContextOptions<AuthDbContext> options) : DbContext(options)
{
    public DbSet<UserCredential> Credentials => Set<UserCredential>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("identity");
        modelBuilder.Entity<UserCredential>(entity =>
        {
            entity.ToTable("credentials");
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Id).ValueGeneratedNever();
            entity.Property(c => c.Email).HasMaxLength(EmailAddress.MaxLength).IsRequired();
            entity.HasIndex(c => c.Email).IsUnique();
            entity.Property(c => c.PasswordHash).HasMaxLength(100).IsRequired();
            entity.Property(c => c.IsActive).IsRequired();
            entity.Property(c => c.FailedLoginAttempts);
            entity.Property(c => c.LockoutEnd);
            entity.Property(c => c.CreatedAt).IsRequired();
            entity.Property(c => c.LastLoginAt);
        });
    }
}

/// <summary>Repositorio EF Core (consultas parametrizadas → sin SQL injection).</summary>
public sealed class CredentialRepository(AuthDbContext db) : ICredentialRepository
{
    public Task<UserCredential?> GetByEmailAsync(string normalizedEmail, CancellationToken ct) =>
        db.Credentials.FirstOrDefaultAsync(c => c.Email == normalizedEmail, ct);

    public Task<UserCredential?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.Credentials.FirstOrDefaultAsync(c => c.Id == id, ct);

    public Task<bool> ExistsByEmailAsync(string normalizedEmail, CancellationToken ct) =>
        db.Credentials.AnyAsync(c => c.Email == normalizedEmail, ct);

    public async Task AddAsync(UserCredential credential, CancellationToken ct) =>
        await db.Credentials.AddAsync(credential, ct);

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
