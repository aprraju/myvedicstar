using MyVedicStar.Kundali.Shared;

namespace MyVedicStar.Kundali.Business;

/// <summary>
/// The remedy catalogue: the editorial asset the remedy section is rendered from.
/// </summary>
/// <remarks>
/// <para><b>Why this is code and not data yet.</b> The contract carries <c>catalogVersion</c> so a remedy set is
/// identifiable by the asset that produced it. The entries themselves are subject to editorial sign-off on traditions
/// and sourcing (api#128 records that as an owner decision), so this file holds the <i>structure</i> and a deliberately
/// minimal, fully-sourced set - enough for the contract and its tests to be real, not enough to imply the catalogue is
/// written. Adding unsourced remedies here would be the same defect as a fixture value nobody owns.</para>
/// <para><b>Every entry carries a reference, including <c>unclassified</c>.</b> That is enforced by
/// <see cref="Validate"/>: an entry without provenance fails at composition, so <c>unclassified</c> cannot quietly
/// become the one member that skips attribution.</para>
/// </remarks>
public static class RemedyCatalogue
{
    /// <summary>Identifies this edition of the catalogue. Changes whenever an entry or a reference changes.</summary>
    public const string Version = "remedies-2026-11-a";

    /// <summary>
    /// The finding code used when a finding exists but has not been assigned a catalogue identity yet.
    /// </summary>
    /// <remarks>
    /// A real catalogue member with its own reference, deliberately not a sentinel the UI special-cases: a special-cased
    /// sentinel is a null wearing a name, and exclusion would be indistinguishable from "no remedy exists".
    /// </remarks>
    public const string UnclassifiedCode = "unclassified";

    private static readonly KundaliRemedy[] Entries =
    [
        new(UnclassifiedCode, "evaluated", "general",
            "A remedy is catalogued for this finding, but its identity has not been assigned yet.",
            "Unattributed pending editorial review",
            "Not yet attributable: this entry exists so an uncatalogued finding is distinguishable from one with no "
            + "remedy, rather than silently absent. It must be replaced by a sourced entry before it describes anything."),
    ];

    /// <summary>Every catalogue entry, validated.</summary>
    public static IReadOnlyList<KundaliRemedy> All
    {
        get
        {
            Validate(Entries);
            return Entries;
        }
    }

    /// <summary>The entry for a finding code, or null when the catalogue has none.</summary>
    public static KundaliRemedy? For(string findingCode) =>
        Entries.FirstOrDefault(entry => string.Equals(entry.FindingCode, findingCode, StringComparison.Ordinal));

    /// <summary>
    /// Enforces that every entry carries provenance, and that <c>unclassified</c> is present.
    /// </summary>
    /// <remarks>
    /// Deleting the <c>unclassified</c> entry's reference must break this, which is what makes it load-bearing rather
    /// than decorative. The same applies to any other entry: a remedy with no source is an assertion the product cannot
    /// attribute, and the product does not publish unattributed assertions.
    /// </remarks>
    /// <exception cref="InvalidOperationException">An entry is missing provenance, or the reserved code is absent.</exception>
    public static void Validate(IReadOnlyList<KundaliRemedy> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.FindingCode))
                throw new InvalidOperationException("A catalogue entry has no finding code.");
            if (string.IsNullOrWhiteSpace(entry.Tradition))
                throw new InvalidOperationException(
                    $"Catalogue entry '{entry.FindingCode}' has no attributed tradition.");
            if (string.IsNullOrWhiteSpace(entry.Reference))
                throw new InvalidOperationException(
                    $"Catalogue entry '{entry.FindingCode}' has no reference. A remedy with no source is an assertion "
                    + "the product cannot attribute.");
        }

        if (!entries.Any(entry => string.Equals(entry.FindingCode, UnclassifiedCode, StringComparison.Ordinal)))
            throw new InvalidOperationException(
                $"The catalogue must contain the reserved '{UnclassifiedCode}' entry, so an uncatalogued finding is "
                + "distinguishable from one with no remedy.");
    }
}

