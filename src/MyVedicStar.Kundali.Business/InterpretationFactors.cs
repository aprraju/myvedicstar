using MyVedicStar.Kundali.Shared;

namespace MyVedicStar.Kundali.Business;

/// <summary>
/// The factors an interpretation for one domain is allowed to cite, built from an engine result (api#54, KUN-14).
/// </summary>
/// <remarks>
/// <para><b>Why the factor list is assembled here and not by the model.</b> The model is given facts and asked to
/// reference them; it is never asked what the facts are. A factor the engine did not publish cannot enter this list, so
/// it cannot survive <see cref="InterpretationRule"/> either.</para>
///
/// <para><b>Only published positions become factors.</b> A graha whose longitude was withheld contributes nothing, so a
/// domain that depends solely on withheld positions ends up with no factors and is reported <c>not-evaluated</c> with
/// <c>input-missing</c> rather than being narrated from a chart the engine did not fully compute.</para>
/// </remarks>
public static class InterpretationFactors
{
    /// <summary>Factor ids for one domain, from the graha positions the engine actually published.</summary>
    /// <remarks>
    /// Ids are namespaced (<c>graha:saturn:sign</c>) so a domain can later add other namespaces - dignities, yogas,
    /// dasha periods - without a name collision silently merging two different facts under one id.
    /// </remarks>
    public static IReadOnlyList<KundaliInterpretationFactor> For(string domain,
        IEnumerable<(string Graha, string? Sign)> publishedPositions)
    {
        ArgumentNullException.ThrowIfNull(publishedPositions);
        if (!KundaliInterpretationDomains.IsKnown(domain))
            throw new ArgumentOutOfRangeException(nameof(domain), domain,
                "Unknown interpretation domain. The closed set is in KundaliInterpretationDomains.");

        var factors = new List<KundaliInterpretationFactor>();
        foreach (var (graha, sign) in publishedPositions)
        {
            if (string.IsNullOrWhiteSpace(graha) || string.IsNullOrWhiteSpace(sign))
                continue;
            factors.Add(new KundaliInterpretationFactor(
                $"graha:{graha.ToLowerInvariant()}:sign",
                $"{graha} is in {sign}"));
        }

        return factors;
    }
}
