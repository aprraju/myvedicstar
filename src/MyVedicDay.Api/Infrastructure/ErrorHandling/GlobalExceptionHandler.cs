using MyVedicPlatform.Api;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using MyVedicDay.Shared.Exceptions;

namespace MyVedicDay.Api.Infrastructure.ErrorHandling;

/// <summary>
/// Maps exceptions to RFC 7807 Problem Details. Only validation and business-rule messages, which are written for
/// consumers, reach the response; everything else returns a generic message.
/// </summary>
internal sealed partial class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    private const int ClientClosedRequest = 499;

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            LogRequestAborted();
            httpContext.Response.StatusCode = ClientClosedRequest;
            return true;
        }

        var problem = CreateProblemDetails(exception);
        if (exception is IntegrationException integration)
        {
            LogIntegrationFailure(exception, integration.Provider);
        }
        else if (problem.Status >= StatusCodes.Status500InternalServerError)
        {
            LogServerError(exception, exception.GetType().Name);
        }
        else
        {
            LogClientError(problem.Status ?? 0, exception.GetType().Name);
        }

        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
        });
    }

    private static ProblemDetails CreateProblemDetails(Exception exception) => exception switch
    {
        ValidationException validation => new ValidationProblemDetails(validation.Errors.ToDictionary(StringComparer.OrdinalIgnoreCase))
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "One or more validation errors occurred.",
        },
        BusinessRuleException businessRule => new ProblemDetails
        {
            Status = StatusCodes.Status422UnprocessableEntity,
            Title = "The request cannot be fulfilled.",
            Detail = businessRule.Message,
        },
        IntegrationException or DataAccessException => new ProblemDetails
        {
            Status = StatusCodes.Status503ServiceUnavailable,
            Title = ProblemDetailsPolicy.DependencyTitle,
        },
        CalculationException => new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "The Panchang could not be calculated.",
        },
        _ => new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = ProblemDetailsPolicy.UnexpectedTitle,
        },
    };

    [LoggerMessage(Level = LogLevel.Information, Message = "Request aborted by the client")]
    private partial void LogRequestAborted();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Request rejected with {StatusCode} ({ExceptionType})")]
    private partial void LogClientError(int statusCode, string exceptionType);

    [LoggerMessage(Level = LogLevel.Error, Message = "Request failed with {ExceptionType}")]
    private partial void LogServerError(Exception exception, string exceptionType);

    /// <summary>The provider name goes to the logs only, never to the response.</summary>
    [LoggerMessage(Level = LogLevel.Error, Message = "Request failed because provider {Provider} is unavailable")]
    private partial void LogIntegrationFailure(Exception exception, string provider);
}
