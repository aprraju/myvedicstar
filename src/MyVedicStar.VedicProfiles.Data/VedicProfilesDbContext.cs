using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using MyVedicStar.VedicProfiles.Shared;

namespace MyVedicStar.VedicProfiles.Data;

internal sealed class VedicProfileEntity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = "";
    public DateOnly DateOfBirth { get; set; }
    public BirthTimeMode BirthTimeMode { get; set; }
    public TimeOnly? BirthTime { get; set; }
    public TimeOnly? BirthTimeFrom { get; set; }
    public TimeOnly? BirthTimeTo { get; set; }
    public string? TimeAccuracyNote { get; set; }
    public string LocationName { get; set; } = "";
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public string TimezoneId { get; set; } = "";
    public string Gender { get; set; } = "";
    public string Occupation { get; set; } = "";
    public Relationship Relationship { get; set; }
    public string? CustomRelationship { get; set; }
    public long Revision { get; set; }
}

internal sealed class AccountReference { public Guid Id { get; set; } }

/// <summary>Owns schema myvedicstarvedicprofiles and its own migration history.
/// The account table is mapped read-only (ExcludeFromMigrations) with FK Restrict so
/// identity rows are never written from this module.</summary>
internal sealed class VedicProfilesDbContext(DbContextOptions<VedicProfilesDbContext> options) : DbContext(options)
{
    public const string Schema = "myvedicstarvedicprofiles";
    public DbSet<VedicProfileEntity> Profiles => Set<VedicProfileEntity>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema(Schema);
        model.Entity<AccountReference>(b =>
        {
            b.ToTable("User", "myvedicstar", table => table.ExcludeFromMigrations());
            b.HasKey(x => x.Id);
        });
        model.Entity<VedicProfileEntity>(b =>
        {
            b.ToTable("VedicProfiles", table =>
            {
                table.HasCheckConstraint("CK_VedicProfiles_BirthTimeMode", "\"BirthTimeMode\" IN (0,1,2)");
                table.HasCheckConstraint("CK_VedicProfiles_Relationship", "\"Relationship\" BETWEEN 0 AND 7");
                table.HasCheckConstraint("CK_VedicProfiles_Gender", "\"Gender\" <> ''");
                table.HasCheckConstraint("CK_VedicProfiles_Coordinates", "\"Latitude\" BETWEEN -90 AND 90 AND \"Longitude\" BETWEEN -180 AND 180");
                table.HasCheckConstraint("CK_VedicProfiles_Times",
                    "((\"BirthTimeMode\" = 0 AND \"BirthTime\" IS NOT NULL AND \"BirthTimeFrom\" = \"BirthTime\" AND \"BirthTimeTo\" = \"BirthTime\")" +
                    " OR (\"BirthTimeMode\" = 1 AND \"BirthTime\" IS NULL AND \"BirthTimeFrom\" IS NOT NULL AND \"BirthTimeTo\" IS NOT NULL AND \"BirthTimeTo\" >= \"BirthTimeFrom\")" +
                    " OR (\"BirthTimeMode\" = 2 AND \"BirthTime\" IS NULL AND \"BirthTimeFrom\" IS NULL AND \"BirthTimeTo\" IS NULL))");
                table.HasCheckConstraint("CK_VedicProfiles_CustomRelationship",
                    "((\"Relationship\" = 7 AND \"CustomRelationship\" IS NOT NULL AND \"CustomRelationship\" <> '')" +
                    " OR (\"Relationship\" <> 7 AND \"CustomRelationship\" IS NULL))");
                table.HasCheckConstraint("CK_VedicProfiles_Revision", "\"Revision\" > 0");
            });
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Name).HasMaxLength(256).IsRequired();
            b.Property(x => x.DateOfBirth).HasColumnType("date");
            b.Property(x => x.BirthTime).HasColumnType("time without time zone");
            b.Property(x => x.BirthTimeFrom).HasColumnType("time without time zone");
            b.Property(x => x.BirthTimeTo).HasColumnType("time without time zone");
            b.Property(x => x.TimeAccuracyNote).HasMaxLength(2000);
            b.Property(x => x.LocationName).HasMaxLength(512).IsRequired();
            b.Property(x => x.Latitude).HasPrecision(9, 6);
            b.Property(x => x.Longitude).HasPrecision(9, 6);
            b.Property(x => x.TimezoneId).HasMaxLength(128).IsRequired();
            b.Property(x => x.Gender).HasMaxLength(128).IsRequired();
            b.Property(x => x.Occupation).HasMaxLength(256).IsRequired();
            b.Property(x => x.CustomRelationship).HasMaxLength(256);
            b.Property(x => x.Revision).IsConcurrencyToken();
            b.HasOne<AccountReference>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => x.UserId);
            b.HasIndex(x => x.UserId).HasDatabaseName("UX_VedicProfiles_Self").IsUnique()
                .HasFilter("\"Relationship\" = 0");
        });
    }
}

internal sealed class VedicProfilesDesignTimeFactory : IDesignTimeDbContextFactory<VedicProfilesDbContext>
{
    public VedicProfilesDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<VedicProfilesDbContext>()
        .UseNpgsql("Host=localhost;Database=myvedicstar_design_time",
            b => b.MigrationsHistoryTable("__EFMigrationsHistory", VedicProfilesDbContext.Schema)).Options);
}
