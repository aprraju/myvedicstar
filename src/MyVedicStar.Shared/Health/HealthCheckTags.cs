namespace MyVedicStar.Shared.Health;

/// <summary>Tags that decide which health endpoint runs a registered health check.</summary>
public static class HealthCheckTags
{
    /// <summary>
    /// Dependencies the instance needs before it can serve traffic (for example its database).
    /// Checked by the readiness endpoint only, never by liveness, so a dependency outage does not restart containers.
    /// </summary>
    public const string Readiness = "ready";
}
