using System.Text.Json;
using System.Text.Json.Serialization;
using MyVedicStar.Kundali.Shared;

namespace MyVedicStar.Kundali.Data;

/// <summary>
/// The one producer and the one parser for the interpretation payload (KUN-14, api#54).
/// </summary>
/// <remarks>
/// <para><b>Why a codec and not <c>JsonSerializer.Serialize</c> at each call site.</b> Two call sites with their own
/// options is two ways to write the same column, and the difference only shows up when one of them round-trips a
/// status it did not model. Having exactly one place that writes and one that reads means the withheld/not-evaluated
/// distinction has a single implementation to get right, and a single one to test.</para>
///
/// <para><b>Nothing is normalised, defaulted or repaired on read.</b> A stored status that is not one of the three
/// known values round-trips as text. Coercing it - to <c>withheld</c>, say - would turn a stored anomaly into a claim
/// about the chart that nobody made. The contract is deliberately string-typed for the same reason.</para>
///
/// <para><b>The list is preserved as a list.</b> Serialisation goes through the record itself, so the order and the
/// count of <see cref="KundaliInterpretationsSection.Sections"/> are what was written. A dictionary keyed by domain
/// would silently drop a repeated domain and reorder the rest, which is the enumerate-all-five defect at the list
/// level: a caller enumerating the result would see a set that no longer matches what was computed.</para>
/// </remarks>
internal static class KundaliInterpretationCodec
{
    /// <summary>
    /// Serialise options: no naming policy, no enum strings, no indentation. The wire shape is the record shape, and
    /// the stored bytes are for this process to read back, not for a browser.
    /// </summary>
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>Serialises a section for storage. Throws rather than writing a partial document.</summary>
    public static string ToJson(KundaliInterpretationsSection section)
    {
        ArgumentNullException.ThrowIfNull(section);
        return JsonSerializer.Serialize(section, Options);
    }

    /// <summary>
    /// Reads a stored section. Returns false and a reason rather than throwing, because a stored row that cannot be
    /// parsed is a data-integrity fact the caller has to decide about - it is not the same as a missing row, and
    /// collapsing the two would hide corruption behind an empty result.
    /// </summary>
    public static bool TryRead(string? json, out KundaliInterpretationsSection? section, out string? failure)
    {
        section = null;
        failure = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            failure = "stored payload is empty";
            return false;
        }

        try
        {
            section = JsonSerializer.Deserialize<KundaliInterpretationsSection>(json, Options);
        }
        catch (JsonException ex)
        {
            failure = "stored payload is not valid JSON for the interpretation section: " + ex.Message;
            return false;
        }

        if (section is null)
        {
            failure = "stored payload deserialised to null";
            return false;
        }

        return true;
    }
}
