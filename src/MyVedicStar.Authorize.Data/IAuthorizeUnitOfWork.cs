namespace MyVedicStar.Authorize.Data;

/// <summary>
/// Runs several repository operations as one database transaction.
/// </summary>
public interface IAuthorizeUnitOfWork
{
    /// <summary>
    /// Runs <paramref name="operation"/> inside a transaction and commits it if the operation succeeds.
    /// On a transient failure the whole operation may be retried, so it must be safe to repeat.
    /// </summary>
    /// <exception cref="Shared.Exceptions.DataAccessException">The transaction could not be completed.</exception>
    Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken);
}
