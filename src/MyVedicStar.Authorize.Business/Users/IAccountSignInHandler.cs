namespace MyVedicStar.Authorize.Business.Users;

/// <summary>Post-commit account lifecycle port. Implementations must be idempotent and honor cancellation.</summary>
public interface IAccountSignInHandler
{
    Task EnsureAccountDefaultsAsync(Guid userId, CancellationToken cancellationToken);
}
