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

    /// <summary>
    /// Projects in the astronomy solution. Probes that consume the packages are deliberately excluded (api#76): a
    /// probe must depend only on what ships, and asserting its project files here would let a probe reach into the
    /// solution and still pass this test - the exact coupling the probe exists to rule out.
    /// </summary>
    private static IEnumerable<FileInfo> ProjectFiles() =>
        SolutionRoot.EnumerateFiles("*.csproj", SearchOption.AllDirectories)
            .Where(project => !project.FullName.Contains(".PackageCheck" + Path.DirectorySeparatorChar, StringComparison.Ordinal));

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
            // A reference is allowed only when it is another project in THIS solution. Anything named
            // MyVedicStar.* or MyVedicDay.* that is not MyVedicStar.Astronomy.* is a product project.
            Assert.DoesNotContain(references, r =>
                r.StartsWith("MyVedicDay", StringComparison.Ordinal)
                || (r.StartsWith("MyVedicStar.", StringComparison.Ordinal)
                    && !r.StartsWith("MyVedicStar.Astronomy.", StringComparison.Ordinal)));
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
        foreach (var project in ProjectFiles().Where(p => p.Directory!.Name.StartsWith("MyVedicStar.Astronomy.Jpl", StringComparison.Ordinal)))
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

    [Fact]
    public void Every_third_party_row_in_the_inventory_names_a_provenance_source()
    {
        // api#73 (FR-08 AC-1): a row that says "MIT" and nothing else is a claim with no provenance. Every row whose
        // licence cell points at a section of the evidence document must carry, in that same document, a linked
        // source. The licence alone is not evidence; the source is.
        var inventory = File.ReadAllText(Path.Combine(SolutionRoot.FullName, "FR-08-DEPENDENCY-INVENTORY.md"));
        var evidence = File.ReadAllText(Path.Combine(SolutionRoot.FullName, "FR-08-LICENSING-EVIDENCE.md"));

        // Each "see evidence doc §N.M" reference in a licence cell must point at a section that exists.
        var sectionReferences = System.Text.RegularExpressions.Regex
            .Matches(inventory, @"evidence doc §(\d+(?:\.\d+)?)")
            .Select(match => match.Groups[1].Value)
            .Distinct()
            .ToArray();
        Assert.NotEmpty(sectionReferences);
        foreach (var section in sectionReferences)
            Assert.Contains("### " + section + " ", evidence, StringComparison.Ordinal);

        // Every named dependency row must either carry its own link, or be one of the two rows this story recorded as
        // having no stateable licence - and even those must name the URL they were actually checked against.
        var rows = inventory
            .Split('\n')
            .Where(line => line.StartsWith("| ", StringComparison.Ordinal))
            .Where(line => !line.Contains("---", StringComparison.Ordinal))
            .Select(line => line.Trim('|', ' ').Split('|').Select(cell => cell.Trim()).ToArray())
            .Where(cells => cells.Length >= 3)
            .Where(cells => !cells[0].StartsWith("Package", StringComparison.OrdinalIgnoreCase)
                            && !cells[0].StartsWith("Component", StringComparison.OrdinalIgnoreCase)
                            && !cells[0].StartsWith("Dataset", StringComparison.OrdinalIgnoreCase)
                            && !cells[0].StartsWith("*(none)*", StringComparison.Ordinal))
            .ToArray();
        Assert.NotEmpty(rows);

        var withoutEvidence = rows
            .Where(cells => !cells.Any(cell => cell.Contains("http", StringComparison.OrdinalIgnoreCase))
                            && !cells.Any(cell => cell.Contains("evidence doc", StringComparison.OrdinalIgnoreCase)))
            .Select(cells => cells[0])
            .ToArray();

        Assert.True(withoutEvidence.Length == 0,
            "These inventory rows state a licence with neither a source nor a pointer to the evidence document: "
            + string.Join(", ", withoutEvidence));
    }

    [Fact]
    public void The_inventory_does_not_claim_public_domain_for_the_spice_toolkit()
    {
        // api#73: the NAIF rules page never uses the words "public domain". It grants commercial use with no fee, an
        // export classification and an AS-IS disclaimer with a recipient indemnity. Asserting a term the source does
        // not use is the same defect as an unverified number, so a future edit cannot quietly put it back.
        var inventory = File.ReadAllText(Path.Combine(SolutionRoot.FullName, "FR-08-DEPENDENCY-INVENTORY.md"));
        Assert.DoesNotContain("Public domain", inventory, StringComparison.OrdinalIgnoreCase);
    }
}
