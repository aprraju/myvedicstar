using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
namespace MyVedicStar.Settings.Data;

internal sealed class SettingsEntity
{
    public Guid UserId { get; set; }
    public string? DefaultTradition { get; set; }
    public Guid? DefaultLocationId { get; set; }
    public long Revision { get; set; }
    public SavedLocationEntity? DefaultLocation { get; set; }
}
internal sealed class SavedLocationEntity
{
    public Guid LocationId { get; set; }
    public Guid UserId { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? CountryCode { get; set; }
    public string? PostalCode { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string TimeZone { get; set; } = "";
    public string Identity { get; set; } = "";
}
internal sealed class AccountReference { public Guid Id { get; set; } }
internal sealed class SettingsDbContext(DbContextOptions<SettingsDbContext> options) : DbContext(options)
{
    public DbSet<SettingsEntity> Settings => Set<SettingsEntity>();
    public DbSet<SavedLocationEntity> Locations => Set<SavedLocationEntity>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema("myvedicstarsettings");
        model.Entity<AccountReference>(b => { b.ToTable("User", "myvedicstar", table => table.ExcludeFromMigrations()); b.HasKey(x => x.Id); });
        model.Entity<SavedLocationEntity>(b =>
        {
            b.ToTable("UserSavedLocations"); b.HasKey(x => x.LocationId); b.Property(x => x.LocationId).ValueGeneratedNever();
            b.HasAlternateKey(x => new { x.UserId, x.LocationId });
            b.HasOne<AccountReference>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.UserId, x.Identity }).IsUnique();
            b.Property(x => x.Identity).HasMaxLength(160); b.Property(x => x.TimeZone).HasMaxLength(64);
            b.Property(x => x.City).HasMaxLength(256); b.Property(x => x.State).HasMaxLength(256);
            b.Property(x => x.Country).HasMaxLength(256); b.Property(x => x.CountryCode).HasMaxLength(2); b.Property(x => x.PostalCode).HasMaxLength(20);
        });
        model.Entity<SettingsEntity>(b =>
        {
            b.ToTable("UserSettings"); b.HasKey(x => x.UserId); b.Property(x => x.UserId).ValueGeneratedNever();
            b.Property(x => x.DefaultTradition).HasMaxLength(80); b.Property(x => x.Revision).IsConcurrencyToken();
            b.HasOne<AccountReference>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.DefaultLocation).WithMany().HasForeignKey(x => new { x.UserId, x.DefaultLocationId })
                .HasPrincipalKey(x => new { x.UserId, x.LocationId }).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
internal sealed class SettingsDesignTimeFactory : IDesignTimeDbContextFactory<SettingsDbContext>
{
    public SettingsDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<SettingsDbContext>()
        .UseNpgsql("Host=localhost;Database=myvedicstar_design_time", b => b.MigrationsHistoryTable("__EFMigrationsHistory", "myvedicstarsettings")).Options);
}
