using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace MyVedicDay.Ai;

/// <summary>Generative-AI providers selectable by configuration (ADR-0012).</summary>
public enum AiProvider
{
    /// <summary>No AI: features use their deterministic text.</summary>
    None = 0,

    /// <summary>Anthropic Claude through the official <c>Anthropic</c> SDK.</summary>
    Anthropic,

    /// <summary>OpenAI (ChatGPT models).</summary>
    OpenAI,

    /// <summary>Azure OpenAI / Azure AI Foundry, the Microsoft (Copilot) route; managed identity by default.</summary>
    AzureOpenAI,
}

/// <summary>
/// Generative-AI settings. Bound from the <c>Ai</c> section and validated at startup. Keys belong in user-secrets, Key
/// Vault or environment variables, never in <c>appsettings.json</c>.
/// </summary>
public sealed class AiOptions
{
    public const string SectionName = "Ai";

    /// <summary>Master switch. Off by default: every feature works without AI.</summary>
    public bool Enabled { get; set; }

    /// <summary>Provider used when <see cref="Enabled"/>.</summary>
    public AiProvider Provider { get; set; } = AiProvider.None;

    /// <summary>
    /// Model id (Anthropic, OpenAI). Overrides the provider's configured default (<c>Ai:Anthropic:Model</c>); required
    /// for OpenAI.
    /// </summary>
    public string? Model { get; set; }

    /// <summary>Maximum output tokens per request.</summary>
    [Range(256, 32000)]
    public int MaxOutputTokens { get; set; } = 4000;

    public AnthropicSettings Anthropic { get; set; } = new();

    public OpenAiSettings OpenAI { get; set; } = new();

    public AzureOpenAiSettings AzureOpenAI { get; set; } = new();

    /// <summary>The model actually used, after provider defaults.</summary>
    public string? ResolvedModel => Provider switch
    {
        AiProvider.Anthropic => string.IsNullOrWhiteSpace(Model) ? Anthropic.Model : Model,
        AiProvider.AzureOpenAI => AzureOpenAI.Deployment,
        _ => Model,
    };

    /// <summary>Anthropic settings. Without <see cref="ApiKey"/> the SDK reads <c>ANTHROPIC_API_KEY</c>.</summary>
    public sealed class AnthropicSettings
    {
        public string? ApiKey { get; set; }

        /// <summary>Model used when <see cref="AiOptions.Model"/> is not set (configured in <c>appsettings.json</c>).</summary>
        public string? Model { get; set; }
    }

    /// <summary>OpenAI settings.</summary>
    public sealed class OpenAiSettings
    {
        public string? ApiKey { get; set; }
    }

    /// <summary>Azure OpenAI settings. Without <see cref="ApiKey"/>, <c>DefaultAzureCredential</c> (managed identity) is used.</summary>
    public sealed class AzureOpenAiSettings
    {
        public string? Endpoint { get; set; }

        public string? Deployment { get; set; }

        public string? ApiKey { get; set; }
    }
}

/// <summary>Fails startup when the selected provider is missing what it needs.</summary>
internal sealed class AiOptionsValidator : IValidateOptions<AiOptions>
{
    public ValidateOptionsResult Validate(string? name, AiOptions options)
    {
        if (!options.Enabled || options.Provider == AiProvider.None)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();
        switch (options.Provider)
        {
            case AiProvider.Anthropic:
                if (string.IsNullOrWhiteSpace(options.ResolvedModel))
                {
                    failures.Add("Ai:Model or Ai:Anthropic:Model is required for Anthropic.");
                }

                break;
            case AiProvider.OpenAI:
                if (string.IsNullOrWhiteSpace(options.Model))
                {
                    failures.Add("Ai:Model is required for OpenAI.");
                }

                if (string.IsNullOrWhiteSpace(options.OpenAI.ApiKey))
                {
                    failures.Add("Ai:OpenAI:ApiKey is required (use user-secrets or Key Vault).");
                }

                break;
            case AiProvider.AzureOpenAI:
                if (!Uri.TryCreate(options.AzureOpenAI.Endpoint, UriKind.Absolute, out var endpoint) || endpoint.Scheme != Uri.UriSchemeHttps)
                {
                    failures.Add("Ai:AzureOpenAI:Endpoint must be an https URL.");
                }

                if (string.IsNullOrWhiteSpace(options.AzureOpenAI.Deployment))
                {
                    failures.Add("Ai:AzureOpenAI:Deployment is required.");
                }

                break;
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
