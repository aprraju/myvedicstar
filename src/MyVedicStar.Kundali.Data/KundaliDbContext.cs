using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using MyVedicStar.Kundali.Shared;
using MyVedicStar.VedicProfiles.Shared;

namespace MyVedicStar.Kundali.Data;

internal sealed class KundaliAggregateEntity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid ProfileId { get; set; }
    public Guid? ActiveRevisionId { get; set; }
    public long Revision { get; set; }

    /// <summary>The Vedic Profile revision this aggregate's results are judged against (KUN-22). Advanced by a profile
    /// edit, never by a generation: a generation records the revision it consumed, it does not move the profile on.
    /// </summary>
    public long ProfileRevision { get; set; }

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

    /// <summary>The profile revision this result was computed from, copied from the input snapshot so an obsolete result
    /// never has to be joined to prove it is obsolete (KUN-22).</summary>
    public long ProfileRevision { get; set; }

    /// <summary>The revision that replaced this one after a profile edit. Non-null means the payload must not be shown as
    /// current; the row is kept so history and comparisons stay auditable.</summary>
    public long? SupersededByProfileRevision { get; set; }
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

/// <summary>
/// KUN-13 opt-in alert consent. One row per owner, profile and event type, so consent is granted and revoked per event
/// rather than as a single blanket switch - a reader who wants Saturn transits but not Sade Sati must be able to say so.
/// </summary>
/// <remarks>
/// <para><c>ConsentedAtUtc</c> is the record of when consent was given, and it is the reason this is a table of its own
/// rather than a JSON column on the preference row: an audit of when a person agreed to receive email is a different
/// obligation from a display preference, and it must survive a preference delete.</para>
/// <para>Delivery is email-only. <c>Channel</c> exists so an approved future channel is an added value, not a migration,
/// but the domain port validates the value against the approved set rather than accepting what a client sends.</para>
/// </remarks>
internal sealed class KundaliAlertPreferenceEntity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid ProfileId { get; set; }

    /// <summary>Event type id from the published catalogue, for example <c>sade-sati</c>.</summary>
    public string EventType { get; set; } = "";

    /// <summary>Delivery channel. Email only in this phase.</summary>
    public string Channel { get; set; } = "";

    public DateTimeOffset ConsentedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

/// <summary>
/// One stored Ashtakoota comparison between two profiles of the same account (KUN-15).
/// </summary>
/// <remarks>
/// <para><b>The primary key is the cache identity, not a surrogate.</b> A comparison is a pure function of
/// (pairKey, revisionA, revisionB, rulesVersion), so that tuple is the key. A surrogate id would allow two rows for
/// one identity, which is a second answer to the same question waiting to disagree with the first.</para>
/// <para><b><c>PairKey</c> is order-independent</b> - the two profile ids sorted ordinally and joined - so A-to-B and
/// B-to-A are one row. The stored <c>ProfileAId</c>/<c>ProfileBId</c> are in that sorted order too, so the row reads the
/// same way regardless of which direction was requested.</para>
/// <para><b>No TTL column.</b> Freshness is a property of the identity: the row is replaced when a revision or the rule
/// version moves, never because time passed. <c>CreatedAtUtc</c> exists for audit, not for expiry.</para>
/// <para>Both revisions are stored because a single "revision" field could not express that the PLUS sides disagree
/// about which snapshot was compared.</para>
/// </remarks>
internal sealed class KundaliAshtakootaComparisonEntity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    /// <summary>Order-independent key: both profile ids sorted ordinally and joined.</summary>
    public string PairKey { get; set; } = "";

    /// <summary>Lower sorting profile id; the pair's canonical first side.</summary>
    public Guid ProfileAId { get; set; }

    /// <summary>Higher sorting profile id; the pair's canonical second side.</summary>
    public Guid ProfileBId { get; set; }

    public long RevisionA { get; set; }
    public long RevisionB { get; set; }

    /// <summary>The Ashtakoota rule table version. Part of the identity: a table change invalidates the row.</summary>
    public string RulesVersion { get; set; } = "";

    /// <summary>The computed comparison, serialised. Read back and never recomputed from the row's columns.</summary>
    public string PayloadJson { get; set; } = "";

    public DateTimeOffset CreatedAtUtc { get; set; }
}

/// <summary>
/// KUN-17 (api#57) one report request. Written before generation and settled after it, so a caller who polls always
/// reads a row that exists rather than racing the renderer for one.
/// </summary>
/// <remarks>
/// <c>UserId</c> is on the row, not only on a join: report reads are owner-scoped, and a query that filtered through the
/// profile would make "not mine" and "does not exist" different amounts of work - a timing difference the
/// 404-indistinguishability requirement exists to prevent.
/// </remarks>
internal sealed class KundaliReportEntity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid ProfileId { get; set; }
    public string Variant { get; set; } = "";
    public string Sections { get; set; } = "";
    public string Status { get; set; } = "";
    public long SourceRevision { get; set; }
    public DateTimeOffset RequestedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public string? DocumentReference { get; set; }
    public string? FailuresJson { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

internal sealed class AccountReference { public Guid Id { get; set; } }

/// <summary>
/// One transactionally published invalidation event per profile revision change (KUN-22). Written in the same
/// transaction as the revision bump, so a published event always describes a change that actually committed. The payload
/// deliberately carries ids, revisions and a change kind only - never birth data, a name, coordinates or settings
/// values.
/// </summary>
internal sealed class KundaliProfileInvalidationEntity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid ProfileId { get; set; }
    public long OldRevision { get; set; }
    public long NewRevision { get; set; }
    public string ChangeKind { get; set; } = "";
    public Guid? CancelledRevisionId { get; set; }
    public DateTimeOffset PublishedAtUtc { get; set; }
}

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
    public DbSet<KundaliProfileInvalidationEntity> ProfileInvalidations => Set<KundaliProfileInvalidationEntity>();
    public DbSet<KundaliAlertPreferenceEntity> AlertPreferences => Set<KundaliAlertPreferenceEntity>();
    public DbSet<KundaliAshtakootaComparisonEntity> Comparisons => Set<KundaliAshtakootaComparisonEntity>();
    public DbSet<KundaliReportEntity> Reports => Set<KundaliReportEntity>();

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
                table.HasCheckConstraint("CK_KundaliAggregates_Revision", @"""""Revision"""" >= 0"));
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Revision).IsConcurrencyToken();
            // The profile revision is a compare-and-swap target for KUN-22: a second edit computed from the same base
            // revision must fail rather than overwrite the first one's invalidation.
            b.Property(x => x.ProfileRevision).IsConcurrencyToken().HasDefaultValue(0L);
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
                table.HasCheckConstraint("CK_KundaliRevisions_Status", @"""""Status"""" BETWEEN 0 AND 3");
                table.HasCheckConstraint("CK_KundaliRevisions_Revision", @"""""Revision"""" > 0");
                // A failed revision must carry a reason code and a succeeded one must not, so a failure is never
                // presented without an explanation and a success never carries a stale one.
                table.HasCheckConstraint("CK_KundaliRevisions_FailureReason",
                    @"((""""Status"""" = 3 AND """"FailureReasonCode"""" IS NOT NULL) OR (""""Status"""" <> 3 AND """"FailureReasonCode"""" IS NULL))");
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
            b.Property(x => x.ProfileRevision).HasDefaultValue(0L);
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
                table.HasCheckConstraint("CK_KundaliPreferences_Revision", @"""""Revision"""" >= 0"));
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.ChartStyle).HasMaxLength(32).IsRequired();
            b.Property(x => x.Revision).IsConcurrencyToken();
            b.HasOne<AccountReference>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.UserId, x.ProfileId }).IsUnique()
                .HasDatabaseName("UX_KundaliPreferences_UserProfile");
        });

        model.Entity<KundaliProfileInvalidationEntity>(b =>
        {
            b.ToTable("KundaliProfileInvalidations", table =>
            {
                table.HasCheckConstraint("CK_KundaliProfileInvalidations_Revision",
                    "\"Revision\" > 0 AND \"NewRevision\" > \"OldRevision\"");
                table.HasCheckConstraint("CK_KundaliProfileInvalidations_ChangeKind",
                    "\"ChangeKind\" IN ('display', 'birth-input', 'calculation-settings')\"");
            });
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.ChangeKind).HasMaxLength(32).IsRequired();
            b.HasOne<AccountReference>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            // Exactly one event per profile revision, enforced by the database: a duplicated or replayed delivery cannot
            // produce a second event, so consumers can be idempotent on the revision and still be correct.
            b.HasIndex(x => new { x.UserId, x.ProfileId, x.NewRevision }).IsUnique()
                .HasDatabaseName("UX_KundaliProfileInvalidations_UserProfileRevision");
        });

        model.Entity<KundaliReportEntity>(b =>
        {
            b.ToTable("KundaliReports", table =>
            {
                // The closed vocabularies, enforced at the last line of defence: a row that violates the contract cannot
                // exist even if a future writer skips the business-layer validator.
                table.HasCheckConstraint("CK_KundaliReports_Variant", "\"Variant\" IN ('short', 'detailed')");
                table.HasCheckConstraint("CK_KundaliReports_Status",
                    "\"Status\" IN ('queued', 'running', 'ready', 'failed')");
                table.HasCheckConstraint("CK_KundaliReports_SourceRevision", "\"SourceRevision\" > 0");
                table.HasCheckConstraint("CK_KundaliReports_CompletedAt",
                    "(\"Status\" IN ('ready', 'failed') AND \"CompletedAtUtc\" IS NOT NULL) OR "
                    + "(\"Status\" NOT IN ('ready', 'failed') AND \"CompletedAtUtc\" IS NULL)");
            });
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Variant).HasMaxLength(16).IsRequired();
            b.Property(x => x.Sections).HasMaxLength(256).IsRequired();
            b.Property(x => x.Status).HasMaxLength(16).IsRequired();
            b.Property(x => x.DocumentReference).HasMaxLength(512);
            b.Property(x => x.SourceRevision).HasDefaultValue(0L);
            b.HasOne<AccountReference>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);

            // Owner-scoped reads are the only reads this table serves, so the index leads with the owner.
            b.HasIndex(x => new { x.UserId, x.ProfileId, x.RequestedAtUtc })
                .HasDatabaseName("IX_KundaliReports_UserProfileRequested");

            // api#57: one report per (owner, profile, revision). A re-request returns the existing record instead of
            // starting a second render.
            b.HasIndex(x => new { x.UserId, x.ProfileId, x.SourceRevision }).IsUnique()
                .HasDatabaseName("UX_KundaliReports_UserProfileRevision");
        });

        model.Entity<KundaliAlertPreferenceEntity>(b =>
        {
            b.ToTable("KundaliAlertPreferences", table => table.HasCheckConstraint(
                "CK_KundaliAlertPreferences_Channel", @"""Channel"" IN ('email')"));
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.EventType).HasMaxLength(64).IsRequired();
            b.Property(x => x.Channel).HasMaxLength(32).IsRequired();
            b.HasOne<AccountReference>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);

            // The architect's uniqueness rule, and the reason a repeated PUT cannot create duplicate subscriptions:
            // the database rejects the second row, and the repository upserts instead of relying on a read-then-write
            // race that two concurrent requests would win together.
            b.HasIndex(x => new { x.UserId, x.ProfileId, x.EventType }).IsUnique()
                .HasDatabaseName("UX_KundaliAlertPreferences_UserProfileEvent");
        });

        model.Entity<KundaliAshtakootaComparisonEntity>(b =>
        {
            b.ToTable("KundaliAshtakootaComparisons", table =>
            {
                table.HasCheckConstraint("CK_KundaliAshtakootaComparisons_Revisions",
                    "\"RevisionA\" >= 0 AND \"RevisionB\" >= 0");
                // The two sides must be distinct profiles: a self-comparison is a 400 at the edge and must never be a
                // storable row, or a later read could serve it as a legitimate result.
                table.HasCheckConstraint("CK_KundaliAshtakootaComparisons_DistinctProfiles",
                    "\"ProfileAId\" <> \"ProfileBId\"");
                // And the pair is stored in sorted order, so the same pair cannot occupy two rows.
                table.HasCheckConstraint("CK_KundaliAshtakootaComparisons_SortedPair",
                    "\"ProfileAId\" < \"ProfileBId\"");
            });
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.PairKey).HasMaxLength(80).IsRequired();
            b.Property(x => x.RulesVersion).HasMaxLength(64).IsRequired();
            b.Property(x => x.PayloadJson).IsRequired();
            b.HasOne<AccountReference>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);

            // The cache identity, enforced by the database. A concurrent pair of requests computes the same answer twice
            // and one wins the insert; neither can create a second row that would later disagree.
            b.HasIndex(x => new { x.UserId, x.PairKey, x.RevisionA, x.RevisionB, x.RulesVersion }).IsUnique()
                .HasDatabaseName("UX_KundaliAshtakootaComparisons_Identity");
        });
    }
}

internal sealed class KundaliDesignTimeFactory : IDesignTimeDbContextFactory<KundaliDbContext>
{
    public KundaliDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<KundaliDbContext>()
        .UseNpgsql("Host=localhost;Database=myvedicstar_design_time",
            b => b.MigrationsHistoryTable("__EFMigrationsHistory", KundaliDbContext.Schema)).Options);
}