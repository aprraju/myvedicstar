using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using MyVedicStar.Shared.Exceptions;
using MyVedicStar.VedicProfiles.Business;
using MyVedicStar.VedicProfiles.Shared;
using NSubstitute;

namespace MyVedicStar.VedicProfiles.UnitTests;

/// <summary>
/// Profile deletion with participants. The properties that matter are ordering, idempotency and failure handling -
/// not the happy path, which is the one case that would work even if the design were wrong.
/// </summary>
/// <remarks>
/// These tests live beside the profile suite because the behaviour under test is VedicProfiles' deletion contract;
/// the Kundali participant is exercised separately.
/// </remarks>
public sealed class ProfileDeletionParticipantTests
{
    private readonly ClaimsPrincipal user = new(new ClaimsIdentity([], "test"));
    private readonly Guid accountId = Guid.NewGuid();
    private readonly Guid profileId = Guid.NewGuid();
    private readonly IVedicProfileAccountReader accounts = Substitute.For<IVedicProfileAccountReader>();
    private readonly IVedicProfileRepository repository = Substitute.For<IVedicProfileRepository>();
    private static readonly CancellationToken Token = CancellationToken.None;

    /// <summary>
    /// Builds the real facade through DI, with the supplied participants registered. Constructing the internal class
    /// directly would bypass the composition path these tests exist to exercise.
    /// </summary>
    private IVedicProfilesFacade Facade(params IVedicProfileDeletionParticipant[] participants)
    {
        accounts.GetRegisteredAccountIdAsync(user, Token).Returns(accountId);
        var services = new ServiceCollection();
        services.AddSingleton(accounts);
        services.AddSingleton(repository);
        services.AddSingleton<TimeProvider>(new FixedClock());
        foreach (var participant in participants)
            services.AddSingleton(participant);
        services.AddVedicProfilesBusiness();
        return services.BuildServiceProvider().GetRequiredService<IVedicProfilesFacade>();
    }

    [Fact]
    public async Task Every_participant_is_asked_to_remove_before_the_profile_row_is_deleted()
    {
        var first = Substitute.For<IVedicProfileDeletionParticipant>();
        var second = Substitute.For<IVedicProfileDeletionParticipant>();
        repository.DeleteAsync(accountId, profileId, Token).Returns(true);

        await Facade(first, second).DeleteAsync(user, profileId, Token);

        await first.Received(1).RemoveAsync(accountId, profileId, Token);
        await second.Received(1).RemoveAsync(accountId, profileId, Token);

        // The ordering claim, asserted rather than assumed: participants first, profile row last.
        Received.InOrder(() =>
        {
            first.RemoveAsync(accountId, profileId, Token);
            second.RemoveAsync(accountId, profileId, Token);
            repository.DeleteAsync(accountId, profileId, Token);
        });
    }

    [Fact]
    public async Task A_failing_participant_aborts_the_deletion_so_the_profile_is_not_orphaned()
    {
        var failing = Substitute.For<IVedicProfileDeletionParticipant>();
        failing.RemoveAsync(accountId, profileId, Token)
            .Returns(Task.FromException(new InvalidOperationException("module cleanup failed")));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Facade(failing).DeleteAsync(user, profileId, Token));

        // The profile must survive: an orphaned profile is recoverable, unreachable leftover rows are not.
        await repository.DidNotReceive().DeleteAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_missing_profile_still_runs_participants_then_reports_not_found()
    {
        // A profile that is already gone has no rows to remove, and each participant treats that as success. The 404
        // is still correct for the caller - the delete is idempotent, not silently successful.
        var participant = Substitute.For<IVedicProfileDeletionParticipant>();
        repository.DeleteAsync(accountId, profileId, Token).Returns(false);

        await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
            Facade(participant).DeleteAsync(user, profileId, Token));

        await participant.Received(1).RemoveAsync(accountId, profileId, Token);
    }

    [Fact]
    public async Task No_participants_registered_still_deletes_the_profile()
    {
        // Composition without any module should not break deletion.
        repository.DeleteAsync(accountId, profileId, Token).Returns(true);

        await Facade().DeleteAsync(user, profileId, Token);

        await repository.Received(1).DeleteAsync(accountId, profileId, Token);
    }
}

