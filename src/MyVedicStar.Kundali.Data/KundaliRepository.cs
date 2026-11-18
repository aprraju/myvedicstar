using Microsoft.EntityFrameworkCore;
using MyVedicStar.Kundali.Business;
using MyVedicStar.Kundali.Shared;
using MyVedicStar.Shared.Exceptions;
using MyVedicStar.VedicProfiles.Shared;

namespace MyVedicStar.Kundali.Data;

/// <summary>Owner-scoped persistence. Every keyed query filters accountId AND profileId, so a foreign row is
/// invisible rather than merely forbidden. One aggregate per profile is protected by a unique index, which makes
/// concurrent first generations resolve to exactly one winner and a safe 409 for the loser.</summary>
internal sealed class KundaliRepository(KundaliDbContext context, TimeProvider clock) : IKundaliRepository
{
    private const string UniqueViolation = "23505";

    public async Task<KundaliView?> GetAsync(Guid accountId, Guid profileId, long? currentProfileRevision,
        CancellationToken token)
    {
        var aggregate = await context.Aggregates.AsNoTracking()
            .SingleOrDefaultAsync(x => x.UserId == accountId && x.ProfileId == profileId, token);
        if (aggregate is null) return null;

        KundaliRevisionSummary? current = null;
        var inputRevision = 0L;
        if (aggregate.ActiveRevisionId is { } activeId)
        {
            var revision = await context.Revisions.AsNoTracking()
                .SingleOrDefaultAsync(x => x.UserId == accountId && x.ProfileId == profileId && x.Id == activeId, token);
            if (revision is not null)
            {
                inputRevision = revision.ProfileRevision;
                current = ToSummary(revision, currentProfileRevision);
            }
        }

        // ProfileRevision falls back to the stored one when the caller could not read the profile: reporting a made-up
        // revision would be worse than reporting the last known value alongside the staleness the caller already has.
        return new KundaliView(ToContract(aggregate), current,
            currentProfileRevision ?? aggregate.ProfileRevision, inputRevision);
    }

    public async Task<KundaliView> EnsureAggregateWithRevisionAsync(Guid accountId, Guid profileId,
        KundaliInputSnapshot snapshot, KundaliProvenance provenance, CancellationToken token)
    {
        var now = clock.GetUtcNow();
        var aggregate = new KundaliAggregateEntity
        {
            Id = Guid.NewGuid(), UserId = accountId, ProfileId = profileId, Revision = 1,
            CreatedAtUtc = now, UpdatedAtUtc = now
        };
        var revision = NewRevision(aggregate.Id, accountId, profileId, 1, snapshot, provenance, now);
        aggregate.ActiveRevisionId = revision.Id;

        context.Aggregates.Add(aggregate);
        context.Revisions.Add(revision);
        context.InputSnapshots.Add(NewSnapshot(revision.Id, accountId, profileId, snapshot, now));

        try
        {
            await context.SaveChangesAsync(token);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // A concurrent first generation won the unique index. If its committed row is already visible, return it;
            // otherwise report a safe conflict so the client re-reads. Never assume the winner is readable here --
            // the losing transaction may commit after ours.
            context.ChangeTracker.Clear();
            return await GetAsync(accountId, profileId, snapshot.ProfileRevision, token)
                ?? throw new KundaliConflictException(KundaliErrors.AlreadyRunning);
        }
        return new KundaliView(ToContract(aggregate), ToSummary(revision, snapshot.ProfileRevision));
    }

    public async Task<KundaliView> AddRevisionAsync(Guid accountId, Guid profileId, long aggregateRevision,
        KundaliInputSnapshot snapshot, KundaliProvenance provenance, CancellationToken token)
    {
        var aggregate = await context.Aggregates
            .SingleOrDefaultAsync(x => x.UserId == accountId && x.ProfileId == profileId, token)
            ?? throw new ResourceNotFoundException(KundaliErrors.NotFound);
        if (aggregate.Revision != aggregateRevision || aggregateRevision == long.MaxValue)
            throw new KundaliConflictException(KundaliErrors.StaleSnapshot);

        var now = clock.GetUtcNow();
        var next = await context.Revisions.AsNoTracking()
            .Where(x => x.UserId == accountId && x.ProfileId == profileId)
            .MaxAsync(x => (long?)x.Revision, token) ?? 0;

        var revision = NewRevision(aggregate.Id, accountId, profileId, next + 1, snapshot, provenance, now);
        aggregate.Revision = aggregateRevision + 1;
        aggregate.ActiveRevisionId = revision.Id;
        aggregate.UpdatedAtUtc = now;

        context.Revisions.Add(revision);
        if (!await context.InputSnapshots.AsNoTracking().AnyAsync(
                x => x.UserId == accountId && x.ProfileId == profileId
                     && x.InputSnapshotHash == revision.InputSnapshotHash, token))
            context.InputSnapshots.Add(NewSnapshot(revision.Id, accountId, profileId, snapshot, now));

        try
        {
            await context.SaveChangesAsync(token);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new KundaliConflictException(KundaliErrors.StaleSnapshot);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            throw new KundaliConflictException(KundaliErrors.AlreadyRunning);
        }
        return new KundaliView(ToContract(aggregate), ToSummary(revision, snapshot.ProfileRevision));
    }

    public async Task<KundaliRevisionSummary> SaveSectionAsync(Guid accountId, Guid profileId, Guid revisionId,
        string section, string payloadJson, bool isFinal, string? failureReasonCode, CancellationToken token)
    {
        // Bound and name-check the payload before any database work, so an oversized or misnamed section cannot
        // half-write a revision.
        if (!KundaliSections.All.Contains(section, StringComparer.Ordinal))
            throw new ValidationException(KundaliErrors.InvalidGenerationRequest);
        if (payloadJson is null
            || System.Text.Encoding.UTF8.GetByteCount(payloadJson) > KundaliLimits.SectionPayloadBytes)
            throw new ValidationException(KundaliErrors.InvalidGenerationRequest);

        var revision = await context.Revisions
            .SingleOrDefaultAsync(x => x.UserId == accountId && x.ProfileId == profileId && x.Id == revisionId, token)
            ?? throw new ResourceNotFoundException(KundaliErrors.NotFound);

        // KUN-22: a late worker write for a revision the profile has already left is discarded, not applied. Failing the
        // write is the only way to guarantee an obsolete payload can never become readable.
        if (revision.SupersededByProfileRevision is not null)
            throw new KundaliConflictException(KundaliErrors.ObsoleteResult);

        // A failure marks the revision failed without registering its section as completed, so a section that did not
        // finish can never be reported as present. A terminal status is also authoritative in the other direction: a
        // late intermediate write must not resurrect a revision that already succeeded or failed.
        if (revision.Status is KundaliGenerationStatus.Succeeded or KundaliGenerationStatus.Failed && !isFinal)
            throw new KundaliConflictException(KundaliErrors.ObsoleteResult);

        var failed = isFinal && failureReasonCode is not null;
        if (!failed)
        {
            var column = SectionColumn(section);
            context.Entry(revision).Property(column).CurrentValue = payloadJson;
        }
        revision.UpdatedAtUtc = clock.GetUtcNow();

        if (isFinal && failureReasonCode is null)
        {
            revision.Status = KundaliGenerationStatus.Succeeded;
            revision.FailureReasonCode = null;
        }
        else if (isFinal)
        {
            // A failed regeneration keeps whatever sections already exist and is flagged; the prior valid revision
            // stays current and is marked stale, never deleted.
            revision.Status = KundaliGenerationStatus.Failed;
            revision.FailureReasonCode = failureReasonCode;
        }
        else if (revision.Status is not (KundaliGenerationStatus.Succeeded or KundaliGenerationStatus.Failed))
        {
            // Intermediate writes advance an in-flight revision only. A terminal status is authoritative and is
            // never downgraded back to Running by a late section write.
            revision.Status = KundaliGenerationStatus.Running;
            revision.IsPartial = true;
        }

        await context.SaveChangesAsync(token);
        // The revision's own profile revision is authoritative here: it is what the payload was computed against, and a
        // later edit moves the aggregate's value, not this row's history.
        return ToSummary(revision, revision.ProfileRevision);
    }

    public async Task<KundaliPreferences?> GetPreferencesAsync(Guid accountId, Guid profileId, CancellationToken token)
    {
        var entity = await context.Preferences.AsNoTracking()
            .SingleOrDefaultAsync(x => x.UserId == accountId && x.ProfileId == profileId, token);
        return entity is null ? null : new KundaliPreferences(entity.ChartStyle, entity.Revision, entity.UpdatedAtUtc);
    }

    public async Task<KundaliPreferences> SavePreferencesAsync(Guid accountId, Guid profileId, string chartStyle,
        long revision, CancellationToken token)
    {
        var entity = await context.Preferences
            .SingleOrDefaultAsync(x => x.UserId == accountId && x.ProfileId == profileId, token);
        var now = clock.GetUtcNow();
        if (entity is null)
        {
            if (revision != 0) throw new ValidationException(KundaliErrors.InvalidConfig);
            entity = new KundaliPreferenceEntity
            {
                Id = Guid.NewGuid(), UserId = accountId, ProfileId = profileId, ChartStyle = chartStyle,
                Revision = 1, UpdatedAtUtc = now
            };
            context.Preferences.Add(entity);
        }
        else
        {
            if (entity.Revision != revision || revision == long.MaxValue)
                throw new KundaliConflictException(KundaliErrors.StaleSnapshot);
            entity.ChartStyle = chartStyle;
            entity.Revision = revision + 1;
            entity.UpdatedAtUtc = now;
        }

        try { await context.SaveChangesAsync(token); }
        catch (DbUpdateConcurrencyException) { throw new KundaliConflictException(KundaliErrors.StaleSnapshot); }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Two first-time preference writes raced; the loser reports a conflict and the client re-reads.
            context.ChangeTracker.Clear();
            throw new KundaliConflictException(KundaliErrors.StaleSnapshot);
        }
        return new KundaliPreferences(entity.ChartStyle, entity.Revision, entity.UpdatedAtUtc);
    }


    public async Task<int> DeleteForProfileAsync(Guid accountId, Guid profileId, CancellationToken token)
    {
        // Ordered children-first. The aggregate row is removed last so that a partially-applied delete can never leave
        // a profile looking alive while its revisions are gone - the reverse order would.
        await using var transaction = await context.Database.BeginTransactionAsync(token);

        var revisions = await context.Revisions
            .Where(x => x.UserId == accountId && x.ProfileId == profileId).ExecuteDeleteAsync(token);
        await context.InputSnapshots
            .Where(x => x.UserId == accountId && x.ProfileId == profileId).ExecuteDeleteAsync(token);
        await context.Preferences
            .Where(x => x.UserId == accountId && x.ProfileId == profileId).ExecuteDeleteAsync(token);
        var aggregates = await context.Aggregates
            .Where(x => x.UserId == accountId && x.ProfileId == profileId).ExecuteDeleteAsync(token);

        await transaction.CommitAsync(token);

        // Revision count is not reported: only aggregates tell a caller whether the profile had Kundali data at all,
        // and mixing the two would make the number mean two different things depending on the profile's history.
        _ = revisions;
        return aggregates;
    }
    public async Task<KundaliStoredSections?> GetSectionsAsync(Guid accountId, Guid profileId, Guid revisionId,
        CancellationToken token)
    {
        var revision = await context.Revisions.AsNoTracking()
            .SingleOrDefaultAsync(x => x.UserId == accountId && x.ProfileId == profileId && x.Id == revisionId, token);
        return revision is null
            ? null
            : new KundaliStoredSections(revision.Id, revision.Revision, revision.ProfileRevision, revision.Status,
                revision.IsPartial, revision.OverviewJson, revision.ChartsJson, revision.DashasJson);
    }

    /// <summary>
    /// KUN-22 propagation, in one transaction: advance the profile revision (compare-and-swap), bump the aggregate
    /// revision, mark any in-flight revision superseded with a reason so a late worker result is visibly obsolete, and
    /// publish exactly one invalidation event. The event is written in the same transaction as the change, so a
    /// published event always describes a change that committed - never one that rolled back.
    /// </summary>
    public async Task<KundaliProfileChangeResult> ApplyProfileChangeAsync(Guid accountId, Guid profileId,
        long oldRevision, long newRevision, string changeKind, CancellationToken token)
    {
        var displayOnly = KundaliProfileChangeKinds.IsDisplayOnly(changeKind);
        var aggregate = await context.Aggregates
            .SingleOrDefaultAsync(x => x.UserId == accountId && x.ProfileId == profileId, token)
            ?? throw new ResourceNotFoundException(KundaliErrors.NotFound);

        // Idempotent on the target revision: a replayed or out-of-order delivery of an already-applied change is a
        // no-op, so it can never cancel work that a later revision legitimately started.
        if (aggregate.ProfileRevision >= newRevision)
            return new KundaliProfileChangeResult(profileId, aggregate.ProfileRevision, Applied: false, displayOnly,
                aggregate.Revision, null);

        // A display-only change is not a calculation event: the profile revision still advances (so the profile and the
        // aggregate cannot disagree) but no revision is abandoned and no recalculation is implied.
        var aggregateRevisionBefore = aggregate.Revision;
        var now = clock.GetUtcNow();
        aggregate.ProfileRevision = newRevision;
        if (!displayOnly)
        {
            aggregate.Revision = aggregate.Revision + 1;
            aggregate.UpdatedAtUtc = now;
        }

        Guid? abandoned = null;
        if (!displayOnly && aggregate.ActiveRevisionId is { } activeId)
        {
            var active = await context.Revisions
                .SingleOrDefaultAsync(x => x.UserId == accountId && x.ProfileId == profileId && x.Id == activeId, token);
            if (active is not null
                && active.Status is KundaliGenerationStatus.Pending or KundaliGenerationStatus.Running)
            {
                // Abandons the in-flight revision explicitly. The payload is left untouched and the row is not deleted:
                // history stays auditable and a late worker write is rejected by the supersession check below.
                active.Status = KundaliGenerationStatus.Failed;
                active.FailureReasonCode = KundaliFailureReasons.SupersededByProfileEdit;
                active.SupersededByProfileRevision = newRevision;
                active.IsPartial = false;
                active.UpdatedAtUtc = now;
                abandoned = active.Id;
            }

            // Sections of revisions that already succeeded stay readable as history; the supersession marker on the
            // active revision is what stops them being presented as current.
            if (active is not null && active.SupersededByProfileRevision is null)
                active.SupersededByProfileRevision = newRevision;
        }

        context.ProfileInvalidations.Add(new KundaliProfileInvalidationEntity
        {
            Id = Guid.NewGuid(), UserId = accountId, ProfileId = profileId, OldRevision = oldRevision,
            NewRevision = newRevision, ChangeKind = changeKind, CancelledRevisionId = abandoned, PublishedAtUtc = now
        });

        try
        {
            await context.SaveChangesAsync(token);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Two edits raced from the same base revision. The loser must not silently fold into the winner's change.
            context.ChangeTracker.Clear();
            throw new KundaliConflictException(KundaliErrors.StaleSnapshot);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // The unique (user, profile, new revision) index: the event was already published by a concurrent delivery.
            context.ChangeTracker.Clear();
            var published = await context.Aggregates.AsNoTracking()
                .SingleOrDefaultAsync(x => x.UserId == accountId && x.ProfileId == profileId, token);
            return new KundaliProfileChangeResult(profileId, published?.ProfileRevision ?? newRevision,
                Applied: false, displayOnly, published?.Revision ?? aggregateRevisionBefore, abandoned);
        }

        return new KundaliProfileChangeResult(profileId, newRevision, Applied: true, displayOnly,
            aggregate.Revision, abandoned);
    }

    private static KundaliResultRevisionEntity NewRevision(Guid aggregateId, Guid accountId, Guid profileId,
        long revision, KundaliInputSnapshot snapshot, KundaliProvenance provenance, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(), AggregateId = aggregateId, UserId = accountId, ProfileId = profileId,
        Revision = revision, Status = KundaliGenerationStatus.Pending,
        ProfileRevision = snapshot.ProfileRevision,
        InputSnapshotHash = snapshot.ComputeHash(),
        EngineVersion = provenance.EngineVersion, EphemerisVersion = provenance.EphemerisVersion,
        RuleSetVersion = provenance.RuleSetVersion, TimezoneDataVersion = provenance.TimezoneDataVersion,
        Ayanamsa = provenance.Ayanamsa, CreatedAtUtc = now, UpdatedAtUtc = now
    };

    private static KundaliInputSnapshotEntity NewSnapshot(Guid revisionId, Guid accountId, Guid profileId,
        KundaliInputSnapshot snapshot, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(), UserId = accountId, ProfileId = profileId, RevisionId = revisionId,
        InputSnapshotHash = snapshot.ComputeHash(), DateOfBirth = snapshot.DateOfBirth,
        BirthTimeMode = snapshot.BirthTimeMode, BirthTime = snapshot.BirthTime,
        BirthTimeFrom = snapshot.BirthTimeFrom, BirthTimeTo = snapshot.BirthTimeTo,
        Latitude = snapshot.Latitude, Longitude = snapshot.Longitude, TimezoneId = snapshot.TimezoneId,
        ProfileRevision = snapshot.ProfileRevision, CreatedAtUtc = now
    };

    private static string SectionColumn(string section) => section switch
    {
        KundaliSections.Overview => nameof(KundaliResultRevisionEntity.OverviewJson),
        KundaliSections.Charts => nameof(KundaliResultRevisionEntity.ChartsJson),
        KundaliSections.Planets => nameof(KundaliResultRevisionEntity.PlanetsJson),
        KundaliSections.Dashas => nameof(KundaliResultRevisionEntity.DashasJson),
        KundaliSections.Yogas => nameof(KundaliResultRevisionEntity.YogasJson),
        KundaliSections.Ashtakavarga => nameof(KundaliResultRevisionEntity.AshtakavargaJson),
        KundaliSections.Bala => nameof(KundaliResultRevisionEntity.BalaJson),
        KundaliSections.Transits => nameof(KundaliResultRevisionEntity.TransitsJson),
        KundaliSections.Interpretations => nameof(KundaliResultRevisionEntity.InterpretationsJson),
        _ => throw new ValidationException(KundaliErrors.InvalidGenerationRequest)
    };

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is Npgsql.PostgresException { SqlState: UniqueViolation };

    private static KundaliAggregate ToContract(KundaliAggregateEntity e) =>
        new(e.Id, e.ProfileId, e.ActiveRevisionId, e.Revision, e.CreatedAtUtc, e.UpdatedAtUtc);

    private static KundaliRevisionSummary ToSummary(KundaliResultRevisionEntity e, long? currentProfileRevision) => new(
        e.Id, e.Revision, e.Status, e.FailureReasonCode,
        new KundaliProvenance(e.EngineVersion, e.EphemerisVersion, e.RuleSetVersion, e.TimezoneDataVersion, e.Ayanamsa),
        e.InputSnapshotHash,
        // Staleness is computed from the profile revision as it is now, and from the supersession marker, never from the
        // cached copy of a result: a stale cache entry cannot make a stale revision look current (KUN-21/KUN-22).
        IsStale: e.SupersededByProfileRevision is not null
            || (currentProfileRevision is { } current && e.ProfileRevision != current),
        e.IsPartial, CompletedSections(e), e.CreatedAtUtc,
        SupersededByRevision: e.SupersededByProfileRevision);

    private static IReadOnlyList<string> CompletedSections(KundaliResultRevisionEntity e)
    {
        var completed = new List<string>();
        if (e.OverviewJson is not null) completed.Add(KundaliSections.Overview);
        if (e.ChartsJson is not null) completed.Add(KundaliSections.Charts);
        if (e.PlanetsJson is not null) completed.Add(KundaliSections.Planets);
        if (e.DashasJson is not null) completed.Add(KundaliSections.Dashas);
        if (e.YogasJson is not null) completed.Add(KundaliSections.Yogas);
        if (e.AshtakavargaJson is not null) completed.Add(KundaliSections.Ashtakavarga);
        if (e.BalaJson is not null) completed.Add(KundaliSections.Bala);
        if (e.TransitsJson is not null) completed.Add(KundaliSections.Transits);
        if (e.InterpretationsJson is not null) completed.Add(KundaliSections.Interpretations);
        return completed;
    }
}
