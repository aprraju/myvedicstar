using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MyVedicStar.Authorize.Data.Persistence;
using MyVedicStar.Shared.Exceptions;

namespace MyVedicStar.Authorize.Data;

internal sealed class AuthorizeUnitOfWork : IAuthorizeUnitOfWork
{
    private readonly AuthorizeDbContext _dbContext;
    private readonly ILogger<AuthorizeUnitOfWork> _logger;

    public AuthorizeUnitOfWork(AuthorizeDbContext dbContext, ILogger<AuthorizeUnitOfWork> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
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
            _logger.LogError(ex, "Database transaction failed");
            throw new DataAccessException(
                "Your details could not be saved. Please try again later.",
                $"Database transaction failed: {ex.GetType().Name}.",
                ex);
        }
    }
}
