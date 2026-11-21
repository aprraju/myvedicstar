using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using MyVedicStar.Kundali.Business;
using MyVedicStar.Kundali.Shared;
using MyVedicStar.Shared.Exceptions;
using MyVedicStar.VedicProfiles.Shared;
using NSubstitute;

namespace MyVedicStar.Kundali.Tests.Unit;

/// <summary>
/// KUN-21 revision-aware caching. Each test pins one property that would let a stale result be served if it were wrong:
/// the key covers the profile revision and the version tuple, a cache failure is a miss rather than an error, an expired
/// dasha highlight is not served, and a payload computed for a superseded revision is discarded.
/// </summary>
public sealed class KundaliRevisionCacheTests
{
    private readonly ClaimsPrincipal user = new(new ClaimsIdentity([], "test"));
    private readonly Guid accountId = Guid.NewGuid();
    private readonly Guid profileId = Guid.NewGuid();
    private readonly IKundaliProfileReader profiles = Substitute.For<IKundaliProfileReader>();
    private readonly IKundaliRepository repository = Substitute.For<IKundaliRepository>();
    private readonly IKundaliAstronomyMetadataReader metadata = Substitute.For<IKundaliAstronomyMetadataReader>();
    private readonly IKundaliCalculationEngine engine = Substitute.For<IKundaliCalculationEngine>();
    private readonly IKundaliResultCache cache = Substitute.For<IKundaliResultCache>();
    private readonly IKundaliAlertRepository alerts = Substitute.For<IKundaliAlertRepository>();
    private readonly MutableClock clock = new();
    private static readonly CancellationToken Token = CancellationToken.None;

    private static readonly KundaliAstronomyMetadata Metadata =
        new("analytical", "analytical", "Lahiri", "WholeSign", "Mean", "Sidereal");

    private static readonly KundaliDasha CurrentPeriod =
        new("Venus", "Mahadasha", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero));

    public KundaliRevisionCacheTests()
    {
        profiles.GetRegisteredAccountIdAsync(user, Token).Returns(accountId);
        profiles.GetSnapshotAsync(user, profileId, Token).Returns(Snapshot(profileRevision: 7));
        profiles.GetDisplayAsync(user, profileId, Token).Returns(Display());
        profiles.GetAyanamsaAsync(user, Token).Returns("Lahiri");
        metadata.GetAsync(user, Token).Returns(Metadata);
        engine.Calculate(Arg.Any<KundaliEngineInput>())
            .Returns(new KundaliEngineResult(null, null, null, null,
                [new KundaliEngineDasha(CurrentPeriod.Lord, CurrentPeriod.StartUtc, CurrentPeriod.EndUtc, false)],
                null, null, []));
    }

    /// <summary>
    /// api#124: built from the container rather than by enumerating the facade's constructor, so an added parameter is
    /// one registration in <see cref="KundaliTestProvider"/> instead of an edit in every test file.
    /// </summary>
    private IKundaliFacade Facade() => KundaliTestProvider.Create()
        .With(profiles)
        .With(repository)
        .With(alerts)
        .With(metadata)
        .With(engine)
        .With(cache)
        // The mutable clock must reach the validator as well as the facade, so it is registered once as the clock port
        // rather than passed twice. Two instances would let a test advance one and not the other.
        .With<TimeProvider>(clock)
        .With<IKundaliDivisionalMapper>(new UnconfiguredDivisionalMapperStub())
        .With<IKundaliDignityRules>(new NoDignityRules2())
        .With<IKundaliAshtakavargaRules>(new NoAshtakavargaRules())
        .With<IKundaliShadbalaRules>(new NoShadbalaRules())
        .With<IKundaliYogaRules>(new NoYogaRules())
        .BuildFacade();

    private static KundaliInputSnapshot Snapshot(long profileRevision = 1) =>
        new(new DateOnly(1990, 4, 12), BirthTimeMode.Known, new TimeOnly(6, 15, 0), new TimeOnly(6, 15, 0),
            new TimeOnly(6, 15, 0), 13.0827m, 80.2707m, "Asia/Kolkata", profileRevision);

    private static KundaliProfileDisplay Display() =>
        new("Asha", new DateOnly(1990, 4, 12), BirthTimeMode.Known, new TimeOnly(6, 15, 0), new TimeOnly(6, 15, 0),
            new TimeOnly(6, 15, 0), "Chennai", 13.0827m, 80.2707m, "Asia/Kolkata");

    private sealed class UnconfiguredDivisionalMapperStub : IKundaliDivisionalMapper
    {
        public bool IsImplemented(int divisional) => false;
        public int SignIn(int divisional, double siderealLongitude) =>
            throw new ArgumentOutOfRangeException(nameof(divisional));
    }

    // ---- key identity -----------------------------------------------------------------------------------------

    [Fact]
    public void The_key_changes_when_the_profile_revision_changes()
    {
        var provenance = KundaliProvenance();
        var first = KundaliCacheKeys.RevisionKey(accountId, profileId, 7, provenance, ["foundation-0", "ashtakavarga-0"]);
        var second = KundaliCacheKeys.RevisionKey(accountId, profileId, 8, provenance, ["foundation-0", "ashtakavarga-0"]);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void The_key_changes_when_any_version_or_setting_changes()
    {
        var baseline = KundaliProvenance();
        var key = KundaliCacheKeys.RevisionKey(accountId, profileId, 7, baseline, ["foundation-0", "ashtakavarga-0"]);

        Assert.NotEqual(key, KundaliCacheKeys.RevisionKey(accountId, profileId, 7,
            baseline with { EngineVersion = "kundali-foundation-1" }, ["foundation-0", "ashtakavarga-0"]));
        Assert.NotEqual(key, KundaliCacheKeys.RevisionKey(accountId, profileId, 7,
            baseline with { EphemerisVersion = "analytical-2" }, ["foundation-0", "ashtakavarga-0"]));
        Assert.NotEqual(key, KundaliCacheKeys.RevisionKey(accountId, profileId, 7,
            baseline with { RuleSetVersion = "foundation-1" }, ["foundation-0", "ashtakavarga-0"]));
        Assert.NotEqual(key, KundaliCacheKeys.RevisionKey(accountId, profileId, 7,
            baseline with { TimezoneDataVersion = "2026b" }, ["foundation-0", "ashtakavarga-0"]));
        Assert.NotEqual(key, KundaliCacheKeys.RevisionKey(accountId, profileId, 7,
            baseline with { Ayanamsa = "Raman" }, ["foundation-0", "ashtakavarga-0"]));
    }

    [Fact]
    public void The_key_changes_when_the_ashtakavarga_rule_version_changes()
    {
        // KUN-11 tables are versioned independently of the engine rule set. A table change alters published numbers,
        // so it must invalidate the cache; if the key covered only RuleSetVersion it would silently reuse them.
        var provenance = KundaliProvenance();
        var before = KundaliCacheKeys.RevisionKey(accountId, profileId, 7, provenance,
            ["foundation-0", "ashtakavarga-0"]);
        var after = KundaliCacheKeys.RevisionKey(accountId, profileId, 7, provenance,
            ["foundation-0", "ashtakavarga-1"]);

        Assert.NotEqual(before, after);
    }

    [Fact]
    public void The_key_is_namespaced_by_account_so_one_account_can_never_read_another()
    {
        var provenance = KundaliProvenance();
        var mine = KundaliCacheKeys.RevisionKey(accountId, profileId, 7, provenance, ["foundation-0", "ashtakavarga-0"]);
        var theirs = KundaliCacheKeys.RevisionKey(Guid.NewGuid(), profileId, 7, provenance, ["foundation-0", "ashtakavarga-0"]);

        Assert.NotEqual(mine, theirs);
        // Hashed before storage: the account id is an input to the key, never part of the key text. Assert.Contains on the
        // raw id would be asserting the opposite of the privacy property this test exists to protect.
        Assert.DoesNotContain(accountId.ToString("N"), mine, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(profileId.ToString("N"), mine, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_key_carries_no_birth_input_and_is_stable_across_calls()
    {
        var provenance = KundaliProvenance();
        var key = KundaliCacheKeys.RevisionKey(accountId, profileId, 7, provenance, ["foundation-0", "ashtakavarga-0"]);

        // Stable: two hosts must agree, or a shared L2 entry would never be found.
        Assert.Equal(key, KundaliCacheKeys.RevisionKey(accountId, profileId, 7, provenance, ["foundation-0", "ashtakavarga-0"]));
        // Hashed: no date, time, coordinate, name or timezone appears in the key.
        Assert.DoesNotContain("1990", key, StringComparison.Ordinal);
        Assert.DoesNotContain("Kolkata", key, StringComparison.Ordinal);
        Assert.DoesNotContain("13.", key, StringComparison.Ordinal);
        Assert.True(KundaliCacheKeys.IsWellFormed(key), key + " len=" + key.Length);
    }

    // ---- rule-set registration (KUN-12) ---------------------------------------------------------------------

    [Fact]
    public void A_shadbala_rule_change_produces_a_different_cache_key()
    {
        // KUN-12 registers Shadbala's own rule set alongside Ashtakavarga's. If that registration is ever dropped during
        // a merge, a Shadbala rule change would silently reuse strengths computed under the old rules - the section
        // would look current while being stale. This pins the behaviour the registration exists for: the version tuple
        // is part of the key, so changing it MUST change the key.
        var provenance = KundaliProvenance();

        var key = KundaliCacheKeys.RevisionKey(accountId, profileId, 7, provenance,
            ["foundation-0", "ashtakavarga-0", "shadbala-classical-partial-1"]);
        var changed = KundaliCacheKeys.RevisionKey(accountId, profileId, 7, provenance,
            ["foundation-0", "ashtakavarga-0", "shadbala-classical-partial-2"]);

        Assert.NotEqual(key, changed);
    }

    [Fact]
    public void A_yoga_rule_change_produces_a_different_cache_key()
    {
        // KUN-10 registers the yoga catalogue as another independently versioned rule set. Without it, a catalogue
        // change would leave assessments cached under the old rules being served as current - the same staleness the
        // Ashtakavarga and Shadbala entries exist to prevent.
        var provenance = KundaliProvenance();

        var key = KundaliCacheKeys.RevisionKey(accountId, profileId, 7, provenance,
            ["foundation-0", "ashtakavarga-0", "shadbala-classical-partial-1", "yogas-classical-1"]);
        var changed = KundaliCacheKeys.RevisionKey(accountId, profileId, 7, provenance,
            ["foundation-0", "ashtakavarga-0", "shadbala-classical-partial-1", "yogas-classical-2"]);

        Assert.NotEqual(key, changed);
    }

    [Fact]
    public void Omitting_the_shadbala_rule_set_changes_the_key()
    {
        // Guards the fan-out rather than a single version: a key built without the Shadbala rule set (the pre-rebase
        // shape) must not collide with one that includes it. If this ever passes as EQUAL, the registration has been
        // lost and stale bala results would be served as current.
        var provenance = KundaliProvenance();

        var withShadbala = KundaliCacheKeys.RevisionKey(accountId, profileId, 7, provenance,
            ["foundation-0", "ashtakavarga-0", "shadbala-classical-partial-1"]);
        var withoutShadbala = KundaliCacheKeys.RevisionKey(accountId, profileId, 7, provenance,
            ["foundation-0", "ashtakavarga-0"]);

        Assert.NotEqual(withShadbala, withoutShadbala);
        Assert.True(KundaliCacheKeys.IsWellFormed(withShadbala));
    }

    private static KundaliProvenance KundaliProvenance() =>
        new(KundaliEngineVersions.Foundation.Engine, KundaliEngineVersions.Foundation.Ephemeris,
            KundaliEngineVersions.Foundation.RuleSet, KundaliEngineVersions.Foundation.TimezoneData, "Lahiri");

    // ---- section read behaviour -------------------------------------------------------------------------------

    [Fact]
    public async Task A_miss_computes_once_and_caches_under_the_revision_key()
    {
        cache.GetAsync<KundaliOverview>(Arg.Any<string>(), Token).Returns((KundaliOverview?)null);

        var overview = await Facade().GetOverviewAsync(user, profileId, Token);

        Assert.Equal(7, overview.Freshness.ProfileRevision);
        Assert.Equal(CurrentPeriod.StartUtc, overview.CurrentDasha!.StartUtc);
        // The dasha boundary, not an arbitrary lifetime, is what the entry expires at.
        Assert.Equal(CurrentPeriod.EndUtc, overview.Freshness.CurrentDashaExpiresAtUtc);
        await cache.Received(1).SetAsync(Arg.Any<string>(), Arg.Any<KundaliOverview>(),
            CurrentPeriod.EndUtc, "kundali", Token);
    }

    [Fact]
    public async Task A_warm_read_is_served_from_the_cache_without_recomputing()
    {
        var warm = await Facade().GetOverviewAsync(user, profileId, Token);
        cache.GetAsync<KundaliOverview>(Arg.Any<string>(), Token).Returns(warm);
        engine.ClearReceivedCalls();

        var served = await Facade().GetOverviewAsync(user, profileId, Token);

        Assert.Same(warm, served);
        engine.DidNotReceive().Calculate(Arg.Any<KundaliEngineInput>());
    }

    [Fact]
    public async Task An_edit_after_the_cache_was_written_is_never_served()
    {
        // Revision 7 while the result is produced (initial read and compare-and-swap re-read), then 8 afterwards.
        var revision = 7L;
        profiles.GetSnapshotAsync(user, profileId, Token).Returns(_ =>
            Task.FromResult<KundaliInputSnapshot?>(Snapshot(profileRevision: revision)));

        var stale = await Facade().GetOverviewAsync(user, profileId, Token);
        Assert.Equal(7, stale.Freshness.ProfileRevision);
        cache.GetAsync<KundaliOverview>(Arg.Any<string>(), Token).Returns(stale);

        revision = 8;
        // Explicit resets: NSubstitute keeps the FIRST matching return for a non-Args-specific stub, so the mutable
        // delegate above is restored here rather than assumed to be re-evaluated.
        profiles.GetSnapshotAsync(user, profileId, Token).Returns(_ =>
            Task.FromResult<KundaliInputSnapshot?>(Snapshot(profileRevision: 8)));
        cache.GetAsync<KundaliOverview>(Arg.Any<string>(), Token).Returns((KundaliOverview?)null);

        var fresh = await Facade().GetOverviewAsync(user, profileId, Token);

        // The cached copy is keyed on revision 7; the read for revision 8 can never resolve to it.
        Assert.Equal(8, fresh.Freshness.ProfileRevision);
        Assert.NotEqual(stale.Freshness.RevisionKey, fresh.Freshness.RevisionKey);
    }

    [Fact]
    public async Task A_cached_copy_past_its_dasha_boundary_is_discarded()
    {
        var boundary = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var expired = new KundaliOverview(profileId, KundaliBirthDetailsStub(), KundaliConventions(),
            null, null, null, new KundaliDasha("Venus", "Mahadasha", CurrentPeriod.StartUtc, boundary), [], [])
        {
            Freshness = new KundaliResultFreshness(profileId, 7, "kundali:" + new string('A', 64), "hash",
                KundaliProvenance(), boundary.AddHours(-1), boundary)
        };
        cache.GetAsync<KundaliOverview>(Arg.Any<string>(), Token).Returns(expired);
        clock.Now = boundary.AddMinutes(1);
        // The recomputation must also land inside a period, or the highlight would be absent for a different reason.
        engine.Calculate(Arg.Any<KundaliEngineInput>())
            .Returns(new KundaliEngineResult(null, null, null, null,
                [new KundaliEngineDasha("Venus", boundary.AddMinutes(-1), boundary.AddMonths(6), false)],
                null, null, []));

        var overview = await Facade().GetOverviewAsync(user, profileId, Token);

        // Recomputed, not served: the highlight it carried no longer describes "now".
        Assert.NotSame(expired, overview);
        Assert.NotEqual(expired.Freshness.AsOfUtc, overview.Freshness.AsOfUtc);
    }

    [Fact]
    public async Task A_cache_failure_degrades_to_a_recompute_and_never_to_an_error()
    {
        cache.GetAsync<KundaliOverview>(Arg.Any<string>(), Token)
            .Returns<KundaliOverview>(_ => throw new InvalidOperationException("cache backend down"));

        var overview = await Facade().GetOverviewAsync(user, profileId, Token);

        Assert.Equal(profileId, overview.ProfileId);
    }

    [Fact]
    public async Task A_profile_edit_during_computation_discards_the_payload()
    {
        // First read sees revision 7; the re-read after computing sees 8.
        var reads = 0;
        profiles.GetSnapshotAsync(user, profileId, Token).Returns(_ =>
        {
            reads++;
            return Task.FromResult<KundaliInputSnapshot?>(Snapshot(profileRevision: reads == 1 ? 7 : 8));
        });

        await Assert.ThrowsAsync<KundaliConflictException>(() => Facade().GetOverviewAsync(user, profileId, Token));
        // Nothing computed for the abandoned revision is written.
        await cache.DidNotReceive().SetAsync(Arg.Any<string>(), Arg.Any<KundaliOverview>(), Arg.Any<DateTimeOffset>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private static KundaliBirthDetails KundaliBirthDetailsStub() =>
        new("Asha", new DateOnly(1990, 4, 12), KundaliBirthTimeMode.Exact, new TimeOnly(6, 15, 0), null, null,
            "Chennai", 13.0827m, 80.2707m, "Asia/Kolkata");

    private static KundaliOverviewConventions KundaliConventions() =>
        new("analytical", "analytical", "Lahiri", "WholeSign", "Mean", "Sidereal");

    // ---- view stamping ----------------------------------------------------------------------------------------

    [Fact]
    public async Task A_result_computed_from_an_older_revision_is_reported_stale_and_not_current()
    {
        var provenance = KundaliProvenance();
        profiles.GetSnapshotAsync(user, profileId, Token).Returns(Snapshot(profileRevision: 9));
        repository.GetAsync(accountId, profileId, 9, Token).Returns(new KundaliView(
            new KundaliAggregate(Guid.NewGuid(), profileId, Guid.NewGuid(), 3, DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch),
            new KundaliRevisionSummary(Guid.NewGuid(), 3, KundaliGenerationStatus.Succeeded, null, provenance,
                "hash", IsStale: true, IsPartial: false, [], DateTimeOffset.UnixEpoch),
            ProfileRevision: 9, InputRevision: 8));

        var view = await Facade().GetAsync(user, profileId, Token);

        Assert.False(view.IsCurrent);
        Assert.Equal(9, view.ProfileRevision);
        Assert.Equal(8, view.InputRevision);
        Assert.NotNull(view.RevisionKey);
    }

    [Fact]
    public async Task A_result_computed_from_the_present_revision_is_current()
    {
        var provenance = KundaliProvenance();
        profiles.GetSnapshotAsync(user, profileId, Token).Returns(Snapshot(profileRevision: 8));
        repository.GetAsync(accountId, profileId, 8, Token).Returns(new KundaliView(
            new KundaliAggregate(Guid.NewGuid(), profileId, Guid.NewGuid(), 3, DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch),
            new KundaliRevisionSummary(Guid.NewGuid(), 3, KundaliGenerationStatus.Succeeded, null, provenance,
                "hash", IsStale: false, IsPartial: false, [], DateTimeOffset.UnixEpoch),
            ProfileRevision: 8, InputRevision: 8));

        var view = await Facade().GetAsync(user, profileId, Token);

        Assert.True(view.IsCurrent);
        // The facade builds the key from the rules it actually has registered, which in this fixture is the stub
        // ("none"), not a literal - so the expectation must come from the same place the production code does.
        // The rules list must now carry Shadbala's version too: KUN-12 registered it as a third independently
        // versioned rule set. This expectation is still derived from the same source the production code uses
        // (the registered stub versions), not from a literal, so it fails if the fan-out changes shape again.
        Assert.Equal(KundaliCacheKeys.RevisionKey(accountId, profileId, 8, provenance,
            [provenance.RuleSetVersion, "none", "none", "none"]), view.RevisionKey);
    }
}

/// <summary>Deterministic clock; the tests decide "now" so a dasha boundary comparison is exact.</summary>
internal sealed class MutableClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
}

internal sealed class NoDignityRules2 : IKundaliDignityRules
{
    public IReadOnlyList<string> DignityStates => [];
    public string DignityOf(string planet, double siderealLongitude) => string.Empty;
    public IReadOnlyList<int> AspectedSigns(string planet, int fromSign) => [];
}



internal sealed class NoAshtakavargaRules : IKundaliAshtakavargaRules
{
    public string RulesVersion => "none";
    public IReadOnlyList<string> ScoredPlanets => [];
    public IReadOnlyList<int> Bhinna(string planet, IReadOnlyDictionary<string, int> signByReference) => [];
    public IReadOnlyList<int> Sarva(IReadOnlyDictionary<string, int[]> bhinnaByPlanet) => [];


}

internal sealed class NoShadbalaRules : IKundaliShadbalaRules
{
    public string RulesVersion => "none";
    public string Unit => "rupas";
    public IReadOnlyList<string> ScoredPlanets => [];
    public IReadOnlyDictionary<string, string> WithheldComponents =>
        new Dictionary<string, string>(StringComparer.Ordinal);
    public IReadOnlyList<KundaliShadbalaResult> Compute(IReadOnlyDictionary<string, double> longitudeByPlanet) => [];
}
internal sealed class NoYogaRules : IKundaliYogaRules
{
    public string RulesVersion => "none";

    public IReadOnlyList<string> Statuses => [];

    public IReadOnlyList<string> Catalogue => [];

    public KundaliYogaEvaluation Evaluate(IReadOnlyDictionary<string, int> signByGraha) =>
        new("none", [], []);
}


/// <summary>
/// The fallback rules report the version "unconfigured". This proves that string can never back a cacheable payload:
/// with no rules registered the section is returned empty and explanation-only, and the scoring path is never reached.
/// </summary>
/// <remarks>
/// The guard this relies on is <c>signByReference.Count &lt; AshtakavargaScored().Count + 1</c>, i.e. n &lt; 8. That is
/// unconditionally true <b>today only because the ascendant is withheld</b>: <c>SignByReference</c> builds its map
/// from <c>engine.GrahaPositions</c> alone and deliberately adds an <c>unvalidated-reference</c> gap for the
/// ascendant instead of a value, so the map holds at most the 7 scored planets. When the ascendant is validated and
/// added, n reaches 8, this guard starts passing, and the unconfigured path no longer short-circuits here. Such a
/// guard is *supposed* to pass then - but anyone relying on this invariant must re-check it, and must not delete
/// this test on the assumption that the guard is permanently fail-closed. The one thing that stays true regardless
/// is the second mechanism: <c>Bhinna</c>/<c>Sarva</c> throw rather than zero-fill.
/// </remarks>
public sealed class KundaliUnconfiguredAshtakavargaTests
{
    [Fact]
    public async Task The_unconfigured_rules_never_produce_scored_data_under_the_unconfigured_version()
    {
        var rules = new UnconfiguredAshtakavargaRules();
        var unsupported = new List<KundaliUnsupportedField>();

        // Empty scored-planets is what makes the facade take its cannot-build branch before any Bhinna/Sarva call.
        // Note that branch is reachable today because the ascendant is withheld, not because 8 references are
        // structurally impossible; see the remarks above.
        Assert.Empty(rules.ScoredPlanets);

        // And the scoring calls themselves refuse rather than return zero-filled tables, so a future caller that
        // bypasses the branch fails loudly instead of publishing a plausible all-zero Ashtakavarga.
        Assert.Throws<InvalidOperationException>(() => rules.Bhinna("Sun", new Dictionary<string, int>()));
        Assert.Throws<InvalidOperationException>(() =>
            rules.Sarva(new Dictionary<string, int[]>()));

        // api#149: the version itself refuses. The string "unconfigured" used to answer here, before any scoring call
        // threw, and the facade reads it on the ordinary path to build every result's revision key - so the fallback
        // contributed to identity while claiming to be unconfigured.
        Assert.Throws<InvalidOperationException>(() => rules.RulesVersion);
        _ = unsupported;
        await Task.CompletedTask;
    }
}

/// <summary>
/// api#149. A fallback may not contribute a <c>RulesVersion</c> that reaches identity. Every unconfigured rule set used
/// to answer <c>"unconfigured"</c> here, which meant every unconfigured result keyed identically to every other
/// unconfigured result, and an unconfigured result could not be told apart from one under a different future rule set.
/// </summary>
/// <remarks>
/// The assertions are deliberately ordered so a <c>try</c>/<c>catch</c> around the cache write cannot satisfy them: the
/// request must throw, and the cache must hold nothing afterwards. A catch that swallowed the throw and still produced
/// the key would leave an entry, and the second assertion would be red.
/// </remarks>
public sealed class KundaliUnconfiguredRulesVersionTests
{
    private readonly ClaimsPrincipal user = new(new ClaimsIdentity([], "test"));
    private readonly Guid accountId = Guid.NewGuid();
    private readonly Guid profileId = Guid.NewGuid();
    private readonly IKundaliProfileReader profiles = Substitute.For<IKundaliProfileReader>();
    private readonly IKundaliRepository repository = Substitute.For<IKundaliRepository>();
    private readonly IKundaliAstronomyMetadataReader metadata = Substitute.For<IKundaliAstronomyMetadataReader>();
    private readonly IKundaliCalculationEngine engine = Substitute.For<IKundaliCalculationEngine>();
    private readonly IKundaliAlertRepository alerts = Substitute.For<IKundaliAlertRepository>();
    private readonly MutableClock clock = new();

    private static readonly KundaliAstronomyMetadata Metadata =
        new("analytical", "analytical", "Lahiri", "WholeSign", "Mean", "Sidereal");

    private static KundaliInputSnapshot Snapshot(long profileRevision) =>
        new(new DateOnly(1990, 4, 12), BirthTimeMode.Known, new TimeOnly(6, 15, 0), new TimeOnly(6, 15, 0),
            new TimeOnly(6, 15, 0), 13.0827m, 80.2707m, "Asia/Kolkata", profileRevision);

    private static KundaliProfileDisplay Display() =>
        new("Asha", new DateOnly(1990, 4, 12), BirthTimeMode.Known, new TimeOnly(6, 15, 0), new TimeOnly(6, 15, 0),
            new TimeOnly(6, 15, 0), "Chennai", 13.0827m, 80.2707m, "Asia/Kolkata");

    [Fact]
    public void Every_unconfigured_rule_set_refuses_to_name_a_version()
    {
        Assert.Throws<InvalidOperationException>(() => new UnconfiguredAshtakavargaRules().RulesVersion);
        Assert.Throws<InvalidOperationException>(() => new UnconfiguredShadbalaRules().RulesVersion);
        Assert.Throws<InvalidOperationException>(() => new UnconfiguredYogaRules().RulesVersion);
    }

    /// <summary>
    /// The fallback must fail where the version is asked for, not where a cache key is built - and the failure must
    /// leave nothing behind. Three separate entry points are exercised because they reach the version tuple at
    /// different points: the overview reads it before its first cache read, a generate path reaches it after repository
    /// work, and a profile-change invalidation reaches it on the removal path.
    /// </summary>
    [Theory]
    [InlineData("overview")]
    [InlineData("generate")]
    [InlineData("invalidate")]
    public async Task An_unconfigured_rules_fallback_fails_the_request_and_writes_no_cache_entry(string entryPoint)
    {
        var (facade, cache) = UnconfiguredRulesFacade();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            if (entryPoint == "overview")
                await facade.GetOverviewAsync(user, profileId, CancellationToken.None);
            else if (entryPoint == "generate")
                await facade.GenerateAsync(user, profileId, CancellationToken.None);
            else
                await facade.ApplyProfileChangeAsync(user, profileId, 7,
                    KundaliProfileChangeKinds.BirthInput, CancellationToken.None);
        });

        // "No entry exists" asserted positively: a `try`/`catch` around the cache write satisfies the throw assertion
        // above and still produces the key once. The recorded key lists are what make that red rather than invisible.
        Assert.Empty(cache.WrittenKeys);
    }

    /// <summary>
    /// The positive control for the invalidation assertions. <see cref="RecordingResultCache.RemovedKeys"/> is fed only by
    /// <c>RemoveAsync</c>, so "nothing was written" above - and in the invalidate case of the theory - only means something
    /// if this same cache, in this same fixture shape, records the removal when the rule sets are registered. A cache that
    /// could never record a removal would make those assertions pass vacuously. Proved by flipping registerRules to false:
    /// this test then fails on the fallback's throw, which is the only difference between the two cases.
    /// </summary>
    [Fact]
    public async Task A_registered_rules_fallback_does_record_the_invalidation_it_performs()
    {
        var (facade, cache) = UnconfiguredRulesFacade(registerRules: true);

        var result = await facade.ApplyProfileChangeAsync(user, profileId, 7,
            KundaliProfileChangeKinds.BirthInput, CancellationToken.None);

        Assert.False(result.DisplayOnly);
        // Exactly one removal, and no store: a birth-input change invalidates exactly the one cached payload it dirtied.
        // The count is asserted rather than the key's text because the key is a hash; pinning its text would pin the
        // hashing scheme instead of the behaviour.
        Assert.Single(cache.RemovedKeys);
        Assert.StartsWith("kundali:", Assert.Single(cache.RemovedKeys), StringComparison.Ordinal);
        Assert.Single(cache.WrittenKeys);
    }

    /// <summary>Builds a facade whose only gap is the rule registrations, so the failure is attributable to them.</summary>
    private (IKundaliFacade Facade, RecordingResultCache Cache) UnconfiguredRulesFacade(
        bool registerRules = false)
    {
        var profiles = Substitute.For<IKundaliProfileReader>();
        profiles.GetRegisteredAccountIdAsync(user, CancellationToken.None).Returns(accountId);
        profiles.GetSnapshotAsync(user, profileId, CancellationToken.None).Returns(Snapshot(profileRevision: 7));
        profiles.GetDisplayAsync(user, profileId, CancellationToken.None).Returns(Display());
        profiles.GetAyanamsaAsync(user, CancellationToken.None).Returns("Lahiri");

        var metadata = Substitute.For<IKundaliAstronomyMetadataReader>();
        metadata.GetAsync(user, CancellationToken.None).Returns(Metadata);

        // The engine is stubbed to return no placements rather than being left as a bare substitute whose Calculate
        // answers null: an unstubbed engine fails the overview on a null dereference before the version tuple is read,
        // and the throw assertion would then be satisfied by the wrong defect.
        engine.Calculate(Arg.Any<KundaliEngineInput>()).Returns(new KundaliEngineResult(
            null, null, null, null, [], null, null, [], []));

        // An empty view is returned rather than null: the generate path reads the aggregate's revision to build the
        // next one, so a null here would fail before the version tuple is ever asked for.
        repository.GetAsync(accountId, profileId, Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(new KundaliView(new KundaliAggregate(Guid.NewGuid(), profileId, Guid.NewGuid(), 7,
                DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch),
                new KundaliRevisionSummary(Guid.NewGuid(), 7, KundaliGenerationStatus.Succeeded, null,
                    new KundaliProvenance("kundali-foundation-0", "analytical", "foundation-0", "host", "Lahiri"),
                    Snapshot(7).ComputeHash(), IsStale: false, IsPartial: false, CompletedSections: [],
                    DateTimeOffset.UnixEpoch)));

        // The aggregate add is the last step of a generate for a profile that already has a current revision; it must
        // return a view with a revision, or the stamping step dereferences null before the version tuple is asked for.
        repository.AddRevisionAsync(accountId, profileId, Arg.Any<long>(), Arg.Any<KundaliInputSnapshot>(),
                Arg.Any<KundaliProvenance>(), Arg.Any<CancellationToken>())
            .Returns(new KundaliView(new KundaliAggregate(Guid.NewGuid(), profileId, Guid.NewGuid(), 8,
                DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch),
                new KundaliRevisionSummary(Guid.NewGuid(), 8, KundaliGenerationStatus.Pending, null,
                    new KundaliProvenance("kundali-foundation-0", "analytical", "foundation-0", "host", "Lahiri"),
                    Snapshot(7).ComputeHash(), IsStale: false, IsPartial: false, CompletedSections: [],
                    DateTimeOffset.UnixEpoch)));

        // The invalidation path reads the applied change's result before it builds the version tuple; an unstubbed
        // substitute returns null and the test would fail on a null reference instead of on the property under test.
        repository.ApplyProfileChangeAsync(accountId, profileId, Arg.Any<long>(), Arg.Any<long>(),
                KundaliProfileChangeKinds.BirthInput, Arg.Any<CancellationToken>())
            .Returns(new KundaliProfileChangeResult(profileId, 7, Applied: true, DisplayOnly: false,
                AggregateRevision: 8, AbandonedRevisionId: null));

        var cache = new RecordingResultCache();

        var builder = KundaliTestProvider.Create()
            .With(profiles)
            .With(repository)
            .With(alerts)
            .With(metadata)
            .With(engine)
            .With<IKundaliResultCache>(cache)
            .With<TimeProvider>(clock)
            .With<IKundaliDivisionalMapper>(new UnconfiguredDivisionalMapperStubForRulesVersionTests())
            .With<IKundaliDignityRules>(new NoDignityRules2());

        // The control case registers real stand-in rule sets; the theory deliberately leaves them unregistered so the
        // container fallbacks are what refuses. Both use this one builder, so the two differ only in that fact.
        if (registerRules)
            builder = builder
                .With<IKundaliAshtakavargaRules>(new CacheTestAshtakavargaRules())
                .With<IKundaliShadbalaRules>(new CacheTestShadbalaRules())
                .With<IKundaliYogaRules>(new CacheTestYogaRules());

        var facade = builder.BuildFacade();

        return (facade, cache);
    }

    private sealed class UnconfiguredDivisionalMapperStubForRulesVersionTests : IKundaliDivisionalMapper
    {
        public bool IsImplemented(int divisional) => false;

        public int SignIn(int divisional, double siderealLongitude) =>
            throw new ArgumentOutOfRangeException(nameof(divisional));
    }

    private sealed class NoDignityRulesForRulesVersionTests : IKundaliDignityRules
    {
        public IReadOnlyList<string> DignityStates => [];

        public string DignityOf(string planet, double siderealLongitude) => "neutral";

        public IReadOnlyList<int> AspectedSigns(string planet, int fromSign) => [];
    }

    /// <summary>Records every key that reached the cache, so "no entry exists" is an observation rather than an absence.</summary>
    private sealed class RecordingResultCache : IKundaliResultCache
    {
        public List<string> ReadKeys { get; } = [];

        public List<string> WrittenKeys { get; } = [];

        /// <summary>Keys that reached a removal. Kept apart from writes so a test can tell an invalidation from a store.</summary>
        public List<string> RemovedKeys { get; } = [];

        public Task<T?> GetAsync<T>(string key, CancellationToken token) where T : class
        {
            ReadKeys.Add(key);
            return Task.FromResult<T?>(null);
        }

        public Task SetAsync<T>(string key, T value, DateTimeOffset validUntil, string policy,
            CancellationToken token) where T : class
        {
            WrittenKeys.Add(key);
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken token)
        {
            WrittenKeys.Add(key);
            RemovedKeys.Add(key);
            return Task.CompletedTask;
        }
    }
}

/// <summary>Stand-in rule sets for the invalidation control: a nameable version and no data. They exist so the control
/// exercises the version tuple through a registration that answers, rather than through a fallback that refuses.</summary>
internal sealed class CacheTestAshtakavargaRules : IKundaliAshtakavargaRules
{
    public string RulesVersion => "none";
    public IReadOnlyList<string> ScoredPlanets => [];
    public IReadOnlyList<int> Bhinna(string planet, IReadOnlyDictionary<string, int> signByReference) => [];
    public IReadOnlyList<int> Sarva(IReadOnlyDictionary<string, int[]> bhinnaByPlanet) => [];
}

internal sealed class CacheTestShadbalaRules : IKundaliShadbalaRules
{
    public string RulesVersion => "none";
    public string Unit => "rupas";
    public IReadOnlyList<string> ScoredPlanets => [];
    public IReadOnlyDictionary<string, string> WithheldComponents =>
        new Dictionary<string, string>(StringComparer.Ordinal);
    public IReadOnlyList<KundaliShadbalaResult> Compute(IReadOnlyDictionary<string, double> longitudeByPlanet) => [];
}

internal sealed class CacheTestYogaRules : IKundaliYogaRules
{
    public string RulesVersion => "none";
    public IReadOnlyList<string> Statuses => [];
    public IReadOnlyList<string> Catalogue => [];
    public KundaliYogaEvaluation Evaluate(IReadOnlyDictionary<string, int> signByGraha) => new("none", [], []);
}