using System.Collections.Concurrent;
using MyVedicStar.Authorize.Data;
using MyVedicStar.Authorize.Data.Users;

namespace MyVedicStar.Authorize.Tests.Integration.Fakes;

/// <summary>In-memory stand-in for the PostgreSQL repositories, used to test the HTTP pipeline.</summary>
public sealed class InMemoryUserStore : IUserRepository, IUserLoginRepository, IAuthorizeUnitOfWork
{
    private readonly ConcurrentDictionary<string, (Guid Id, UserProfileData Profile)> _users = new();
    private readonly ConcurrentQueue<UserLoginData> _logins = new();

    public IReadOnlyCollection<UserLoginData> Logins => _logins.ToArray();

    public int TransactionCount { get; private set; }

    public UserProfileData? FindUser(string externalId) =>
        _users.TryGetValue(externalId, out var user) ? user.Profile : null;

    public Guid? FindUserId(string externalId) =>
        _users.TryGetValue(externalId, out var user) ? user.Id : null;

    public void Reset()
    {
        _users.Clear();
        _logins.Clear();
        TransactionCount = 0;
    }

    public Task<Guid> UpsertAsync(UserProfileData profile, CancellationToken cancellationToken)
    {
        var user = _users.AddOrUpdate(
            profile.ExternalId,
            _ => (Guid.CreateVersion7(), profile),
            (_, existing) => (existing.Id, profile));

        return Task.FromResult(user.Id);
    }

    public Task AddAsync(UserLoginData login, CancellationToken cancellationToken)
    {
        _logins.Enqueue(login);
        return Task.CompletedTask;
    }

    public Task<TResult> ExecuteInTransactionAsync<TResult>(Func<CancellationToken, Task<TResult>> operation, CancellationToken cancellationToken)
    {
        TransactionCount++;
        return operation(cancellationToken);
    }
}
