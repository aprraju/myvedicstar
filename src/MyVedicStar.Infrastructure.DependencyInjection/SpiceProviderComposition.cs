using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyVedicDay.Astronomy;
using MyVedicStar.Astronomy.Core;
using MyVedicStar.Astronomy.Jpl.Provider;

namespace MyVedicStar.Infrastructure.DependencyInjection;

/// <summary>
/// Composes the JPL/SPICE astronomy engine from host configuration (FR-06, AST-12 #77).
/// </summary>
/// <remarks>
/// <para>The SPICE provider needs an <see cref="IEphemerisEngine"/>, and <see cref="AstronomyProviderRegistration"/>
/// refuses to select <c>spice</c> or <c>compare</c> without one rather than falling back. Since AST-12 makes
/// <c>spice</c> the absent-key default, every product host must be able to supply it, so the factory lives here,
/// next to the provider registration, instead of being duplicated per host.</para>
/// <para><b>Kernel paths are configuration, the licence statement is code.</b> Mount points are read from
/// <c>Astronomy:Spice:Kernels</c> because the deployment image decides them; the toolkit provenance and the reported
/// toolkit version come from the image environment as well. The <see cref="SpiceConfiguration.Default"/> kernel set is
/// the documented ship set and is used only when configuration names none, and it changes nothing about which span is
/// supported: coverage is still derived from whatever kernels are declared.</para>
/// <para>The engine is constructed eagerly on first resolution, never probing the native library at build time, so
/// composition succeeds on a host without the toolkit and the first calculation fails loudly with
/// <c>SpiceUnavailableException</c> — the D1 contract.</para>
/// </remarks>
public static class SpiceProviderComposition
{
    /// <summary>Configuration section holding the SPICE mount points and toolkit report.</summary>
    public const string SectionName = "Astronomy:Spice";

    /// <summary>Builds the engine factory handed to <see cref="AstronomyProviderRegistration.AddAstronomyProvider"/>.</summary>
    public static Func<IServiceProvider, IEphemerisEngine> EngineFactory(IConfiguration configuration) =>
        _ => new ProviderEngineAdapter(new SpiceAstronomyEngine(ReadConfiguration(configuration)));

    /// <summary>
    /// Reads <see cref="SpiceConfiguration"/> from host configuration, layering it over
    /// <see cref="SpiceConfiguration.Default"/> rather than inventing values.
    /// </summary>
    /// <remarks>
    /// A configured kernel list replaces the default list entirely. Merging the two would make a deployment that
    /// names only DE441 silently keep the DE440 span it did not name - a supported-range claim assembled from two
    /// sources, which is exactly the drift the single serviceability gate exists to prevent.
    /// </remarks>
    public static SpiceConfiguration ReadConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(SectionName);
        var kernels = ReadKernels(section);
        return SpiceConfiguration.Default with
        {
            ToolkitSource = section["ToolkitSource"] ?? SpiceConfiguration.Default.ToolkitSource,
            ReportedToolkitVersion = section["ReportedToolkitVersion"],
            Kernels = kernels ?? SpiceConfiguration.Default.Kernels
        };
    }

    private static IReadOnlyList<SpiceKernel>? ReadKernels(IConfigurationSection section)
    {
        var entries = section.GetSection("Kernels").GetChildren().ToList();
        if (entries.Count == 0)
            return null;

        var kernels = new List<SpiceKernel>(entries.Count);
        foreach (var entry in entries)
        {
            var path = entry["Path"];
            if (string.IsNullOrWhiteSpace(path))
                throw new InvalidOperationException(
                    $"{SectionName}:Kernels has an entry with no Path. A kernel row without a mount point would " +
                    "silently shrink or widen the supported span, so it is an error.");

            kernels.Add(new SpiceKernel(path, ReadDate(entry["From"], path), ReadDate(entry["To"], path)));
        }

        return kernels;
    }

    private static DateOnly? ReadDate(string? value, string kernelPath)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (DateOnly.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var parsed))
            return parsed;

        throw new InvalidOperationException(
            $"{SectionName}:Kernels entry '{kernelPath}' has a coverage date '{value}' that is not an ISO date.");
    }
}
