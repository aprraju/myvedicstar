using System.Security.Cryptography;
using System.Text;
using MyVedicStar.Kundali.Shared;

namespace MyVedicStar.Kundali.Business;

/// <summary>
/// Cache identity for Kundali results (KUN-21). Every part of a key is something that can change an answer:
/// <list type="bullet">
/// <item>the owner and profile, so a shared L2 entry is unreadable across accounts;</item>
/// <item>the profile input revision, so an edited profile cannot be served a pre-edit result;</item>
/// <item>the effective calculation settings (ayanamsa), read from the existing Settings store;</item>
/// <item>every engine / ephemeris / rule-set / timezone-data version, so an upgrade cannot be served as current.</item>
/// </list>
/// The key is a SHA-256 over a canonical, ordinally sorted string: no personal input (birth date, time, coordinates,
/// name) ever appears in a cache key, a database row or a log line.
/// </summary>
public static class KundaliCacheKeys
{
    private const string Category = "kundali";

    /// <summary>
    /// Revision identity of a result: profile + input revision + effective settings + version tuple.
    /// </summary>
    /// <remarks>
    /// <para><c>ruleSetVersions</c> is every rule version that can change a published number, not just the engine rule
    /// set. Ashtakavarga tables are a separate, independently versioned rule set (KUN-11), so passing only
    /// <see cref="KundaliProvenance.RuleSetVersion"/> would let a table change reuse cached results computed under the
    /// old tables - the exact staleness this key exists to prevent. Callers supply the list so adding the next rule set
    /// is a data change here, not a second key-construction path that can be forgotten.</para>
    /// </remarks>
    public static string RevisionKey(Guid accountId, Guid profileId, long profileRevision, KundaliProvenance provenance,
        IReadOnlyList<string> ruleSetVersions) =>
        Hash(Category, "revision", [
            ("account", accountId.ToString("N")),
            ("profile", profileId.ToString("N")),
            ("revision", profileRevision.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("engine", provenance.EngineVersion),
            ("ephemeris", provenance.EphemerisVersion),
            ("rules", provenance.RuleSetVersion),
            ("rulesets", string.Join(",", ruleSetVersions)),
            ("tzdb", provenance.TimezoneDataVersion),
            ("ayanamsa", provenance.Ayanamsa)
        ]);

    /// <summary>One section payload. A section is its own key so regenerating one section does not invalidate the rest.</summary>
    public static string Section(string revisionKey, Guid profileId, string section) =>
        Hash(Category, "section", [("revision", revisionKey), ("profile", profileId.ToString("N")), ("section", section)]);

    /// <summary>The revision-summary envelope returned with the aggregate.</summary>
    public static string View(string revisionKey, Guid profileId) =>
        Hash(Category, "view", [("revision", revisionKey), ("profile", profileId.ToString("N"))]);

    /// <summary>True when a key was produced by this factory for this category, so a foreign or hand-built key is never
    /// accepted as a cache identity.</summary>
    public static bool IsWellFormed(string? key) =>
        key is { Length: 72 } && key.StartsWith(Category + ":", StringComparison.Ordinal);

    private static string Hash(string category, string kind, IEnumerable<(string Name, string? Value)> inputs)
    {
        // Ordinal, sorted: two hosts must produce the same key for the same inputs, whatever order they were added in.
        var canonical = new StringBuilder(category).Append(':').Append(kind);
        foreach (var (name, value) in inputs.OrderBy(entry => entry.Name, StringComparer.Ordinal))
            canonical.Append('|').Append(name).Append('=').Append(value ?? "");
        return category + ":" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }
}

/// <summary>
/// The freshness rule, in one place: a result may be presented as current only when it was computed from the profile's
/// present revision, under the present version tuple. Nothing here reads a cache or a clock beyond the supplied
/// `now`, so the rule is testable without either.
/// </summary>
public static class KundaliFreshness
{
    /// <summary>The dasha highlight a cached overview carries goes stale at the next mahadasha boundary.</summary>
    public static DateTimeOffset? CurrentDashaBoundary(IEnumerable<KundaliDasha>? mahadashas, DateTimeOffset now)
    {
        if (mahadashas is null) return null;
        foreach (var period in mahadashas)
        {
            // Half-open [start, end): consecutive periods share a boundary and the later one owns it, matching the
            // selection rule the overview already uses, so the expiry instant and the value cannot disagree.
            if (now >= period.StartUtc && now < period.EndUtc) return period.EndUtc;
        }
        return null;
    }

    /// <summary>
    /// True when a cached copy may be served. `copy` is what the cache holds for the key built from the profile's
    /// present revision; a copy whose own freshness says otherwise (expired dasha boundary, missing key) is a miss.
    /// </summary>
    public static bool IsServable(KundaliOverview? copy, DateTimeOffset now) =>
        copy is not null && copy.Freshness.IsFreshAt(now);

    /// <summary>
    /// True when a background result is obsolete (KUN-22). A result computed for a different profile revision - or under
    /// a different version tuple - is discarded, never shown, however complete it looks.
    /// </summary>
    public static bool IsObsolete(long resultProfileRevision, long currentProfileRevision,
        KundaliProvenance resultProvenance, KundaliProvenance currentProvenance) =>
        resultProfileRevision != currentProfileRevision || resultProvenance != currentProvenance;
}
