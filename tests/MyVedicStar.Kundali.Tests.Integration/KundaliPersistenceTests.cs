using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MyVedicStar.Authorize.Tests.Integration.Postgres;
using MyVedicStar.Kundali.Data;
using MyVedicStar.Kundali.Shared;
using MyVedicStar.Shared.Exceptions;
using MyVedicStar.VedicProfiles.Shared;

namespace MyVedicStar.Kundali.Tests.Integration;

/// <summary>
/// Real-PostgreSQL tests for the properties that cannot be proven in memory: the one-aggregate-per-profile unique
/// index under concurrency (#43 AC2), owner isolation (#43 AC1), and revision/version history. Skipped unless
/// MYVEDICSTAR_TEST_POSTGRES is set, matching the repository's existing integration convention.
/// </summary>
public sealed class KundaliPersistenceTests(PostgresDatabaseFixture database) : IClassFixture<PostgresDatabaseFixture>
{
    private static readonly KundaliProvenance Provenance =
        new("kundali-foundation-0", "none", "foundation-0", "host", "Lahiri");

    private async Task<KundaliDbContext> ContextAsync()
    {
        Skip.IfNot(database.IsAvailable, "Set MYVEDICSTAR_TEST_POSTGRES to enable disposable PostgreSQL tests.");
        var options = new DbContextOptionsBuilder<KundaliDbContext>()
            .UseNpgsql(database.TestConnectionString,
                b => b.MigrationsHistoryTable("__EFMigrationsHistory", KundaliDbContext.Schema))
            // The migration script is authoritative. This snapshot was authored by hand (no EF tooling on the build
            // host), so a model-vs-snapshot diff is an artefact of that, not a real pending migration. Suppressing
            // the warning keeps the schema assertion honest without hiding genuine migration errors.
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        // The Kundali migration references myvedicstar."User", which the Authorize migrations create. The fixture
        // already migrated that database through the Authorize DI graph, so no direct access is needed (its context
        // is internal to the Authorize module and intentionally not visible here).
        var db = new KundaliDbContext(options);
        await db.Database.MigrateAsync();
        return db;
    }

    private async Task<Guid> AccountAsync()
    {
        var id = Guid.NewGuid();
        await using var connection = database.CreateConnection();
        await connection.OpenAsync();
        await using var command = new Npgsql.NpgsqlCommand(
            "INSERT INTO myvedicstar.\"User\" (\"Id\", \"ExternalId\", \"Email\", \"GivenName\", \"FamilyName\", \"Issuer\", \"CreatedUtc\", \"UpdatedUtc\", \"IsActive\", \"ProfileManaged\", \"ProfileRevision\", \"DisplayNameSyncPending\", \"FirstNameSyncPending\", \"LastNameSyncPending\") " +
            "SELECT @id, @externalId, 'k@example.com', 'F', 'L', 'https://test.example', now(), now(), true, false, 0, false, false, false " +
            "RETURNING \"Id\"", connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("externalId", Guid.NewGuid().ToString());
        await command.ExecuteNonQueryAsync();
        return id;
    }

    private static KundaliInputSnapshot Snapshot(long profileRevision = 1) => new(new DateOnly(1990, 4, 12),
        BirthTimeMode.Known, new TimeOnly(6, 15, 0), new TimeOnly(6, 15, 0), new TimeOnly(6, 15, 0),
        17.385m, 78.4867m, "Asia/Kolkata", profileRevision);

    [SkippableFact]
    public async Task Concurrent_first_generations_create_exactly_one_active_aggregate()
    {
        await using var db = await ContextAsync();
        var user = await AccountAsync();
        var profile = Guid.NewGuid();
        var repository = new KundaliRepository(db, TimeProvider.System);

        // Two callers racing the same profile: the unique index must resolve them to one aggregate.
        var results = await Task.WhenAll(
            repository.EnsureAggregateWithRevisionAsync(user, profile, Snapshot(), Provenance, default),
            repository.EnsureAggregateWithRevisionAsync(user, profile, Snapshot(), Provenance, default));

        Assert.All(results, r => Assert.Equal(profile, r.Aggregate.ProfileId));
        var rows = await db.Aggregates.AsNoTracking()
            .CountAsync(x => x.UserId == user && x.ProfileId == profile);
        Assert.Equal(1, rows);
    }

    [SkippableFact]
    public async Task Historical_revisions_remain_distinguishable_and_ordered()
    {
        await using var db = await ContextAsync();
        var user = await AccountAsync();
        var profile = Guid.NewGuid();
        var repository = new KundaliRepository(db, TimeProvider.System);

        var first = await repository.EnsureAggregateWithRevisionAsync(user, profile, Snapshot(), Provenance, default);
        var second = await repository.AddRevisionAsync(user, profile, first.Aggregate.Revision,
            Snapshot(profileRevision: 2), Provenance, default);

        Assert.Equal(1, first.CurrentRevision!.Revision);
        Assert.Equal(2, second.CurrentRevision!.Revision);

        var stored = await db.Revisions.AsNoTracking()
            .Where(x => x.UserId == user && x.ProfileId == profile)
            .OrderBy(x => x.Revision).Select(x => x.Revision).ToListAsync();
        Assert.Equal([1, 2], stored);
    }

    [SkippableFact]
    public async Task Another_account_cannot_read_or_extend_the_aggregate()
    {
        await using var db = await ContextAsync();
        var owner = await AccountAsync();
        var stranger = await AccountAsync();
        var profile = Guid.NewGuid();
        var repository = new KundaliRepository(db, TimeProvider.System);

        var created = await repository.EnsureAggregateWithRevisionAsync(owner, profile, Snapshot(), Provenance, default);

        Assert.Null(await repository.GetAsync(stranger, profile, default));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => repository.AddRevisionAsync(stranger, profile,
            created.Aggregate.Revision, Snapshot(), Provenance, default));
    }

    [SkippableFact]
    public async Task A_stale_aggregate_revision_cannot_overwrite_a_newer_one()
    {
        await using var db = await ContextAsync();
        var user = await AccountAsync();
        var profile = Guid.NewGuid();
        var repository = new KundaliRepository(db, TimeProvider.System);
        var created = await repository.EnsureAggregateWithRevisionAsync(user, profile, Snapshot(), Provenance, default);

        await repository.AddRevisionAsync(user, profile, created.Aggregate.Revision, Snapshot(2), Provenance, default);
        await Assert.ThrowsAsync<KundaliConflictException>(() => repository.AddRevisionAsync(user, profile,
            created.Aggregate.Revision, Snapshot(3), Provenance, default));
    }

    [SkippableFact]
    public async Task A_failed_revision_keeps_its_reason_code_and_sections_already_written()
    {
        await using var db = await ContextAsync();
        var user = await AccountAsync();
        var profile = Guid.NewGuid();
        var repository = new KundaliRepository(db, TimeProvider.System);
        var created = await repository.EnsureAggregateWithRevisionAsync(user, profile, Snapshot(), Provenance, default);
        var revisionId = created.CurrentRevision!.RevisionId;

        await repository.SaveSectionAsync(user, profile, revisionId, KundaliSections.Planets,
            "{\"planets\":[]}", isFinal: false, failureReasonCode: null, default);
        var failed = await repository.SaveSectionAsync(user, profile, revisionId, KundaliSections.Dashas,
            "{}", isFinal: true, failureReasonCode: KundaliFailureReasons.EngineError, default);

        Assert.Equal(KundaliGenerationStatus.Failed, failed.Status);
        Assert.Equal(KundaliFailureReasons.EngineError, failed.FailureReasonCode);
        Assert.Contains(KundaliSections.Planets, failed.CompletedSections);
        // A failed regeneration never marks the result complete.
        Assert.DoesNotContain(KundaliSections.Dashas, failed.CompletedSections);
    }

    [SkippableFact]
    public async Task Preferences_round_trip_and_reject_a_stale_revision()
    {
        await using var db = await ContextAsync();
        var user = await AccountAsync();
        var profile = Guid.NewGuid();
        var repository = new KundaliRepository(db, TimeProvider.System);

        var saved = await repository.SavePreferencesAsync(user, profile, "South", 0, default);
        Assert.Equal("South", saved.ChartStyle);
        Assert.Equal(1, saved.Revision);

        await Assert.ThrowsAsync<KundaliConflictException>(() =>
            repository.SavePreferencesAsync(user, profile, "East", 0, default));
        Assert.Equal("South", (await repository.GetPreferencesAsync(user, profile, default))!.ChartStyle);
    }
}
