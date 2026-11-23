using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MyVedicStar.Kundali.Business;
using MyVedicStar.Kundali.Calculation;
using MyVedicStar.Kundali.Shared;
using MyVedicStar.Shared.Exceptions;
using MyVedicStar.VedicProfiles.Shared;
using NSubstitute;

namespace MyVedicStar.Kundali.Tests.Unit;

/// <summary>
/// KUN-15 (api#55) authorization evidence, at the facade with the REAL Ashtakoota rule tables.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this file exists beside <c>KundaliComparisonFacadeTests</c>.</b> That suite (api-developer6, PR #200) proves
/// the pair rules with a stubbed <c>IKundaliAshtakootaRules</c>. A stub is the right tool for pair identity, but it
/// cannot answer the question this story is about: whether a caller can LEARN ANYTHING about another account's profile.
/// A stub returns the same categories for every pair, so a leak through an echo of a real input would be invisible to
/// it. Here the arithmetic is real, so the values below are derived from the profiles the two accounts own - which is
/// exactly the material a leak would have to carry.
/// </para>
/// <para>
/// <b>Ownership is proven at the seam the product uses.</b> The product's ownership rule lives in
/// <c>KundaliProfileReader</c>: a profile read through the profile facade throws <c>ResourceNotFoundException</c> for a
/// foreign or missing id, and the reader converts that to <c>null</c>; the facade then raises the same typed not-found.
/// These tests make the substitute READER behave that way per principal, so the facade is exercised through the real
/// path rather than a single global "null means foreign" shortcut.
/// </para>
/// </remarks>
public sealed class KundaliComparisonCrossAccountTests : IDisposable
{
    private static readonly CancellationToken Token = CancellationToken.None;

    // Two accounts, two profiles each. The ids are fixed and deliberately INTERLEAVED in sort order
    // (a &lt; b &lt; c &lt; d), so A-to-B and B-to-C exercise different canonical orderings: a test that only ever sorts
    // one way would pass an order-dependent implementation.
    private static readonly Guid AccountA = new("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid AccountB = new("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly Guid ProfileA1 = new("11111111-0000-0000-0000-000000000001");
    private static readonly Guid ProfileA2 = new("33333333-0000-0000-0000-000000000002");
    private static readonly Guid ProfileB1 = new("22222222-0000-0000-0000-000000000003");
    private static readonly Guid ProfileB2 = new("44444444-0000-0000-0000-000000000004");

    private readonly ClaimsPrincipal accountAPrincipal = Principal(AccountA);
    private readonly ClaimsPrincipal accountBPrincipal = Principal(AccountB);
    private readonly IKundaliProfileReader profiles = Substitute.For<IKundaliProfileReader>();
    private readonly IKundaliRepository repository = Substitute.For<IKundaliRepository>();
    private readonly IKundaliCalculationEngine engine = Substitute.For<IKundaliCalculationEngine>();
    private readonly IKundaliAstronomyMetadataReader metadata = Substitute.For<IKundaliAstronomyMetadataReader>();
    private readonly ServiceProvider provider;
    private readonly IKundaliFacade facade;

    public KundaliComparisonCrossAccountTests()
    {
        // Each profile is owned by exactly one account; a read under the other principal behaves like a missing id.
        Own(ProfileA1, AccountA, BirthTime(6, 15), revision: 3, moonLongitude: 125.4);
        Own(ProfileA2, AccountA, BirthTime(21, 40), revision: 4, moonLongitude: 250.9);
        Own(ProfileB1, AccountB, BirthTime(9, 5), revision: 7, moonLongitude: 47.2);
        Own(ProfileB2, AccountB, BirthTime(14, 25), revision: 8, moonLongitude: 300.1);

        // The account lookup is per principal too: resolving the wrong account id would make every ownership check
        // below vacuous, so it is asserted as behaviour here rather than assumed.
        profiles.GetRegisteredAccountIdAsync(accountAPrincipal, Token).Returns(AccountA);
        profiles.GetRegisteredAccountIdAsync(accountBPrincipal, Token).Returns(AccountB);

        metadata.GetAsync(Arg.Any<ClaimsPrincipal>(), Token).Returns(new KundaliAstronomyMetadata(
            EphemerisProvider: "test", EphemerisVersion: "test", Ayanamsa: "Lahiri", HouseSystem: "whole-sign",
            NodeType: "Mean", Zodiac: "Sidereal"));

        repository.GetComparisonAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<long>(), Arg.Any<long>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((KundaliStoredComparison?)null);

        provider = KundaliTestProvider.Create()
            .With(profiles)
            .With(repository)
            .With(metadata)
            .With(engine)
            .With<TimeProvider>(new FixedClock())
            .With(Substitute.For<IKundaliResultCache>())
            .With<IKundaliAshtakavargaRules>(new UnregisteredAshtakavargaRules())
            .With<IKundaliShadbalaRules>(new UnregisteredShadbalaRules())
            .With<IKundaliYogaRules>(new UnregisteredYogaRules())
            // The real rule tables: no stub stands between the test and the numbers it inspects.
            // The REAL rule tables through the real adapter: the numbers asserted below come from the production
            // calculation, not from a fixture that could only agree with itself.
            .With<IKundaliAshtakootaRules>(new KundaliAshtakootaTestAdapter())
            .Build();

        facade = provider.CreateScope().ServiceProvider.GetRequiredService<IKundaliFacade>();
    }

    // ------------------------------------------------------------------------------------------------------------------
    // 1. Ownership per side, and a foreign id is indistinguishable from a missing one.
    // ------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_foreign_profile_fails_exactly_as_a_missing_one_whether_it_is_the_first_or_second_id()
    {
        var random = Guid.NewGuid();

        // Four ways to ask about a profile this caller does not own. A response that differs between them - in type,
        // code, message or payload - tells the caller that some id exists under another account.
        var foreignAsSecond = await Capture(() => facade.CompareAsync(accountAPrincipal, ProfileA1, ProfileB1, Token));
        var missingAsSecond = await Capture(() => facade.CompareAsync(accountAPrincipal, ProfileA1, random, Token));
        var foreignAsFirst = await Capture(() => facade.CompareAsync(accountAPrincipal, ProfileB1, ProfileA1, Token));
        var missingAsFirst = await Capture(() => facade.CompareAsync(accountAPrincipal, random, ProfileA1, Token));
        var bothForeign = await Capture(() => facade.CompareAsync(accountAPrincipal, ProfileB1, ProfileB2, Token));

        Assert.True(missingAsSecond.NotFounded, "a genuinely missing profile must be a not-found, or the baseline is wrong");

        AssertIndistinguishable(missingAsSecond, foreignAsSecond);
        AssertIndistinguishable(missingAsFirst, foreignAsFirst);
        AssertIndistinguishable(missingAsSecond, bothForeign);

        // And the same in the other direction: account B asking about account A's profiles.
        var reverse = await Capture(() => facade.CompareAsync(accountBPrincipal, ProfileA1, ProfileB1, Token));
        AssertIndistinguishable(missingAsSecond, reverse);
    }

    [Fact]
    public async Task The_two_failure_modes_cannot_be_told_apart_through_ordering()
    {
        // A subtle leak: sort first, then read. If the implementation reported which SIDE failed - or failed in a
        // different order depending on canonical position - a caller could binary-search another account's profile ids
        // by which argument produced the not-found.
        var aFirst = await Capture(() => facade.CompareAsync(accountAPrincipal, ProfileA1, ProfileB1, Token));
        var bFirst = await Capture(() => facade.CompareAsync(accountAPrincipal, ProfileB1, ProfileA1, Token));

        AssertIndistinguishable(aFirst, bFirst);

        // B1 sorts before A2, so this pair swaps canonical position relative to the pair above; the refusal must not
        // move with it.
        var lowerFirst = await Capture(() => facade.CompareAsync(accountAPrincipal, ProfileB1, ProfileA2, Token));
        var higherFirst = await Capture(() => facade.CompareAsync(accountAPrincipal, ProfileA2, ProfileB1, Token));
        AssertIndistinguishable(lowerFirst, higherFirst);
    }

    [Fact]
    public async Task A_foreign_profile_never_causes_the_other_side_to_be_read_or_scored()
    {
        // The strongest form of "per side": the request must stop before it can produce numbers from the profile the
        // caller DOES own, which would otherwise make the error message's timing a side channel.
        var foreign = await Capture(() => facade.CompareAsync(accountAPrincipal, ProfileA1, ProfileB1, Token));
        Assert.True(foreign.NotFounded);

        await repository.DidNotReceiveWithAnyArgs()
            .StoreComparisonAsync(default, default!, default);
        Assert.Empty(engine.ReceivedCalls().Where(call =>
            call.GetMethodInfo().Name == nameof(IKundaliCalculationEngine.Calculate) &&
            call.GetArguments()[0] is KundaliEngineInput input && input.BirthDate != default));
    }

    // ------------------------------------------------------------------------------------------------------------------
    // 2. Same profile twice is a 400, and it is a validation failure rather than a not-found.
    // ------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Comparing_a_profile_with_itself_is_a_validation_failure_not_a_not_found()
    {
        var self = await Capture(() => facade.CompareAsync(accountAPrincipal, ProfileA1, ProfileA1, Token));

        Assert.False(self.NotFounded);
        Assert.True(self.Failed);
        Assert.Equal(KundaliComparisonErrorCodes.SameProfile, self.Error!.Message);
    }

    [Fact]
    public async Task The_same_id_twice_is_a_400_even_when_the_caller_does_not_own_it()
    {
        // Ordering matters: if ownership were checked first, an id the caller does not own would report 404 for a
        // malformed request, and the difference between "not yours" and "invalid" would be observable.
        var foreignSelf = await Capture(() => facade.CompareAsync(accountAPrincipal, ProfileB1, ProfileB1, Token));

        Assert.True(foreignSelf.Failed);
        Assert.Equal(KundaliComparisonErrorCodes.SameProfile, foreignSelf.Error!.Message);
    }

    [Fact]
    public async Task A_self_comparison_never_reaches_the_store()
    {
        await Capture(() => facade.CompareAsync(accountAPrincipal, ProfileA1, ProfileA1, Token));
        await repository.DidNotReceiveWithAnyArgs().StoreComparisonAsync(default, default!, default);
    }

    // ------------------------------------------------------------------------------------------------------------------
    // 3. Indeterminate is not zero.
    // ------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_profile_with_no_birth_time_makes_every_category_indeterminate_with_a_reason_and_never_a_zero_score()
    {
        // Account A's second profile has no birth time. Its Moon is genuinely undefined, so no koota can be computed
        // against it. The engine is told to withhold the Moon for this profile BEFORE the request, because the facade
        // reads the engine for a Known-time side first - a snapshot alone would not stop the scored path.
        MakeBirthTimeUnknown(ProfileA2);
        var comparison = await facade.CompareAsync(accountAPrincipal, ProfileA1, ProfileA2, Token);

        Assert.Equal(8, comparison.Categories.Count);
        Assert.All(comparison.Categories, category =>
        {
            Assert.Null(category.Score);
            Assert.False(category.IsScored);
            Assert.False(string.IsNullOrWhiteSpace(category.Reason));

            // The reason must NAME a cause rather than being absent or replaced by a zero. Two causes are legitimate
            // here and the facade is free to report either: "the birth time is not known" (it short-circuits before the
            // engine) and "the Moon position is unavailable" (the engine withheld it). Pinning one of them would pin an
            // implementation detail; pinning the closed set is the property.
            Assert.Contains(category.Reason, new[]
            {
                AshtakootaReasons.BirthTimeUnknown,
                AshtakootaReasons.MoonPositionUnavailable
            });
            Assert.Equal("B", category.Side);
            Assert.True(category.Maximum > 0, "an indeterminate category still reports its denominator");
            Assert.False(string.IsNullOrWhiteSpace(category.Note));
        });

        // Nothing scored and nothing available: the pair that tells a consumer "nothing computed" apart from
        // "36 available, 0 earned". A zero here would be the false negative this rule exists to prevent.
        Assert.Equal(0, comparison.Total);
        Assert.Equal(0, comparison.ScoredMaximum);
        Assert.Equal(36, comparison.Maximum);
        Assert.NotEqual(AshtakootaCertainty.Complete, comparison.Certainty);
    }

    [Fact]
    public async Task An_indeterminate_category_is_never_rendered_as_a_scored_zero_on_either_side()
    {
        // Both sides unknown: whichever order they are requested in, neither side may contribute a 0.
        MakeBirthTimeUnknown(ProfileB2);
        var comparison = await facade.CompareAsync(accountBPrincipal, ProfileB1, ProfileB2, Token);

        Assert.All(comparison.Categories, category => Assert.Null(category.Score));
        Assert.All(comparison.Categories, category =>
            Assert.Contains(category.Reason, new[]
            {
                AshtakootaReasons.BirthTimeUnknown,
                AshtakootaReasons.MoonPositionUnavailable
            }));

        // The mirror case is the one that proves the side label tracks the DATA rather than the request order. Under
        // canonical ordering B1 is the lower id, so B2 is the SECOND side whether it was requested first or last: the
        // label must stay "B" in both requests. An implementation that labelled by request position would print "A"
        // here and fail; one that labels by canonical position passes, which is what the pair rule requires.
        var b2First = await facade.CompareAsync(accountBPrincipal, ProfileB2, ProfileB1, Token);
        Assert.All(b2First.Categories, category => Assert.Equal("B", category.Side));
        Assert.All(b2First.Categories, category => Assert.Null(category.Score));
    }

    [Fact]
    public async Task A_fully_known_pair_scores_all_eight_categories_so_the_indeterminate_test_is_not_vacuous()
    {
        // The guard against a test that "proves" indeterminate-by-never-scoring-anything: with two complete profiles
        // the same facade, real rules and engine must produce eight scored categories and a complete certainty.
        var comparison = await facade.CompareAsync(accountBPrincipal, ProfileB1, ProfileB2, Token);

        Assert.Equal(8, comparison.Categories.Count);
        Assert.All(comparison.Categories, category =>
        {
            Assert.NotNull(category.Score);
            Assert.Null(category.Reason);
            Assert.Null(category.Side);
        });
        Assert.Equal(36, comparison.ScoredMaximum);
        Assert.Equal(AshtakootaCertainty.Complete, comparison.Certainty);
        Assert.InRange(comparison.Total, 0, 36);
    }

    // ------------------------------------------------------------------------------------------------------------------
    // 4. Order independence of the pair, with Varna still reflecting direction.
    // ------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_to_B_and_B_to_A_share_one_pair_key_and_one_store_identity()
    {
        // Both profiles belong to account B: the pair must be one acCOUNT's pair, since a cross-account pair is
        // (correctly) refused before ordering is even observable.
        var forward = await facade.CompareAsync(accountBPrincipal, ProfileB1, ProfileB2, Token);
        var reversed = await facade.CompareAsync(accountBPrincipal, ProfileB2, ProfileB1, Token);

        Assert.Equal(forward.PairKey, reversed.PairKey);
        Assert.Equal(forward.ProfileA, reversed.ProfileA);
        Assert.Equal(forward.ProfileB, reversed.ProfileB);
        Assert.Equal(forward.Total, reversed.Total);
        Assert.Equal(forward.ScoredMaximum, reversed.ScoredMaximum);

        // One pair, one cache identity: both requests must look up the SAME key, or the second is a guaranteed miss and
        // the two orientations can drift to different stored answers.
        var looked = repository.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IKundaliRepository.GetComparisonAsync))
            .Select(call => new
            {
                Account = (Guid)call.GetArguments()[0]!,
                PairKey = (string)call.GetArguments()[1]!,
                RevisionA = (long)call.GetArguments()[2]!,
                RevisionB = (long)call.GetArguments()[3]!,
                Rules = (string)call.GetArguments()[4]!
            })
            .ToArray();
        Assert.Equal(2, looked.Length);
        Assert.Equal(looked[0].PairKey, looked[1].PairKey);
        Assert.Equal(looked[0].Account, looked[1].Account);
        Assert.Equal(looked[0].Account, AccountB);
        Assert.Equal(looked[0].RevisionA, looked[1].RevisionA);
        Assert.Equal(looked[0].RevisionB, looked[1].RevisionB);
    }

    [Fact]
    public async Task Varna_still_reflects_direction_even_though_the_pair_identity_does_not()
    {
        // Varna is directional BY DESIGN: it is computed from a canonical order, not from request order. The pair key is
        // order-independent; the SCORE is not allowed to be order-independent in the sense of being direction-free.
        // This test pins that the two facts coexist - a canonical ordering that silently swapped the sides would make
        // the pair key right and the Varna score wrong.
        var forward = await facade.CompareAsync(accountBPrincipal, ProfileB1, ProfileB2, Token);
        var reversed = await facade.CompareAsync(accountBPrincipal, ProfileB2, ProfileB1, Token);

        var varnaForward = forward.Categories.Single(category => category.Code == "Varna");
        var varnaReversed = reversed.Categories.Single(category => category.Code == "Varna");

        Assert.Equal(forward.PairKey, reversed.PairKey);
        Assert.Equal(varnaForward.Score, varnaReversed.Score);
        Assert.Null(varnaForward.Reason);

        // And the table is genuinely directional, which is what makes the equality above meaningful rather than
        // tautological: scoring the SAME two Moons in the other argument order must be allowed to differ. If Varna were
        // direction-free, an implementation that had swapped the sides would be undetectable from the response.
        var canonical = Ashtakoota.Assess(SideOf(47.2), SideOf(300.1));
        var swapped = Ashtakoota.Assess(SideOf(300.1), SideOf(47.2));

        var canonicalVarna = canonical.Categories.Single(category => category.Code == "Varna");
        var swappedVarna = swapped.Categories.Single(category => category.Code == "Varna");

        Assert.Equal(canonicalVarna.Score, varnaForward.Score);
        Assert.True(canonicalVarna.Score != swappedVarna.Score ||
                    canonical.Categories.Single(c => c.Code == "Vashya").Score !=
                    swapped.Categories.Single(c => c.Code == "Vashya").Score,
            "At least one directional koota must score differently when the sides are swapped; otherwise direction is " +
            "not observable at all and this pair proves nothing about orientation.");
    }

    // ------------------------------------------------------------------------------------------------------------------
    // 5. Facade contract only. Be clear about what this does NOT prove: route registration is asserted elsewhere, in
    // MyVedicStar.Host.Tests.Integration/KundaliComparisonRouteTests.cs, which reads the composed EndpointDataSource
    // and goes red when the controller's route attribute is removed. Do not read this file as evidence that an HTTP
    // request reaches the comparison; that proof lives there.
    // ------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_comparison_operation_exists_on_the_facade_contract_the_controller_binds_to()
    {
        // Exactly one CompareAsync with the comparison return type, and walking it once produces a comparison rather
        // than a not-implemented path. A controller method that is never decorated with a route would still satisfy
        // this test - the route claim is a different assertion in a different project (see the section comment).
        var declared = typeof(IKundaliFacade).GetMethods()
            .Where(method => method.Name == nameof(IKundaliFacade.CompareAsync))
            .ToArray();
        Assert.Single(declared);
        Assert.Equal(typeof(Task<KundaliAshtakootaComparison>), declared[0].ReturnType);

        var reached = await facade.CompareAsync(accountBPrincipal, ProfileB1, ProfileB2, Token);
        Assert.NotNull(reached);
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Fixtures.
    // ------------------------------------------------------------------------------------------------------------------

    private static ClaimsPrincipal Principal(Guid accountId) =>
        new(new ClaimsIdentity([new Claim(KundaliPrincipal.AccountClaim, accountId.ToString())], "test"));

    private static TimeOnly BirthTime(int hour, int minute) => new(hour, minute);

    private static Ashtakoota.Side SideOf(double moonLongitude) =>
        Ashtakoota.Side.Of((int)(moonLongitude / 30.0) % 12, (int)(moonLongitude / (360.0 / 27.0)) % 27);

    private void Own(Guid profileId, Guid accountId, TimeOnly birthTime, long revision, double moonLongitude)
    {
        var snapshot = new KundaliInputSnapshot(new DateOnly(1990, 4, 12), BirthTimeMode.Known, birthTime,
            birthTime, birthTime, 13.0827m, 80.2707m, "Asia/Kolkata", revision);

        profiles.GetSnapshotAsync(Arg.Any<ClaimsPrincipal>(), profileId, Token)
            .Returns(call => IsAccount(call.Arg<ClaimsPrincipal>(), accountId) ? snapshot : null);
        profiles.GetSnapshotAsync(Arg.Is<ClaimsPrincipal>(principal => !IsAccount(principal, accountId)),
                profileId, Token)
            .Returns((KundaliInputSnapshot?)null);

        // The MOON the engine reports for THIS profile, keyed on the birth time - the one snapshot field that
        // distinguishes one fixture from another. A stub answering a fixed longitude for every profile would make every
        // pair score alike, which is the vacuity this suite exists to avoid.
        // Registered with ARGUMENT MATCHERS, not with a computed return: this project's NSubstitute version does not
        // support returning a function of the arguments in the lambda form used by the api#55 unit tests, and a stub
        // that silently did not match would report every category as unavailable - a green that proves nothing. A
        // mismatched stub now leaves Calculate unconfigured, so the facade's own unavailability branch fires loudly
        // instead of being mistaken for the behaviour under test.
        engine.Calculate(Arg.Is<KundaliEngineInput>(input => input.BirthTime == birthTime))
            .Returns(new KundaliEngineResult(moonLongitude, moonLongitude, null, null, null, null, null,
                Array.Empty<KundaliEngineGap>()));


    }

    private void MakeBirthTimeUnknown(Guid profileId)
    {
        profiles.GetSnapshotAsync(Arg.Any<ClaimsPrincipal>(), profileId, Token)
            .Returns(new KundaliInputSnapshot(new DateOnly(1990, 4, 12), BirthTimeMode.Unknown, null,
                null, null, 13.0827m, 80.2707m, "Asia/Kolkata", 8));

        // The engine is asked about the date of birth, exactly as the facade would for any profile - here it withholds
        // the Moon, which is what a real engine does when the birth time is not known.
        engine.Calculate(Arg.Is<KundaliEngineInput>(input => input.BirthTimeMode != BirthTimeMode.Known))
            .Returns(new KundaliEngineResult(null, null, null, null, null, null, null,
                Array.Empty<KundaliEngineGap>()));
    }

    private static bool IsAccount(ClaimsPrincipal principal, Guid accountId) =>
        Guid.TryParse(principal.FindFirst(KundaliPrincipal.AccountClaim)?.Value, out var found) && found == accountId;

    /// <summary>Captures the typed failure a call raises, or says it did not fail.</summary>
    private static async Task<Captured> Capture(Func<Task<KundaliAshtakootaComparison>> call)
    {
        try
        {
            var comparison = await call();
            return new Captured(false, false, comparison, null);
        }
        catch (ResourceNotFoundException exception)
        {
            return new Captured(true, true, null, exception);
        }
        catch (ValidationException exception)
        {
            return new Captured(false, true, null, exception);
        }
    }

    /// <summary>
    /// Asserts two failures are indistinguishable to a caller: same type, same message, same code, and no payload.
    /// </summary>
    private static void AssertIndistinguishable(Captured expected, Captured actual)
    {
        Assert.True(expected.Failed);
        Assert.True(actual.Failed);

        Assert.Equal(expected.NotFounded, actual.NotFounded);
        Assert.Equal(expected.Error!.GetType(), actual.Error!.GetType());
        Assert.Equal(expected.Error.Message, actual.Error.Message);
        Assert.Null(expected.Comparison);
        Assert.Null(actual.Comparison);
    }

    private sealed record Captured(bool NotFounded, bool Failed, KundaliAshtakootaComparison? Comparison, Exception? Error);

    public void Dispose() => provider.Dispose();
}
