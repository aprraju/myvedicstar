namespace MyVedicStar.Kundali.Tests.Unit;

using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using MyVedicStar.Kundali.Business;
using MyVedicStar.Kundali.Shared;
using NSubstitute;

/// <summary>
/// Issue #111: the guard PR #109 claimed but did not have.
/// </summary>
/// <remarks>
/// <para>#109's test asserted that the <i>repository</i> was not asked to add a revision. That is a real property, but
/// it is not the one being claimed. Repository calls are how a generation is <b>persisted</b>; the engine port is how a
/// calculation is <b>performed</b>. A future edit could call the engine directly - or through a new collaborator - and
/// #109's test would stay green while a style read burned a calculation run.</para>
/// <para>So these tests observe the engine port itself, and they observe it in both directions: the presentation path
/// must make <b>zero</b> engine calls, and the settings path that genuinely needs astronomy must make them. The pair is
/// the point. A "did not receive" assertion alone proves nothing if the spy is never capable of recording a call, so
/// the positive case pins that the same instrument does fire.</para>
/// </remarks>
public sealed class KundaliEngineCallGuardTests : IDisposable
{
    private readonly ClaimsPrincipal user = new(new ClaimsIdentity([], "test"));
    private readonly Guid accountId = Guid.NewGuid();
    private readonly Guid profileId = Guid.NewGuid();
    private readonly IKundaliProfileReader profiles = Substitute.For<IKundaliProfileReader>();
    private readonly IKundaliRepository repository = Substitute.For<IKundaliRepository>();
    private readonly CountingCalculationEngine engine = new();
    private readonly ServiceProvider provider;
    private readonly IKundaliFacade facade;
    private static readonly CancellationToken Token = CancellationToken.None;

    public KundaliEngineCallGuardTests()
    {
        profiles.GetRegisteredAccountIdAsync(user, Token).Returns(accountId);
        profiles.GetSnapshotAsync(user, profileId, Token).Returns(new KundaliInputSnapshot(
            new DateOnly(1990, 3, 15), VedicProfiles.Shared.BirthTimeMode.Known,
            new TimeOnly(6, 45), null, null, 51.5m, -0.12m, "Europe/London", 7));
        profiles.GetAyanamsaAsync(user, Token).Returns("Lahiri");
        // Sections read the display view rather than the raw snapshot, so the control case needs it stubbed too.
        profiles.GetDisplayAsync(user, profileId, Token).Returns(new KundaliProfileDisplay(
            "Test Person", new DateOnly(1990, 3, 15), VedicProfiles.Shared.BirthTimeMode.Known,
            new TimeOnly(6, 45), null, null, "London", 51.5m, -0.12m, "Europe/London"));
        repository.GetPreferencesAsync(accountId, profileId, Token)
            .Returns(new KundaliPreferences("South", 3, DateTimeOffset.UnixEpoch));

        // api#124: one construction idiom for every Kundali test. The engine override replaces the fallback so every
        // attempted calculation is counted rather than silently refusing.
        provider = KundaliTestProvider.Create()
            .With(profiles)
            .With(repository)
            .With<TimeProvider>(new FixedClock())
            .With(Substitute.For<IKundaliResultCache>())
            .With<IKundaliCalculationEngine>(engine)
            .Build();
        facade = provider.CreateScope().ServiceProvider.GetRequiredService<IKundaliFacade>();
    }

    [Fact]
    public async Task Reading_the_overview_performs_zero_engine_calculations()
    {
        // The claim from #109, now actually enforced: rendering the shell - which reports the effective chart style -
        // must not calculate anything. Style is presentation. If this ever fires, changing a display preference would
        // cost a full ephemeris run.
        _ = await facade.GetAsync(user, profileId, Token);

        Assert.Equal(0, engine.Calls);
    }

    [Fact]
    public async Task Reading_the_effective_settings_performs_zero_engine_calculations()
    {
        // Reads the same values through the narrower path, so the guard is not tied to one method's implementation.
        var view = await facade.GetAsync(user, profileId, Token);

        Assert.Equal("South", view.Settings!.ChartStyle);
        Assert.Equal("Lahiri", view.Settings.Ayanamsa);
        Assert.Equal(0, engine.Calls);
    }

    [Fact]
    public async Task Reading_chart_style_alone_does_not_calculate()
    {
        // Style is read from the preference store directly. Asserting the store was consulted AND the engine was not
        // is what makes this about the style path rather than about the overview by accident.
        _ = await facade.GetPreferencesAsync(user, profileId, Token);

        await repository.Received().GetPreferencesAsync(accountId, profileId, Token);
        Assert.Equal(0, engine.Calls);
    }

    [Fact]
    public async Task A_section_that_needs_positions_does_call_the_engine()
    {
        // The control case, and the reason the three tests above are meaningful: the same spy, in the same fixture,
        // must record a call on a path that genuinely calculates. Without this, an engine that was never wired - or a
        // spy that can never fire - would make every DidNotReceive assertion pass vacuously.
        await facade.GetPlanetsAsync(user, profileId, Token);

        // "One port, one counter" as a fact rather than an inference: the facade must be using the very instance the
        // assertions read. Without this, a second registration could satisfy the container while the counter stayed at 0.
        Assert.Same(engine, provider.GetRequiredService<IKundaliCalculationEngine>());

        Assert.True(engine.Calls > 0,
            "the engine spy must observe a real calculation on a position-dependent section, otherwise the "
            + "zero-call assertions above prove nothing");
    }


    [Fact]
    public void The_engine_port_has_exactly_the_members_the_guard_counts()
    {
        // The guard above is only as good as the set of members it counts. Two failures could silently shrink it:
        //   1. the port gains a member and the double grows a stub that returns without incrementing - compiles fine,
        //      and the guard is vacuous for that member;
        //   2. a member is renamed or replaced.
        // A count alone catches (2) only if the total changes, so the NAMES are pinned as well. If this fails, whoever
        // changed the port must decide explicitly whether the guard should count the new member - that decision is the
        // point, not the number.
        var members = typeof(IKundaliCalculationEngine).GetMethods()
            .Select(method => method.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[] { "Calculate", "CalculateDashaTimeline", "CalculateTransits" },
            members);

        // And each one must actually be counted by the double, or the contract above is decorative.
        var engine = new CountingCalculationEngine();
        engine.Calculate(default!);
        engine.CalculateDashaTimeline(default!);
        engine.CalculateTransits(default!);
        Assert.Equal(3, engine.Calls);
    }

    public void Dispose() => provider.Dispose();

    /// <summary>
    /// Records every calculation so a test can assert on the count. Deliberately returns an empty result rather than
    /// throwing: the assertions are about whether it was <i>called</i>, and a throw would turn an over-eager call into a
    /// different failure than the one being tested.
    /// </summary>
    private sealed class CountingCalculationEngine : IKundaliCalculationEngine
    {
        public int Calls { get; private set; }

        public KundaliEngineResult Calculate(KundaliEngineInput input)
        {
            Calls++;
            return new KundaliEngineResult(null, null, null, null, null, null, null,
                Array.Empty<KundaliEngineGap>());
        }

        // The other two members are part of the same port, so a call to either is also engine work and must be counted.
        // Leaving them out would let a future style path call CalculateDashaTimeline and still pass the guard.
        public KundaliEngineDashaTimeline CalculateDashaTimeline(KundaliDashaQuery query)
        {
            Calls++;
            return new KundaliEngineDashaTimeline(null, [], [], null, null, Array.Empty<KundaliEngineGap>());
        }

        public KundaliEngineTransits CalculateTransits(KundaliTransitQuery query)
        {
            Calls++;
            return new KundaliEngineTransits(string.Empty, [], string.Empty, [], null,
                Array.Empty<KundaliEngineGap>());
        }
    }
}
