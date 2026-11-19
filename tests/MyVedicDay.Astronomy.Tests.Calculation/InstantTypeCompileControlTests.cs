using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using MyVedicStar.Astronomy.Core;
using Xunit;

namespace MyVedicDay.Astronomy.Tests.Calculation;

/// <summary>
/// api#140. The type pair exists so a <see cref="TtInstant"/> where a <see cref="UtInstant"/> is expected does not
/// compile. That is a claim about the compiler, so it is tested by RUNNING the compiler, not by asserting in prose.
/// </summary>
/// <remarks>
/// A type that is never checked against misuse is a comment with extra steps. This control compiles two snippets
/// against the real <c>MyVedicStar.Astronomy.Core</c> assembly: the wrong-argument one must fail, and a correct one is
/// compiled as a control so that a broken harness cannot report success by failing everything.
/// </remarks>
public sealed class InstantTypeCompileControlTests
{
    private static readonly MetadataReference[] References =
        AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => !assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
            .Select(assembly => MetadataReference.CreateFromFile(assembly.Location))
            .ToArray();

    private static (bool Success, string Diagnostics) Compile(string source)
    {
        var compilation = CSharpCompilation.Create(
            "InstantTypeControl",
            [CSharpSyntaxTree.ParseText(source)],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        return (result.Success, string.Join(" | ", result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"{d.Id}: {d.GetMessage()}")));
    }

    [Fact]
    public void A_TtInstant_where_a_UtInstant_is_expected_does_not_compile()
    {
        const string wrong = """
            using MyVedicStar.Astronomy.Core;

            internal static class Probe
            {
                internal static UtInstant Misuse(TtInstant tt) => tt;
            }
            """;

        var (success, diagnostics) = Compile(wrong);

        Assert.False(success, "Passing a TtInstant where a UtInstant is expected must not compile.");
        Assert.Contains("CS0029", diagnostics, StringComparison.Ordinal);
    }

    [Fact]
    public void A_UtInstant_where_a_TtInstant_is_expected_does_not_compile()
    {
        // The other direction, because a one-way guard would still let half the mistake through.
        const string wrong = """
            using MyVedicStar.Astronomy.Core;

            internal static class Probe
            {
                internal static TtInstant Misuse(UtInstant ut) => ut;
            }
            """;

        var (success, diagnostics) = Compile(wrong);

        Assert.False(success, "Passing a UtInstant where a TtInstant is expected must not compile.");
        Assert.Contains("CS0029", diagnostics, StringComparison.Ordinal);
    }

    [Fact]
    public void The_correct_conversion_compiles()
    {
        // The control's own control: if the harness rejected everything, the two tests above would pass for the wrong
        // reason and prove nothing.
        const string right = """
            using MyVedicStar.Astronomy.Core;

            internal static class Probe
            {
                internal static TtInstant Correct(UtInstant ut) => ut.ToTerrestrialTime();
            }
            """;

        var (success, diagnostics) = Compile(right);

        Assert.True(success, $"The correct conversion must compile. Diagnostics: {diagnostics}");
    }
}
