using System.Reflection;

namespace MyVedicDay.Api.Tests.Integration.Architecture;

/// <summary>
/// Guards the module and layer dependency rules (see docs/adr/0008). Each assembly may reference only the MyVedicDay
/// assemblies listed for it:
/// <list type="bullet">
/// <item><description>the API host depends only on Business modules (facades) and Shared;</description></item>
/// <item><description>platform modules (Astronomy, Location, Localization, Traditions, Caching) never depend on a feature
/// domain such as Panchang;</description></item>
/// <item><description>calculation engines stay free of I/O, web and persistence frameworks.</description></item>
/// </list>
/// </summary>
public sealed class LayerDependencyTests
{
    private const string Shared = "MyVedicDay.Shared";

    public static TheoryData<string, string[]> AllowedReferences() => new()
    {
        {
            "MyVedicDay.Api",
            [
                "MyVedicDay.Panchang.Business", "MyVedicDay.Festivals.Business", "MyVedicDay.Muhurta.Business", "MyVedicDay.Location.Business",
                "MyVedicDay.Localization.Business", "MyVedicDay.Traditions.Business", Shared,
            ]
        },

        // Platform modules.
        { "MyVedicDay.Astronomy.Calculation", [Shared] },
        { "MyVedicDay.Caching", [Shared] },
        { "MyVedicDay.Ai.Integration", [Shared] },
        { "MyVedicDay.Localization.Data", [Shared] },
        { "MyVedicDay.Localization.Business", ["MyVedicDay.Localization.Data", Shared] },
        { "MyVedicDay.Location.Integration", [Shared] },
        { "MyVedicDay.Location.Business", ["MyVedicDay.Location.Integration", Shared] },
        { "MyVedicDay.Traditions.Business", ["MyVedicDay.Localization.Business", "MyVedicDay.Localization.Data", Shared] },

        // Panchang domain.
        { "MyVedicDay.Panchang.Caching", ["MyVedicDay.Caching", "MyVedicDay.Panchang.Calculation", "MyVedicDay.Astronomy.Calculation"] },
        { "MyVedicDay.Panchang.Calculation", ["MyVedicDay.Astronomy.Calculation", Shared] },
        { "MyVedicDay.Panchang.Data", ["MyVedicDay.Localization.Data", Shared] },
        {
            "MyVedicDay.Panchang.Business",
            [
                "MyVedicDay.Panchang.Calculation", "MyVedicDay.Panchang.Data", "MyVedicDay.Astronomy.Calculation",
                "MyVedicDay.Location.Business", "MyVedicDay.Localization.Business", "MyVedicDay.Traditions.Business",
                "MyVedicDay.Caching", "MyVedicDay.Panchang.Caching", Shared,
            ]
        },

        // Festivals domain: may use Panchang's calculation contract (ADR-0008 rule 3), never Panchang.Business or Data.
        { "MyVedicDay.Festivals.Calculation", ["MyVedicDay.Panchang.Calculation", "MyVedicDay.Astronomy.Calculation", Shared] },
        { "MyVedicDay.Festivals.Data", ["MyVedicDay.Localization.Data", Shared] },
        {
            "MyVedicDay.Festivals.Business",
            [
                "MyVedicDay.Festivals.Calculation", "MyVedicDay.Festivals.Data", "MyVedicDay.Panchang.Calculation",
                "MyVedicDay.Astronomy.Calculation", "MyVedicDay.Location.Business", "MyVedicDay.Localization.Business",
                "MyVedicDay.Traditions.Business", "MyVedicDay.Caching", Shared,
            ]
        },

        // Muhurta domain: may use Panchang's calculation contract (ADR-0008 rule 3), never Panchang.Business or Data.
        { "MyVedicDay.Muhurta.Calculation", ["MyVedicDay.Panchang.Calculation", "MyVedicDay.Astronomy.Calculation", Shared] },
        { "MyVedicDay.Muhurta.Data", ["MyVedicDay.Localization.Data", Shared] },
        {
            "MyVedicDay.Muhurta.Business",
            [
                "MyVedicDay.Muhurta.Calculation", "MyVedicDay.Muhurta.Data", "MyVedicDay.Panchang.Calculation",
                "MyVedicDay.Astronomy.Calculation", "MyVedicDay.Location.Business", "MyVedicDay.Localization.Business",
                "MyVedicDay.Traditions.Business", "MyVedicDay.Caching", "MyVedicDay.Panchang.Caching", "MyVedicDay.Ai.Integration", Shared,
            ]
        },
    };

    [Theory]
    [MemberData(nameof(AllowedReferences))]
    public void Module_ReferencesOnlyAllowedModules(string assemblyName, string[] allowed)
    {
        var references = GetReferencedAssemblyNames(Assembly.Load(assemblyName))
            .Where(name => name.StartsWith("MyVedicDay.", StringComparison.Ordinal));

        Assert.All(references, reference => Assert.Contains(reference, allowed));
    }

    [Fact]
    public void EveryMyVedicDayAssembly_HasARule()
    {
        var ruled = AllowedReferences().Select(row => (string)row[0]).Append(Shared).ToHashSet(StringComparer.Ordinal);
        var loaded = GetReferencedAssemblyNames(typeof(Program).Assembly)
            .Where(name => name.StartsWith("MyVedicDay.", StringComparison.Ordinal))
            .SelectMany(name => GetReferencedAssemblyNames(Assembly.Load(name)).Append(name))
            .Where(name => name.StartsWith("MyVedicDay.", StringComparison.Ordinal))
            .Distinct();

        Assert.All(loaded, name => Assert.Contains(name, ruled));
    }

    [Theory]
    [InlineData("MyVedicDay.Astronomy.Calculation")]
    [InlineData("MyVedicDay.Panchang.Calculation")]
    [InlineData("MyVedicDay.Festivals.Calculation")]
    [InlineData("MyVedicDay.Muhurta.Calculation")]
    public void CalculationEngines_DoNotReferenceWebOrPersistenceFrameworks(string assemblyName)
    {
        var references = GetReferencedAssemblyNames(Assembly.Load(assemblyName));

        Assert.DoesNotContain(references, name =>
            name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal) ||
            name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) ||
            name.StartsWith("Microsoft.Extensions.Caching", StringComparison.Ordinal));
    }

    private static string[] GetReferencedAssemblyNames(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(reference => reference.Name ?? string.Empty).ToArray();
}
