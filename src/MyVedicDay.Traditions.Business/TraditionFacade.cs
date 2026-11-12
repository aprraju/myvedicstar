using MyVedicDay.Caching;
using MyVedicStar.Caching;
using MyVedicDay.Localization;

namespace MyVedicDay.Traditions;

/// <summary>A tradition with its localized name and region description.</summary>
public sealed record TraditionInfo(string Key, string Name, string? Description);

/// <summary>The available traditions in one language.</summary>
public sealed record TraditionsResult(string Language, IReadOnlyList<TraditionInfo> Traditions);

/// <summary>
/// Lists the traditions for a tradition picker.
/// </summary>
public interface ITraditionFacade
{
    /// <param name="preferredLanguages">Language tags in order of preference.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    Task<TraditionsResult> GetTraditionsAsync(IReadOnlyList<string>? preferredLanguages, CancellationToken cancellationToken);
}

internal sealed class TraditionFacade(ITraditionRegistry registry, ILocalizedContentProvider contentProvider, IApplicationCache? referenceCache = null) : ITraditionFacade
{
    public const string ContentCategory = "traditions";

    public Task<TraditionsResult> GetTraditionsAsync(IReadOnlyList<string>? preferredLanguages, CancellationToken cancellationToken) =>
        ReferenceCache.GetAsync(referenceCache, "traditions", preferredLanguages ?? [], null, token => GetTraditionsAsyncCore(preferredLanguages, token), cancellationToken);

    private async Task<TraditionsResult> GetTraditionsAsyncCore(IReadOnlyList<string>? preferredLanguages, CancellationToken cancellationToken)
    {
        var content = await contentProvider.GetContentAsync(preferredLanguages, tradition: null, cancellationToken);
        var keys = await registry.GetKeysAsync(cancellationToken);

        return new TraditionsResult(
            content.Language,
            keys.Select(key =>
            {
                var entry = content.GetEntry(ContentCategory, key);
                return new TraditionInfo(key, entry.Name, entry.Description);
            }).ToArray());
    }
}
