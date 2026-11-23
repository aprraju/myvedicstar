using System.Text.Json;
using MyVedicStar.Kundali.Shared;

namespace MyVedicStar.Kundali.Data;

/// <summary>
/// Serialisation for the two list-shaped columns on an interpretation row (KUN-14, api#54).
/// </summary>
/// <remarks>
/// <para><b>Why a separate type from <see cref="KundaliInterpretationCodec"/>.</b> The section codec owns the payload
/// shape; these two helpers own the small lists inside one row. Both live in one file so the row's storage format has
/// exactly one producer and one parser - the same reason the section codec exists.</para>
/// <para><b>Never throws on read.</b> A row whose factor list cannot be parsed is corrupt data, but the status, the
/// reason and the text are still readable and still meaningful. Returning an empty list for a corrupt column keeps the
/// row's status intact - refusing to read the row would lose the withheld signal entirely, which is the failure this
/// story exists to prevent.</para>
/// </remarks>
internal static class KundaliInterpretationJson
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    public static string FactorsToJson(IReadOnlyList<KundaliInterpretationFactor>? factors) =>
        JsonSerializer.Serialize(factors ?? [], Options);

    public static IReadOnlyList<KundaliInterpretationFactor> FactorsFromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            return JsonSerializer.Deserialize<KundaliInterpretationFactor[]>(json, Options) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static string StringsToJson(IReadOnlyList<string>? values) =>
        JsonSerializer.Serialize(values ?? [], Options);

    public static IReadOnlyList<string> StringsFromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            return JsonSerializer.Deserialize<string[]>(json, Options) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
