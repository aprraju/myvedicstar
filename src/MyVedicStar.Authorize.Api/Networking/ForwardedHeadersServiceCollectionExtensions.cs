using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

namespace MyVedicStar.Authorize.Api.Networking;

internal static class ForwardedHeadersServiceCollectionExtensions
{
    /// <summary>
    /// Honours <c>X-Forwarded-For</c>/<c>X-Forwarded-Proto</c> only from the configured trusted proxies.
    /// The framework's default trust of loopback is removed so nothing is trusted unless configured.
    /// </summary>
    public static IServiceCollection AddTrustedForwardedHeaders(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<TrustedProxyOptions>()
            .Bind(configuration.GetSection(TrustedProxyOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                options => options.KnownProxies.All(proxy => IPAddress.TryParse(proxy, out _)),
                $"{TrustedProxyOptions.SectionName}:{nameof(TrustedProxyOptions.KnownProxies)} must contain only IP addresses.")
            .Validate(
                options => options.KnownNetworks.All(network => System.Net.IPNetwork.TryParse(network, out _)),
                $"{TrustedProxyOptions.SectionName}:{nameof(TrustedProxyOptions.KnownNetworks)} must contain only CIDR ranges.")
            .ValidateOnStart();

        services.AddOptions<ForwardedHeadersOptions>()
            .Configure<IOptions<TrustedProxyOptions>>((forwardedHeaders, trustedProxies) =>
            {
                var trusted = trustedProxies.Value;

                forwardedHeaders.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                forwardedHeaders.ForwardLimit = trusted.ForwardLimit;

                forwardedHeaders.KnownProxies.Clear();
                forwardedHeaders.KnownIPNetworks.Clear();

                foreach (var proxy in trusted.KnownProxies)
                {
                    forwardedHeaders.KnownProxies.Add(IPAddress.Parse(proxy));
                }

                foreach (var network in trusted.KnownNetworks)
                {
                    forwardedHeaders.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
                }
            });

        return services;
    }
}
