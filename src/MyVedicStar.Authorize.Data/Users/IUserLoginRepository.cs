namespace MyVedicStar.Authorize.Data.Users;

/// <summary>Append-only login/logout auditing with database-enforced session idempotency.</summary>
public interface IUserLoginRepository
{
    /// <summary>Records an event once per user, session and type. The database assigns its UTC time.</summary>
    Task AddAsync(UserLoginData login, CancellationToken cancellationToken);
}
