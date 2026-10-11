using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyVedicStar.ApiProtection.Extensions;
using MyVedicStar.ApiProtection.Configuration;
using MyVedicStar.ApiProtection.Resilience;
using MyVedicStar.ApiProtection.Security;
using System.Net;

namespace MyVedicDay.Ai.Tests.Unit;

/// <summary>
/// The provider-neutral AI port: outcomes instead of exceptions, timeout and circuit breaker, configuration-selected
/// providers, and startup validation.
/// </summary>
public sealed class StructuredTextGeneratorTests
{
    private static readonly AiRequest Request = new("test.operation", "Instructions", "{\"input\":1}", "v1");

    [Fact]
    public async Task ValidJson_IsParsedIntoTheRequestedType()
    {
        var client = new FakeChatClient((_, _) => Task.FromResult(Reply("""{"summary":"Hello"}""")));

        var result = await CreateGenerator(client).GenerateAsync<TestOutput>(Request, CancellationToken.None);

        Assert.Equal(AiOutcome.Succeeded, result.Outcome);
        Assert.Equal("Hello", result.Value!.Summary);
        Assert.Equal(("Anthropic", "claude-opus-5-5"), (result.Provider, result.Model));
        Assert.Equal(ChatRole.System, client.LastMessages[0].Role);
        Assert.Equal("Instructions", client.LastMessages[0].Text);
    }

    [Fact]
    public async Task UnparseableOutput_IsInvalidOutput()
    {
        var client = new FakeChatClient((_, _) => Task.FromResult(Reply("Sorry, I can't help with that.")));

        var result = await CreateGenerator(client).GenerateAsync<TestOutput>(Request, CancellationToken.None);

        Assert.Equal(AiOutcome.InvalidOutput, result.Outcome);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task ProviderError_IsReportedNotThrown()
    {
        var client = new FakeChatClient((_, _) => throw new HttpRequestException("401"));

        var result = await CreateGenerator(client).GenerateAsync<TestOutput>(Request, CancellationToken.None);

        Assert.Equal(AiOutcome.Failed, result.Outcome);
    }

    [Fact]
    public async Task SlowProvider_TimesOut()
    {
        var client = new FakeChatClient(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return Reply("{}");
        });

        var result = await CreateGenerator(client, options => options.Timeouts.IntegrationCallMs = 100).GenerateAsync<TestOutput>(Request, CancellationToken.None);

        Assert.Equal(AiOutcome.TimedOut, result.Outcome);
    }

    [Fact]
    public async Task RepeatedFailures_OpenTheCircuit_AndSkipTheProvider()
    {
        var client = new FakeChatClient((_, _) => throw new HttpRequestException("503"));
        var generator = CreateGenerator(client, options => options.CircuitBreaker.FailureThreshold = 2);

        await generator.GenerateAsync<TestOutput>(Request, CancellationToken.None);
        await generator.GenerateAsync<TestOutput>(Request, CancellationToken.None);
        var third = await generator.GenerateAsync<TestOutput>(Request, CancellationToken.None);

        Assert.Equal(AiOutcome.CircuitOpen, third.Outcome);
        Assert.Equal(2, client.Calls);
    }

    [Fact]
    public async Task CallerCancellation_Propagates()
    {
        using var cancellation = new CancellationTokenSource();
        var client = new FakeChatClient(async (_, token) =>
        {
            await cancellation.CancelAsync();
            token.ThrowIfCancellationRequested();
            return Reply("{}");
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateGenerator(client).GenerateAsync<TestOutput>(Request, cancellation.Token));
    }

    [Theory]
    [InlineData(400, 1)]
    [InlineData(401, 1)]
    [InlineData(429, 1)]
    [InlineData(503, 3)]
    public async Task HttpTransport_RetriesServerErrorsOnly(int status, int expectedCalls)
    {
        var configuration = TransportConfiguration(ssrfEnabled: false);
        var handler = new StubHttpHandler((HttpStatusCode)status);
        var services = new ServiceCollection().AddLogging().AddMyVedicStarApiProtection(configuration).AddAiIntegration(configuration);
        services.AddHttpClient("ai-provider").ConfigurePrimaryHttpMessageHandler(() => handler);
        using var provider = services.BuildServiceProvider();
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("ai-provider");

        using var response = await client.GetAsync(new Uri("https://example.com"));

        Assert.Equal((HttpStatusCode)status, response.StatusCode);
        Assert.Equal(expectedCalls, handler.Calls);
    }

    [Fact]
    public async Task HttpTransport_BlocksMetadataBeforeSending()
    {
        var configuration = TransportConfiguration(ssrfEnabled: true);
        var handler = new StubHttpHandler(HttpStatusCode.OK);
        var services = new ServiceCollection().AddLogging().AddMyVedicStarApiProtection(configuration).AddAiIntegration(configuration);
        services.AddHttpClient("ai-provider").ConfigurePrimaryHttpMessageHandler(() => handler);
        using var provider = services.BuildServiceProvider();
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("ai-provider");

        await Assert.ThrowsAsync<SsrfException>(() => client.GetAsync(new Uri("https://169.254.169.254/latest/meta-data")));
        Assert.Equal(0, handler.Calls);
    }

    private static IConfiguration TransportConfiguration(bool ssrfEnabled) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ai:Enabled"] = "true",
            ["Ai:Provider"] = "OpenAI",
            ["Ai:Model"] = "test-model",
            ["Ai:OpenAI:ApiKey"] = "test-key",
            ["ApiProtection:SsrfProtection:Enabled"] = ssrfEnabled.ToString(),
        }).Build();

    private sealed class StubHttpHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }

    [Fact]
    public async Task Disabled_IsTheDefault_AndReportsDisabled()
    {
        using var provider = new ServiceCollection().AddLogging().AddAiIntegration(new ConfigurationBuilder().Build()).BuildServiceProvider();

        var generator = provider.GetRequiredService<IStructuredTextGenerator>();
        var result = await generator.GenerateAsync<TestOutput>(Request, CancellationToken.None);

        Assert.False(generator.IsEnabled);
        Assert.Equal("none", generator.Fingerprint);
        Assert.Equal(AiOutcome.Disabled, result.Outcome);
    }

    [Theory]
    [InlineData("Anthropic", null, "Anthropic/claude-opus-5-5")]
    [InlineData("Anthropic", "claude-sonnet-5-5", "Anthropic/claude-sonnet-5-5")]
    [InlineData("OpenAI", "gpt-test", "OpenAI/gpt-test")]
    [InlineData("AzureOpenAI", null, "AzureOpenAI/vedic-deployment")]
    public void Provider_IsChosenByConfiguration(string provider, string? model, string fingerprint)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ai:Enabled"] = "true",
            ["Ai:Provider"] = provider,
            ["Ai:Model"] = model,
            ["Ai:Anthropic:ApiKey"] = "test-key",
            ["Ai:Anthropic:Model"] = "claude-opus-5-5",
            ["Ai:OpenAI:ApiKey"] = "test-key",
            ["Ai:AzureOpenAI:Endpoint"] = "https://example.openai.azure.com",
            ["Ai:AzureOpenAI:Deployment"] = "vedic-deployment",
            ["Ai:AzureOpenAI:ApiKey"] = "test-key",
        }).Build();
        using var services = new ServiceCollection().AddLogging().AddMyVedicStarApiProtection(configuration).AddAiIntegration(configuration).BuildServiceProvider();

        var generator = services.GetRequiredService<IStructuredTextGenerator>();

        Assert.True(generator.IsEnabled);
        Assert.Equal(fingerprint, generator.Fingerprint);
    }

    [Theory]
    [InlineData("OpenAI", null, null, null, "Ai:Model is required")]
    [InlineData("OpenAI", "gpt-test", null, null, "Ai:OpenAI:ApiKey is required")]
    [InlineData("AzureOpenAI", null, "http://insecure.example", "deployment", "must be an https URL")]
    [InlineData("AzureOpenAI", null, "https://example.openai.azure.com", null, "Deployment is required")]
    public void InvalidProviderSettings_FailValidation(string provider, string? model, string? endpoint, string? deployment, string expected)
    {
        var options = new AiOptions
        {
            Enabled = true,
            Provider = Enum.Parse<AiProvider>(provider),
            Model = model,
            AzureOpenAI = new AiOptions.AzureOpenAiSettings { Endpoint = endpoint, Deployment = deployment },
        };

        var result = new AiOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(expected, result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Anthropic_WithoutKey_IsValid_BecauseTheSdkReadsTheEnvironment()
    {
        var result = new AiOptionsValidator().Validate(null, new AiOptions
        {
            Enabled = true,
            Provider = AiProvider.Anthropic,
            Anthropic = new AiOptions.AnthropicSettings { Model = "claude-opus-5-5" },
        });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Anthropic_WithoutAnyModel_FailsValidation()
    {
        var result = new AiOptionsValidator().Validate(null, new AiOptions { Enabled = true, Provider = AiProvider.Anthropic });

        Assert.True(result.Failed);
        Assert.Contains("Ai:Anthropic:Model is required", result.FailureMessage, StringComparison.Ordinal);
    }

    private static ChatClientStructuredTextGenerator CreateGenerator(FakeChatClient client, Action<ApiProtectionOptions>? configure = null)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddOptions<AiOptions>().Configure(options =>
        {
            options.Enabled = true;
            options.Provider = AiProvider.Anthropic;
            options.Anthropic.Model = "claude-opus-5-5";
        });
        services.AddMyVedicStarApiProtection(new ConfigurationBuilder().Build());
        services.Configure<ApiProtectionOptions>(options => configure?.Invoke(options));
        var provider = services.BuildServiceProvider();

        return new ChatClientStructuredTextGenerator(
            client,
            provider.GetRequiredService<IOptions<AiOptions>>(),
            provider.GetRequiredService<ICircuitBreakerRegistry>(),
            provider.GetRequiredService<IBulkheadPolicy>(),
            provider.GetRequiredService<ICalculationTimeoutService>(),
            provider.GetRequiredService<ILogger<ChatClientStructuredTextGenerator>>());
    }

    private static ChatResponse Reply(string text) => new(new ChatMessage(ChatRole.Assistant, text));

    public sealed record TestOutput(string Summary);

    /// <summary>An <see cref="IChatClient"/> that answers with a scripted response.</summary>
    private sealed class FakeChatClient(Func<IReadOnlyList<ChatMessage>, CancellationToken, Task<ChatResponse>> respond) : IChatClient
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public IReadOnlyList<ChatMessage> LastMessages { get; private set; } = [];

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            LastMessages = messages.ToArray();
            return respond(LastMessages, cancellationToken);
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
