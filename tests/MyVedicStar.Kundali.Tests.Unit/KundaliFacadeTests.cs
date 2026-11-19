using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using MyVedicStar.Kundali.Business;
using MyVedicStar.Kundali.Shared;
using MyVedicStar.Shared.Exceptions;
using MyVedicStar.VedicProfiles.Shared;
using NSubstitute;

namespace MyVedicStar.Kundali.Tests.Unit;

public sealed class KundaliFacadeTests : IDisposable
{
    private readonly ClaimsPrincipal user = new(new ClaimsIdentity([], "test"));
    private readonly Guid accountId = Guid.NewGuid();
    private readonly Guid profileId = Guid.NewGuid();
    private readonly Guid aggregateId = Guid.NewGuid();
    private readonly Guid revisionId = Guid.NewGuid();
    private readonly IKundaliProfileReader profiles = Substitute.For<IKundaliProfileReader>();
    private readonly IKundaliRepository repository = Substitute.For<IKundaliRepository>();
    private readonly ServiceProvider provider;
    private readonly IKundaliFacade facade;
    private static readonly CancellationToken Token = CancellationToken.None;

    private static readonly KundaliProvenance Provenance =
        new("kundali-foundation-0", "none", "foundation-0", "host", "Lahiri");

    public KundaliFacadeTests()
    {
        profiles.GetRegisteredAccountIdAsync(user, Token).Returns(accountId);
        profiles.GetSnapshotAsync(user, profileId, Token).Returns(Snapshot());
        profiles.GetAyanamsaAsync(user, Token).Returns("Lahiri");

        // api#124: the shared provider, so every Kundali test builds the facade the same way and ValidateOnBuild cannot
        // be forgotten in one file and remembered in another.
        provider = KundaliTestProvider.Create()
            .With(profiles)
            .With(repository)
            .With<TimeProvider>(new FixedClock())
            .With(Substitute.For<IKundaliResultCache>())
            .Build();
        facade = provider.CreateScope().ServiceProvider.GetRequiredService<IKundaliFacade>();
    }

    [Fact]
    public async Task Get_for_a_foreign_profile_returns_not_found_and_never_leaks_existence()
    {
        profiles.GetSnapshotAsync(user, profileId, Token).Returns((KundaliInputSnapshot?)null);

        await Assert.ThrowsAsync<ResourceNotFoundException>(() => facade.GetAsync(user, profileId, Token));
        await repository.DidNotReceive().GetAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<long?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Get_for_an_owned_profile_without_a_kundali_returns_an_empty_state_not_an_error()
    {
        repository.GetAsync(accountId, profileId, Arg.Any<long?>(), Token).Returns((KundaliView?)null);

        var view = await facade.GetAsync(user, profileId, Token);

        Assert.Null(view.CurrentRevision);
        Assert.Equal(profileId, view.Aggregate.ProfileId);
    }

    [Fact]
    public async Task Generate_creates_the_aggregate_when_none_exists()
    {
        repository.GetAsync(accountId, profileId, Arg.Any<long?>(), Token).Returns((KundaliView?)null);
        repository.EnsureAggregateWithRevisionAsync(accountId, profileId, Arg.Any<KundaliInputSnapshot>(),
                Arg.Any<KundaliProvenance>(), Token)
            .Returns(new KundaliView(new KundaliAggregate(aggregateId, profileId, revisionId, 1,
                DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch), Summary()));

        var view = await facade.GenerateAsync(user, profileId, Token);

        Assert.Equal(revisionId, view.CurrentRevision!.RevisionId);
        await repository.Received(1).EnsureAggregateWithRevisionAsync(accountId, profileId,
            Arg.Any<KundaliInputSnapshot>(), Provenance, Token);
    }

    [Fact]
    public async Task Repeated_generation_for_identical_inputs_does_not_enqueue_twice()
    {
        var snapshot = Snapshot();
        var running = new KundaliRevisionSummary(revisionId, 1, KundaliGenerationStatus.Running, null, Provenance,
            snapshot.ComputeHash(), false, true, [], DateTimeOffset.UnixEpoch);
        repository.GetAsync(accountId, profileId, Arg.Any<long?>(), Token)
            .Returns(new KundaliView(new KundaliAggregate(aggregateId, profileId, revisionId, 1,
                DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch), running));

        var view = await facade.GenerateAsync(user, profileId, Token);

        Assert.Equal(KundaliGenerationStatus.Running, view.CurrentRevision!.Status);
        await repository.DidNotReceive().AddRevisionAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<long>(),
            Arg.Any<KundaliInputSnapshot>(), Arg.Any<KundaliProvenance>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Generation_after_an_edit_adds_a_revision_with_a_compare_and_swap()
    {
        var succeeded = new KundaliRevisionSummary(revisionId, 1, KundaliGenerationStatus.Succeeded, null,
            Provenance, "previoushash", false, false, [], DateTimeOffset.UnixEpoch);
        repository.GetAsync(accountId, profileId, Arg.Any<long?>(), Token)
            .Returns(new KundaliView(new KundaliAggregate(aggregateId, profileId, revisionId, 3,
                DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch), succeeded));
        repository.AddRevisionAsync(accountId, profileId, 3, Arg.Any<KundaliInputSnapshot>(),
                Arg.Any<KundaliProvenance>(), Token)
            .Returns(new KundaliView(new KundaliAggregate(aggregateId, profileId, revisionId, 4,
                DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch), Summary()));

        await facade.GenerateAsync(user, profileId, Token);

        await repository.Received(1).AddRevisionAsync(accountId, profileId, 3, Arg.Any<KundaliInputSnapshot>(),
            Provenance, Token);
    }

    [Fact]
    public async Task Retry_refuses_when_the_current_revision_did_not_fail()
    {
        var succeeded = new KundaliRevisionSummary(revisionId, 1, KundaliGenerationStatus.Succeeded, null,
            Provenance, "h", false, false, [], DateTimeOffset.UnixEpoch);
        repository.GetAsync(accountId, profileId, Arg.Any<long?>(), Token)
            .Returns(new KundaliView(new KundaliAggregate(aggregateId, profileId, revisionId, 1,
                DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch), succeeded));

        await Assert.ThrowsAsync<KundaliConflictException>(() => facade.RetryAsync(user, profileId, Token));
    }

    [Fact]
    public async Task Retry_is_allowed_for_a_failed_revision()
    {
        var failed = new KundaliRevisionSummary(revisionId, 1, KundaliGenerationStatus.Failed,
            KundaliFailureReasons.EngineError, Provenance, "h", false, false, [], DateTimeOffset.UnixEpoch);
        repository.GetAsync(accountId, profileId, Arg.Any<long?>(), Token)
            .Returns(new KundaliView(new KundaliAggregate(aggregateId, profileId, revisionId, 1,
                DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch), failed));
        repository.AddRevisionAsync(accountId, profileId, 1, Arg.Any<KundaliInputSnapshot>(),
                Arg.Any<KundaliProvenance>(), Token)
            .Returns(new KundaliView(new KundaliAggregate(aggregateId, profileId, revisionId, 2,
                DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch), Summary()));

        var view = await facade.RetryAsync(user, profileId, Token);

        Assert.Equal(2, view.Aggregate.Revision);
    }

    [Fact]
    public async Task An_out_of_range_birth_date_is_refused_before_any_persistence()
    {
        profiles.GetSnapshotAsync(user, profileId, Token).Returns(Snapshot() with { DateOfBirth = new DateOnly(1750, 1, 1) });

        await Assert.ThrowsAsync<ValidationException>(() => facade.GenerateAsync(user, profileId, Token));
        await repository.DidNotReceive().EnsureAggregateWithRevisionAsync(Arg.Any<Guid>(), Arg.Any<Guid>(),
            Arg.Any<KundaliInputSnapshot>(), Arg.Any<KundaliProvenance>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_unresolvable_timezone_is_refused()
    {
        profiles.GetSnapshotAsync(user, profileId, Token)
            .Returns(Snapshot() with { TimezoneId = "Mars/Olympus_Mons" });

        await Assert.ThrowsAsync<ValidationException>(() => facade.GenerateAsync(user, profileId, Token));
    }

    [Theory]
    [InlineData("Venus")]
    [InlineData("")]
    [InlineData("north")]
    public async Task An_unsupported_chart_style_is_refused(string chartStyle)
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            facade.SavePreferencesAsync(user, profileId, chartStyle, 0, Token));
    }

    [Theory]
    [InlineData("North")]
    [InlineData("South")]
    [InlineData("East")]
    public async Task A_supported_chart_style_is_saved_without_touching_results(string chartStyle)
    {
        repository.SavePreferencesAsync(accountId, profileId, chartStyle, 0, Token)
            .Returns(new KundaliPreferences(chartStyle, 1, DateTimeOffset.UnixEpoch));

        var saved = await facade.SavePreferencesAsync(user, profileId, chartStyle, 0, Token);

        Assert.Equal(chartStyle, saved.ChartStyle);
        // Chart style is presentation-only: it must never create a revision or trigger a recalculation.
        await repository.DidNotReceive().AddRevisionAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<long>(),
            Arg.Any<KundaliInputSnapshot>(), Arg.Any<KundaliProvenance>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Saving_preferences_requires_the_current_revision()
    {
        repository.SavePreferencesAsync(accountId, profileId, "South", 7, Token)
            .Returns(new KundaliPreferences("South", 8, DateTimeOffset.UnixEpoch));

        var saved = await facade.SavePreferencesAsync(user, profileId, "South", 7, Token);

        Assert.Equal(8, saved.Revision);
        await repository.Received(1).SavePreferencesAsync(accountId, profileId, "South", 7, Token);
    }

    [Fact]
    public async Task A_negative_preferences_revision_is_refused()
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            facade.SavePreferencesAsync(user, profileId, "North", -1, Token));
    }

    [Fact]
    public void Capabilities_are_exposed_through_the_business_facade()
    {
        var report = facade.GetCapabilities();

        Assert.Contains(60, report.ProvisionalDivisionalCharts);
        Assert.Contains("Chesta", report.UnsupportedShadbalaComponents);
    }

    private static KundaliInputSnapshot Snapshot() => new(new DateOnly(1990, 4, 12), BirthTimeMode.Known,
        new TimeOnly(6, 15, 0), new TimeOnly(6, 15, 0), new TimeOnly(6, 15, 0), 17.385m, 78.4867m,
        "Asia/Kolkata", 1);

    private KundaliRevisionSummary Summary() => new(revisionId, 1, KundaliGenerationStatus.Pending, null,
        Provenance, Snapshot().ComputeHash(), false, false, [], DateTimeOffset.UnixEpoch);

    public void Dispose() => provider.Dispose();

    [Fact]
    public async Task A_read_reports_the_effective_settings_that_would_apply_now()
    {
        // An empty profile still reports settings, so the shell can render the right style before anything is
        // generated. A null here would force the client to guess.
        var view = await facade.GetAsync(user, profileId, Token);

        Assert.NotNull(view.Settings);
        Assert.Equal("Lahiri", view.Settings!.Ayanamsa);
        Assert.Equal("North", view.Settings.ChartStyle);
    }

    [Fact]
    public async Task A_stored_chart_style_preference_is_reported_rather_than_the_default()
    {
        repository.GetPreferencesAsync(accountId, profileId, Token)
            .Returns(new KundaliPreferences("South", 3, DateTimeOffset.UtcNow));

        var view = await facade.GetAsync(user, profileId, Token);

        // The preference must win over the default, or the shell would silently render the wrong style.
        Assert.Equal("South", view.Settings!.ChartStyle);
    }

    [Fact]
    public async Task Chart_style_never_causes_a_generation()
    {
        repository.GetPreferencesAsync(accountId, profileId, Token)
            .Returns(new KundaliPreferences("East", 3, DateTimeOffset.UtcNow));

        _ = await facade.GetAsync(user, profileId, Token);

        // Style is presentation-only. If reading ever enqueued work, changing a display preference would burn an
        // engine run - the engine is the expensive, slow, rate-limited part of the system.
        await repository.DidNotReceive().AddRevisionAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<long>(),
            Arg.Any<KundaliInputSnapshot>(), Arg.Any<KundaliProvenance>(), Arg.Any<CancellationToken>());
        await repository.DidNotReceive().EnsureAggregateWithRevisionAsync(Arg.Any<Guid>(), Arg.Any<Guid>(),
            Arg.Any<KundaliInputSnapshot>(), Arg.Any<KundaliProvenance>(), Arg.Any<CancellationToken>());
    }
}

internal sealed class FixedClock : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);
}
