using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using MyVedicStar.ApiProtection.Resilience;
using MyVedicStar.ApiProtection.Security;

namespace MyVedicDay.Ai;

/// <summary>Registers the provider-neutral AI port (platform module; never references a feature).</summary>
public static class AiServiceCollectionExtensions
{
    /// <summary>
    /// Adds <see cref="IStructuredTextGenerator"/>: the configured provider with a timeout and circuit breaker, or a
    /// disabled implementation when <c>Ai:Enabled</c> is false. Safe to call from several modules.
    /// </summary>
    public static IServiceCollection AddAiIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        if (services.Any(descriptor => descriptor.ServiceType == typeof(IStructuredTextGenerator)))
        {
            return services;
        }

        services.AddOptions<AiOptions>()
            .Bind(configuration.GetSection(AiOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<AiOptions>, AiOptionsValidator>());

        var settings = configuration.GetSection(AiOptions.SectionName).Get<AiOptions>() ?? new AiOptions();
        if (!settings.Enabled || settings.Provider == AiProvider.None)
        {
            services.AddSingleton<IStructuredTextGenerator, DisabledStructuredTextGenerator>();
            return services;
        }

        services.AddHttpClient("ai-provider", client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false })
            .AddResilienceHandler("transient-retry", builder => builder.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = 2,
                Delay = TimeSpan.FromMilliseconds(200),
                ShouldHandle = args => ValueTask.FromResult(
                    args.Outcome.Result is { StatusCode: >= System.Net.HttpStatusCode.InternalServerError }),
            }));
        services.AddHttpClient("ai-provider")
            .AddHttpMessageHandler<SsrfProtectionHandler>();

        services.AddSingleton<IStructuredTextGenerator>(provider => new ChatClientStructuredTextGenerator(
            AiChatClientFactory.Create(provider.GetRequiredService<IOptions<AiOptions>>().Value,
                provider.GetRequiredService<IHttpClientFactory>().CreateClient("ai-provider")),
            provider.GetRequiredService<IOptions<AiOptions>>(),
            provider.GetRequiredService<ICircuitBreakerRegistry>(),
            provider.GetRequiredService<IBulkheadPolicy>(),
            provider.GetRequiredService<ICalculationTimeoutService>(),
            provider.GetRequiredService<ILogger<ChatClientStructuredTextGenerator>>()));

        return services;
    }

}
