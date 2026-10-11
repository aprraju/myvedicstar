using System.Reflection;
using MyVedicStar.Caching;
namespace MyVedicDay.Api.Infrastructure.Localization;

internal static class ReferenceCache
{
    // Deployment-specific module IDs invalidate copied reference results when the corresponding content modules change.
    private static readonly string ContentVersion = string.Join(":", new[] { typeof(ReferenceCache).Assembly,
        Assembly.Load("MyVedicDay.Panchang.Business"), Assembly.Load("MyVedicDay.Muhurta.Business"), Assembly.Load("MyVedicDay.Localization.Business"), Assembly.Load("MyVedicDay.Traditions.Business"), Assembly.Load("MyVedicDay.Localization.Data"), Assembly.Load("MyVedicDay.Panchang.Data"), Assembly.Load("MyVedicDay.Muhurta.Data") }.Select(a => a.ManifestModule.ModuleVersionId));
    internal static Task<T> GetAsync<T>(IApplicationCache? cache, string catalog, IReadOnlyList<string> languages, string? tradition, Func<CancellationToken, Task<T>> factory, CancellationToken token) where T : class
    {
        var key = CacheKeyBuilder.Create("reference", new Dictionary<string, string?>
        {
            ["catalog"] = catalog,
            ["languages"] = string.Join(",", languages.Select(l => l.Trim().ToLowerInvariant())),
            ["tradition"] = tradition?.Trim().ToLowerInvariant(),
            ["content"] = ContentVersion
        });
        return cache is null ? factory(token) : cache.GetOrCreateAsync(key, factory, DateTimeOffset.UtcNow.AddDays(2), token);
    }
}
