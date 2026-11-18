using System.Reflection;

namespace MyVedicStar.Kundali.Tests.Unit;

/// <summary>
/// KUN-20 shape guard: the calculation project must stay pure. A dependency on persistence, HTTP, AI or UI would not
/// fail a behavioural test - it would simply work - so the boundary is asserted mechanically instead of trusted.
/// </summary>
public sealed class KundaliCalculationPurityTests
{
    private static readonly string[] ForbiddenAssemblyPrefixes =
    [
        "Microsoft.EntityFrameworkCore",
        "Microsoft.AspNetCore",
        "Npgsql",
        "Microsoft.Extensions.Http",
        "MyVedicStar.VedicProfiles.Data",
        "MyVedicStar.Kundali.Data",
        "MyVedicStar.Settings.Data",
        "MyVedicStar.Ai",
        "MyVedicDay.Ai"
    ];

    [Fact]
    public void The_calculation_assembly_references_no_persistence_http_ai_or_ui_library()
    {
        var assembly = typeof(Calculation.KundaliEngine).Assembly;
        var referenced = assembly.GetReferencedAssemblies().Select(name => name.Name ?? string.Empty).ToArray();

        var offending = referenced
            .Where(name => ForbiddenAssemblyPrefixes.Any(prefix =>
                name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        Assert.True(offending.Length == 0,
            "The Kundali calculation assembly must not depend on infrastructure. Offending: "
            + string.Join(", ", offending));
    }

    [Fact]
    public void The_calculation_assembly_does_not_reference_any_other_module_data_project()
    {
        // Broader than the list above: any '.Data' reference at all is a layering violation for a pure engine.
        var assembly = typeof(Calculation.KundaliEngine).Assembly;
        var dataReferences = assembly.GetReferencedAssemblies()
            .Select(name => name.Name ?? string.Empty)
            .Where(name => name.EndsWith(".Data", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.Empty(dataReferences);
    }
}

