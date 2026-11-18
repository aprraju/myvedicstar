using System.Security.Claims;
using MyVedicStar.Kundali.Shared;
using MyVedicStar.VedicProfiles.Shared;
using MyVedicStar.Shared.Exceptions;

namespace MyVedicStar.Kundali.Business;

internal sealed class KundaliFacade(IKundaliProfileReader profiles, IKundaliRepository repository,
    KundaliValidator validator, KundaliEngineVersions versions, IKundaliAstronomyMetadataReader metadataReader,
    IKundaliCalculationEngine calculationEngine, TimeProvider clock) : IKundaliFacade
{
    public async Task<KundaliView> GetAsync(ClaimsPrincipal user, Guid profileId, CancellationToken token)
    {
        var accountId = await profiles.GetRegisteredAccountIdAsync(user, token);
        // Ownership is proved by reading the profile as this principal, not by trusting the path id: a foreign
        // profile must be indistinguishable from a missing one.
        _ = await ReadOwnedSnapshotAsync(user, profileId, token);
        return await repository.GetAsync(accountId, profileId, token) ?? EmptyView(profileId);
    }

    public async Task<KundaliView> GenerateAsync(ClaimsPrincipal user, Guid profileId, CancellationToken token)
    {
        var accountId = await profiles.GetRegisteredAccountIdAsync(user, token);
        var snapshot = await ReadOwnedSnapshotAsync(user, profileId, token);
        validator.ValidateSnapshot(snapshot);
        var provenance = await BuildProvenanceAsync(user, token);

        var existing = await repository.GetAsync(accountId, profileId, token);
        if (existing is null)
            return await repository.EnsureAggregateWithRevisionAsync(accountId, profileId, snapshot, provenance, token);

        // Repeated clicks for identical inputs must not enqueue twice: return the running revision untouched.
        if (existing.CurrentRevision is { Status: KundaliGenerationStatus.Pending or KundaliGenerationStatus.Running }
            && existing.CurrentRevision.InputSnapshotHash == snapshot.ComputeHash()
            && existing.CurrentRevision.Provenance == provenance)
            return existing;

        return await repository.AddRevisionAsync(accountId, profileId, existing.Aggregate.Revision,
            snapshot, provenance, token);
    }

    public async Task<KundaliView> RetryAsync(ClaimsPrincipal user, Guid profileId, CancellationToken token)
    {
        var accountId = await profiles.GetRegisteredAccountIdAsync(user, token);
        var snapshot = await ReadOwnedSnapshotAsync(user, profileId, token);
        validator.ValidateSnapshot(snapshot);
        var current = await repository.GetAsync(accountId, profileId, token) ?? EmptyView(profileId);
        var provenance = await BuildProvenanceAsync(user, token);

        // Retry re-uses the snapshot; only a failure is retryable. Success or in-flight work is not silently redone.
        if (current.CurrentRevision is not { Status: KundaliGenerationStatus.Failed })
            throw new KundaliConflictException(KundaliErrors.AlreadyRunning);

        return await repository.AddRevisionAsync(accountId, profileId, current.Aggregate.Revision, snapshot,
            provenance, token);
    }

    public async Task<KundaliPreferences> GetPreferencesAsync(ClaimsPrincipal user, Guid profileId,
        CancellationToken token)
    {
        var accountId = await profiles.GetRegisteredAccountIdAsync(user, token);
        _ = await ReadOwnedSnapshotAsync(user, profileId, token);
        return await repository.GetPreferencesAsync(accountId, profileId, token) ?? DefaultPreferences();
    }

    public async Task<KundaliPreferences> SavePreferencesAsync(ClaimsPrincipal user, Guid profileId,
        string chartStyle, long revision, CancellationToken token)
    {
        var accountId = await profiles.GetRegisteredAccountIdAsync(user, token);
        _ = await ReadOwnedSnapshotAsync(user, profileId, token);
        validator.ValidateChartStyle(chartStyle);
        if (revision < 0) throw new ValidationException(KundaliErrors.InvalidConfig);
        return await repository.SavePreferencesAsync(accountId, profileId, chartStyle, revision, token);
    }

    public KundaliCapabilityReport GetCapabilities() => KundaliCapabilities.Report();

    public async Task<KundaliOverview> GetOverviewAsync(ClaimsPrincipal user, Guid profileId, CancellationToken token)
    {
        var display = await ReadOwnedDisplayAsync(user, profileId, token);
        var metadata = await metadataReader.GetAsync(user, token);

        var unsupported = new List<KundaliUnsupportedField>();

        // The engine computes sun and moon signs when the birth time is exact; anything it withholds is reported
        // with the engine's own capability-specific reason rather than a blanket engine-unavailable.
        var engine = calculationEngine.Calculate(new KundaliEngineInput(
            display.BirthDate, display.BirthTimeMode, display.BirthTime, display.Latitude, display.Longitude,
            display.TimezoneId, metadata.Ayanamsa));

        // Ascendant stays withheld as unvalidated-reference even though the engine can compute it: no independent
        // reference exists to validate against, so it is published only when we can defend the number.
        AddUnsupported(unsupported, nameof(KundaliOverview.Ascendant),
            KundaliUnsupportedReasons.UnvalidatedReference);

        // Presenting placements needs divisional charts and shadbala, neither of which exists yet.
        AddUnsupported(unsupported, nameof(KundaliOverview.KeyPlacements),
            KundaliUnsupportedReasons.EngineUnavailable);

        // Anything the engine itself withheld (for example because the birth time is not exact) is surfaced with
        // its own reason, so a caller can distinguish an indeterminate value from an unimplemented one.
        foreach (var gap in engine.Gaps)
            AddUnsupported(unsupported, gap.Path, gap.Reason);

        return new KundaliOverview(
            profileId,
            ToBirthDetails(display),
            new KundaliOverviewConventions(metadata.EphemerisProvider, metadata.EphemerisVersion, metadata.Ayanamsa,
                metadata.HouseSystem, metadata.NodeType, metadata.Zodiac),
            Ascendant: null,
            MoonSign: engine.MoonSign is { } moon ? KundaliSigns.NameOf(moon) : null,
            SunSign: engine.SunSign is { } sun ? KundaliSigns.NameOf(sun) : null,
            CurrentDasha: CurrentDashaOf(engine, unsupported),
            KeyPlacements: [],
            Unsupported: unsupported);
    }


    /// <summary>Only Vimshottari is implemented. Yogini and Chara are named in the capability list and rejected here,
    /// rather than silently falling back to Vimshottari - returning the wrong system's dates would be far worse than
    /// an explicit refusal.</summary>
    private const string VimshottariSystem = "vimshottari";
    private const string YoginiSystem = "yogini";
    private const string CharaSystem = "chara";

    /// <summary>The systems this module will answer for. Chara is listed so a caller gets a capability-specific
    /// reason rather than a rejection, but its timeline is never published unvalidated.</summary>
    private static readonly string[] KnownSystems = [VimshottariSystem, YoginiSystem, CharaSystem];

    public async Task<KundaliDashaTimeline> GetDashasAsync(ClaimsPrincipal user, Guid profileId, string system,
        DateTimeOffset? at, CancellationToken token)
    {
        var requested = string.IsNullOrWhiteSpace(system) ? VimshottariSystem : system.Trim().ToLowerInvariant();

        // An unknown system is rejected, but a known-but-unavailable one is answered with a reason code: the two are
        // different claims and the caller must be able to tell "you asked for something that does not exist" from
        // "that system exists but is not yet proven".
        if (!KnownSystems.Contains(requested, StringComparer.Ordinal))
        {
            throw new ValidationException(
                "Unknown dasha system. Supported values are vimshottari, yogini and chara.");
        }

        var display = await ReadOwnedDisplayAsync(user, profileId, token);
        var metadata = await metadataReader.GetAsync(user, token);

        // Omitting "at" means server now. The resolved instant is echoed back, so a caller always knows which moment
        // the path describes rather than having to assume it matched its own clock.
        var queryAt = at ?? clock.GetUtcNow();

        var timeline = calculationEngine.CalculateDashaTimeline(new KundaliDashaQuery(
            new KundaliEngineInput(display.BirthDate, display.BirthTimeMode, display.BirthTime, display.Latitude,
                display.Longitude, display.TimezoneId, metadata.Ayanamsa),
            queryAt));

        var gaps = timeline.Gaps
            .Select(gap => new KundaliUnsupportedField(gap.Path, gap.Reason))
            .ToList();

        var (mahadashas, path) = requested switch
        {
            YoginiSystem => BuildYogini(timeline, queryAt, gaps),
            CharaSystem => BuildChara(timeline, gaps),
            _ => (timeline.Mahadashas.Select(ToDasha).ToArray(),
                timeline.PeriodPath.Select(level => new KundaliDasha(level.Lord, level.Level, level.StartUtc,
                    level.EndUtc)).ToArray())
        };

        return new KundaliDashaTimeline(
            requested,
            display.TimezoneId,
            timeline.MoonSiderealLongitude,
            queryAt,
            mahadashas,
            path,
            gaps.ToArray());
    }

    /// <summary>Gap paths used by the dasha section. Constants rather than literals so the reason and the field it
    /// describes cannot drift apart.</summary>
    private static class DashaPath
    {
        internal const string Mahadashas = "Mahadashas";
        internal const string PeriodPath = "PeriodPath";
        internal const string Yogini = "YoginiPeriods";
    }


    /// <summary>Yogini periods rendered as dasha entries; the path is the single period containing the instant, since
    /// Yogini sub-periods are not implemented.</summary>
    private static (IReadOnlyList<KundaliDasha> Periods, IReadOnlyList<KundaliDasha> Path) BuildYogini(
        KundaliEngineDashaTimeline timeline, DateTimeOffset queryAt, List<KundaliUnsupportedField> gaps)
    {
        if (timeline.YoginiPeriods is not { Count: > 0 } yogini)
        {
            // A null list means the engine withheld Yogini entirely and has already given its own reason; a present
            // but empty list would mean it returned nothing without explaining, which is also a gap we must state.
            if (timeline.YoginiPeriods is null &&
                !gaps.Any(gap => gap.Path == DashaPath.Yogini))
            {
                gaps.Add(new KundaliUnsupportedField(DashaPath.Yogini,
                    KundaliUnsupportedReasons.EngineUnavailable));
            }

            return ([], []);
        }

        var periods = yogini
            .Select(period => new KundaliDasha(period.Name, "Yogini", period.StartUtc, period.EndUtc))
            .ToArray();

        var current = yogini.FirstOrDefault(period => queryAt >= period.StartUtc && queryAt < period.EndUtc);

        // Yogini antara periods are a further level we have not built; say so rather than implying the mahadasha is
        // the whole answer.
        gaps.Add(new KundaliUnsupportedField(DashaPath.PeriodPath, KundaliUnsupportedReasons.EngineUnavailable));

        return (periods, current is null
            ? []
            : [new KundaliDasha(current.Name, "Yogini", current.StartUtc, current.EndUtc)]);
    }

    /// <summary>Chara is withheld: its period lengths need an ascendant, and the ascendant is itself unvalidated.</summary>
    private static (IReadOnlyList<KundaliDasha> Periods, IReadOnlyList<KundaliDasha> Path) BuildChara(
        KundaliEngineDashaTimeline timeline, List<KundaliUnsupportedField> gaps)
    {
        // The engine supplies the sign progression, but publishing it as Chara dasha would present an equal-duration
        // simplification as the classical result. Withheld, with the reason naming the real dependency.
        _ = timeline;
        gaps.Add(new KundaliUnsupportedField(DashaPath.Mahadashas, KundaliUnsupportedReasons.UnvalidatedReference));
        gaps.Add(new KundaliUnsupportedField(DashaPath.PeriodPath, KundaliUnsupportedReasons.UnvalidatedReference));
        return ([], []);
    }

    private static KundaliDasha ToDasha(KundaliEngineDasha period) =>

        new(period.Lord, "Mahadasha", period.StartUtc, period.EndUtc);


    /// <summary>
    /// The mahadasha containing now, or null when the engine supplied no sequence.
    /// </summary>
    /// <remarks>
    /// <para>An engine that withheld the sequence has already recorded its own reason code, so this adds nothing -
    /// duplicating the reason here would make the same gap appear twice with potentially different wording.</para>
    /// <para>When the sequence exists but now falls outside it, that is reported as its own gap rather than
    /// silently returning null: the caller must be able to tell "no dasha for this moment" from "no dashas at
    /// all".</para>
    /// </remarks>
    private KundaliDasha? CurrentDashaOf(KundaliEngineResult engine, List<KundaliUnsupportedField> unsupported)
    {
        if (engine.VimshottariMahadashas is not { Count: > 0 } periods)
            return null;

        var now = clock.GetUtcNow();

        // Half-open [start, end): consecutive periods share a boundary, and the boundary belongs to the later one, so
        // no instant is ever claimed by two dashas.
        foreach (var period in periods)
        {
            if (now >= period.StartUtc && now < period.EndUtc)
                return new KundaliDasha(period.Lord, "Mahadasha", period.StartUtc, period.EndUtc);
        }

        AddUnsupported(unsupported, nameof(KundaliOverview.CurrentDasha),
            KundaliUnsupportedReasons.RequiresExactBirthTime);
        return null;
    }
    /// <summary>Maps the profile's stored time mode to the wire modes, and carries the window only for a range.
    /// Absence is the contract, so nothing is ever substituted for a missing time.</summary>
    private static KundaliBirthDetails ToBirthDetails(KundaliProfileDisplay display) =>
        display.BirthTimeMode switch
        {
            BirthTimeMode.Known => new KundaliBirthDetails(display.Name, display.BirthDate,
                KundaliBirthTimeMode.Exact, display.BirthTime, null, null, display.PlaceName, display.Latitude,
                display.Longitude, display.TimezoneId),
            BirthTimeMode.Approximate => new KundaliBirthDetails(display.Name, display.BirthDate,
                KundaliBirthTimeMode.Approximate, null, display.BirthTimeFrom, display.BirthTimeTo, display.PlaceName,
                display.Latitude, display.Longitude, display.TimezoneId),
            _ => new KundaliBirthDetails(display.Name, display.BirthDate, KundaliBirthTimeMode.Unknown, null, null,
                null, display.PlaceName, display.Latitude, display.Longitude, display.TimezoneId)
        };

    private static void AddUnsupported(List<KundaliUnsupportedField> target, string path, string reason) =>
        target.Add(new KundaliUnsupportedField(path, reason));

    private async Task<KundaliProfileDisplay> ReadOwnedDisplayAsync(ClaimsPrincipal user, Guid profileId,
        CancellationToken token)
    {
        if (profileId == Guid.Empty) throw new ResourceNotFoundException(KundaliErrors.ProfileNotFound);
        return await profiles.GetDisplayAsync(user, profileId, token)
            ?? throw new ResourceNotFoundException(KundaliErrors.ProfileNotFound);
    }



    private async Task<KundaliInputSnapshot> ReadOwnedSnapshotAsync(ClaimsPrincipal user, Guid profileId,
        CancellationToken token)
    {
        if (profileId == Guid.Empty) throw new ResourceNotFoundException(KundaliErrors.ProfileNotFound);
        return await profiles.GetSnapshotAsync(user, profileId, token)
            ?? throw new ResourceNotFoundException(KundaliErrors.ProfileNotFound);
    }

    private async Task<KundaliProvenance> BuildProvenanceAsync(ClaimsPrincipal user, CancellationToken token)
    {
        var provenance = new KundaliProvenance(versions.Engine, versions.Ephemeris, versions.RuleSet,
            versions.TimezoneData, await profiles.GetAyanamsaAsync(user, token));
        validator.ValidateProvenance(provenance);
        return provenance;
    }

    /// <summary>A profile with no Kundali yet is a valid, empty state, not an error: the module shell uses it to
    /// render "not generated yet" rather than a failure.</summary>
    private static KundaliView EmptyView(Guid profileId) => new(
        new KundaliAggregate(Guid.Empty, profileId, null, 0, DateTimeOffset.MinValue, DateTimeOffset.MinValue),
        null);

    private static KundaliPreferences DefaultPreferences() => new("North", 0, DateTimeOffset.MinValue);
}
