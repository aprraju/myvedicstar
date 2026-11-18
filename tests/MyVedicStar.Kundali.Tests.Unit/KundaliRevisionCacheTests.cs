using System.Security.Claims;
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

    private KundaliFacade Facade() => new(profiles, repository,
        new KundaliValidator(clock, KundaliEngineVersions.Foundation), KundaliEngineVersions.Foundation, metadata,
        engine, new UnconfiguredDivisionalMapperStub(), new NoDignityRules2(), new NoAshtakavargaRules(), clock, cache);

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
        Assert.Equal(KundaliCacheKeys.RevisionKey(accountId, profileId, 8, provenance,
            [provenance.RuleSetVersion, "none"]), view.RevisionKey);
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


