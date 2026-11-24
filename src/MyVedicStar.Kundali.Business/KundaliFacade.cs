using System.Security.Claims;
using MyVedicStar.Kundali.Shared;
using MyVedicStar.VedicProfiles.Shared;
using MyVedicStar.Shared.Exceptions;

namespace MyVedicStar.Kundali.Business;

internal sealed class KundaliFacade(IKundaliProfileReader profiles, IKundaliRepository repository,
    IKundaliAlertRepository alerts,
    KundaliValidator validator, KundaliEngineVersions versions, IKundaliAstronomyMetadataReader metadataReader,
    IKundaliCalculationEngine calculationEngine, IKundaliDivisionalMapper divisionalMapper,
    IKundaliDignityRules dignityRules, IKundaliAshtakavargaRules ashtakavargaRules,
    IKundaliAshtakootaRules ashtakootaRules,
    IKundaliShadbalaRules shadbalaRules,
    IKundaliYogaRules yogaRules,
    IKundaliInterpretationNarrator interpreter,
    TimeProvider clock,
    IKundaliResultCache cache,
    IKundaliReportStore reportStore,
    IKundaliReportRenderer reportRenderer) : IKundaliFacade
{
    /// <summary>How long a finished report remains downloadable. MaxValue means "until the profile is deleted", the
    /// approved default; the download lease still caps each URL at its issue window.</summary>
    private static readonly DateTimeOffset ReportLifetime = DateTimeOffset.MaxValue;

    public async Task<KundaliView> GetAsync(ClaimsPrincipal user, Guid profileId, CancellationToken token)
    {
        var accountId = await profiles.GetRegisteredAccountIdAsync(user, token);
        // Ownership is proved by reading the profile as this principal, not by trusting the path id: a foreign
        // profile must be indistinguishable from a missing one. The profile's own revision is what the result is
        // judged against, so it is read on every path rather than cached.
        var snapshot = await ReadOwnedSnapshotAsync(user, profileId, token);
        // Effective settings are resolved on every read rather than stored on the aggregate (KUN-18), so a client
        // always sees what would apply if a generation ran now. Attached together with the revision verdict (KUN-21)
        // so neither fact can go stale silently.
        var metadata = await metadataReader.GetAsync(user, token);
        var settings = new KundaliEffectiveSettings(metadata.Ayanamsa,
            (await repository.GetPreferencesAsync(accountId, profileId, token))?.ChartStyle ?? DefaultPreferences().ChartStyle);
        var view = await repository.GetAsync(accountId, profileId, snapshot.ProfileRevision, token)
            ?? EmptyView(profileId);
        return Stamped(view, accountId, snapshot.ProfileRevision, ashtakavargaRules.RulesVersion, shadbalaRules.RulesVersion, yogaRules.RulesVersion) with { Settings = settings };
    }

    public async Task<KundaliView> GenerateAsync(ClaimsPrincipal user, Guid profileId, CancellationToken token)
    {
        var accountId = await profiles.GetRegisteredAccountIdAsync(user, token);
        var snapshot = await ReadOwnedSnapshotAsync(user, profileId, token);
        validator.ValidateSnapshot(snapshot);
        var provenance = await BuildProvenanceAsync(user, token);

        var existing = await repository.GetAsync(accountId, profileId, snapshot.ProfileRevision, token);
        if (existing is null)
            return Stamped(await repository.EnsureAggregateWithRevisionAsync(accountId, profileId, snapshot,
                provenance, token), accountId, snapshot.ProfileRevision, ashtakavargaRules.RulesVersion, shadbalaRules.RulesVersion, yogaRules.RulesVersion);

        // Repeated clicks for identical inputs must not enqueue twice: return the running revision untouched.
        if (existing.CurrentRevision is { Status: KundaliGenerationStatus.Pending or KundaliGenerationStatus.Running }
            && existing.CurrentRevision.InputSnapshotHash == snapshot.ComputeHash()
            && existing.CurrentRevision.Provenance == provenance)
            return Stamped(existing, accountId, snapshot.ProfileRevision, ashtakavargaRules.RulesVersion, shadbalaRules.RulesVersion, yogaRules.RulesVersion);

        return Stamped(await repository.AddRevisionAsync(accountId, profileId, existing.Aggregate.Revision,
            snapshot, provenance, token), accountId, snapshot.ProfileRevision, ashtakavargaRules.RulesVersion, shadbalaRules.RulesVersion, yogaRules.RulesVersion);
    }

    public async Task<KundaliView> RetryAsync(ClaimsPrincipal user, Guid profileId, CancellationToken token)
    {
        var accountId = await profiles.GetRegisteredAccountIdAsync(user, token);
        var snapshot = await ReadOwnedSnapshotAsync(user, profileId, token);
        validator.ValidateSnapshot(snapshot);
        var current = await repository.GetAsync(accountId, profileId, snapshot.ProfileRevision, token)
            ?? EmptyView(profileId);
        var provenance = await BuildProvenanceAsync(user, token);

        // Retry re-uses the snapshot; only a failure is retryable. Success or in-flight work is not silently redone.
        if (current.CurrentRevision is not { Status: KundaliGenerationStatus.Failed })
            throw new KundaliConflictException(KundaliErrors.AlreadyRunning);

        return Stamped(await repository.AddRevisionAsync(accountId, profileId, current.Aggregate.Revision, snapshot,
            provenance, token), accountId, snapshot.ProfileRevision, ashtakavargaRules.RulesVersion, shadbalaRules.RulesVersion, yogaRules.RulesVersion);
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


    /// <summary>
    /// KUN-12: Shadbala per planet and Bhava Bala per house for an owned profile.
    /// </summary>
    /// <remarks>
    /// <para><b>Shadbala is reported as explicitly partial.</b> Kala needs the birth moment's diurnal division and Chesta
    /// needs a true velocity series; neither is available, so four of the six classical components are produced and the
    /// result says <c>Complete = false</c> with both missing components named per planet. A total silently missing two
    /// components would be compared against a six-component threshold and read as a weak chart.</para>
    /// <para><b>Bhava Bala is withheld entirely.</b> Every classical Bhava Bala scheme requires the house cusps, which
    /// require the ascendant, which is not validated. It is returned as an explained empty list rather than as twelve
    /// zeros - zero would read as "computed and weak", which is a different and false statement.</para>
    /// </remarks>
    public async Task<KundaliBalaSection> GetBalaAsync(ClaimsPrincipal user, Guid profileId, CancellationToken token)
    {
        var display = await ReadOwnedDisplayAsync(user, profileId, token);
        var metadata = await metadataReader.GetAsync(user, token);

        var engine = calculationEngine.Calculate(new KundaliEngineInput(
            display.BirthDate, display.BirthTimeMode, display.BirthTime, display.Latitude, display.Longitude,
            display.TimezoneId, metadata.Ayanamsa));

        var unsupported = new List<KundaliUnsupportedField>();

        var longitudeByPlanet = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var planet in shadbalaRules.ScoredPlanets)
        {
            var position = engine.GrahaPositions?.FirstOrDefault(p =>
                string.Equals(p.Graha, planet, StringComparison.Ordinal) && p.SiderealLongitude.HasValue);

            if (position?.SiderealLongitude is { } longitude)
                longitudeByPlanet[planet] = longitude;
        }

        // A planet with no position is a gap, not a zero. If any scored planet is missing, Shadbala is withheld whole:
        // computing seven planets' totals from six positions would rank a missing planet at the bottom for no reason
        // other than the engine not having looked at it.
        if (longitudeByPlanet.Count < shadbalaRules.ScoredPlanets.Count)
        {
            var missing = shadbalaRules.ScoredPlanets
                .Where(planet => !longitudeByPlanet.ContainsKey(planet))
                .ToArray();

            foreach (var planet in missing)
                unsupported.Add(new KundaliUnsupportedField(planet, KundaliUnsupportedReasons.EngineUnavailable));

            unsupported.Add(new KundaliUnsupportedField(nameof(KundaliBalaSection.Shadbala),
                KundaliUnsupportedReasons.EngineUnavailable));
        }

        // Bhava Bala depends on house cusps, hence on the ascendant, which is withheld as unvalidated. The cause is our
        // own confidence, not the user's birth time, so the code says so.
        unsupported.Add(new KundaliUnsupportedField(nameof(KundaliBalaSection.BhavaBala),
            KundaliUnsupportedReasons.UnvalidatedReference));

        if (longitudeByPlanet.Count < shadbalaRules.ScoredPlanets.Count)
        {
            return new KundaliBalaSection(shadbalaRules.RulesVersion, shadbalaRules.Unit, false, [], [], unsupported);
        }

        var computed = shadbalaRules.Compute(longitudeByPlanet);

        var planets = computed
            .Select(planet => new KundaliShadbalaPlanet(
                planet.Planet,
                planet.Components.ToDictionary(
                    component => component.Component,
                    component => component.Rupas,
                    StringComparer.Ordinal),
                planet.Components.ToDictionary(
                    component => component.Component,
                    component => component.State,
                    StringComparer.Ordinal),
                planet.TotalRupas,
                planet.RequiredRupas,
                planet.RatioToThreshold,
                planet.ComponentsWithheld,
                planet.ComponentsInTotal,
                planet.ComponentsExcludedFromTotal))
            .ToArray();

        // Each withheld component is named on the section too, so a consumer reading only the top level still learns why
        // Complete is false without walking every planet.
        foreach (var withheld in shadbalaRules.WithheldComponents)
            unsupported.Add(new KundaliUnsupportedField(withheld.Key, withheld.Value));

        return new KundaliBalaSection(
            shadbalaRules.RulesVersion,
            shadbalaRules.Unit,
            Complete: false,
            planets,
            [],
            unsupported);
    }


    /// <summary>
    /// KUN-10: yoga and dosha assessments for an owned profile.
    /// </summary>
    /// <remarks>
    /// <para><b>Present and withheld coexist.</b> <c>Evaluated</c> carries finished claims - satisfied or failed.
    /// <c>NotEvaluated</c> carries combinations that were never tested, each enumerated by name with its own reason. Both
    /// live in one payload and every entry carries its own completeness, because a section-level caveat would let a caller
    /// read the satisfied list and never learn that half the catalogue was never looked at.</para>
    /// <para><b>Why some rules are not evaluated.</b> Only sign placements are published for all nine grahas today; the
    /// ascendant is withheld as unvalidated. Rules that need a house frame - Manglik, in its owner-approved Lagna-only
    /// form - are therefore reported indeterminate rather than redefined from the Moon, which would be a different
    /// quantity under the same name.</para>
    /// </remarks>
    public async Task<KundaliYogasSection> GetYogasAsync(ClaimsPrincipal user, Guid profileId, CancellationToken token)
    {
        var display = await ReadOwnedDisplayAsync(user, profileId, token);
        var metadata = await metadataReader.GetAsync(user, token);

        var engine = calculationEngine.Calculate(new KundaliEngineInput(
            display.BirthDate, display.BirthTimeMode, display.BirthTime, display.Latitude, display.Longitude,
            display.TimezoneId, metadata.Ayanamsa));

        var unsupported = new List<KundaliUnsupportedField>();

        // Only grahas with a published longitude reach the catalogue. A graha that is withheld simply does not appear, so
        // each rule reports it by name as an untested component rather than the whole section being disabled.
        var signByGraha = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var position in engine.GrahaPositions ?? [])
        {
            if (position.SiderealLongitude is { } longitude && position.Sign is not null)
                signByGraha[position.Graha] = SignIndex(position.Sign);
        }

        var evaluation = yogaRules.Evaluate(signByGraha);

        // A withheld graha is surfaced once at the section level too, so a consumer that renders only NotEvaluated still
        // sees why. The per-assessment reasons remain the authoritative explanation.
        foreach (var position in (engine.GrahaPositions ?? []).Where(p => !p.SiderealLongitude.HasValue))
            unsupported.Add(new KundaliUnsupportedField(position.Graha, KundaliUnsupportedReasons.UnvalidatedReference));

        if (evaluation.NotEvaluated.Count > 0)
            unsupported.Add(new KundaliUnsupportedField(nameof(KundaliYogasSection.NotEvaluated),
                KundaliUnsupportedReasons.UnvalidatedReference));

        return new KundaliYogasSection(
            evaluation.RulesVersion,
            evaluation.Evaluated.Select(MapYoga).ToArray(),
            evaluation.NotEvaluated.Select(MapYoga).ToArray(),
            unsupported);
    }

    private static KundaliYogaAssessment MapYoga(KundaliYogaResult result) => new(
        result.Code,
        result.Name,
        result.Scope,
        result.Status,
        result.Conditions.Select(condition => new KundaliYogaCondition(condition.Text, condition.Truth)).ToArray(),
        result.Supporting,
        result.Opposing,
        result.Exceptions,
        result.Cancellations,
        result.Complete,
        result.Reason,
        result.UntestedComponents);

    /// <summary>Sign index 0..11 from a sign name, matching the engine's vocabulary.</summary>
    private static int SignIndex(string sign)
    {
        for (var index = 0; index < KundaliSigns.All.Count; index++)
        {
            if (string.Equals(KundaliSigns.All[index], sign, StringComparison.Ordinal))
                return index;
        }

        // An unrecognised sign name is a contract defect, not data: the engine and the shared vocabulary must agree.
        throw new ArgumentOutOfRangeException(nameof(sign), sign, "Unrecognised sign name from the calculation engine.");
    }

    /// <summary>
    /// KUN-14 interpretations for one domain (api#54). Every domain in the closed set is returned with its own status
    /// and reason: the requested one is narrated, the rest carry their own reason for not being narrated in this
    /// response. A refusal to narrate never removes the domain from the payload.
    /// </summary>
    /// <remarks>
    /// <para><b>Only published positions become factors.</b> A graha the engine withheld contributes nothing, so a domain
    /// with no factors is reported <c>not-evaluated</c> with <c>input-missing</c> - "we did not have the inputs" - rather
    /// than being narrated from a partial chart or withheld as if the product had declined.</para>
    /// <para><b>Not-requested is not withheld.</b> A domain that was not asked for in this call is reported with the
    /// <c>not-requested</c> reason under <c>not-evaluated</c>, because saying "withheld" about a domain nobody asked for
    /// would invent an assessment that never happened.</para>
    /// </remarks>
    public async Task<KundaliInterpretationsSection> GetInterpretationsAsync(ClaimsPrincipal user, Guid profileId,
        string? domain, CancellationToken token)
    {
        // An ABSENT domain means "no filter": every domain in the closed set is returned, un-narrated, with its own
        // reason. A domain that IS supplied but is not in the closed set is a 400 - the two cases are deliberately not
        // collapsed, because treating absent as unknown would break the plain route.
        var domainSupplied = !string.IsNullOrWhiteSpace(domain);
        if (domainSupplied && !KundaliInterpretationDomains.IsKnown(domain))
            throw new ValidationException(KundaliErrors.InvalidInterpretationDomain);

        var display = await ReadOwnedDisplayAsync(user, profileId, token);
        var snapshot = await ReadOwnedSnapshotAsync(user, profileId, token);
        var metadata = await metadataReader.GetAsync(user, token);

        var engine = calculationEngine.Calculate(new KundaliEngineInput(
            display.BirthDate, display.BirthTimeMode, display.BirthTime, display.Latitude, display.Longitude,
            display.TimezoneId, metadata.Ayanamsa));

        var published = (engine.GrahaPositions ?? [])
            .Where(position => position.SiderealLongitude.HasValue && position.Sign is not null)
            .Select(position => (position.Graha, position.Sign))
            .ToArray();

        var verdicts = new Dictionary<string, InterpretationVerdict>(StringComparer.Ordinal);
        if (domainSupplied)
        {
            var factors = InterpretationFactors.For(domain!, published);
            verdicts[domain!] = factors.Count == 0
                ? InterpretationRule.NotEvaluated(domain!, KundaliInterpretationReasons.InputMissing)
                : await interpreter.NarrateAsync(domain!, "en", snapshot.ProfileRevision, factors, token);
        }

        // Domains not narrated in this response say so individually; "not requested" is not "withheld", which would
        // claim an assessment that never happened.
        var missing = KundaliInterpretationDomains.All
            .Where(other => !verdicts.ContainsKey(other))
            .ToDictionary(other => other, _ => KundaliInterpretationReasons.NotRequested, StringComparer.Ordinal);

        return InterpretationSection.Compose(
            snapshot.ProfileRevision,
            "en",
            interpreter.PromptVersion,
            interpreter.Fingerprint,
            verdicts,
            missing);
    }

    public KundaliCapabilityReport GetCapabilities() => KundaliCapabilities.Report();

    /// <summary>
    /// KUN-22 propagation. The profile revision is not compared here by the caller: the profile is read as the
    /// principal, the change kind is classified, and the persistence layer applies the compare-and-swap so a concurrent
    /// edit cannot be silently folded into this one.
    /// </summary>
    public async Task<KundaliProfileChangeResult> ApplyProfileChangeAsync(ClaimsPrincipal user, Guid profileId,
        long profileRevision, string changeKind, CancellationToken token)
    {
        if (!KundaliProfileChangeKinds.All.Contains(changeKind, StringComparer.Ordinal))
            throw new ValidationException(KundaliErrors.InvalidGenerationRequest);
        if (profileRevision <= 0)
            throw new ValidationException(KundaliErrors.InvalidGenerationRequest);

        var accountId = await profiles.GetRegisteredAccountIdAsync(user, token);
        var snapshot = await ReadOwnedSnapshotAsync(user, profileId, token);

        // The profile's present revision is read, not assumed: a caller-supplied revision could otherwise cancel work
        // for a revision the profile has not reached.
        var result = await repository.ApplyProfileChangeAsync(accountId, profileId,
            Math.Max(0, snapshot.ProfileRevision - 1), snapshot.ProfileRevision, changeKind, token);

        // A display change does not touch calculated data by construction, so it costs no engine call and no cache work:
        // a result computed from the same birth inputs is still correct after a rename. Every other kind changes what
        // the cache key is built from, so the previous entry can never be read again and is removed eagerly rather than
        // waiting for its lifetime to run out.
        if (!result.DisplayOnly)
        {
            var provenance = await BuildProvenanceAsync(user, token);
            await cache.RemoveAsync(KundaliCacheKeys.Section(
                KundaliCacheKeys.RevisionKey(accountId, profileId, snapshot.ProfileRevision, provenance,
                    RuleSetVersions(provenance, ashtakavargaRules.RulesVersion, shadbalaRules.RulesVersion, yogaRules.RulesVersion)), profileId,
                KundaliSections.Overview), token);
        }

        return result;
    }

    /// <summary>
    /// KUN-22 admission of a background result. The stored revision is compared with the profile as it is now and with
    /// the present version tuple; a mismatch is discarded with a reason rather than published.
    /// </summary>
    public async Task<KundaliBackgroundResultDisposition> DiscardObsoleteResultAsync(ClaimsPrincipal user,
        Guid profileId, Guid revisionId, CancellationToken token)
    {
        var accountId = await profiles.GetRegisteredAccountIdAsync(user, token);
        var snapshot = await ReadOwnedSnapshotAsync(user, profileId, token);
        var current = await repository.GetAsync(accountId, profileId, snapshot.ProfileRevision, token);
        var stored = await repository.GetSectionsAsync(accountId, profileId, revisionId, token);

        if (stored is null)
            return new KundaliBackgroundResultDisposition(false,
                KundaliBackgroundResultDisposition.Reasons.NotFound, revisionId, 0, 0, snapshot.ProfileRevision);

        if (current?.CurrentRevision is { } active && active.RevisionId != revisionId)
            return new KundaliBackgroundResultDisposition(false,
                KundaliBackgroundResultDisposition.Reasons.Superseded, revisionId, stored.Revision,
                stored.ProfileRevision, snapshot.ProfileRevision);

        if (stored.ProfileRevision != snapshot.ProfileRevision)
            return new KundaliBackgroundResultDisposition(false,
                KundaliBackgroundResultDisposition.Reasons.ProfileRevisionChanged, revisionId, stored.Revision,
                stored.ProfileRevision, snapshot.ProfileRevision);

        var provenance = await BuildProvenanceAsync(user, token);
        if (current?.CurrentRevision is { } revision && revision.RevisionId == revisionId
            && revision.Provenance != provenance)
            return new KundaliBackgroundResultDisposition(false,
                KundaliBackgroundResultDisposition.Reasons.ProvenanceChanged, revisionId, stored.Revision,
                stored.ProfileRevision, snapshot.ProfileRevision);

        return new KundaliBackgroundResultDisposition(true, null, revisionId, stored.Revision,
            stored.ProfileRevision, snapshot.ProfileRevision);
    }

    /// <summary>
    /// KUN-21 section read. The result is served from the account-scoped cache when the cached copy belongs to the
    /// profile's present revision and the versions that produced it, and is recomputed otherwise. A cache failure is a
    /// miss, never an error, and a cached payload is never trusted for freshness: the freshness envelope is compared
    /// before the copy is returned and the copy is discarded when it disagrees.
    /// </summary>
    public async Task<KundaliOverview> GetOverviewAsync(ClaimsPrincipal user, Guid profileId, CancellationToken token)
    {
        var accountId = await profiles.GetRegisteredAccountIdAsync(user, token);
        var snapshot = await ReadOwnedSnapshotAsync(user, profileId, token);
        var display = await ReadOwnedDisplayAsync(user, profileId, token);
        var provenance = await BuildProvenanceAsync(user, token);

        var revisionKey = KundaliCacheKeys.RevisionKey(accountId, profileId, snapshot.ProfileRevision, provenance,
            RuleSetVersions(provenance, ashtakavargaRules.RulesVersion, shadbalaRules.RulesVersion, yogaRules.RulesVersion));
        var key = KundaliCacheKeys.Section(revisionKey, profileId, KundaliSections.Overview);
        var now = clock.GetUtcNow();

        // The key already contains the revision and versions, so a copy under this key cannot be another revision's
        // result; the envelope check additionally rejects a copy whose dasha highlight has crossed its boundary.
        var cached = await ReadCacheAsync<KundaliOverview>(key, token);
        if (KundaliFreshness.IsServable(cached, now)) return cached!;

        return await ProjectAndCacheOverviewAsync(user, profileId, snapshot, display, provenance, revisionKey, key,
            token);
    }

    /// <summary>Computes a section payload and caches it under its revision key, discarding it when the profile moved
    /// while it was being computed.</summary>
    private async Task<KundaliOverview> ProjectAndCacheOverviewAsync(ClaimsPrincipal user, Guid profileId,
        KundaliInputSnapshot snapshot, KundaliProfileDisplay display, KundaliProvenance provenance,
        string revisionKey, string key, CancellationToken token)
    {
        var computed = await ComputeOverviewAsync(user, profileId, display, provenance, token);

        // Compare-and-swap against the profile as it is now: a payload computed for a revision the profile has since
        // left is obsolete work (KUN-22) and is discarded, not cached and not returned as current.
        var latest = await ReadOwnedSnapshotAsync(user, profileId, token);
        if (latest.ProfileRevision != snapshot.ProfileRevision)
        {
            await cache.RemoveAsync(key, token);
            throw new KundaliConflictException(KundaliErrors.ProfileChanged);
        }

        var now = clock.GetUtcNow();
        var boundary = KundaliFreshness.CurrentDashaBoundary(computed.MahadashaChain, now);
        // The chain is a derivation aid, not part of the published payload (and a cached copy must not carry it twice).
        var overview = computed with { MahadashaChain = null };
        var stamped = overview with
        {
            Freshness = new KundaliResultFreshness(profileId, snapshot.ProfileRevision, revisionKey,
                snapshot.ComputeHash(), provenance, now, boundary)
        };

        // Validity is bounded by the dasha boundary, not by an arbitrary lifetime: the current dasha highlight is the
        // only part of this payload that changes without a revision change.
        await WriteCacheAsync(key, stamped, boundary ?? now + CacheCeiling, token);
        return stamped;
    }

    /// <summary>Backstop lifetime for a payload that carries no boundary of its own. The revision key, not this
    /// lifetime, is what prevents a stale revision being served.</summary>
    private static readonly TimeSpan CacheCeiling = TimeSpan.FromHours(12);

    private const string CachePolicy = "kundali";

    private async Task<T?> ReadCacheAsync<T>(string key, CancellationToken token) where T : class
    {
        try { return await cache.GetAsync<T>(key, token); }
        catch (Exception) { return null; }
    }

    private async Task WriteCacheAsync<T>(string key, T value, DateTimeOffset validUntil, CancellationToken token)
        where T : class
    {
        try { await cache.SetAsync(key, value, validUntil, CachePolicy, token); }
        catch (Exception) { /* the persisted result is the source of truth; a cache failure is not a request failure */ }
    }

    private async Task<KundaliOverview> ComputeOverviewAsync(ClaimsPrincipal user, Guid profileId,
        KundaliProfileDisplay display, KundaliProvenance provenance, CancellationToken token)
    {
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
            Unsupported: unsupported)
        {
            // Not part of the wire payload: the mahadasha chain the current highlight was selected from, so the
            // expiry boundary can be derived from the same periods rather than recomputed differently.
            MahadashaChain = engine.VimshottariMahadashas?
                .Select(period => new KundaliDasha(period.Lord, "Mahadasha", period.StartUtc, period.EndUtc))
                .ToArray()
        };
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
        // Status and reason must agree: both describe the same fact. A chart needs a longitude per planet, we have two
        // (and only when the birth time is exact), so a chart built from two bodies is unvalidated rather than
        // complete - that is the unvalidated-reference case, not a birth-time case. Emitting status
        // "unvalidated-reference" beside reason "requires-exact-birth-time" made the two fields contradict.
        var precision = engine.SiderealSunLongitude is not null && engine.SiderealMoonLongitude is not null
            ? new KundaliChartPrecision(ValidatedStatus, null)
            : new KundaliChartPrecision(UnvalidatedReferenceStatus,
                KundaliUnsupportedReasons.UnvalidatedReference);

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

        // Reached only with a sequence in hand: the chain was computed, but this instant falls outside it. That is not
        // birth-time uncertainty - an inexact birth time withholds the whole sequence upstream - and it is not an
        // unvalidated reference either, because the numbers are the engine's own. It is a range fact, stated as such.
        AddUnsupported(unsupported, nameof(KundaliOverview.CurrentDasha),
            KundaliUnsupportedReasons.OutsideDashaSpan);
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

    /// <summary>
    /// KUN-15: compares two owned profiles with Ashtakoota.
    /// </summary>
    /// <remarks>
    /// <para><b>Order independence is proved, not assumed.</b> <paramref name="profileIdA"/> and
    /// <paramref name="profileIdB"/> are sorted ordinally into a canonical pair BEFORE anything is computed, so A-to-B
    /// and B-to-A produce the same pairKey, the same stored row and the same scores. Sorting at the edge is what makes
    /// the cache identity a function of the pair rather than of the request.</para>
    /// <para><b>Ownership is read per side.</b> Each profile is read as the authenticated principal, so a foreign id
    /// fails identically to a missing one. Neither id is trusted from the path, and no owner id is accepted from the
    /// client.</para>
    /// <para><b>The threshold is absent.</b> This returns a score and never a verdict. See
    /// <see cref="KundaliAshtakootaComparison"/> - the shape has no field a "match" judgement could occupy, which is a
    /// stronger guarantee than an unwritten convention.</para>
    /// </remarks>
    public async Task<KundaliAshtakootaComparison> CompareAsync(ClaimsPrincipal user, Guid profileIdA,
        Guid profileIdB, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (profileIdA == profileIdB)
            throw new ValidationException(KundaliComparisonErrorCodes.SameProfile);

        var accountId = await profiles.GetRegisteredAccountIdAsync(user, token);

        // Canonical order first: everything downstream - pairKey, storage, the response's A/B labelling - depends on
        // this, so it happens before a single profile read.
        var (firstId, secondId) = profileIdA.CompareTo(profileIdB) <= 0
            ? (profileIdA, profileIdB)
            : (profileIdB, profileIdA);

        var first = await ReadOwnedSnapshotAsync(user, firstId, token);
        var second = await ReadOwnedSnapshotAsync(user, secondId, token);
        var metadata = await metadataReader.GetAsync(user, token);

        var pairKey = PairKey(firstId, secondId);

        var cached = await repository.GetComparisonAsync(accountId, pairKey, first.ProfileRevision,
            second.ProfileRevision, AshtakootaRulesVersion, token);
        if (cached is not null)
            return Deserialize(cached.PayloadJson);

        var sideA = MoonSideOf(first, metadata.Ayanamsa);
        var sideB = MoonSideOf(second, metadata.Ayanamsa);
        var assessment = ashtakootaRules.Assess(sideA, sideB);

        var categories = assessment.Categories
            .Select(category => new KundaliAshtakootaCategory(category.Code, DisplayName(category.Code),
                category.Maximum, category.Score, category.Reason, category.Side, category.Note))
            .ToArray();

        var scoredMaximum = assessment.Categories.Where(category => category.IsScored)
            .Sum(category => category.Maximum);

        var comparison = new KundaliAshtakootaComparison(
            pairKey,
            new KundaliComparisonSide(firstId, first.ProfileRevision),
            new KundaliComparisonSide(secondId, second.ProfileRevision),
            categories,
            assessment.ScoredTotal,
            // Never null, and 0 when nothing was scored: a caller that receives 0 can tell "nothing scored" from
            // "36 points available and none earned" by looking at Certainty, rather than by inferring from absence.
            scoredMaximum,
            AshtakootaShape.MaximumTotal,
            assessment.Certainty,
            ManglikFor(first, second, metadata.Ayanamsa),
            AshtakootaRulesVersion);

        await repository.StoreComparisonAsync(accountId, new KundaliStoredComparison(pairKey, firstId, secondId,
            first.ProfileRevision, second.ProfileRevision, AshtakootaRulesVersion, Serialize(comparison)), token);

        return comparison;
    }

    /// <summary>The Ashtakoota rule version, taken from the engine so the stored identity tracks the tables.</summary>
    private string AshtakootaRulesVersion => ashtakootaRules.RulesVersion;

    /// <summary>Order-independent pair identity: both ids sorted ordinally and joined.</summary>
    private static string PairKey(Guid first, Guid second) =>
        string.Join(':', first.ToString("D"), second.ToString("D"));

    /// <summary>
    /// The Moon's sign and nakshatra for one side, or an unavailable side carrying the reason.
    /// </summary>
    /// <remarks>
    /// The engine withholds a Moon position when the birth time is not exactly known, and Ashtakoota cannot invent one.
    /// That absence is propagated with its own reason instead of being converted into index 0, which would silently be
    /// Aries and Ashwini - a real-looking score for a Moon nobody measured.
    /// </remarks>
    private AshtakootaSide MoonSideOf(KundaliInputSnapshot snapshot, string ayanamsa)
    {
        if (snapshot.BirthTimeMode != BirthTimeMode.Known || snapshot.BirthTime is null)
            return AshtakootaSide.Unavailable(AshtakootaReasons.BirthTimeUnknown);

        try
        {
            var calculation = calculationEngine.Calculate(new KundaliEngineInput(snapshot.DateOfBirth,
                snapshot.BirthTimeMode, snapshot.BirthTime, snapshot.Latitude, snapshot.Longitude,
                snapshot.TimezoneId, ayanamsa));

            if (calculation.SiderealMoonLongitude is not { } moon)
                return AshtakootaSide.Unavailable(AshtakootaReasons.MoonPositionUnavailable);

            var signIndex = (int)(moon / 30.0) % 12;
            var nakshatraIndex = (int)(moon / (360.0 / 27.0)) % 27;
            return AshtakootaSide.Of(signIndex, nakshatraIndex);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A calculation failure for one side must not fail the comparison: the other seven categories that do not
            // depend on that side's Moon are still answerable, and a partial comparison with a reason is more useful
            // than a 500. The reason is recorded per category by the engine.
            return AshtakootaSide.Unavailable(AshtakootaReasons.MoonPositionUnavailable);
        }
    }

    /// <summary>
    /// The Manglik comparison for the two profiles.
    /// </summary>
    /// <remarks>
    /// Reported per side with a status, never as a combined verdict. Manglik is an assessment of each chart; whether two
    /// Mangliks "cancel" is an interpretation the owner has not approved as API output, so no such field exists here.
    /// </remarks>
    private KundaliManglikComparison ManglikFor(KundaliInputSnapshot first, KundaliInputSnapshot second,
        string ayanamsa) =>
        new(ManglikStatusOf(first, ayanamsa), ManglikStatusOf(second, ayanamsa),
            "Manglik status is reported per profile. No combined compatibility verdict is inferred from it.");

    private string ManglikStatusOf(KundaliInputSnapshot snapshot, string ayanamsa)
    {
        if (snapshot.BirthTimeMode != BirthTimeMode.Known || snapshot.BirthTime is null)
            return "indeterminate";

        try
        {
            var calculation = calculationEngine.Calculate(new KundaliEngineInput(snapshot.DateOfBirth,
                snapshot.BirthTimeMode, snapshot.BirthTime, snapshot.Latitude, snapshot.Longitude,
                snapshot.TimezoneId, ayanamsa));
            // The engine reports Manglik as an assessment with a status; an unvalidated reference is not "not manglik".
            // Literal rather than a shared constant: Business does not reference the calculation project (the
            // dependency runs inward), and importing one constant to reference a string is not worth a project edge.
            return calculation.Gaps.Any(gap => gap.Reason == "unvalidated-reference")
                ? "indeterminate"
                : "known";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return "indeterminate";
        }
    }

    /// <summary>Display name for a koota code. Falls back to the code so a new koota is never rendered blank.</summary>
    private static string DisplayName(string code) => code switch
    {
        "GrahaMaitri" => "Graha Maitri",
        _ => code
    };

    private static string Serialize(KundaliAshtakootaComparison comparison) =>
        System.Text.Json.JsonSerializer.Serialize(comparison,
            new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
            });

    private static KundaliAshtakootaComparison Deserialize(string payload) =>
        System.Text.Json.JsonSerializer.Deserialize<KundaliAshtakootaComparison>(payload,
            new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
            }) ?? throw new InvalidOperationException("A stored comparison payload was empty or invalid.");

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

    /// <summary>
    /// Every independently versioned rule set that can change a published number. The cache key binds all of them, so
    /// a rule change can never be served from a result computed under the previous rules.
    /// </summary>
    /// <remarks>Static and taking the Ashtakavarga version explicitly, so the same list is used on the read path (which
    /// has the registered rules) and in <c>Stamped</c> (which only has the stored provenance). Two ways of building this
    /// list is how the two would silently diverge.</remarks>
    /// <remarks>
    /// Every independently versioned rule set that can change a published number must appear here. Shadbala carries its
    /// own component/threshold rule set (KUN-12), so omitting it would let a Shadbala rule change reuse a cached strength
    /// computed under the old rules - the same staleness the Ashtakavarga entry exists to prevent.
    /// </remarks>
    private static IReadOnlyList<string> RuleSetVersions(KundaliProvenance provenance, string ashtakavargaRulesVersion,
        string shadbalaRulesVersion, string yogaRulesVersion) =>
        [provenance.RuleSetVersion, ashtakavargaRulesVersion, shadbalaRulesVersion, yogaRulesVersion];

    private async Task<KundaliProvenance> BuildProvenanceAsync(ClaimsPrincipal user, CancellationToken token)
    {
        var provenance = new KundaliProvenance(versions.Engine, versions.Ephemeris, versions.RuleSet,
            versions.TimezoneData, await profiles.GetAyanamsaAsync(user, token));
        validator.ValidateProvenance(provenance);
        return provenance;
    }

    /// <summary>A profile with no Kundali yet is a valid, empty state, not an error: the module shell uses it to
    /// render "not generated yet" rather than a failure.</summary>
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
            // Neither is a birth-time problem. Retrograde is a derivative of a position, and combustion needs the
            // Sun's and the planet's longitudes; with no published positions there is nothing to derive either from.
            // The cause is the withheld reference, so that is what the reader is told - pointing at a birth time that
            // may already be correct sends them to fix the wrong input.
            unsupported.Add(new KundaliUnsupportedField("Planets.Retrograde",
                KundaliUnsupportedReasons.UnvalidatedReference));
            unsupported.Add(new KundaliUnsupportedField("Planets.Combust",
                KundaliUnsupportedReasons.UnvalidatedReference));
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
            // Reached only when the engine produced no longitude for this body. The engine's own reason travels in the
            // section's gaps, so this is the per-planet echo of it; it must not claim birth-time uncertainty when the
            // real cause is an engine that could not compute the body or a provider whose value is unproven.
            unsupported.Add(new KundaliUnsupportedField($"Planets.{planet}",
                KundaliUnsupportedReasons.UnvalidatedReference));
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

    /// <summary>
    /// KUN-13 transit section. The reference instant is resolved once and echoed, so the caller never has to assume
    /// which instant produced the answer.
    /// </summary>
    public async Task<KundaliTransitsSection> GetTransitsAsync(ClaimsPrincipal user, Guid profileId,
        DateTimeOffset? at, string? timezone, CancellationToken token)
    {
        var display = await ReadOwnedDisplayAsync(user, profileId, token);
        var metadata = await metadataReader.GetAsync(user, token);

        // An unknown zone is rejected, never quietly replaced by the profile's: the caller asked for a display zone,
        // and answering in a different one would mislabel every instant in the response.
        var displayTimezone = timezone ?? display.TimezoneId;
        if (!ZoneIsResolvable(displayTimezone))
            throw new ValidationException(KundaliErrors.InvalidTimezone);

        var computedAt = clock.GetUtcNow();
        var referenceInstant = at ?? computedAt;

        var unsupported = new List<KundaliUnsupportedField>();

        // Transit positions are computed for the reference instant, not for the birth instant. Reusing the natal
        // engine call would answer a different question.
        var transits = calculationEngine.CalculateTransits(new KundaliTransitQuery(
            new KundaliEngineInput(display.BirthDate, display.BirthTimeMode, display.BirthTime, display.Latitude,
                display.Longitude, display.TimezoneId, metadata.Ayanamsa),
            referenceInstant, displayTimezone));

        foreach (var gap in transits.Gaps)
            AddUnsupported(unsupported, gap.Path, gap.Reason);

        var saturn = new KundaliTransitGraha("Saturn", transits.SaturnSign, null, null,
            transits.SaturnWindows.Select(ToWindow).ToArray());
        var jupiter = new KundaliTransitGraha("Jupiter", transits.JupiterSign, null, null,
            transits.JupiterWindows.Select(ToWindow).ToArray());

        KundaliSadeSati? sadeSati = null;
        if (transits.SadeSati is { } interval)
        {
            var phase = interval.Phases.FirstOrDefault(candidate =>
                candidate.StartUtc <= referenceInstant && referenceInstant < candidate.EndUtc);
            sadeSati = new KundaliSadeSati(
                phase?.Phase ?? NextPhaseName(interval, referenceInstant),
                Active: phase is not null,
                interval.MoonSign,
                interval.StartUtc,
                interval.EndUtc,
                interval.IsPartial,
                interval.Phases.Select(candidate => new KundaliSadeSatiPhase(candidate.Phase, candidate.Sign,
                    candidate.StartUtc, candidate.EndUtc)).ToArray());

            // Before the first phase begins there is nothing active to report, and saying so is not a gap in the
            // calculation - the engine knows when it starts. Reported only when the whole interval is in the future.
            if (phase is null && interval.StartUtc > referenceInstant)
                AddUnsupported(unsupported, $"{nameof(KundaliTransitsSection.SadeSati)}.Active",
                    KundaliUnsupportedReasons.NotYetActive);
        }
        else
        {
            AddUnsupported(unsupported, nameof(KundaliTransitsSection.SadeSati),
                KundaliUnsupportedReasons.NoIntervalInSpan);
        }

        // Dated near-term events: the first ingress of each slow graha after the reference instant. An event list that
        // included the past would make an "upcoming" alert feed depend on how far back it looked.
        var dated = new List<KundaliDatedTransit>(2);
        AddNextIngress(dated, transits.SaturnWindows, "Saturn", KundaliAlertEventTypes.SaturnIngress, referenceInstant);
        AddNextIngress(dated, transits.JupiterWindows, "Jupiter", KundaliAlertEventTypes.JupiterIngress, referenceInstant);

        return new KundaliTransitsSection(referenceInstant, displayTimezone, computedAt,
            computedAt.Add(TransitFreshnessFor), saturn, jupiter, sadeSati, dated, unsupported);
    }

    public async Task<IReadOnlyList<KundaliAlertPreference>> GetAlertsAsync(ClaimsPrincipal user, Guid profileId,
        CancellationToken token)
    {
        var accountId = await profiles.GetRegisteredAccountIdAsync(user, token);
        _ = await ReadOwnedSnapshotAsync(user, profileId, token);
        return await alerts.GetAsync(accountId, profileId, token);
    }

    public async Task<IReadOnlyList<KundaliAlertPreference>> GrantAlertsAsync(ClaimsPrincipal user, Guid profileId,
        IReadOnlyList<string> eventTypes, string channel, CancellationToken token)
    {
        var accountId = await profiles.GetRegisteredAccountIdAsync(user, token);

        // Consent is only meaningful for a profile that exists and is owned: a subscription for a deleted profile
        // would be a queued email about nothing, and the ownership read is what proves the profile is real.
        _ = await ReadOwnedSnapshotAsync(user, profileId, token);

        return await alerts.GrantAsync(accountId, profileId, eventTypes, channel, token);
    }

    public async Task<int> RevokeAlertsAsync(ClaimsPrincipal user, Guid profileId, CancellationToken token)
    {
        var accountId = await profiles.GetRegisteredAccountIdAsync(user, token);

        // No ownership pre-read, for the same reason as the profile delete: the revoke is account-scoped, so a
        // foreign profile revokes nothing and reports zero - indistinguishable from a profile that never existed.
        return await alerts.RevokeAllAsync(accountId, profileId, token);
    }

    /// <summary>
    /// How long a transit answer stays useful. Thirty minutes: the slow grahas this section reports move by
    /// arcseconds over that window, so the numbers stay valid far longer, but a reader watching an ingress wants a
    /// refresh cadence closer to their session than to the astronomy.
    /// </summary>
    private static readonly TimeSpan TransitFreshnessFor = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Whether the zone id resolves in the IANA database. Checked through <see cref="TimeZoneInfo"/> rather than a
    /// NodaTime zone provider so the Business layer keeps its existing dependency set; both use the same tz database
    /// on Linux, and the calculation engine resolves the birth zone through NodaTime, so an id that passes here and
    /// fails there would surface as an engine error, not a wrong time.
    /// </summary>
    private static bool ZoneIsResolvable(string timezone)
    {
        if (string.IsNullOrWhiteSpace(timezone))
            return false;

        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(timezone);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }

    private static KundaliTransitWindow ToWindow(KundaliSignWindow window) =>
        new(window.Sign, window.StartUtc, window.EndUtc);

    private static string NextPhaseName(KundaliEngineSadeSati interval, DateTimeOffset at) =>
        interval.Phases.Where(phase => phase.StartUtc > at).Select(phase => phase.Phase).FirstOrDefault()
        ?? SadeSatiPhaseNames.AfterInterval;

    private static void AddNextIngress(List<KundaliDatedTransit> target, IReadOnlyList<KundaliSignWindow> windows,
        string graha, string eventType, DateTimeOffset at)
    {
        // The first window that STARTS after the reference instant. The window containing it started in the past and
        // is already reported as the current sign, so repeating it as an upcoming event would duplicate it.
        var next = windows.Where(window => window.StartUtc > at).OrderBy(window => window.StartUtc).FirstOrDefault();
        if (next is not null)
            target.Add(new KundaliDatedTransit(graha, eventType, next.Sign, next.StartUtc));
    }

    public async Task<int> DeleteForProfileAsync(ClaimsPrincipal user, Guid profileId, CancellationToken token)
    {
        var accountId = await profiles.GetRegisteredAccountIdAsync(user, token);

        // No ownership pre-read: the delete is account-scoped, so a foreign profile deletes nothing and reports zero -
        // which is the same answer as a profile that never existed. A pre-read would add a round trip and a timing
        // difference between "exists but not yours" and "does not exist".
        return await repository.DeleteForProfileAsync(accountId, profileId, token);
    }


    public async Task<KundaliAshtakavargaSection> GetAshtakavargaAsync(ClaimsPrincipal user, Guid profileId,
        CancellationToken token)
    {
        var display = await ReadOwnedDisplayAsync(user, profileId, token);
        var metadata = await metadataReader.GetAsync(user, token);

        var engine = calculationEngine.Calculate(new KundaliEngineInput(
            display.BirthDate, display.BirthTimeMode, display.BirthTime, display.Latitude, display.Longitude,
            display.TimezoneId, metadata.Ayanamsa));

        var unsupported = new List<KundaliUnsupportedField>();

        // Ashtakavarga needs a sign position for all seven scored planets plus the ascendant. Any gap makes the whole
        // table wrong rather than partially right, so nothing is scored unless every reference is available - a
        // partial table would look complete and be silently incorrect.
        var signByReference = SignByReference(engine, unsupported);

        if (signByReference.Count < AshtakavargaScored().Count + 1)
        {
            // The cause is an unvalidated ascendant, not an inexact birth time: the birth time may be perfectly
            // exact and the table still cannot be built. Labelling it requires-exact-birth-time would tell a user to
            // go and fix a birth time that is already correct, in the one place this module is trying hardest to be
            // honest about why something is missing.
            unsupported.Add(new KundaliUnsupportedField(nameof(KundaliAshtakavargaSection.Bhinna),
                KundaliUnsupportedReasons.UnvalidatedReference));
            unsupported.Add(new KundaliUnsupportedField(nameof(KundaliAshtakavargaSection.Sarva),
                KundaliUnsupportedReasons.UnvalidatedReference));

            return new KundaliAshtakavargaSection(ashtakavargaRules.RulesVersion,
                new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal), [],
                new KundaliAshtakavargaTotals(new Dictionary<string, int>(StringComparer.Ordinal), 0),
                unsupported);
        }

        var bhinna = ashtakavargaRules.ScoredPlanets.ToDictionary(
            planet => planet,
            planet => ashtakavargaRules.Bhinna(planet, signByReference),
            StringComparer.Ordinal);

        // Sarva is derived from the bhinnas inside the rules, so the reconciliation the contract demands holds by
        // construction rather than by two implementations agreeing.
        var sarva = ashtakavargaRules.Sarva(bhinna.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(),
            StringComparer.Ordinal));

        var perPlanet = bhinna.ToDictionary(pair => pair.Key, pair => pair.Value.Sum(), StringComparer.Ordinal);

        return new KundaliAshtakavargaSection(
            ashtakavargaRules.RulesVersion,
            bhinna.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<int>)pair.Value, StringComparer.Ordinal),
            sarva,
            new KundaliAshtakavargaTotals(perPlanet, sarva.Sum()),
            unsupported);
    }

    /// <summary>
    /// Maps each reference point to its sign index. The ascendant is deliberately excluded: it is withheld as
    /// unvalidated-reference, so no Ashtakavarga table can be scored and the caller receives a gap instead.
    /// </summary>
    private static Dictionary<string, int> SignByReference(KundaliEngineResult engine,
        List<KundaliUnsupportedField> unsupported)
    {
        var signs = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var graha in engine.GrahaPositions ?? [])
        {
            if (graha.Sign is null || graha.SiderealLongitude is not { } longitude)
                continue;

            signs[graha.Graha] = ((int)Math.Floor(longitude / 30.0) % 12 + 12) % 12;
        }

        // The ascendant is required by every classical table and is not yet validated, so its absence is reported with
        // its own reason rather than folded into a generic gap.
        unsupported.Add(new KundaliUnsupportedField("Ascendant", KundaliUnsupportedReasons.UnvalidatedReference));
        return signs;
    }

    private static IReadOnlyList<string> AshtakavargaScored() => AshtakavargaScoredPlanets;

    private static readonly string[] AshtakavargaScoredPlanets =
        ["Sun", "Moon", "Mars", "Mercury", "Jupiter", "Venus", "Saturn"];


    private static KundaliView EmptyView(Guid profileId) => new(
        new KundaliAggregate(Guid.Empty, profileId, null, 0, DateTimeOffset.MinValue, DateTimeOffset.MinValue),
        null);

    /// <summary>
    /// Stamps the revision identity and the current/stale verdict onto a view (KUN-21). The verdict is decided here, from
    /// the profile revision the caller just read, so no consumer has to reconstruct the rule from hashes and statuses.
    /// </summary>
    private static KundaliView Stamped(KundaliView view, Guid accountId, long profileRevision,
        string ashtakavargaRulesVersion, string shadbalaRulesVersion, string yogaRulesVersion)
    {
        var revision = view.CurrentRevision;
        var inputRevision = revision is null ? 0 : view.InputRevision;
        var isCurrent = revision is
        {
            Status: KundaliGenerationStatus.Succeeded,
            SupersededByRevision: null
        } && inputRevision == profileRevision;

        var revisionKey = revision is null || inputRevision == 0
            ? null
            : KundaliCacheKeys.RevisionKey(accountId, view.Aggregate.ProfileId, inputRevision, revision.Provenance,
                RuleSetVersions(revision.Provenance, ashtakavargaRulesVersion, shadbalaRulesVersion, yogaRulesVersion));

        return view with
        {
            ProfileRevision = profileRevision,
            InputRevision = inputRevision,
            IsCurrent = isCurrent,
            RevisionKey = revisionKey
        };
    }

    private static KundaliPreferences DefaultPreferences() => new("North", 0, DateTimeOffset.MinValue);
    /// <summary>
    /// KUN-13: why Sade Sati has nothing to report at the reference instant, when that is a fact about time rather
    /// than a gap in the calculation. Kept here so the UI has one vocabulary for "not yet" and one for "not known".
    /// </summary>
    private static class SadeSatiPhaseNames
    {
        /// <summary>A following interval exists, but every phase of it starts after the reference instant.</summary>
        public const string AfterInterval = "not-yet-active";
    }

    /// <summary>
    /// KUN-17 (api#57) report request. The request is validated against both closed vocabularies before anything is
    /// stored, the source revision is bound at request time, and generation runs inline so the caller never polls a
    /// record that no one will ever settle.
    /// </summary>
    /// <remarks>
    /// Validation precedes the store for a reason: a rejected section must cost nothing and must not leave a queued
    /// record behind. The store de-duplicates on (profile, revision), so a re-request for the same revision is free
    /// rather than a second render.
    /// </remarks>
    public async Task<KundaliReport> RequestReportAsync(
        ClaimsPrincipal user, Guid profileId, KundaliReportRequest request, CancellationToken token)
    {
        var accountId = await profiles.GetRegisteredAccountIdAsync(user, token);
        var snapshot = await ReadOwnedSnapshotAsync(user, profileId, token);

        var validated = KundaliReportRequestValidator.Validate(request);
        var queued = await reportStore.EnqueueAsync(accountId, profileId, validated, snapshot.ProfileRevision,
            clock, token);

        return await ProduceAsync(accountId, queued, token);
    }

    /// <summary>
    /// KUN-17 report read. A report the caller does not own is reported as missing, not forbidden: distinguishing the
    /// two would confirm that a report id exists for someone else's profile.
    /// </summary>
    /// <remarks>
    /// The download URL is issued here, not at generation: its expiry is the minimum of the issue window and the
    /// report's own lifetime, and an instant baked in at generation would already be wrong when a caller asked.
    /// </remarks>
    public async Task<KundaliReport> GetReportAsync(
        ClaimsPrincipal user, Guid profileId, Guid reportId, CancellationToken token)
    {
        var accountId = await profiles.GetRegisteredAccountIdAsync(user, token);
        var stored = await reportStore.FindAsync(accountId, profileId, reportId, token)
            ?? throw new ResourceNotFoundException(KundaliErrors.ReportNotFound);

        return stored is { Status: KundaliReportStatus.Ready, DownloadUrl: not null }
            ? stored with
            {
                DownloadExpiresAtUtc = KundaliReportDownloadLease.EffectiveExpiry(clock.GetUtcNow(), ReportLifetime)
            }
            : stored;
    }

    /// <summary>
    /// Runs the renderer and settles the record. Every failure is a value from the closed
    /// <see cref="KundaliReportFailureCodes"/> set - never an exception message - so nothing a renderer says can reach
    /// a user as server prose.
    /// </summary>
    private async Task<KundaliReport> ProduceAsync(Guid accountId, KundaliReport queued, CancellationToken token)
    {
        if (!reportRenderer.IsAvailable)
            return await SettleFailedAsync(accountId, queued, KundaliReportFailureCodes.RendererUnavailable, token);

        KundaliReportRenderResult rendered;
        try
        {
            // Rebuilt from the stored record rather than a caller's request object: what renders must be what was
            // admitted, so a store's own copy is the only safe input here.
            var admitted = new KundaliReportRequest(queued.Variant, queued.Sections);
            rendered = await reportRenderer.RenderAsync(accountId, queued.ProfileId, admitted, queued.Revision,
                token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // A renderer that throws is a generation failure, not a server error the caller can act on. The exception
            // message is deliberately not propagated: it is free text, and this contract has no field for free text.
            return await SettleFailedAsync(accountId, queued, KundaliReportFailureCodes.GenerationFailed, token);
        }

        if (!rendered.Succeeded)
            return await SettleFailedAsync(accountId, queued,
                rendered.FailureCode ?? KundaliReportFailureCodes.GenerationFailed, token);

        // Empty sections are reported alongside a ready document, so a caller can tell a short report from a complete
        // one. They do not fail the report: the sections that DID render are still worth reading.
        var failures = rendered.EmptySections
            .Select(section => new KundaliReportFailure(section, KundaliReportFailureCodes.SectionEmpty))
            .ToArray();

        return await reportStore.SettleAsync(accountId, queued.ProfileId, queued.ReportId,
            KundaliReportStatus.Ready, rendered.DocumentReference, null, failures, clock, token);
    }

    private Task<KundaliReport> SettleFailedAsync(Guid accountId, KundaliReport queued, string code,
        CancellationToken token) =>
        reportStore.SettleAsync(accountId, queued.ProfileId, queued.ReportId, KundaliReportStatus.Failed, null, null,
            [new KundaliReportFailure(string.Empty, code)], clock, token);


    /// <summary>
    /// KUN-16 remedies (api#128). The production emission rule is applied here: a remedy is never written beside a
    /// finding that was not evaluated. The read-time resolver is not applied here - see RemedyEmission's remarks for
    /// why only one of the two may act at this layer.
    /// </summary>
    /// <remarks>
    /// Findings are taken from the current engine result, so the status carried on each emitted remedy is this run's
    /// status. Nothing withheld or unassessed is emitted; each suppressed finding is reported through
    /// <c>Unsupported</c> instead, merged with the capability path's entries so neither producer loses its own.
    /// </remarks>
    public async Task<KundaliRemediesSection> GetRemediesAsync(ClaimsPrincipal user, Guid profileId,
        CancellationToken token)
    {
        var display = await ReadOwnedDisplayAsync(user, profileId, token);
        var metadata = await metadataReader.GetAsync(user, token);

        var engine = calculationEngine.Calculate(new KundaliEngineInput(
            display.BirthDate, display.BirthTimeMode, display.BirthTime, display.Latitude, display.Longitude,
            display.TimezoneId, metadata.Ayanamsa));

        // Capability-path entries: a graha without a published longitude. Kept separate so the merge below has two
        // genuine producers rather than one list pretending to be two.
        var capabilityUnsupported = (engine.GrahaPositions ?? [])
            .Where(position => !position.SiderealLongitude.HasValue)
            .Select(position => new KundaliUnsupportedField(position.Graha,
                KundaliUnsupportedReasons.UnvalidatedReference))
            .ToArray();

        // Every finding the yoga/dosha evaluation knows about, with its state as of this run. A finding absent from the
        // evaluated set but present in the not-evaluated set is NotEvaluated; one in neither is simply not offered.
        var evaluation = yogaRules.Evaluate(BuildSignByGraha(engine));
        var stateByCode = new Dictionary<string, RemedyFindingState>(StringComparer.Ordinal);
        foreach (var assessed in evaluation.Evaluated)
            stateByCode[assessed.Code] = RemedyFindingState.Evaluated;
        foreach (var missed in evaluation.NotEvaluated)
            stateByCode[missed.Code] = RemedyFindingState.NotEvaluated;

        var candidates = RemedyCatalogue.All
            .Select(remedy => (remedy, state: stateByCode.TryGetValue(remedy.FindingCode, out var state)
                ? state
                : RemedyFindingState.NotEvaluated));

        var (emitted, emissionUnsupported) = RemedyEmission.Select(candidates);

        // Findings and catalogue compared HERE, not in RemedyEmission.Select: that loop iterates the CATALOGUE, so a
        // finding the catalogue has no entry for is never visited and cannot be reported from there. This is the only
        // place where both sets are in hand.
        //
        // Only a positively Evaluated finding can produce CatalogueMissing. A NotEvaluated finding stays NotEvaluated
        // even when the catalogue is also empty: "we did not assess it" is a fact about the engine, "the catalogue has
        // nothing" is only a fact about us, and the engine fact outranks it. Without this filter the new outcome would
        // absorb the old distinction instead of adding to it.
        var catalogueMissing = stateByCode
            .Where(finding => finding.Value == RemedyFindingState.Evaluated
                && RemedyCatalogue.For(finding.Key) is null)
            .Select(finding => new KundaliUnsupportedField(finding.Key,
                Reasons.ToWire(RemedyReason.CatalogueMissing)))
            .ToArray();

        // api#154: the same shape one row down. A finding the engine could NOT assess, whose code the catalogue does
        // not carry, was likewise never visited - invisible in both lists. Its reason is InputMissing, not
        // CatalogueMissing: "we did not assess it" is a fact about the engine and outranks "the catalogue has nothing",
        // which is only a fact about us. The distinction is the whole point of this issue, so it is asserted on the
        // wire text rather than on presence alone.
        var unassessableWithoutEntry = stateByCode
            .Where(finding => finding.Value == RemedyFindingState.NotEvaluated
                && RemedyCatalogue.For(finding.Key) is null)
            .Select(finding => new KundaliUnsupportedField(finding.Key,
                Reasons.ToWire(RemedyReason.InputMissing)))
            .ToArray();

        return new KundaliRemediesSection(
            RemedyCatalogue.Version,
            emitted,
            RemedyEmission.MergeUnsupported(capabilityUnsupported, emissionUnsupported, catalogueMissing,
                unassessableWithoutEntry));
    }

    /// <summary>Sign index 0..11 per graha, for the rule engine. Grahas without a longitude are omitted.</summary>
    private static Dictionary<string, int> BuildSignByGraha(KundaliEngineResult engine)
    {
        var signByGraha = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var position in engine.GrahaPositions ?? [])
        {
            if (position.SiderealLongitude is not null && position.Sign is not null)
                signByGraha[position.Graha] = SignIndex(position.Sign);
        }

        return signByGraha;
    }

}
