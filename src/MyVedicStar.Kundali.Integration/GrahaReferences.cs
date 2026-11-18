using MyVedicStar.Astronomy.Core;

namespace MyVedicStar.Kundali.Integration;

/// <summary>
/// Locates and reads the checked-in planetary reference set at composition time.
/// </summary>
/// <remarks>
/// <para>Resolution order is deliberate: the deployment asset directory first (so a published reference set can be
/// updated without a rebuild), then the repository layout (so tests and local runs work), then an empty set. An empty
/// set is not an error - it is the honest state in which the engine withholds every graha longitude rather than
/// asserting a tolerance nobody can defend.</para>
/// <para>The absolute file-system reads happen once, here, rather than inside the pure engine: the calculation project
/// stays free of I/O, which its purity test enforces.</para>
/// </remarks>
public static class GrahaReferences
{
    private static IReadOnlyList<PlanetaryReference>? _cached;

    /// <summary>The reference set for this process, loaded once.</summary>
    public static IReadOnlyList<PlanetaryReference> Load(IServiceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return _cached ??= LoadFromCandidates(CandidateRoots());
    }

    /// <summary>Explicit root, for tests and for a caller that already knows where the assets live.</summary>
    public static IReadOnlyList<PlanetaryReference> LoadFrom(string directory) =>
        Directory.Exists(directory) ? PlanetaryReferences.Load(directory) : [];

    private static IReadOnlyList<PlanetaryReference> LoadFromCandidates(IEnumerable<string> roots)
    {
        foreach (var root in roots)
        {
            if (!Directory.Exists(root))
                continue;

            var references = PlanetaryReferences.Load(root);
            if (references.Count > 0)
                return references;
        }

        return [];
    }

    private static IEnumerable<string> CandidateRoots()
    {
        // 1. An explicit deployment asset directory, when the host sets one.
        var configured = Environment.GetEnvironmentVariable("ASTRONOMY_ASSETS_PATH");
        if (!string.IsNullOrWhiteSpace(configured))
            yield return configured;

        // 2. The application directory, which ships astronomy assets in container images.
        yield return AppContext.BaseDirectory;

        // 3. Upward from the working directory, which covers running from the repository root or a solution folder.
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null)
        {
            yield return directory.FullName;
            directory = directory.Parent;
        }
    }
}
