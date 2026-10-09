using Microsoft.EntityFrameworkCore;
using MyVedicStar.Authorize.Data.Persistence.Entities;

namespace MyVedicStar.Authorize.Data.Persistence;

/// <summary>
/// EF Core context for the Authorize domain. Internal: other layers use the repositories.
/// </summary>
internal sealed class AuthorizeDbContext : DbContext
{
    /// <summary>PostgreSQL schema that owns every Authorize table, index and the migrations history.</summary>
    /// <remarks>Fixed rather than configurable because migrations are generated against it.</remarks>
    public const string Schema = "myvedicstar";

    public const string MigrationsHistoryTable = "__EFMigrationsHistory";

    public AuthorizeDbContext(DbContextOptions<AuthorizeDbContext> options)
        : base(options)
    {
    }

    public DbSet<UserEntity> Users => Set<UserEntity>();

    public DbSet<UserLoginEntity> UserLogins => Set<UserLoginEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AuthorizeDbContext).Assembly);
    }
}
