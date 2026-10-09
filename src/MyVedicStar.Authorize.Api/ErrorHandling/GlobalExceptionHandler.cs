using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using MyVedicStar.Shared.Exceptions;

namespace MyVedicStar.Authorize.Api.ErrorHandling;

/// <summary>
/// Maps unhandled exceptions to RFC 7807 Problem Details without exposing internal details.
/// </summary>
internal sealed class GlobalExceptionHandler : IExceptionHandler
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
            _logger.LogError(exception, "Request failed with {StatusCode}", statusCode);
        }
        else
        {
            _logger.LogWarning("Request failed with {StatusCode}: {Reason}", statusCode, exception.Message);
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

    private static (int StatusCode, string Title, string Detail) MapException(Exception exception) => exception switch
    {
        ValidationException e => (StatusCodes.Status400BadRequest, "Invalid request", e.PublicMessage),
        AuthenticationFailedException e => (StatusCodes.Status401Unauthorized, "Authentication failed", e.PublicMessage),
        ResourceNotFoundException e => (StatusCodes.Status404NotFound, "Not found", e.PublicMessage),
        IntegrationException e => (StatusCodes.Status503ServiceUnavailable, "Service unavailable", e.PublicMessage),
        DataAccessException e => (StatusCodes.Status503ServiceUnavailable, "Service unavailable", e.PublicMessage),
        _ => (StatusCodes.Status500InternalServerError, "Unexpected error", "An unexpected error occurred."),
    };
}
