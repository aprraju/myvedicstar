using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using MyVedicStar.Kundali.Business;
using MyVedicStar.Kundali.Shared;
using MyVedicStar.Shared.Exceptions;
using MyVedicStar.VedicProfiles.Shared;
using NSubstitute;

namespace MyVedicStar.Kundali.Tests.Unit;

/// <summary>
/// KUN-15 compare-two-profiles behaviour at the facade: pair identity, ownership, order independence and the
/// deliberately absent verdict threshold.
/// </summary>
/// <remarks>
/// These test the rules the endpoint promises, not the koota arithmetic - that is <see cref="AshtakootaTests"/>' job.
/// The distinction matters because the arithmetic can be right while the pair cache serves B-to-A from A-to-B's entry
/// with the wrong orientation, which no arithmetic test would notice.
/// </remarks>
public sealed class KundaliComparisonFacadeTests : IDisposable
{
    private readonly ClaimsPrincipal user = new(new ClaimsIdentity([], "test"));
    private readonly Guid accountId = Guid.NewGuid();
    private readonly Guid profileA = new("11111111-1111-1111-1111-111111111111");
    private readonly Guid profileB = new("22222222-2222-2222-2222-222222222222");
    private readonly IKundaliProfileReader profiles = Substitute.For<IKundaliProfileReader>();
    private readonly IKundaliRepository repository = Substitute.For<IKundaliRepository>();
    private readonly IKundaliCalculationEngine engine = Substitute.For<IKundaliCalculationEngine>();
    private readonly ServiceProvider provider;
    private readonly IKundaliFacade facade;
    private static readonly CancellationToken Token = CancellationToken.None;

    public KundaliComparisonFacadeTests()
    {
        profiles.GetRegisteredAccountIdAsync(user, Token).Returns(accountId);

        // Both profiles are owned by default; individual tests withdraw one to prove the 404.
        profiles.GetSnapshotAsync(user, profileA, Token).Returns(Snapshot(revision: 3));
        profiles.GetSnapshotAsync(user, profileB, Token).Returns(Snapshot(revision: 7));

        repository.GetComparisonAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<long>(), Arg.Any<long>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((KundaliStoredComparison?)null);

        // A REAL engine result with a Moon longitude. Without this the facade would catch the stub's lack of a result
        // and report every category as unavailable - correct behaviour, but it would make this suite assert the
        // unavailable path while claiming to test the scored one.
        engine.Calculate(Arg.Any<KundaliEngineInput>())
            .Returns(new KundaliEngineResult(125.4, 125.4, 4, 4, null, null, null, []));

        provider = KundaliTestProvider.Create()
            .With(profiles)
            .With(repository)
            .With<TimeProvider>(new FixedClock())
            .With(Substitute.For<IKundaliResultCache>())
            .With(engine)
            .With<IKundaliAshtakavargaRules>(new UnregisteredAshtakavargaRules())
            .With<IKundaliShadbalaRules>(new UnregisteredShadbalaRules())
            .With<IKundaliYogaRules>(new UnregisteredYogaRules())
            .With<IKundaliAshtakootaRules>(new FixedAshtakootaRules())
            .Build();

        facade = provider.CreateScope().ServiceProvider.GetRequiredService<IKundaliFacade>();
    }

    [Fact]
    public async Task Comparing_a_profile_with_itself_is_a_400_and_never_a_comparison()
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            facade.CompareAsync(user, profileA, profileA, Token));
    }

    [Fact]
    public async Task Comparing_when_either_profile_is_foreign_is_not_found_and_indistinguishable()
    {
        profiles.GetSnapshotAsync(user, profileB, Token).Returns((KundaliInputSnapshot?)null);

        // The second profile failing must surface exactly as a missing one would, whether it is A or B that is foreign.
        await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
            facade.CompareAsync(user, profileA, profileB, Token));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
            facade.CompareAsync(user, profileB, profileA, Token));
    }

    [Fact]
    public async Task Reversed_selection_returns_the_same_pair_key_and_identical_scores()
    {
        var forward = await facade.CompareAsync(user, profileA, profileB, Token);
        var reversed = await facade.CompareAsync(user, profileB, profileA, Token);

        // The pair identity is order-independent, and so is everything derived from it: an ordered pair would be two
        // cache entries for one question, and (because Varna is directional) two different totals from one pair.
        Assert.Equal(forward.PairKey, reversed.PairKey);

        Assert.Equal(forward.ProfileA.ProfileId, reversed.ProfileA.ProfileId);
        Assert.Equal(forward.ProfileB.ProfileId, reversed.ProfileB.ProfileId);

        // Canonical orientation: profileA sorts lower, so it is "A" whichever order was requested.
        Assert.Equal(profileA, forward.ProfileA.ProfileId);
        Assert.Equal(profileB, forward.ProfileB.ProfileId);

        Assert.Equal(forward.Total, reversed.Total);
        Assert.Equal(forward.Categories.Select(c => c.Score), reversed.Categories.Select(c => c.Score));
    }

    [Fact]
    public async Task The_pair_key_is_both_identifiers_sorted_ordinally()
    {
        var comparison = await facade.CompareAsync(user, profileB, profileA, Token);

        // Written out rather than described, so a change to the key format is a failing test instead of a silent
        // invalidation of every stored comparison.
        Assert.Equal($"{profileA:D}:{profileB:D}", comparison.PairKey);
    }

    [Fact]
    public async Task The_response_reports_scores_but_no_verdict_because_the_threshold_is_an_open_owner_decision()
    {
        var comparison = await facade.CompareAsync(user, profileA, profileB, Token);

        // The score IS reported, with the maximum it is out of.
        Assert.InRange(comparison.Total, 0, comparison.ScoredMaximum);
        Assert.Equal(36, comparison.Maximum);
        Assert.Equal("complete", comparison.Certainty);

        // And no verdict travels with it: the type has no field that could express one.
        string[] forbidden = ["Verdict", "Threshold", "IsAcceptable", "IsMatch", "Recommendation"];
        var names = typeof(KundaliAshtakootaComparison).GetProperties().Select(p => p.Name).ToArray();
        Assert.DoesNotContain(names, name => forbidden.Contains(name, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task An_unknown_birth_time_makes_the_moon_dependent_categories_indeterminate_not_zero()
    {
        profiles.GetSnapshotAsync(user, profileB, Token).Returns(Snapshot(revision: 7, mode: BirthTimeMode.Unknown));

        var comparison = await facade.CompareAsync(user, profileA, profileB, Token);

        // Partial, not complete: the caller is told the total is a partial one rather than being given a number that
        // looks whole.
        Assert.Equal("partial", comparison.Certainty);
        Assert.Equal(0, comparison.ScoredMaximum);

        // Every category names its reason and its side. Crucially NONE is scored 0: "scored zero" and "not computed"
        // must stay distinguishable, or the UI renders a false negative.
        Assert.All(comparison.Categories, category =>
        {
            Assert.False(category.IsScored);
            Assert.Null(category.Score);
            Assert.Equal("BirthTimeUnknown", category.Reason);
            Assert.Equal("B", category.Side);
        });
    }

    [Fact]
    public async Task A_stored_comparison_is_served_for_its_exact_identity()
    {
        await facade.CompareAsync(user, profileA, profileB, Token);

        // The write carries the identity the read will look up: pairKey, both revisions and the rule version.
        await repository.Received(1).StoreComparisonAsync(accountId, Arg.Is<KundaliStoredComparison>(stored =>
            stored.PairKey == $"{profileA:D}:{profileB:D}"
            && stored.RevisionA == 3 && stored.RevisionB == 7
            && stored.RulesVersion == FixedAshtakootaRules.Version), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_new_revision_on_either_side_is_a_cache_miss_not_a_stale_hit()
    {
        // The identity includes both revisions, so a bumped revision cannot find the old row.
        repository.GetComparisonAsync(accountId, Arg.Any<string>(), 3, 7, FixedAshtakootaRules.Version,
            Arg.Any<CancellationToken>()).Returns(new KundaliStoredComparison(
            $"{profileA:D}:{profileB:D}", profileA, profileB, 3, 7, FixedAshtakootaRules.Version,
            System.Text.Json.JsonSerializer.Serialize(new KundaliAshtakootaComparison(
                $"{profileA:D}:{profileB:D}", new KundaliComparisonSide(profileA, 3), new KundaliComparisonSide(profileB, 7),
                [], 10, 36, 36, "complete", new KundaliManglikComparison("known", "known", "note"),
                FixedAshtakootaRules.Version),
                new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                })));

        var served = await facade.CompareAsync(user, profileA, profileB, Token);
        Assert.Equal(10, served.Total);

        profiles.GetSnapshotAsync(user, profileB, Token).Returns(Snapshot(revision: 8));
        var recomputed = await facade.CompareAsync(user, profileA, profileB, Token);

        // Revision 8 is a different identity, so the stored row for (3,7) must not answer it.
        Assert.NotEqual(10, recomputed.Total);
    }

    private static KundaliInputSnapshot Snapshot(long revision, BirthTimeMode mode = BirthTimeMode.Known) =>
        new(new DateOnly(1990, 4, 12), mode, new TimeOnly(6, 15, 0), new TimeOnly(6, 15, 0),
            new TimeOnly(6, 15, 0), 17.385m, 78.4867m, "Asia/Kolkata", revision);

    public void Dispose() => provider.Dispose();

    /// <summary>Deterministic koota rules so the facade tests do not depend on the real tables' values.</summary>
    private sealed class FixedAshtakootaRules : IKundaliAshtakootaRules
    {
        public const string Version = "ashtakoota-test-1";

        public string RulesVersion => Version;

        public AshtakootaAssessment Assess(AshtakootaSide sideA, AshtakootaSide sideB)
        {
            var available = sideA.Available && sideB.Available;
            var categories = AshtakootaShape.Codes.Select(code => new AshtakootaCategoryResult(
                code, 4, available ? 4 : null, available ? null : AshtakootaReasons.BirthTimeUnknown,
                available ? null : "B", available ? "scored" : "not evaluated")).ToArray();

            return new AshtakootaAssessment(Version, categories, available ? 32 : 0,
                available ? AshtakootaCertainty.Complete : AshtakootaCertainty.Partial);
        }
    }
}
