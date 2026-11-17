using System.Xml.Linq;

namespace MyVedicStar.Astronomy.Tests;

/// <summary>
/// FR-01 AC-2/AC-3/AC-4 asserted mechanically against the project files and the solution directory, so a boundary
/// cannot be broken - or a Swiss component reintroduced - without this test failing.
/// </summary>
public sealed class DependencyInspectionTests
{
    private static DirectoryInfo SolutionRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MyVedicStar.Astronomy.slnx")))
                directory = directory.Parent;
            return directory ?? throw new InvalidOperationException("Solution root not found.");
        }
    }

    private static IEnumerable<FileInfo> ProjectFiles() =>
        SolutionRoot.EnumerateFiles("*.csproj", SearchOption.AllDirectories);

    private static string[] ReferencesOf(FileInfo project) =>
        XDocument.Load(project.FullName).Descendants("ProjectReference")
            .Select(e => (string?)e.Attribute("Include") ?? "")
            .Where(include => include.Length > 0)
            .Select(include => Path.GetFileNameWithoutExtension(include.Replace('\\', '/')))
            .ToArray();

    [Fact]
    public void No_project_references_a_product_project()
    {
        foreach (var project in ProjectFiles())
        {
            var references = ReferencesOf(project);
            Assert.DoesNotContain(references, r =>
                r.StartsWith("MyVedicStar.", StringComparison.Ordinal) && r != "MyVedicStar.Astronomy"
                || r.StartsWith("MyVedicDay", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Core_references_nothing_in_this_solution()
    {
        var core = ProjectFiles().Single(p => p.Name == "MyVedicStar.Astronomy.Core.csproj");
        Assert.Empty(ReferencesOf(core));
    }

    [Fact]
    public void No_provider_references_another_provider_or_a_calculation_project()
    {
        foreach (var project in ProjectFiles().Where(p => p.Parent!.Name.StartsWith("MyVedicStar.Astronomy.Jpl", StringComparison.Ordinal)))
        {
            var references = ReferencesOf(project);
            Assert.DoesNotContain(references, r => r.Contains(".Provider", StringComparison.Ordinal));
            Assert.DoesNotContain(references, r => r.Contains(".Events", StringComparison.Ordinal)
                || r.Contains(".Houses", StringComparison.Ordinal) || r.Contains(".Coordinates", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void No_swiss_ephemeris_package_source_or_asset_exists()
    {
        var forbidden = new[] { "SwissEph", "swisseph", "libswe", "Swiss Ephemeris" };
        foreach (var project in ProjectFiles())
        {
            var text = File.ReadAllText(project.FullName);
            foreach (var term in forbidden)
                Assert.DoesNotContain(term, text, StringComparison.OrdinalIgnoreCase);
        }

        var offendingFiles = SolutionRoot.EnumerateFiles("*", SearchOption.AllDirectories)
            .Where(f => !f.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => f.Extension.Equals(".se1", StringComparison.OrdinalIgnoreCase)
                        || forbidden.Any(t => f.Name.Contains(t, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        Assert.Empty(offendingFiles);
    }

    [Fact]
    public void Every_package_reference_appears_in_the_fr08_inventory()
    {
        var inventory = File.ReadAllText(Path.Combine(SolutionRoot.FullName, "FR-08-DEPENDENCY-INVENTORY.md"));
        var packages = ProjectFiles()
            .SelectMany(p => XDocument.Load(p.FullName).Descendants("PackageReference"))
            .Select(e => (string?)e.Attribute("Include"))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct()
            .ToArray();

        foreach (var package in packages)
            Assert.Contains(package!, inventory, StringComparison.Ordinal);
    }

    [Fact]
    public void The_inventory_states_the_provisioning_rule_and_the_absent_swiss_components()
    {
        var inventory = File.ReadAllText(Path.Combine(SolutionRoot.FullName, "FR-08-DEPENDENCY-INVENTORY.md"));
        Assert.Contains("no runtime download", inventory, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("mounted read-only", inventory, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DE440", inventory, StringComparison.Ordinal);
        Assert.Contains("DE441", inventory, StringComparison.Ordinal);
        Assert.Contains("SwissEphNet", inventory, StringComparison.Ordinal);
    }
}
