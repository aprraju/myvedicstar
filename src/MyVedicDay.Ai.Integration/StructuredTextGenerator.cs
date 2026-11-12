using System.Diagnostics;
using Anthropic;
using Azure;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyVedicStar.ApiProtection.Resilience;
using System.ClientModel;
using System.ClientModel.Primitives;

namespace MyVedicDay.Ai;

/// <summary>A request for schema-constrained output.</summary>
/// <param name="Operation">Operation name for logs and metrics, e.g. <c>muhurta.explain</c>.</param>
/// <param name="Instructions">System instructions (versioned data, never user text).</param>
/// <param name="Input">The data to work on, e.g. a JSON evaluation; user text only inside clearly delimited fields.</param>
/// <param name="PromptVersion">Version of the instructions, for traceability.</param>
internal sealed class DisabledStructuredTextGenerator : IStructuredTextGenerator
{
    public bool IsEnabled => false;

    public string Fingerprint => "none";

    public Task<AiResult<T>> GenerateAsync<T>(AiRequest request, CancellationToken cancellationToken)
        where T : class => Task.FromResult(new AiResult<T>(null, AiOutcome.Disabled, "None", null));
}

/// <summary>
/// <see cref="IStructuredTextGenerator"/> over <see cref="IChatClient"/> with a timeout and circuit breaker. Logs provider,
/// operation, outcome, duration and token counts; never the prompt or the response text.
/// </summary>
internal sealed partial class ChatClientStructuredTextGenerator(
    IChatClient client,
    IOptions<AiOptions> options,
    ICircuitBreakerRegistry circuitBreakers,
    IBulkheadPolicy bulkhead,
    ICalculationTimeoutService timeouts,
    ILogger<ChatClientStructuredTextGenerator> logger) : IStructuredTextGenerator
{
    public bool IsEnabled => true;

    public string Fingerprint => $"{options.Value.Provider}/{options.Value.ResolvedModel}";

    public async Task<AiResult<T>> GenerateAsync<T>(AiRequest request, CancellationToken cancellationToken)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(request);

        var provider = options.Value.Provider.ToString();
        var model = options.Value.ResolvedModel;
        var started = Stopwatch.GetTimestamp();
        AiOutcome outcome;
        T? value = null;
        using var timeout = timeouts.CreateToken(TimeoutType.Integration, cancellationToken);
        try
        {
            var response = await bulkhead.ExecuteAsync("ai-provider",
                token => circuitBreakers.GetOrCreate($"ai-{provider}").ExecuteAsync(
                async callToken => await client.GetResponseAsync<T>(
                    [new ChatMessage(ChatRole.System, request.Instructions), new ChatMessage(ChatRole.User, request.Input)],
                    new ChatOptions { ModelId = model, MaxOutputTokens = options.Value.MaxOutputTokens },
                    cancellationToken: callToken), token),
                timeout.Token);

            outcome = response.TryGetResult(out var result) && result is not null ? AiOutcome.Succeeded : AiOutcome.InvalidOutput;
            value = outcome == AiOutcome.Succeeded ? result : null;
            LogUsage(request.Operation, provider, response.Usage?.InputTokenCount, response.Usage?.OutputTokenCount);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (timeout.IsTimedOut)
        {
            outcome = AiOutcome.TimedOut;
        }
        catch (CircuitOpenException)
        {
            outcome = AiOutcome.CircuitOpen;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Provider errors (authentication, quota, refusal, network) must never fail the feature.
            LogProviderError(request.Operation, provider, exception.GetType().Name);
            outcome = AiOutcome.Failed;
        }

        LogCompleted(request.Operation, provider, model, request.PromptVersion, outcome, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        return new AiResult<T>(value, outcome, provider, model);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "AI {Operation} via {Provider} ({Model}, prompt {PromptVersion}): {Outcome} in {DurationMs:0} ms")]
    private partial void LogCompleted(string operation, string provider, string? model, string promptVersion, AiOutcome outcome, double durationMs);

    [LoggerMessage(Level = LogLevel.Debug, Message = "AI {Operation} via {Provider} used {InputTokens} input and {OutputTokens} output tokens")]
    private partial void LogUsage(string operation, string provider, long? inputTokens, long? outputTokens);

    [LoggerMessage(Level = LogLevel.Warning, Message = "AI {Operation} via {Provider} failed with {ErrorType}")]
    private partial void LogProviderError(string operation, string provider, string errorType);
}

/// <summary>Creates the <see cref="IChatClient"/> for the configured provider (adapters behind one interface).</summary>
internal static class AiChatClientFactory
{
    public static IChatClient Create(AiOptions options, HttpClient httpClient) => options.Provider switch
    {
        AiProvider.Anthropic => (string.IsNullOrWhiteSpace(options.Anthropic.ApiKey)
            ? new AnthropicClient { HttpClient = httpClient, MaxRetries = 0 }
            : new AnthropicClient { ApiKey = options.Anthropic.ApiKey, HttpClient = httpClient, MaxRetries = 0 })
            .AsIChatClient(options.ResolvedModel!, options.MaxOutputTokens),
        AiProvider.OpenAI => new OpenAI.Chat.ChatClient(options.Model!, new ApiKeyCredential(options.OpenAI.ApiKey!),
            new OpenAI.OpenAIClientOptions { Transport = new HttpClientPipelineTransport(httpClient), RetryPolicy = new ClientRetryPolicy(0) }).AsIChatClient(),
        AiProvider.AzureOpenAI => (string.IsNullOrWhiteSpace(options.AzureOpenAI.ApiKey)
                ? new AzureOpenAIClient(new Uri(options.AzureOpenAI.Endpoint!), new DefaultAzureCredential(),
                    new AzureOpenAIClientOptions { Transport = new HttpClientPipelineTransport(httpClient), RetryPolicy = new ClientRetryPolicy(0) })
                : new AzureOpenAIClient(new Uri(options.AzureOpenAI.Endpoint!), new AzureKeyCredential(options.AzureOpenAI.ApiKey),
                    new AzureOpenAIClientOptions { Transport = new HttpClientPipelineTransport(httpClient), RetryPolicy = new ClientRetryPolicy(0) }))
            .GetChatClient(options.AzureOpenAI.Deployment!)
            .AsIChatClient(),
        _ => throw new InvalidOperationException("No AI provider is configured."),
    };
}
