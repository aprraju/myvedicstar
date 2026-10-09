namespace MyVedicStar.Authorize.Data.Users;

/// <summary>Persistence operations for the user sign-in audit trail.</summary>
public interface IUserLoginRepository
{
    /// <summary>Records a sign-in. The login time is set by the database server in UTC.</summary>
    Task AddAsync(UserLoginData login, CancellationToken cancellationToken);
}
