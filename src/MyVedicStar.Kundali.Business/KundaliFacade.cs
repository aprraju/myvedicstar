using System.Security.Claims;
using MyVedicStar.Kundali.Shared;
using MyVedicStar.VedicProfiles.Shared;
using MyVedicStar.Shared.Exceptions;

namespace MyVedicStar.Kundali.Business;

internal sealed class KundaliFacade(IKundaliProfileReader profiles, IKundaliRepository repository,
    KundaliValidator validator, KundaliEngineVersions versions, IKundaliAstronomyMetadataReader metadataReader,
    IKundaliCalculationEngine calculationEngine, IKundaliDivisionalMapper divisionalMapper,
    IKundaliDignityRules dignityRules,
    TimeProvider clock) : IKundaliFacade
{
    public async Task<KundaliView> GetAsync(ClaimsPrincipal user, Guid profileId, CancellationToken token)
    {
        var accountId = await profiles.GetRegisteredAccountIdAsync(user, token);
        // Ownership is proved by reading the profile as this principal, not by trusting the path id: a foreign
        // profile must be indistinguishable from a missing one.
        _ = await ReadOwnedSnapshotAsync(user, profileId, token);

        // Effective settings are resolved on every read rather than stored on the aggregate, so a client always sees
        // what would apply if a generation ran now - not what applied when the last one did. The revision's own
        // provenance remains the record of what actually produced it.
        var metadata = await metadataReader.GetAsync(user, token);
        var settings = new KundaliEffectiveSettings(metadata.Ayanamsa,
            (await repository.GetPreferencesAsync(accountId, profileId, token))?.ChartStyle ?? DefaultPreferences().ChartStyle);

        return await repository.GetAsync(accountId, profileId, token) is { } view
            ? view with { Settings = settings }
            : EmptyView(profileId) with { Settings = settings };
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


    public async Task<KundaliChartsSection> GetChartsAsync(ClaimsPrincipal user, Guid profileId, int divisional,
        CancellationToken token)
    {
        // Rejected here rather than in the controller so the rule has one home, and rejected rather than approximated:
        // a wrong divisional sign is indistinguishable from a right one once it leaves this method.
        if (!KundaliCapabilities.IsSupportedDivisional(divisional) &&
            !KundaliCapabilities.IsProvisionalDivisional(divisional))
        {
            throw new ValidationException(
                "Unknown divisional chart. See the capabilities endpoint for the supported divisions.");
        }

        var display = await ReadOwnedDisplayAsync(user, profileId, token);
        var metadata = await metadataReader.GetAsync(user, token);

        var engine = calculationEngine.Calculate(new KundaliEngineInput(
            display.BirthDate, display.BirthTimeMode, display.BirthTime, display.Latitude, display.Longitude,
            display.TimezoneId, metadata.Ayanamsa));

        // A chart needs a longitude per planet. We have two, and only when the birth time is exact. The houses are
        // therefore built from sun and moon only - not a placeholder for the other seven, which would look like a
        // complete chart with five planets silently missing.
        var precision = engine.SiderealSunLongitude is not null && engine.SiderealMoonLongitude is not null
            ? new KundaliChartPrecision(ValidatedStatus, null)
            : new KundaliChartPrecision(UnvalidatedReferenceStatus,
                KundaliUnsupportedReasons.RequiresExactBirthTime);

        return new KundaliChartsSection(
            divisional,
            DivisionalMethodName(divisional),
            precision.Status == ValidatedStatus ? BuildHouses(divisional, engine) : [],
            precision);
    }

    /// <summary>Builds the twelve sign houses and places whichever bodies the engine produced.</summary>
    private IReadOnlyList<KundaliChartHouse> BuildHouses(int divisional, KundaliEngineResult engine)
    {
        var placements = new List<KundaliChartPlacement>(2);

        AddPlacement(placements, "Sun", divisional, engine.SiderealSunLongitude);
        AddPlacement(placements, "Moon", divisional, engine.SiderealMoonLongitude);

        // Every sign appears exactly once, even when empty: a chart with missing houses is not a chart, and a client
        // should not have to synthesise them.
        return KundaliSigns.All
            .Select(sign => new KundaliChartHouse(sign,
                placements.Where(placement => placement.Sign == sign).ToArray()))
            .ToArray();
    }

    private void AddPlacement(List<KundaliChartPlacement> target, string planet, int divisional,
        double? siderealLongitude)
    {
        if (siderealLongitude is not { } longitude)
            return;

        var signIndex = divisionalMapper.SignIn(divisional, longitude);
        var degreeWithinSign = (decimal)(longitude - (Math.Floor(longitude / 30.0) * 30.0));

        target.Add(new KundaliChartPlacement(planet, KundaliSigns.NameOf(signIndex),
            Math.Round(degreeWithinSign, 4)));
    }

    private const string ValidatedStatus = "validated";
    private const string UnvalidatedReferenceStatus = "unvalidated-reference";

    private static string DivisionalMethodName(int divisional) => divisional switch
    {
        1 => "Rasi",
        2 => "Hora",
        3 => "Drekkana",
        7 => "Saptamsa",
        9 => "Navamsa",
        10 => "Dasamsa",
        12 => "Dwadasamsa",
        30 => "Trimsamsa",
        60 => "Shashtiamsa",
        _ => $"D{divisional}"
    };



    /// <summary>Bodies the engine can place today. Anything else is listed as rule-only, never as an empty placement.</summary>
    private static readonly string[] BodiesWithPositions = ["Sun", "Moon"];

    /// <summary>Grahas the dignity and aspect rules cover but for which no position exists yet.</summary>
    private static readonly string[] BodiesWithoutPositions =
        ["Mars", "Mercury", "Jupiter", "Venus", "Saturn", "Rahu", "Ketu"];

    public async Task<KundaliPlanetsSection> GetPlanetsAsync(ClaimsPrincipal user, Guid profileId,
        CancellationToken token)
    {
        var display = await ReadOwnedDisplayAsync(user, profileId, token);
        var metadata = await metadataReader.GetAsync(user, token);

        var engine = calculationEngine.Calculate(new KundaliEngineInput(
            display.BirthDate, display.BirthTimeMode, display.BirthTime, display.Latitude, display.Longitude,
            display.TimezoneId, metadata.Ayanamsa));

        var unsupported = new List<KundaliUnsupportedField>();

        // The rules exist for every graha; the positions do not. Stating both is the only way a caller can tell
        // "we cannot compute this" from "we can compute it but not for your chart yet".
        foreach (var body in BodiesWithoutPositions)
            unsupported.Add(new KundaliUnsupportedField($"Planets.{body}", KundaliUnsupportedReasons.EngineUnavailable));

        var planets = new List<KundaliPlanetDetail>(BodiesWithPositions.Length);

        AddPlanet(planets, unsupported, "Sun", engine.SiderealSunLongitude);
        AddPlanet(planets, unsupported, "Moon", engine.SiderealMoonLongitude);

        var hasAnyPosition = planets.Count > 0;

        // With no positions at all, retrograde state and combustion are not "false" - they are unknown. Reporting
        // false would be a factual claim the engine cannot support.
        if (!hasAnyPosition)
        {
            unsupported.Add(new KundaliUnsupportedField("Planets.Retrograde",
                KundaliUnsupportedReasons.RequiresExactBirthTime));
            unsupported.Add(new KundaliUnsupportedField("Planets.Combust",
                KundaliUnsupportedReasons.RequiresExactBirthTime));
            unsupported.Add(new KundaliUnsupportedField("Planets.House",
                KundaliUnsupportedReasons.UnvalidatedReference));
        }

        return new KundaliPlanetsSection(
            metadata.HouseSystem,
            metadata.AspectMethod,
            new KundaliRuleCapabilities(
                dignityRules.DignityStates,
                ["Parashari-WholeSign"],
                BodiesWithPositions,
                BodiesWithoutPositions),
            planets,
            unsupported);
    }

    /// <summary>
    /// Adds one placement. Dignity is computed from the rule, not looked up; the values the rules need but the engine
    /// cannot supply (retrograde, house) stay null rather than defaulting to a plausible false.
    /// </summary>
    private void AddPlanet(List<KundaliPlanetDetail> target, List<KundaliUnsupportedField> unsupported,
        string planet, double? siderealLongitude)
    {
        if (siderealLongitude is not { } longitude)
        {
            unsupported.Add(new KundaliUnsupportedField($"Planets.{planet}",
                KundaliUnsupportedReasons.RequiresExactBirthTime));
            return;
        }

        var signIndex = (int)Math.Floor(longitude / 30.0) % 12;
        var degreeInSign = (decimal)(longitude - (signIndex * 30.0));

        // Aspects are emitted as the signs this planet projects onto; resolving a sign to occupant bodies is a
        // different question that needs those bodies' positions.
        var aspects = dignityRules.AspectedSigns(planet, signIndex)
            .Select(sign => new KundaliPlanetAspect(KundaliSigns.NameOf(sign), "Parashari-WholeSign"))
            .ToArray();

        target.Add(new KundaliPlanetDetail(
            planet,
            longitude,
            KundaliSigns.NameOf(signIndex),
            Math.Round(degreeInSign, 4),
            null,
            null,
            null,
            null,
            null,
            dignityRules.DignityOf(planet, longitude),
            aspects));
    }

    public async Task<int> DeleteForProfileAsync(ClaimsPrincipal user, Guid profileId, CancellationToken token)
    {
        var accountId = await profiles.GetRegisteredAccountIdAsync(user, token);

        // No ownership pre-read: the delete is account-scoped, so a foreign profile deletes nothing and reports zero -
        // which is the same answer as a profile that never existed. A pre-read would add a round trip and a timing
        // difference between "exists but not yours" and "does not exist".
        return await repository.DeleteForProfileAsync(accountId, profileId, token);
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
    private static KundaliView EmptyView(Guid profileId, KundaliEffectiveSettings? settings = null) => new(
        new KundaliAggregate(Guid.Empty, profileId, null, 0, DateTimeOffset.MinValue, DateTimeOffset.MinValue),
        null,
        // An empty state still reports the settings that would apply, so the shell can render the right style before
        // anything has been generated.
        settings ?? new KundaliEffectiveSettings(string.Empty, DefaultPreferences().ChartStyle));

    private static KundaliPreferences DefaultPreferences() => new("North", 0, DateTimeOffset.MinValue);
}
