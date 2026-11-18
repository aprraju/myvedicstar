using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace MyVedicStar.Caching;

public sealed class ApplicationCacheOptions
{
    public bool Enabled { get; set; } = true;
    public string KeyVersion { get; set; } = "1";
    public long MemoryLimitBytes { get; set; } = 32 * 1024 * 1024;
    public int MaximumPayloadBytes { get; set; } = 512 * 1024;
    public bool MemoryEnabled { get; set; } = true;
    public PostgresCacheSettings L2 { get; set; } = new();
    public Dictionary<string, CachePolicy> Policies { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["default"] = new(),
        ["panchang"] = new() { MemoryLifetime = TimeSpan.FromMinutes(5), DistributedLifetime = TimeSpan.FromDays(30) },
        ["historical"] = new() { MemoryLifetime = TimeSpan.Zero, DistributedLifetime = TimeSpan.FromDays(30) },
        ["calendar"] = new() { MemoryLifetime = TimeSpan.Zero, DistributedLifetime = TimeSpan.FromDays(1) },
        ["muhurta"] = new() { MemoryLifetime = TimeSpan.Zero, DistributedLifetime = TimeSpan.FromHours(6) },
        ["festivals"] = new() { MemoryLifetime = TimeSpan.Zero, DistributedLifetime = TimeSpan.FromDays(2) },
        ["reference"] = new() { MemoryLifetime = TimeSpan.FromMinutes(5), DistributedLifetime = TimeSpan.FromDays(2) },
        ["calculation"] = new() { MemoryLifetime = TimeSpan.Zero, DistributedLifetime = TimeSpan.FromDays(30) },
        // Kundali section reads (KUN-21). Warm reads must stay well inside the 100 ms section budget, so a short L1
        // lifetime is kept; correctness does not depend on it because the key carries the profile revision and versions.
        ["kundali"] = new() { MemoryLifetime = TimeSpan.FromMinutes(2), DistributedLifetime = TimeSpan.FromHours(12) },
    };
}

public sealed class PostgresCacheSettings
{
    public bool Enabled { get; set; }
    public string Schema { get; set; } = "myvedicstar";
    public string Table { get; set; } = "cache_entries";
    public int MaximumConnections { get; set; } = 4;
    public int MaximumConcurrentOperations { get; set; } = 4;
    public int CommandTimeoutSeconds { get; set; } = 2;
    public TimeSpan FailureBackoff { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(30);
}

public sealed class CachePolicy
{
    public TimeSpan MemoryLifetime { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan DistributedLifetime { get; set; } = TimeSpan.FromHours(2);
}

internal sealed class CacheOptionsValidator : IValidateOptions<ApplicationCacheOptions>
{
    public ValidateOptionsResult Validate(string? name, ApplicationCacheOptions options)
    {
        var errors = new List<string>();
        if (options.L2 is null || options.Policies is null) return ValidateOptionsResult.Fail("Caching:L2 and Caching:Policies are required.");
        if (!Regex.IsMatch(options.KeyVersion ?? string.Empty, "^[a-zA-Z0-9_-]{1,32}$")) errors.Add("Caching:KeyVersion must be a short version identifier.");
        if (options.MemoryLimitBytes < 1024 * 1024 || options.MemoryLimitBytes > 256L * 1024 * 1024) errors.Add("Caching:MemoryLimitBytes must be between 1 and 256 MiB.");
        if (options.MaximumPayloadBytes < 1024 || options.MaximumPayloadBytes > options.MemoryLimitBytes / 2) errors.Add("Caching:MaximumPayloadBytes must fit within the memory budget.");
        if (options.L2.Schema != "myvedicstar" || options.L2.Table != "cache_entries") errors.Add("Caching:L2 schema/table must match the migration: myvedicstar.cache_entries.");
        if (options.L2.MaximumConnections is < 1 or > 16 || options.L2.MaximumConcurrentOperations < 1 || options.L2.MaximumConcurrentOperations > options.L2.MaximumConnections) errors.Add("Caching:L2 concurrency must not exceed its bounded connection pool (1-16).");
        if (options.L2.CommandTimeoutSeconds is < 1 or > 30 || options.L2.FailureBackoff < TimeSpan.FromSeconds(1) || options.L2.CleanupInterval < TimeSpan.FromMinutes(5)) errors.Add("Caching:L2 timeout, backoff or cleanup interval is invalid.");
        if (!options.Policies.ContainsKey("default")) errors.Add("Caching:Policies:default is required.");
        foreach (var policy in options.Policies)
            if (policy.Value is null || policy.Value.MemoryLifetime < TimeSpan.Zero || policy.Value.DistributedLifetime <= TimeSpan.Zero || policy.Value.MemoryLifetime > policy.Value.DistributedLifetime || policy.Value.DistributedLifetime > TimeSpan.FromDays(365)) errors.Add($"Caching policy {policy.Key}: require 0 <= memory <= distributed <= 365 days.");
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
