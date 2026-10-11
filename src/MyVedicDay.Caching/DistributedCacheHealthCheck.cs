using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using MyVedicStar.Caching;

namespace MyVedicDay.Caching;

/// <summary>An optional cache failure degrades readiness without making otherwise healthy business services unavailable.</summary>
internal sealed class DistributedCacheHealthCheck(IOptions<ApplicationCacheOptions> options, ApplicationCacheMetrics metrics, TimeProvider time) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var failureTicks = metrics.LastBackendFailureTicks;
        var degraded = options.Value.Enabled && options.Value.L2.Enabled && failureTicks > 0
            && time.GetUtcNow().Ticks - failureTicks < options.Value.L2.FailureBackoff.Ticks;
        return Task.FromResult(degraded ? HealthCheckResult.Degraded("Optional PostgreSQL cache is backing off; calculations remain available.") : HealthCheckResult.Healthy());
    }
}
