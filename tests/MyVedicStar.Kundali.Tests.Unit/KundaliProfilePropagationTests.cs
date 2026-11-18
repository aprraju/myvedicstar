using System.Security.Claims;
using MyVedicStar.Kundali.Business;
using MyVedicStar.Kundali.Shared;
using MyVedicStar.Shared.Exceptions;
using MyVedicStar.VedicProfiles.Shared;
using NSubstitute;

namespace MyVedicStar.Kundali.Tests.Unit;

/// <summary>
/// KUN-22 profile-edit propagation and rejection of obsolete background results. The engine call count is asserted
/// directly: "a rename costs zero calculations" is the property that would silently regress if a display change were
/// classified as a birth-input change.
/// </summary>
public sealed class KundaliProfilePropagationTests
{
    private readonly ClaimsPrincipal user = new(new ClaimsIdentity([], "test"));
    private readonly Guid accountId = Guid.NewGuid();
    private readonly Guid profileId = Guid.NewGuid();
    private readonly Guid revisionId = Guid.NewGuid();
    private readonly IKundaliProfileReader profiles = Substitute.For<IKundaliProfileReader>();
    private readonly IKundaliRepository repository = Substitute.For<IKundaliRepository>();
    private readonly IKundaliAstronomyMetadataReader metadata = Substitute.For<IKundaliAstronomyMetadataReader>();
    private readonly IKundaliCalculationEngine engine = Substitute.For<IKundaliCalculationEngine>();
    private readonly IKundaliResultCache cache = Substitute.For<IKundaliResultCache>();
    private static readonly CancellationToken Token = CancellationToken.None;

    private static readonly KundaliAstronomyMetadata Metadata =
        new("analytical", "analytical", "Lahiri", "WholeSign", "Mean", "Sidereal");

    public KundaliProfilePropagationTests()
    {
        profiles.GetRegisteredAccountIdAsync(user, Token).Returns(accountId);
        profiles.GetSnapshotAsync(user, profileId, Token).Returns(Snapshot(profileRevision: 8));
        profiles.GetAyanamsaAsync(user, Token).Returns("Lahiri");
        metadata.GetAsync(user, Token).Returns(Metadata);
    }

    private KundaliFacade Facade() => new(profiles, repository,
        new KundaliValidator(new FixedClock(), KundaliEngineVersions.Foundation), KundaliEngineVersions.Foundation,
        metadata, engine, new NoDivisions(), new NoDignityRules(), new NoAshtakavargaRulesPropagation(), new NoShadbalaRulesPropagation(), new FixedClock(), cache);

    private static KundaliInputSnapshot Snapshot(long profileRevision) =>
        new(new DateOnly(1990, 4, 12), BirthTimeMode.Known, new TimeOnly(6, 15, 0), new TimeOnly(6, 15, 0),
            new TimeOnly(6, 15, 0), 13.0827m, 80.2707m, "Asia/Kolkata", profileRevision);

    private sealed class NoDivisions : IKundaliDivisionalMapper
    {
        public bool IsImplemented(int divisional) => false;
        public int SignIn(int divisional, double siderealLongitude) =>
            throw new ArgumentOutOfRangeException(nameof(divisional));
    }

    private static KundaliProvenance Provenance() =>
        new(KundaliEngineVersions.Foundation.Engine, KundaliEngineVersions.Foundation.Ephemeris,
            KundaliEngineVersions.Foundation.RuleSet, KundaliEngineVersions.Foundation.TimezoneData, "Lahiri");

    // ---- edit propagation -------------------------------------------------------------------------------------

    [Fact]
    public async Task A_birth_input_edit_invalidates_and_abandons_the_in_flight_revision()
    {
        repository.ApplyProfileChangeAsync(accountId, profileId, 7, 8, KundaliProfileChangeKinds.BirthInput, Token)
            .Returns(new KundaliProfileChangeResult(profileId, 8, true, false, 4, revisionId));

        var result = await Facade().ApplyProfileChangeAsync(user, profileId, 8,
            KundaliProfileChangeKinds.BirthInput, Token);

        Assert.True(result.Applied);
        Assert.False(result.DisplayOnly);
        Assert.Equal(revisionId, result.AbandonedRevisionId);
        Assert.Equal(4, result.AggregateRevision);
    }

    [Fact]
    public async Task A_name_only_edit_calls_the_engine_zero_times()
    {
        repository.ApplyProfileChangeAsync(accountId, profileId, 7, 8, KundaliProfileChangeKinds.Display, Token)
            .Returns(new KundaliProfileChangeResult(profileId, 8, true, true, 3, null));

        var result = await Facade().ApplyProfileChangeAsync(user, profileId, 8, KundaliProfileChangeKinds.Display,
            Token);

        Assert.True(result.DisplayOnly);
        Assert.Null(result.AbandonedRevisionId);
        engine.DidNotReceive().Calculate(Arg.Any<KundaliEngineInput>());
        engine.DidNotReceive().CalculateDashaTimeline(Arg.Any<KundaliDashaQuery>());
        // Nothing calculation-related is invalidated for a rename.
        await cache.DidNotReceive().RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_calculation_settings_edit_invalidates_the_cached_section()
    {
        repository.ApplyProfileChangeAsync(accountId, profileId, 7, 8,
                KundaliProfileChangeKinds.CalculationSettings, Token)
            .Returns(new KundaliProfileChangeResult(profileId, 8, true, false, 4, null));

        await Facade().ApplyProfileChangeAsync(user, profileId, 8, KundaliProfileChangeKinds.CalculationSettings,
            Token);

        await cache.Received(1).RemoveAsync(Arg.Any<string>(), Token);
    }

    [Fact]
    public async Task A_duplicate_or_replayed_edit_is_reported_as_not_applied()
    {
        repository.ApplyProfileChangeAsync(accountId, profileId, 7, 8, KundaliProfileChangeKinds.BirthInput, Token)
            .Returns(new KundaliProfileChangeResult(profileId, 8, false, false, 4, null));

        var result = await Facade().ApplyProfileChangeAsync(user, profileId, 8,
            KundaliProfileChangeKinds.BirthInput, Token);

        Assert.False(result.Applied);
    }

    [Fact]
    public async Task The_profile_revision_to_advance_from_is_read_from_the_profile_not_from_the_caller()
    {
        repository.ApplyProfileChangeAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<long>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new KundaliProfileChangeResult(profileId, 8, true, false, 4, null));

        // The profile says it is at revision 8, so the change is 7 -> 8 whatever a caller might claim.
        await Facade().ApplyProfileChangeAsync(user, profileId, 999, KundaliProfileChangeKinds.BirthInput, Token);

        await repository.Received(1).ApplyProfileChangeAsync(accountId, profileId, 7, 8,
            KundaliProfileChangeKinds.BirthInput, Token);
    }

    [Fact]
    public async Task An_unknown_change_kind_is_rejected()
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            Facade().ApplyProfileChangeAsync(user, profileId, 8, "made-up", Token));
    }

    // ---- obsolete background results ---------------------------------------------------------------------------

    [Fact]
    public async Task A_result_computed_for_an_earlier_revision_is_discarded()
    {
        repository.GetAsync(accountId, profileId, 8, Token).Returns(View(1));
        repository.GetSectionsAsync(accountId, profileId, revisionId, Token)
            .Returns(new KundaliStoredSections(revisionId, 3, 7, KundaliGenerationStatus.Running, true,
                "{\"overview\":true}", null, null));

        var disposition = await Facade().DiscardObsoleteResultAsync(user, profileId, revisionId, Token);

        Assert.False(disposition.Accepted);
        Assert.Equal(KundaliBackgroundResultDisposition.Reasons.ProfileRevisionChanged, disposition.ReasonCode);
        Assert.Equal(7, disposition.ResultProfileRevision);
        Assert.Equal(8, disposition.CurrentProfileRevision);
    }

    [Fact]
    public async Task A_result_for_the_present_revision_is_accepted()
    {
        repository.GetAsync(accountId, profileId, 8, Token).Returns(View(2, Provenance()));
        repository.GetSectionsAsync(accountId, profileId, revisionId, Token)
            .Returns(new KundaliStoredSections(revisionId, 2, 8, KundaliGenerationStatus.Running, true,
                "{}", null, null));

        var disposition = await Facade().DiscardObsoleteResultAsync(user, profileId, revisionId, Token);

        Assert.True(disposition.Accepted);
        Assert.Null(disposition.ReasonCode);
    }

    [Fact]
    public async Task A_result_computed_under_an_older_version_tuple_is_discarded()
    {
        // The stored result was produced by an older engine; the configured versions are the current foundation ones.
        repository.GetAsync(accountId, profileId, 8, Token).Returns(
            View(2, Provenance() with { EngineVersion = "kundali-foundation-older" }));
        repository.GetSectionsAsync(accountId, profileId, revisionId, Token)
            .Returns(new KundaliStoredSections(revisionId, 2, 8, KundaliGenerationStatus.Running, false,
                "{}", null, null));

        var disposition = await Facade().DiscardObsoleteResultAsync(user, profileId, revisionId, Token);

        Assert.False(disposition.Accepted);
        Assert.Equal(KundaliBackgroundResultDisposition.Reasons.ProvenanceChanged, disposition.ReasonCode);
    }

    [Fact]
    public async Task A_result_for_a_superseded_revision_is_discarded()
    {
        // The aggregate's active revision has already moved on to revision 2.
        var activeId = Guid.NewGuid();
        repository.GetAsync(accountId, profileId, 8, Token).Returns(new KundaliView(
            new KundaliAggregate(Guid.NewGuid(), profileId, activeId, 2, DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch),
            new KundaliRevisionSummary(activeId, 2, KundaliGenerationStatus.Succeeded, null, Provenance(), "hash",
                false, false, [], DateTimeOffset.UnixEpoch),
            ProfileRevision: 8, InputRevision: 8));
        repository.GetSectionsAsync(accountId, profileId, revisionId, Token)
            .Returns(new KundaliStoredSections(revisionId, 1, 8, KundaliGenerationStatus.Succeeded, false,
                "{}", null, null));

        var disposition = await Facade().DiscardObsoleteResultAsync(user, profileId, revisionId, Token);

        Assert.False(disposition.Accepted);
        Assert.Equal(KundaliBackgroundResultDisposition.Reasons.Superseded, disposition.ReasonCode);
    }

    [Fact]
    public async Task An_unknown_revision_is_discarded_rather_than_accepted()
    {
        repository.GetAsync(accountId, profileId, 8, Token).Returns(View(2));
        repository.GetSectionsAsync(accountId, profileId, revisionId, Token)
            .Returns((KundaliStoredSections?)null);

        var disposition = await Facade().DiscardObsoleteResultAsync(user, profileId, revisionId, Token);

        Assert.False(disposition.Accepted);
        Assert.Equal(KundaliBackgroundResultDisposition.Reasons.NotFound, disposition.ReasonCode);
    }

    private KundaliView View(long revision, KundaliProvenance? provenance = null) => new(
        new KundaliAggregate(Guid.NewGuid(), profileId, revisionId, revision, DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch),
        new KundaliRevisionSummary(revisionId, revision, KundaliGenerationStatus.Running, null,
            provenance ?? Provenance(), "hash", false, true, [], DateTimeOffset.UnixEpoch),
        ProfileRevision: 8, InputRevision: 8);

    // ---- the classification rule itself ------------------------------------------------------------------------

    [Theory]
    [InlineData(KundaliProfileChangeKinds.Display, true)]
    [InlineData(KundaliProfileChangeKinds.BirthInput, false)]
    [InlineData(KundaliProfileChangeKinds.CalculationSettings, false)]
    [InlineData("something-new", false)]
    public void Only_a_display_change_is_exempt_from_recalculation(string kind, bool displayOnly)
    {
        // An unrecognised kind is treated as a real change: failing to recalculate is recoverable, serving a result
        // computed from different inputs is not.
        Assert.Equal(displayOnly, KundaliProfileChangeKinds.IsDisplayOnly(kind));
    }
}

/// <summary>Rules are irrelevant to these tests; a stub keeps the assertion set focused.</summary>
internal sealed class NoDignityRules : IKundaliDignityRules
{
    public IReadOnlyList<string> DignityStates => [];
    public string DignityOf(string planet, double siderealLongitude) => string.Empty;
    public IReadOnlyList<int> AspectedSigns(string planet, int fromSign) => [];
}



internal sealed class NoAshtakavargaRulesPropagation : IKundaliAshtakavargaRules
{
    public string RulesVersion => "none";
    public IReadOnlyList<string> ScoredPlanets => [];
    public IReadOnlyList<int> Bhinna(string planet, IReadOnlyDictionary<string, int> signByReference) => [];
    public IReadOnlyList<int> Sarva(IReadOnlyDictionary<string, int[]> bhinnaByPlanet) => [];
}

internal sealed class NoShadbalaRulesPropagation : IKundaliShadbalaRules
{
    public string RulesVersion => "none";
    public string Unit => "rupas";
    public IReadOnlyList<string> ScoredPlanets => [];
    public IReadOnlyDictionary<string, string> WithheldComponents =>
        new Dictionary<string, string>(StringComparer.Ordinal);
    public IReadOnlyList<KundaliShadbalaResult> Compute(IReadOnlyDictionary<string, double> longitudeByPlanet) => [];
}
