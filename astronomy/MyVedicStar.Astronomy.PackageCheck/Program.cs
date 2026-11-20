using System.Globalization;
using MyVedicStar.Astronomy.Core;
using MyVedicStar.Astronomy.Jpl.Provider;

// AST-11 (api#76): proves the packaged engine is usable as shipped.
//
// Each check below is something a build-tree build hides. An engine that only works when the repository is beside it
// passes its own test suite and still fails in a container; this probe is what distinguishes the two.
//
// Exit codes: 0 all checks passed, 1 a check failed, 2 the probe itself could not run.

// Args before "--" belong to the host (dotnet run --configfile x). Only the application section is ours: a plain
// args[0] would silently pick up "--configfile" and run the wrong mode - which is exactly the kind of quiet
// wrong-answer a packaging probe must not produce.
// Only arguments after " -- " are ours. Before it, the dotnet host owns the command line and its tokens
// ("--configfile", "-c", "Release") must never be read as a mode: a probe that silently runs the wrong mode
// reports PASS for work it did not do.
// The mode arrives on the command line in CI. With "dotnet <probe>.dll --expect-no-toolkit" that is args[0]; with
// "dotnet run ... -- --expect-no-toolkit" the host has already stripped the separator. args[0] covers both and never
// sees a host option, because a host option before "--" is consumed by the host.
var appArgs = args;
var mode = appArgs.FirstOrDefault() ?? "package";
var failures = 0;

void Check(string name, Func<string> run)
{
    try
    {
        Console.WriteLine($"PASS  {name}: {run()}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.WriteLine($"FAIL  {name}: {exception.GetType().Name}: {exception.Message}");
    }
}

void Expect(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

if (mode == "--expect-no-toolkit")
{
    // Run by CI in an image that does NOT bake the toolkit (astronomy-runtime).
    // The property: with no native library present, every calculation surface REFUSES with a diagnostic that names
    // the dependency. A silent fallback, a NaN, or a NullReferenceException would all be a worse product here, and
    // all of them would pass a "does it run" smoke test.
    Check("engine construction needs no native toolkit", () =>
    {
        var engine = new SpiceAstronomyEngine(SpiceConfiguration.Default);
        Expect(!engine.IsNativeToolkitAvailable, "the toolkit was reported available in a toolkit-free image");
        return "constructed, reports IsNativeToolkitAvailable = false";
    });

    Check("a calculation refuses and names the missing dependency", () =>
    {
        var engine = new SpiceAstronomyEngine(SpiceConfiguration.Default);
        try
        {
            engine.GetPositions(JulianDayUt.J2000, new AstronomySettings());
        }
        catch (SpiceUnavailableException error)
        {
            return $"refused with SpiceUnavailableException: {error.Message}";
        }
        throw new InvalidOperationException(
            "the engine answered a calculation with no native toolkit present; it must refuse instead.");
    });

    Check("coverage is derived from the declared kernels, not asserted", () =>
    {
        var configuration = SpiceConfiguration.Default;
        var serviceable = configuration.IsServiceable(new DateOnly(2000, 1, 1));
        var outsideProductFloor = configuration.IsServiceable(new DateOnly(1700, 1, 1));
        Expect(serviceable, "2000-01-01 should be serviceable under the declared kernel set");
        Expect(!outsideProductFloor, "1700-01-01 is below the product floor and must not be serviceable");
        return "2000-01-01 serviceable, 1700-01-01 refused";
    });
}
else
{
    Check("packaged assemblies load from the package, not the repository", () =>
    {
        // The location of the loaded assembly is the evidence: if it points into the build tree, the probe is not
        // proving what it claims and CI's outside-the-tree run would be meaningless.
        var origin = typeof(SpiceAstronomyEngine).Assembly.Location;
        Console.WriteLine($"      provider assembly: {origin}");
        Expect(!string.IsNullOrEmpty(origin), "the provider assembly has no location, so provenance cannot be shown");
        Expect(origin.Contains("myvedicstar.astronomy.jpl.provider", StringComparison.OrdinalIgnoreCase),
            $"the provider assembly did not load from its package: {origin}");
        return origin;
    });

    Check("the provider's native-library probe is safe with no toolkit present", () =>
    {
        var engine = new SpiceAstronomyEngine(SpiceConfiguration.Default);
        return $"constructed; IsNativeToolkitAvailable = {engine.IsNativeToolkitAvailable}";
    });

    Check("capabilities are readable without a toolkit", () =>
    {
        var engine = new SpiceAstronomyEngine(SpiceConfiguration.Default);
        var report = engine.GetCapabilities();
        Expect(report.SupportedBodies.Count > 0, "the capability report claims no supported bodies");
        return $"{report.SupportedBodies.Count} bodies, {report.MinimumSupportedDate:yyyy-MM-dd}..{report.MaximumSupportedDate:yyyy-MM-dd}, "
             + $"{report.UnsupportedCapabilities.Count} explicit unsupported entries";
    });

    Check("the supported span is the intersection of kernels and product floor", () =>
    {
        var range = SpiceConfiguration.Default.SupportedDateRange
            ?? throw new InvalidOperationException("no ephemeris kernel is declared, so no span can be derived");
        Expect(range.From >= AstronomyCapabilities.MinimumSupportedDate,
            $"derived span starts {range.From:yyyy-MM-dd}, before the product floor");
        Expect(range.To <= AstronomyCapabilities.MaximumSupportedDate,
            $"derived span ends {range.To:yyyy-MM-dd}, after the product ceiling");
        return $"{range.From:yyyy-MM-dd}..{range.To:yyyy-MM-dd}";
    });

    Check("a date outside the derived span is refused with a reason", () =>
    {
        var engine = new SpiceAstronomyEngine(SpiceConfiguration.Default);
        try
        {
            engine.EnsureDateIsCovered(new DateOnly(1700, 1, 1));
        }
        catch (AstronomyNotSupportedException error)
        {
            return $"refused: {error.Message}";
        }
        throw new InvalidOperationException("1700-01-01 was accepted; the serviceability gate did not run.");
    });

    Check("provenance names the toolkit source and kernel set", () =>
    {
        var engine = new SpiceAstronomyEngine(SpiceConfiguration.Default);
        var provenance = engine.DescribeProvenance(new AstronomySettings(), 69.0);
        Expect(provenance.EphemerisSource.Contains("NAIF", StringComparison.Ordinal), "provenance does not name NAIF");
        Expect(provenance.KernelIdentifier.Contains("de440.bsp", StringComparison.Ordinal),
            "provenance does not name the DE440 kernel");
        return $"{provenance.EngineVersion}; kernels {provenance.KernelIdentifier}";
    });
}

Console.WriteLine(failures == 0
    ? $"RESULT: all package checks passed (mode '{mode}')."
    : $"RESULT: {failures} check(s) failed (mode '{mode}').");
return failures == 0 ? 0 : 1;
