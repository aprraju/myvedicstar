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

    public async Task<KundaliView?> GetAsync(Guid accountId, Guid profileId, CancellationToken token)
    {
        var aggregate = await context.Aggregates.AsNoTracking()
            .SingleOrDefaultAsync(x => x.UserId == accountId && x.ProfileId == profileId, token);
        if (aggregate is null) return null;

        KundaliRevisionSummary? current = null;
        if (aggregate.ActiveRevisionId is { } activeId)
        {
            var revision = await context.Revisions.AsNoTracking()
                .SingleOrDefaultAsync(x => x.UserId == accountId && x.ProfileId == profileId && x.Id == activeId, token);
            if (revision is not null) current = ToSummary(revision);
        }
        return new KundaliView(ToContract(aggregate), current);
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
            return await GetAsync(accountId, profileId, token)
                ?? throw new KundaliConflictException(KundaliErrors.AlreadyRunning);
        }
        return new KundaliView(ToContract(aggregate), ToSummary(revision));
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
        return new KundaliView(ToContract(aggregate), ToSummary(revision));
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

        // A failed write must not register its section as completed: a section that did not finish would otherwise
        // be reported as present and could be served as if it were valid.
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
        return ToSummary(revision);
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

    private static KundaliResultRevisionEntity NewRevision(Guid aggregateId, Guid accountId, Guid profileId,
        long revision, KundaliInputSnapshot snapshot, KundaliProvenance provenance, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(), AggregateId = aggregateId, UserId = accountId, ProfileId = profileId,
        Revision = revision, Status = KundaliGenerationStatus.Pending,
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

    private static KundaliRevisionSummary ToSummary(KundaliResultRevisionEntity e) => new(
        e.Id, e.Revision, e.Status, e.FailureReasonCode,
        new KundaliProvenance(e.EngineVersion, e.EphemerisVersion, e.RuleSetVersion, e.TimezoneDataVersion, e.Ayanamsa),
        e.InputSnapshotHash, false, e.IsPartial, CompletedSections(e), e.CreatedAtUtc);

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
