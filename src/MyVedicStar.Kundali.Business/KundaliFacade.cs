using System.Security.Claims;
using MyVedicStar.Kundali.Shared;
using MyVedicStar.VedicProfiles.Shared;
using MyVedicStar.Shared.Exceptions;

namespace MyVedicStar.Kundali.Business;

internal sealed class KundaliFacade(IKundaliProfileReader profiles, IKundaliRepository repository,
    KundaliValidator validator, KundaliEngineVersions versions, IKundaliAstronomyMetadataReader metadataReader,
    IKundaliCalculationEngine calculationEngine) : IKundaliFacade
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

        // Slice 3 work: the engine does not produce these yet, so they are honestly reported as not built rather
        // than as a property of the birth time.
        AddUnsupported(unsupported, nameof(KundaliOverview.CurrentDasha),
            KundaliUnsupportedReasons.EngineUnavailable);
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
            CurrentDasha: null,
            KeyPlacements: [],
            Unsupported: unsupported);
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
