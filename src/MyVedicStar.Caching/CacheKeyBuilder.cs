using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MyVedicStar.Caching;

/// <summary>Versioned, typed, hashed keys; values such as locations and optional birth stars never appear in database keys.</summary>
public static class CacheKeyBuilder
{
    public static string Create(string category, IReadOnlyDictionary<string, string?> inputs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        var normalized = new SortedDictionary<string, string?>(StringComparer.Ordinal);
        foreach (var input in inputs) normalized.Add(input.Key, input.Value);
        return category + ":" + Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(normalized)));
    }

    // Native HybridCache retains tag metadata independently of its bounded IMemoryCache.
    // A fixed set of 4096 hashed buckets bounds this metadata for legacy untyped Remove.
    // Typed Remove uses the exact storage key and does not invalidate adjacent entries.
    internal static string InvalidationTag(string key, string version) => "mvs:" + version + ":tag:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..3];

    internal static string StorageKey<T>(string key, string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return "mvs:" + version + ":" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(typeof(T).FullName + "\0" + key)));
    }
}
