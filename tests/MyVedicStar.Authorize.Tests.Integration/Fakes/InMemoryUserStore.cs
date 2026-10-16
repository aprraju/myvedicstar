using System.Collections.Concurrent;
using MyVedicStar.Authorize.Data;
using MyVedicStar.Authorize.Data.Users;

namespace MyVedicStar.Authorize.Tests.Integration.Fakes;

/// <summary>In-memory stand-in for the PostgreSQL repositories, used to test the HTTP pipeline.</summary>
public sealed class InMemoryUserStore : IUserRepository, IUserLoginRepository, IAuthorizeUnitOfWork
{
    private readonly ConcurrentDictionary<string, (Guid Id, UserProfileData Profile)> _users = new();
    private readonly ConcurrentQueue<UserLoginData> _logins = new();
    private readonly ConcurrentDictionary<(Guid, string, int), bool> _events = new();
    private readonly ConcurrentDictionary<Guid, bool> _inactive = new();

    public IReadOnlyCollection<UserLoginData> Logins => _logins.ToArray();

    public int TransactionCount { get; private set; }

    public UserProfileData? FindUser(string externalId, string issuer = AuthorizeApiFactory.Issuer) =>
        _users.TryGetValue(issuer + "|" + externalId, out var user) ? user.Profile : null;

    public Guid? FindUserId(string externalId, string issuer = AuthorizeApiFactory.Issuer) =>
        _users.TryGetValue(issuer + "|" + externalId, out var user) ? user.Id : null;
    public void Deactivate(Guid id) => _inactive[id] = true;
    public Task<Guid?> FindIdAsync(string issuer, string externalId, CancellationToken cancellationToken) =>
        Task.FromResult(FindUserId(externalId, issuer));

    public void Reset()
    {
        _users.Clear();
        _logins.Clear();
        _events.Clear();
        _inactive.Clear();
        TransactionCount = 0;
    }

    public Task<RegisteredUser> UpsertAsync(UserProfileData profile, CancellationToken cancellationToken)
    {
        // Mirrors the repository rule that a token without a display name never erases the stored one.
        var user = _users.AddOrUpdate(profile.Issuer + "|" + profile.ExternalId,
            _ => (Guid.CreateVersion7(), profile),
            (_, existing) => _inactive.ContainsKey(existing.Id)
                ? throw new Shared.Exceptions.AccountInactiveException()
                : (existing.Id, profile with { DisplayName = profile.DisplayName ?? existing.Profile.DisplayName }));

        return Task.FromResult(new RegisteredUser(user.Id, user.Profile.DisplayName, user.Profile.GivenName, user.Profile.FamilyName));
    }

    public Task AddAsync(UserLoginData login, CancellationToken cancellationToken)
    {
        if (login.SessionKey is null || _events.TryAdd((login.UserId, login.SessionKey, login.Type), true)) _logins.Enqueue(login);
        return Task.CompletedTask;
    }

    public Task<TResult> ExecuteInTransactionAsync<TResult>(Func<CancellationToken, Task<TResult>> operation, CancellationToken cancellationToken)
    {
        TransactionCount++;
        return operation(cancellationToken);
    }
}
