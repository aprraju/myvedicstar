namespace MyVedicDay.Ai;

public sealed record AiRequest(string Operation, string Instructions, string Input, string PromptVersion);

/// <summary>How an AI request ended. Anything but <see cref="Succeeded"/> means the caller uses its deterministic fallback.</summary>
public enum AiOutcome
{
    Succeeded = 1,
    Disabled,
    Failed,
    TimedOut,
    CircuitOpen,
    InvalidOutput,
}

/// <summary>The outcome of an AI request.</summary>
/// <param name="Value">The parsed output when <paramref name="Outcome"/> is <see cref="AiOutcome.Succeeded"/>.</param>
/// <param name="Outcome">How the request ended.</param>
/// <param name="Provider">Provider used, e.g. <c>Anthropic</c>.</param>
/// <param name="Model">Model or deployment used.</param>
public sealed record AiResult<T>(T? Value, AiOutcome Outcome, string Provider, string? Model)
    where T : class;

/// <summary>
/// Provider-neutral port for schema-constrained generation (ADR-0012). Features describe the output as a C# type; the
/// provider is chosen by configuration. Never throws for provider problems: it reports an outcome so callers fall back.
/// </summary>
public interface IStructuredTextGenerator
{
    /// <summary>Whether a provider is configured and enabled.</summary>
    bool IsEnabled { get; }

    /// <summary>The configured provider and model, for cache keys and traceability.</summary>
    string Fingerprint { get; }

    Task<AiResult<T>> GenerateAsync<T>(AiRequest request, CancellationToken cancellationToken)
        where T : class;
}
