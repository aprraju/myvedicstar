using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using MyVedicStar.Kundali.Shared;

namespace MyVedicStar.Kundali.Data;

internal sealed class KundaliAggregateEntity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid ProfileId { get; set; }
    public Guid? ActiveRevisionId { get; set; }
    public long Revision { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

internal sealed class KundaliResultRevisionEntity
{
    public Guid Id { get; set; }
    public Guid AggregateId { get; set; }
    public Guid UserId { get; set; }
    public Guid ProfileId { get; set; }
    public long Revision { get; set; }
    public KundaliGenerationStatus Status { get; set; }
    public string? FailureReasonCode { get; set; }
    public string InputSnapshotHash { get; set; } = "";
    public string EngineVersion { get; set; } = "";
    public string EphemerisVersion { get; set; } = "";
    public string RuleSetVersion { get; set; } = "";
    public string TimezoneDataVersion { get; set; } = "";
    public string Ayanamsa { get; set; } = "";
    public bool IsPartial { get; set; }
    public string? OverviewJson { get; set; }
    public string? ChartsJson { get; set; }
    public string? PlanetsJson { get; set; }
    public string? DashasJson { get; set; }
    public string? YogasJson { get; set; }
    public string? AshtakavargaJson { get; set; }
    public string? BalaJson { get; set; }
    public string? TransitsJson { get; set; }
    public string? InterpretationsJson { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

internal sealed class KundaliInputSnapshotEntity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid ProfileId { get; set; }
    public Guid RevisionId { get; set; }
    public string InputSnapshotHash { get; set; } = "";
    public DateOnly DateOfBirth { get; set; }
    public BirthTimeMode BirthTimeMode { get; set; }
    public TimeOnly? BirthTime { get; set; }
    public TimeOnly? BirthTimeFrom { get; set; }
    public TimeOnly? BirthTimeTo { get; set; }
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public string TimezoneId { get; set; } = "";
    public long ProfileRevision { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}

internal sealed class KundaliPreferenceEntity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid ProfileId { get; set; }
    public string ChartStyle { get; set; } = "";
    public long Revision { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

internal sealed class AccountReference { public Guid Id { get; set; } }

/// <summary>Owns schema myvedicstarkundali and its own migration history. The account table is mapped read-only so
/// identity rows are never written from this module; the Vedic Profiles table is deliberately not mapped, because
/// profile deletion cleanup belongs to KUN-23 (#33), not to an implicit cascade here.</summary>
internal sealed class KundaliDbContext(DbContextOptions<KundaliDbContext> options) : DbContext(options)
{
    public const string Schema = "myvedicstarkundali";

    public DbSet<KundaliAggregateEntity> Aggregates => Set<KundaliAggregateEntity>();
    public DbSet<KundaliResultRevisionEntity> Revisions => Set<KundaliResultRevisionEntity>();
    public DbSet<KundaliInputSnapshotEntity> InputSnapshots => Set<KundaliInputSnapshotEntity>();
    public DbSet<KundaliPreferenceEntity> Preferences => Set<KundaliPreferenceEntity>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema(Schema);
        model.Entity<AccountReference>(b =>
        {
            b.ToTable("User", "myvedicstar", table => table.ExcludeFromMigrations());
            b.HasKey(x => x.Id);
        });

        model.Entity<KundaliAggregateEntity>(b =>
        {
            b.ToTable("KundaliAggregates", table =>
                table.HasCheckConstraint("CK_KundaliAggregates_Revision", '"Revision" >= 0'));
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Revision).IsConcurrencyToken();
            b.HasOne<AccountReference>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            // One active Kundali per profile (KUN-03). The loser of a concurrent first generation gets a unique
            // violation, which the repository surfaces as a safe 409 so the client retries.
            b.HasIndex(x => new { x.UserId, x.ProfileId }).IsUnique()
                .HasDatabaseName("UX_KundaliAggregates_UserProfile");
        });

        model.Entity<KundaliResultRevisionEntity>(b =>
        {
            b.ToTable("KundaliResultRevisions", table =>
            {
                table.HasCheckConstraint("CK_KundaliRevisions_Status", '"Status" BETWEEN 0 AND 3');
                table.HasCheckConstraint("CK_KundaliRevisions_Revision", '"Revision" > 0');
                // A failed revision must carry a reason code and a succeeded one must not, so a failure is never
                // presented without an explanation and a success never carries a stale one.
                table.HasCheckConstraint("CK_KundaliRevisions_FailureReason",
                    "((\"Status\" = 3 AND \"FailureReasonCode\" IS NOT NULL) OR (\"Status\" <> 3 AND \"FailureReasonCode\" IS NULL))");
            });
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.InputSnapshotHash).HasMaxLength(64).IsRequired();
            b.Property(x => x.EngineVersion).HasMaxLength(64).IsRequired();
            b.Property(x => x.EphemerisVersion).HasMaxLength(64).IsRequired();
            b.Property(x => x.RuleSetVersion).HasMaxLength(64).IsRequired();
            b.Property(x => x.TimezoneDataVersion).HasMaxLength(64).IsRequired();
            b.Property(x => x.Ayanamsa).HasMaxLength(32).IsRequired();
            b.Property(x => x.FailureReasonCode).HasMaxLength(64);
            b.Property(x => x.Revision).IsConcurrencyToken();
            b.HasOne<AccountReference>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.UserId, x.ProfileId, x.Revision }).IsUnique()
                .HasDatabaseName("UX_KundaliRevisions_UserProfileRevision");
        });

        model.Entity<KundaliInputSnapshotEntity>(b =>
        {
            b.ToTable("KundaliInputSnapshots");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.InputSnapshotHash).HasMaxLength(64).IsRequired();
            b.Property(x => x.DateOfBirth).HasColumnType("date");
            b.Property(x => x.BirthTime).HasColumnType("time without time zone");
            b.Property(x => x.BirthTimeFrom).HasColumnType("time without time zone");
            b.Property(x => x.BirthTimeTo).HasColumnType("time without time zone");
            b.Property(x => x.Latitude).HasPrecision(9, 6);
            b.Property(x => x.Longitude).HasPrecision(9, 6);
            b.Property(x => x.TimezoneId).HasMaxLength(128).IsRequired();
            b.HasOne<AccountReference>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.UserId, x.ProfileId, x.InputSnapshotHash }).IsUnique()
                .HasDatabaseName("UX_KundaliInputSnapshots_UserProfileHash");
        });

        model.Entity<KundaliPreferenceEntity>(b =>
        {
            b.ToTable("KundaliPreferences", table =>
                table.HasCheckConstraint("CK_KundaliPreferences_Revision", '"Revision" >= 0'));
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.ChartStyle).HasMaxLength(32).IsRequired();
            b.Property(x => x.Revision).IsConcurrencyToken();
            b.HasOne<AccountReference>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.UserId, x.ProfileId }).IsUnique()
                .HasDatabaseName("UX_KundaliPreferences_UserProfile");
        });
    }
}

internal sealed class KundaliDesignTimeFactory : IDesignTimeDbContextFactory<KundaliDbContext>
{
    public KundaliDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<KundaliDbContext>()
        .UseNpgsql("Host=localhost;Database=myvedicstar_design_time",
            b => b.MigrationsHistoryTable("__EFMigrationsHistory", KundaliDbContext.Schema)).Options);
}
