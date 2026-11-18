using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyVedicStar.Authorize.Data.Users;
using MyVedicStar.Authorize.Tests.Integration.Postgres;
using MyVedicStar.Kundali.Data;
using MyVedicStar.Kundali.Shared;
using MyVedicStar.Shared.Exceptions;
using MyVedicStar.VedicProfiles.Shared;

namespace MyVedicStar.Kundali.Tests.Integration;

/// <summary>
/// KUN-22 propagation against real PostgreSQL: the properties here are about a single transaction and a unique index, so
/// an in-memory substitute cannot prove them. Skipped unless MYVEDICSTAR_TEST_POSTGRES is set, matching the module's
/// existing convention.
/// </summary>
public sealed class KundaliProfileRevisionTests(PostgresDatabaseFixture database) : IClassFixture<PostgresDatabaseFixture>
{
    private static readonly KundaliProvenance Provenance =
        new("kundali-foundation-0", "none", "foundation-0", "host", "Lahiri");

    private async Task<KundaliDbContext> ContextAsync()
    {
        Skip.IfNot(database.IsAvailable, "Set MYVEDICSTAR_TEST_POSTGRES to enable disposable PostgreSQL tests.");
        var options = new DbContextOptionsBuilder<KundaliDbContext>()
            .UseNpgsql(database.TestConnectionString,
                b => b.MigrationsHistoryTable("__EFMigrationsHistory", KundaliDbContext.Schema))
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning))
            .Options;
        var db = new KundaliDbContext(options);
        await db.Database.MigrateAsync();
        return db;
    }

    private async Task<Guid> AccountAsync()
    {
        await using var scope = database.Services.CreateAsyncScope();
        var user = await scope.ServiceProvider.GetRequiredService<IUserRepository>()
            .UpsertAsync(new(Guid.NewGuid().ToString(), "k@example.com", "First", "Last", "https://test.example"),
                default);
        return user.Id;
    }

    private static KundaliInputSnapshot Snapshot(long profileRevision = 1) => new(new DateOnly(1990, 4, 12),
        BirthTimeMode.Known, new TimeOnly(6, 15, 0), new TimeOnly(6, 15, 0), new TimeOnly(6, 15, 0),
        17.385m, 78.4867m, "Asia/Kolkata", profileRevision);

    [SkippableFact]
    public async Task A_birth_input_edit_advances_the_revision_and_abandons_the_in_flight_revision()
    {
        await using var db = await ContextAsync();
        var user = await AccountAsync();
        var profile = Guid.NewGuid();
        var repository = new KundaliRepository(db, TimeProvider.System);
        var created = await repository.EnsureAggregateWithRevisionAsync(user, profile, Snapshot(1), Provenance, default);

        var change = await repository.ApplyProfileChangeAsync(user, profile, 1, 2,
            KundaliProfileChangeKinds.BirthInput, default);

        Assert.True(change.Applied);
        Assert.False(change.DisplayOnly);
        Assert.Equal(2, change.ProfileRevision);
        Assert.Equal(created.CurrentRevision!.RevisionId, change.AbandonedRevisionId);

        // The abandoned revision is failed with a stable reason and marked superseded: never shown as running or current.
        var abandoned = await db.Revisions.AsNoTracking().SingleAsync(x => x.Id == change.AbandonedRevisionId);
        Assert.Equal(KundaliGenerationStatus.Failed, abandoned.Status);
        Assert.Equal(KundaliFailureReasons.SupersededByProfileEdit, abandoned.FailureReasonCode);
        Assert.Equal(2, abandoned.SupersededByProfileRevision);
    }

    [SkippableFact]
    public async Task Exactly_one_invalidation_event_is_published_per_revision()
    {
        await using var db = await ContextAsync();
        var user = await AccountAsync();
        var profile = Guid.NewGuid();
        var repository = new KundaliRepository(db, TimeProvider.System);
        await repository.EnsureAggregateWithRevisionAsync(user, profile, Snapshot(1), Provenance, default);

        await repository.ApplyProfileChangeAsync(user, profile, 1, 2, KundaliProfileChangeKinds.BirthInput, default);
        // A replayed delivery of the same change publishes nothing further.
        var replay = await repository.ApplyProfileChangeAsync(user, profile, 1, 2,
            KundaliProfileChangeKinds.BirthInput, default);

        Assert.False(replay.Applied);
        var events = await db.ProfileInvalidations.AsNoTracking()
            .Where(x => x.UserId == user && x.ProfileId == profile).ToListAsync();
        Assert.Single(events);
        Assert.Equal(1, events[0].OldRevision);
        Assert.Equal(2, events[0].NewRevision);
    }

    [SkippableFact]
    public async Task A_display_only_change_advances_the_revision_without_a_new_kundali_revision()
    {
        await using var db = await ContextAsync();
        var user = await AccountAsync();
        var profile = Guid.NewGuid();
        var repository = new KundaliRepository(db, TimeProvider.System);
        var created = await repository.EnsureAggregateWithRevisionAsync(user, profile, Snapshot(1), Provenance, default);

        var change = await repository.ApplyProfileChangeAsync(user, profile, 1, 2,
            KundaliProfileChangeKinds.Display, default);

        Assert.True(change.DisplayOnly);
        Assert.Null(change.AbandonedRevisionId);
        Assert.Equal(created.Aggregate.Revision, change.AggregateRevision);
        // The in-flight revision keeps running: a rename does not invalidate a calculation.
        var revision = await db.Revisions.AsNoTracking().SingleAsync(x => x.Id == created.CurrentRevision!.RevisionId);
        Assert.Equal(KundaliGenerationStatus.Pending, revision.Status);
        Assert.Null(revision.SupersededByProfileRevision);
    }

    [SkippableFact]
    public async Task A_late_section_write_for_a_superseded_revision_is_rejected()
    {
        await using var db = await ContextAsync();
        var user = await AccountAsync();
        var profile = Guid.NewGuid();
        var repository = new KundaliRepository(db, TimeProvider.System);
        var created = await repository.EnsureAggregateWithRevisionAsync(user, profile, Snapshot(1), Provenance, default);
        var revisionId = created.CurrentRevision!.RevisionId;
        await repository.ApplyProfileChangeAsync(user, profile, 1, 2, KundaliProfileChangeKinds.BirthInput, default);

        // The worker finished after the edit. Its payload must not become readable.
        await Assert.ThrowsAsync<KundaliConflictException>(() => repository.SaveSectionAsync(user, profile,
            revisionId, KundaliSections.Overview, "{\"stale\":true}", isFinal: true, failureReasonCode: null,
            default));

        var stored = await db.Revisions.AsNoTracking().SingleAsync(x => x.Id == revisionId);
        Assert.Null(stored.OverviewJson);
    }

    [SkippableFact]
    public async Task A_result_from_an_older_profile_revision_reads_back_as_stale_not_current()
    {
        await using var db = await ContextAsync();
        var user = await AccountAsync();
        var profile = Guid.NewGuid();
        var repository = new KundaliRepository(db, TimeProvider.System);
        var created = await repository.EnsureAggregateWithRevisionAsync(user, profile, Snapshot(1), Provenance, default);
        await repository.SaveSectionAsync(user, profile, created.CurrentRevision!.RevisionId,
            KundaliSections.Overview, "{}", isFinal: true, failureReasonCode: null, default);
        await repository.ApplyProfileChangeAsync(user, profile, 1, 2, KundaliProfileChangeKinds.BirthInput, default);

        // The profile is now at revision 2; the stored result was computed from 1.
        var view = await repository.GetAsync(user, profile, 2, default);

        Assert.NotNull(view);
        Assert.True(view!.CurrentRevision!.IsStale);
        Assert.NotEqual(2, view.InputRevision);
    }
}
