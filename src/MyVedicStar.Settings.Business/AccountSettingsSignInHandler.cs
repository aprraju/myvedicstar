using MyVedicStar.Authorize.Business.Users;
using MyVedicStar.Settings.Data;
namespace MyVedicStar.Settings.Business;

/// <summary>Settings-owned lifecycle adapter; repeat login never resets saved choices.</summary>
internal sealed class AccountSettingsSignInHandler(ISettingsRepository repository) : IAccountSignInHandler
{
    public Task EnsureAccountDefaultsAsync(Guid userId, CancellationToken cancellationToken) =>
        repository.EnsureAsync(userId, cancellationToken);
}
