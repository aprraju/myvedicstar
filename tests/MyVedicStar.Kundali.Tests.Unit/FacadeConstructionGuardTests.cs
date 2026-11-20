using Xunit;

namespace MyVedicStar.Kundali.Tests.Unit;

/// <summary>
/// api#124. Fails the build if any test hand-enumerates <see cref="MyVedicStar.Kundali.Business.KundaliFacade"/>'s
/// constructor instead of going through <see cref="KundaliTestProvider"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a guard and not just the helper.</b> The helper (api#124 / PR #138) made the safe form the convenient one and
/// removed all four hand-enumerated sites, but it changed <i>which failure mode is cheap to write</i>, not which are
/// possible. A deviant <c>new KundaliFacade(...)</c> compiles and passes until the ctor next changes - and because the
/// helper is now the dominant idiom, that deviant site reads as an obvious mistake only to someone already looking for
/// it. The social fix decays precisely because the mistake stops looking like a mistake.
/// </para>
/// <para>
/// <b>What this does and does not do.</b> It makes hand-enumeration a build failure rather than a slow drift. It does
/// not stop someone editing this file. That is a strictly smaller surface than "any test file may reintroduce it", and
/// it is the honest bound: a source-scanning guard can always be weakened by the change that trips it.
/// </para>
/// <para>
/// <b>Deliberately matches the bare shape, not a narrowed one.</b> The scan looks for <c>new KundaliFacade(</c> with
/// optional whitespace, because that is exactly the shape the four removed sites used. A guard that only caught, say,
/// multi-line constructions would pass the single-line form that actually occurred.
/// </para>
/// </remarks>
public sealed class FacadeConstructionGuardTests
{
    /// <summary>How to satisfy this guard. Named in the failure message so the reader has an alternative.</summary>
    private const string Remedy =
        "Use KundaliTestProvider.Create() (optionally .With<TService>(substitute) before .Create()), so the container "
        + "resolves the constructor and an added parameter fails at provider build instead of at each call site.";

    [Fact]
    public void No_test_hand_enumerates_the_kundali_facade_constructor()
    {
        var root = FindRepositoryRoot();
        var testsDirectory = Path.Combine(root, "tests");

        Assert.True(Directory.Exists(testsDirectory), $"Expected a tests directory at '{testsDirectory}'.");

        var offenders = Directory.EnumerateFiles(testsDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(IsSourceFile)
            // This file names the pattern in its own assertion text and remarks; it must not match itself.
            .Where(path => !path.EndsWith("FacadeConstructionGuardTests.cs", StringComparison.Ordinal))
            .Select(path => (Path: path, Text: StripComments(File.ReadAllText(path))))
            .Where(file => ContainsBareConstruction(file.Text))
            .Select(file => Path.GetRelativePath(root, file.Path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.True(offenders.Length == 0,
            $"Hand-enumerated KundaliFacade construction in:{Environment.NewLine}  "
            + string.Join(Environment.NewLine + "  ", offenders)
            + Environment.NewLine + Environment.NewLine + Remedy);
    }

    [Fact]
    public void The_scan_actually_sees_a_violation_it_is_given()
    {
        // Without this, the guard above passes for the wrong reason if the enumeration or the filter ever stops finding
        // files - a green that means "found nothing to check". This feeds the matcher the exact shape that was removed,
        // so the detection is proven separately from the repository being clean.
        Assert.True(ContainsBareConstruction("var facade = new KundaliFacade(clock, a, b);"));
        Assert.True(ContainsBareConstruction("private KundaliFacade Facade() => new KundaliFacade(\n            clock,"));

        // And it must not fire on the helper form, or the remedy would trip its own guard.
        Assert.False(ContainsBareConstruction("var facade = KundaliTestProvider.Create();"));
        Assert.False(ContainsBareConstruction("var facade = KundaliTestProvider.Create().With(clock);"));
    }

    /// <summary>
    /// True when the text constructs the facade directly. Matches <c>new KundaliFacade(</c> with whitespace allowed
    /// between the tokens, and requires a following <c>(</c> so mentioning the type in prose is not a violation.
    /// </summary>
    private static bool ContainsBareConstruction(string text)
    {
        var index = 0;
        while ((index = text.IndexOf("new", index, StringComparison.Ordinal)) >= 0)
        {
            var cursor = index + 3;
            if (SkipWhitespace(text, ref cursor, out _) is false) return false;
            if (!StartsWith(text, cursor, "KundaliFacade")) { index = cursor; continue; }

            cursor += "KundaliFacade".Length;
            if (SkipWhitespace(text, ref cursor, out _) is false) return false;
            if (cursor < text.Length && text[cursor] == '(') return true;

            index = cursor;
        }

        return false;
    }

    /// <summary>Advances past whitespace including newlines. False when the input ends first.</summary>
    private static bool SkipWhitespace(string text, ref int cursor, out int skipped)
    {
        var start = cursor;
        while (cursor < text.Length && char.IsWhiteSpace(text[cursor])) cursor++;
        skipped = cursor - start;
        return cursor < text.Length;
    }

    private static bool StartsWith(string text, int cursor, string value) =>
        cursor + value.Length <= text.Length
        && string.CompareOrdinal(text, cursor, value, 0, value.Length) == 0;

    /// <summary>Source files only; generated output and build artefacts are not hand-written.</summary>
    private static bool IsSourceFile(string path) =>
        !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);


    /// <summary>
    /// Removes line comments and block comments so a doc comment that MENTIONS the banned shape is not a violation.
    /// </summary>
    /// <remarks>
    /// Deliberately does not attempt to understand string literals. A literal containing the banned text would be a
    /// false positive, and the failure message names the file so the author can see that - whereas silently ignoring
    /// text inside strings could let a construction hidden in a string be missed. Failing loudly on an unlikely
    /// false positive is the safer direction for a guard.
    /// </remarks>
    private static string StripComments(string text)
    {
        var builder = new System.Text.StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                while (i < text.Length && text[i] != '\n') i++;
                builder.Append('\n');
                continue;
            }

            if (text[i] == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < text.Length && !(text[i] == '*' && text[i + 1] == '/')) i++;
                i++;
                builder.Append(' ');
                continue;
            }

            builder.Append(text[i]);
        }

        return builder.ToString();
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MyVedicStar.slnx")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return directory.FullName;
    }
}

