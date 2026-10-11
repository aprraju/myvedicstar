using Microsoft.EntityFrameworkCore;
using MyVedicStar.Authorize.Data.Persistence;
using MyVedicStar.Shared.Exceptions;

namespace MyVedicStar.Authorize.Data;

internal sealed class AuthorizeUnitOfWork : IAuthorizeUnitOfWork
{
    private readonly AuthorizeDbContext _dbContext;

    public AuthorizeUnitOfWork(AuthorizeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken)
    {
        // With retry-on-failure enabled, a user transaction must run inside the execution strategy
        // so the whole unit is replayed on a transient failure.
        var strategy = _dbContext.Database.CreateExecutionStrategy();

        try
        {
            return await strategy.ExecuteAsync(
                async token =>
                {
                    _dbContext.ChangeTracker.Clear();

                    await using var transaction = await _dbContext.Database.BeginTransactionAsync(token);
                    var result = await operation(token);
                    await transaction.CommitAsync(token);

                    return result;
                },
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (MyVedicStarException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Not logged here: the API's exception handler logs the wrapped exception, including this inner one.
            throw new DataAccessException(
                "Your details could not be saved. Please try again later.",
                $"Database transaction failed: {ex.GetType().Name}.",
                ex);
        }
    }
}
