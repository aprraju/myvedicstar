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
                "MyVedicDay.Api.Common", "MyVedicDay.Location.Api", "MyVedicDay.Traditions.Api", "MyVedicDay.Panchang.Api", "MyVedicDay.Festivals.Api", "MyVedicDay.Muhurta.Api",
            ]
        },

        { "MyVedicDay.Api.Common", ["MyVedicDay.Location.Business", "MyVedicDay.Localization.Business", "MyVedicDay.Traditions.Business", "MyVedicDay.Panchang.Business", "MyVedicDay.Festivals.Business", "MyVedicDay.Muhurta.Business", Shared] },
        { "MyVedicDay.Location.Api", ["MyVedicDay.Location.Business", "MyVedicDay.Api.Common", Shared] },
        { "MyVedicDay.Traditions.Api", ["MyVedicDay.Traditions.Business", "MyVedicDay.Api.Common", Shared] },
        { "MyVedicDay.Panchang.Api", ["MyVedicDay.Panchang.Business", "MyVedicDay.Festivals.Business", "MyVedicDay.Muhurta.Business", "MyVedicDay.Api.Common", Shared] },
        { "MyVedicDay.Festivals.Api", ["MyVedicDay.Festivals.Business", "MyVedicDay.Api.Common", Shared] },
        { "MyVedicDay.Muhurta.Api", ["MyVedicDay.Muhurta.Business", "MyVedicDay.Api.Common", Shared] },

        // Module-owned ports and transfer records; no implementation dependencies.
        { "MyVedicDay.Ai.Shared", [Shared] },
        { "MyVedicDay.Location.Shared", [Shared] },
        { "MyVedicDay.Localization.Shared", [Shared] },
        { "MyVedicDay.Panchang.Shared", [Shared] },
        { "MyVedicDay.Festivals.Shared", [Shared] },
        { "MyVedicDay.Muhurta.Shared", [Shared] },

        // Platform modules.
        { "MyVedicDay.Astronomy.Calculation", [Shared] },
        { "MyVedicDay.Caching", [Shared] },
        { "MyVedicDay.Ai.Integration", ["MyVedicDay.Ai.Shared", Shared] },
        { "MyVedicDay.Localization.Data", ["MyVedicDay.Localization.Shared", Shared] },
        { "MyVedicDay.Localization.Business", ["MyVedicDay.Localization.Shared", Shared] },
        { "MyVedicDay.Location.Integration", ["MyVedicDay.Location.Shared", Shared] },
        { "MyVedicDay.Location.Business", ["MyVedicDay.Location.Shared", Shared] },
        { "MyVedicDay.Traditions.Business", ["MyVedicDay.Localization.Business", "MyVedicDay.Localization.Shared", "MyVedicDay.Caching", Shared] },

        // Panchang domain.
        { "MyVedicDay.Panchang.Caching", ["MyVedicDay.Caching", "MyVedicDay.Panchang.Calculation", "MyVedicDay.Astronomy.Calculation"] },
        { "MyVedicDay.Panchang.Calculation", ["MyVedicDay.Astronomy.Calculation", Shared] },
        { "MyVedicDay.Panchang.Data", ["MyVedicDay.Localization.Shared", "MyVedicDay.Panchang.Shared", "MyVedicDay.Localization.Data", Shared] },
        {
            "MyVedicDay.Panchang.Business",
            [
                "MyVedicDay.Panchang.Calculation", "MyVedicDay.Panchang.Shared", "MyVedicDay.Astronomy.Calculation",
                "MyVedicDay.Location.Business", "MyVedicDay.Localization.Business", "MyVedicDay.Traditions.Business",
                "MyVedicDay.Caching", "MyVedicDay.Panchang.Caching", Shared,
            ]
        },

        // Festivals domain: may use Panchang's calculation contract (ADR-0008 rule 3), never Panchang.Business or Data.
        { "MyVedicDay.Festivals.Calculation", ["MyVedicDay.Panchang.Calculation", "MyVedicDay.Astronomy.Calculation", Shared] },
        { "MyVedicDay.Festivals.Data", ["MyVedicDay.Localization.Shared", "MyVedicDay.Festivals.Shared", "MyVedicDay.Localization.Data", Shared] },
        {
            "MyVedicDay.Festivals.Business",
            [
                "MyVedicDay.Festivals.Calculation", "MyVedicDay.Festivals.Shared", "MyVedicDay.Panchang.Calculation",
                "MyVedicDay.Astronomy.Calculation", "MyVedicDay.Location.Business", "MyVedicDay.Localization.Business",
                "MyVedicDay.Traditions.Business", "MyVedicDay.Caching", Shared,
            ]
        },

        // Muhurta domain: may use Panchang's calculation contract (ADR-0008 rule 3), never Panchang.Business or Data.
        { "MyVedicDay.Muhurta.Calculation", ["MyVedicDay.Panchang.Calculation", "MyVedicDay.Astronomy.Calculation", Shared] },
        { "MyVedicDay.Muhurta.Data", ["MyVedicDay.Localization.Shared", "MyVedicDay.Muhurta.Shared", "MyVedicDay.Localization.Data", Shared] },
        {
            "MyVedicDay.Muhurta.Business",
            [
                "MyVedicDay.Muhurta.Calculation", "MyVedicDay.Muhurta.Shared", "MyVedicDay.Panchang.Calculation",
                "MyVedicDay.Astronomy.Calculation", "MyVedicDay.Location.Business", "MyVedicDay.Localization.Business",
                "MyVedicDay.Traditions.Business", "MyVedicDay.Caching", "MyVedicDay.Panchang.Caching", "MyVedicDay.Ai.Shared", Shared,
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
