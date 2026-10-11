using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MyVedicDay.Caching;

/// <summary>
/// Readiness check that round-trips a probe value through the distributed cache.
/// </summary>
/// <remarks>
/// Reports <see cref="HealthStatus.Degraded"/> rather than unhealthy: requests still succeed without the cache.
/// </remarks>
internal sealed class DistributedCacheHealthCheck(IDistributedCache cache) : IHealthCheck
{
    private const string ProbeKey = "health:probe";
    private static readonly byte[] ProbeValue = [1];

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await cache.SetAsync(
                ProbeKey,
                ProbeValue,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1) },
                cancellationToken);

            return await cache.GetAsync(ProbeKey, cancellationToken) is null
                ? HealthCheckResult.Degraded("Distributed cache did not return the probe value.")
                : HealthCheckResult.Healthy();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return HealthCheckResult.Degraded("Distributed cache is unavailable.", exception);
        }
    }
}
