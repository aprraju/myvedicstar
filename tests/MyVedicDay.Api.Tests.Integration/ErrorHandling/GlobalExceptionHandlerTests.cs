using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyVedicDay.Api.Infrastructure.ErrorHandling;
using MyVedicDay.Shared.Exceptions;

namespace MyVedicDay.Api.Tests.Integration.ErrorHandling;

/// <summary>
/// Every exception becomes an RFC 7807 response with the right status. Only validation and business-rule messages,
/// written for consumers, are returned; internal details (providers, causes) stay in the logs.
/// </summary>
public sealed class GlobalExceptionHandlerTests
{
    public static TheoryData<Exception, int, string> Mappings => new()
    {
        { new ValidationException("date", "Dates must fall between 1900-01-01 and 2100-12-31."), 400, "One or more validation errors occurred." },
        { new BusinessRuleException("The sun does not rise on this date."), 422, "The request cannot be fulfilled." },
        { new IntegrationException("GeoNames", "Place dataset could not be loaded."), 503, "A dependency is temporarily unavailable. Please retry later." },
        { new DataAccessException("Festival rules could not be loaded."), 503, "A dependency is temporarily unavailable. Please retry later." },
        { new CalculationException("Angular crossing search did not converge."), 500, "The Panchang could not be calculated." },
        { new InvalidOperationException("secret connection string"), 500, "An unexpected error occurred." },
    };

    [Theory]
    [MemberData(nameof(Mappings))]
    public async Task Exceptions_MapToProblemDetails(Exception exception, int status, string title)
    {
        var (handler, context, _) = Create();

        Assert.True(await handler.TryHandleAsync(context, exception, CancellationToken.None));

        Assert.Equal(status, context.Response.StatusCode);
        using var body = await ReadBodyAsync(context);
        Assert.Equal(status, body.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(title, body.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task ConsumerMessages_AreReturned_InternalOnesAreNot()
    {
        var (handler, validationContext, _) = Create();
        await handler.TryHandleAsync(validationContext, new ValidationException("date", "Use yyyy-MM-dd."), CancellationToken.None);
        using var validation = await ReadBodyAsync(validationContext);
        Assert.Equal("Use yyyy-MM-dd.", validation.RootElement.GetProperty("errors").GetProperty("date")[0].GetString());

        var (_, businessContext, _) = Create();
        await handler.TryHandleAsync(businessContext, new BusinessRuleException("The sun does not rise on this date."), CancellationToken.None);
        using var business = await ReadBodyAsync(businessContext);
        Assert.Equal("The sun does not rise on this date.", business.RootElement.GetProperty("detail").GetString());

        foreach (var exception in new Exception[]
        {
            new IntegrationException("GeoNames", "Place dataset could not be loaded."),
            new InvalidOperationException("Server=db;Password=secret"),
        })
        {
            var (_, context, _) = Create();
            await handler.TryHandleAsync(context, exception, CancellationToken.None);
            context.Response.Body.Position = 0;
            var text = await new StreamReader(context.Response.Body).ReadToEndAsync();
            Assert.DoesNotContain("GeoNames", text, StringComparison.Ordinal);
            Assert.DoesNotContain("secret", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Place dataset", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task IntegrationFailure_LogsTheProvider()
    {
        var (handler, context, logs) = Create();

        await handler.TryHandleAsync(context, new IntegrationException("GeoNames", "Place dataset could not be loaded."), CancellationToken.None);

        Assert.Contains(logs.Messages, message => message.Contains("GeoNames", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RequestAbortedByTheClient_Is499_WithoutABody()
    {
        var (handler, context, _) = Create();
        using var aborted = new CancellationTokenSource();
        await aborted.CancelAsync();
        context.RequestAborted = aborted.Token;

        Assert.True(await handler.TryHandleAsync(context, new OperationCanceledException(), CancellationToken.None));

        Assert.Equal(499, context.Response.StatusCode);
        Assert.Equal(0, context.Response.Body.Length);
    }

    private static (GlobalExceptionHandler Handler, DefaultHttpContext Context, CapturingLoggerProvider Logs) Create()
    {
        var logs = new CapturingLoggerProvider();
        var services = new ServiceCollection()
            .AddLogging(builder => builder.AddProvider(logs))
            .AddProblemDetails()
            .BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Response.Body = new MemoryStream();
        context.Request.Headers.Accept = "application/json";

        return (ActivatorUtilities.CreateInstance<GlobalExceptionHandler>(services), context, logs);
    }

    private static async Task<JsonDocument> ReadBodyAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;
        return await JsonDocument.ParseAsync(context.Response.Body);
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public List<string> Messages { get; } = [];

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Messages);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(List<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (messages)
                {
                    messages.Add(formatter(state, exception));
                }
            }
        }
    }
}
