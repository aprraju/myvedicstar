using System.Text.Json.Serialization;

namespace MyVedicStar.Kundali.Shared;

/// <summary>
/// KUN-16 remedy section, contract only (api#128). One entry per remedy, each bound to the finding it addresses and
/// to that finding's status as of this result.
/// </summary>
/// <remarks>
/// <para><b>Why <c>FindingStatus</c> is per item and not per section.</b> A section-level status cannot express a mix,
/// and the point of the field is to distinguish, per finding, "evaluated", "could not be evaluated" and "withheld".
/// A single footer would let a caller read the remedy list and never learn which findings were not even assessed.</para>
/// <para><b>Why the status is a string here and not an enum.</b> An enum in a wire contract invites
/// <c>(SomeEnum)value</c> at the boundary, and C# enums accept any integer - so an unrecognised state becomes
/// constructible and can reach the render path. The stored/published value is therefore a string, and
/// <see cref="TryParseStatus"/> is the only way in: an unrecognised value lands on
/// <see cref="KundaliRemedyFindingStatus.UnknownCode"/> rather than on a default. Parse, never cast.</para>
/// <para><b>CatalogVersion is required.</b> A remedy catalogue that changes under a cached response is the same defect
/// as a stale bindu count: the section must be identifiable by the revision that produced it, including the catalogue
/// it was rendered against.</para>
/// </remarks>
public sealed record KundaliRemediesSection(
    string CatalogVersion,
    IReadOnlyList<KundaliRemedy> Items,
    IReadOnlyList<KundaliUnsupportedField> Unsupported);

/// <summary>
/// One remedy, bound to the finding it addresses.
/// </summary>
/// <param name="FindingCode">Stable, namespaced catalogue key (for example <c>yoga:gaja-kesari</c>). Never a display
/// label and never an index: a code survives a recomputation, an id does not.</param>
/// <param name="FindingStatus">The finding's status in this result. Only <c>evaluated</c> is ever emitted with a
/// remedy; the other statuses exist so a client can assert that rule against a stored response.</param>
/// <param name="Tradition">Required per item, not per section. A remedy with no attributed tradition is an assertion
/// the product cannot stand behind.</param>
/// <param name="Reference">Required per item. A remedy with no source is unattributable.</param>
public sealed record KundaliRemedy(
    string FindingCode,
    string FindingStatus,
    string Category,
    string Suggestion,
    string Tradition,
    string Reference);

/// <summary>The statuses a remedy's finding can be in. Closed set.</summary>
public enum KundaliRemedyFindingStatus
{
    /// <summary>The engine assessed this finding.</summary>
    Evaluated = 0,

    /// <summary>The engine could not assess it because an input was missing.</summary>
    NotEvaluated = 1,

    /// <summary>Assessed but not published: a capability gap.</summary>
    Withheld = 2,

    /// <summary>No such finding in this result: the catalogue entry is stale or superseded.</summary>
    UnknownCode = 3,
}

/// <summary>Parsing for <see cref="KundaliRemedyFindingStatus"/>: the only sanctioned way into the type from a string.</summary>
public static class KundaliRemedyStatuses
{
    private const string EvaluatedText = "evaluated";
    private const string NotEvaluatedText = "not-evaluated";
    private const string WithheldText = "withheld";
    private const string UnknownCodeText = "unknown-code";

    /// <summary>The wire text for a status. Stable; changing one is a contract change.</summary>
    public static string ToText(KundaliRemedyFindingStatus status) => status switch
    {
        KundaliRemedyFindingStatus.Evaluated => EvaluatedText,
        KundaliRemedyFindingStatus.NotEvaluated => NotEvaluatedText,
        KundaliRemedyFindingStatus.Withheld => WithheldText,
        KundaliRemedyFindingStatus.UnknownCode => UnknownCodeText,
        // No default arm, deliberately: a new status must be given wire text rather than inheriting someone else's.
        _ => throw new ArgumentOutOfRangeException(nameof(status), status,
            "Unhandled remedy finding status. Every status needs its own stable wire text."),
    };

    /// <summary>
    /// Parses wire text, falling back to <see cref="KundaliRemedyFindingStatus.UnknownCode"/> for anything unrecognised.
    /// </summary>
    /// <remarks>
    /// The fallback is <c>UnknownCode</c> and not <c>Evaluated</c> on purpose: an unreadable status must never be able to
    /// promote a remedy into the rendered set. Refusing to parse is a claim about the catalogue, which is a safer
    /// default than a claim that the finding was assessed. Use this instead of casting.
    /// </remarks>
    public static KundaliRemedyFindingStatus Parse(string? text) =>
        text?.Trim().ToLowerInvariant() switch
        {
            EvaluatedText => KundaliRemedyFindingStatus.Evaluated,
            NotEvaluatedText => KundaliRemedyFindingStatus.NotEvaluated,
            WithheldText => KundaliRemedyFindingStatus.Withheld,
            UnknownCodeText => KundaliRemedyFindingStatus.UnknownCode,
            _ => KundaliRemedyFindingStatus.UnknownCode,
        };
}

