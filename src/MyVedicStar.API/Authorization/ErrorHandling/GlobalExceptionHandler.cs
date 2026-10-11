using MyVedicPlatform.Api;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using MyVedicStar.Shared.Exceptions;

namespace MyVedicStar.API.Authorization.ErrorHandling;

/// <summary>
/// Maps unhandled exceptions to RFC 7807 Problem Details without exposing internal details.
/// </summary>
/// <remarks>The single place failures are logged: lower layers wrap and throw rather than log and rethrow.</remarks>
internal sealed partial class GlobalExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetailsService;
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(IProblemDetailsService problemDetailsService, ILogger<GlobalExceptionHandler> logger)
    {
        _problemDetailsService = problemDetailsService;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (statusCode, title, detail) = MapException(exception);

        if (statusCode >= StatusCodes.Status500InternalServerError)
        {
            LogServerError(_logger, exception, statusCode);
        }
        else
        {
            LogClientError(_logger, statusCode, exception.Message);
        }

        httpContext.Response.StatusCode = statusCode;

        return await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = statusCode,
                Title = title,
                Detail = detail,
            },
        });
    }

    private static (int StatusCode, string Title, string? Detail) MapException(Exception exception) => exception switch
    {
        ValidationException e => (StatusCodes.Status400BadRequest, "Invalid request", e.PublicMessage),
        AuthenticationFailedException e => (StatusCodes.Status401Unauthorized, "Authentication failed", e.PublicMessage),
        ResourceNotFoundException e => (StatusCodes.Status404NotFound, "Not found", e.PublicMessage),
        IntegrationException or DataAccessException => (StatusCodes.Status503ServiceUnavailable, ProblemDetailsPolicy.DependencyTitle, null),
        _ => (StatusCodes.Status500InternalServerError, ProblemDetailsPolicy.UnexpectedTitle, null),
    };

    [LoggerMessage(Level = LogLevel.Error, Message = "Request failed with {StatusCode}")]
    private static partial void LogServerError(ILogger logger, Exception exception, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Request failed with {StatusCode}: {Reason}")]
    private static partial void LogClientError(ILogger logger, int statusCode, string reason);
}
