using NodaTime;

namespace MyVedicStar.Kundali.Calculation;

/// <summary>
/// Sub-period decomposition of a Vimshottari mahadasha into antara and pratyantar levels.
/// </summary>
/// <remarks>
/// <para>Both levels follow the same rule: a sub-period sequence starts at the sub-lord corresponding to the parent's
/// own lord, and each sub-period lasts <c>parentDuration * (subLordYears / 120)</c>. That is the standard proportional
/// subdivision, and it is applied identically at each level rather than re-derived.</para>
/// <para>Sub-periods are half-open [start, end) and contiguous within their parent, and the last sub-period ends
/// exactly at the parent's end. Accumulated floating-point error is therefore absorbed by the final entry rather than
/// left to drift, so a child sequence can never extend beyond its parent.</para>
/// </remarks>
public static class VimshottariSubPeriods
{
    /// <summary>Sub-periods one level down: antara within a mahadasha, pratyantar within an antara.</summary>
    public static IReadOnlyList<VimshottariSubPeriod> Build(string parentLord, Instant start, Instant end)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentLord);

        if (end <= start)
            return [];

        var parentYears = VimshottariDasha.YearsOf(parentLord);
        var startIndex = VimshottariDasha.IndexOf(parentLord);

        // A parent period that is a remainder is still split proportionally from its own start: the elapsed portion
        // belongs to a previous mahadasha and is not represented here.
        var parentDays = (end - start).TotalDays;
        var lords = VimshottariDasha.Lords;

        var result = new List<VimshottariSubPeriod>(lords.Length);
        var cursorDays = 0.0;

        for (var step = 0; step < lords.Length; step++)
        {
            var lord = lords[(startIndex + step) % lords.Length];
            var share = VimshottariDasha.YearsOf(lord) / VimshottariDasha.CycleYears;

            // The final sub-period takes whatever remains, so rounding cannot leave a gap or overflow past the parent.
            var subDays = step == lords.Length - 1
                ? parentDays - cursorDays
                : parentDays * share;

            var subStart = cursorDays == 0.0 ? start : start + Duration.FromDays(cursorDays);
            var subEnd = step == lords.Length - 1 ? end : start + Duration.FromDays(cursorDays + subDays);

            result.Add(new VimshottariSubPeriod(lord, subStart, subEnd));
            cursorDays += subDays;
        }

        return result;
    }

    /// <summary>
    /// Full three-level path containing <paramref name="at"/>: mahadasha, antara and pratyantar, from the outside in.
    /// </summary>
    /// <remarks>
    /// Returns empty when the instant lies outside every mahadasha. It never returns a partial path: a caller either
    /// gets the whole chain or nothing, because a pratyantar shown without its parent would misrepresent the chart.
    /// </remarks>
    public static IReadOnlyList<VimshottariSubPeriod> PathAt(IReadOnlyList<VimshottariPeriod> mahadashas,
        Instant at)
    {
        ArgumentNullException.ThrowIfNull(mahadashas);

        foreach (var maha in mahadashas)
        {
            if (at < maha.StartUtc || at >= maha.EndUtc)
                continue;

            var antaras = Build(maha.Lord, maha.StartUtc, maha.EndUtc);
            var antara = antaras.FirstOrDefault(a => at >= a.StartUtc && at < a.EndUtc) ?? antaras[^1];

            var pratyantaras = Build(antara.Lord, antara.StartUtc, antara.EndUtc);
            var pratyantara = pratyantaras.FirstOrDefault(p => at >= p.StartUtc && at < p.EndUtc)
                ?? pratyantaras[^1];

            return [new VimshottariSubPeriod(maha.Lord, maha.StartUtc, maha.EndUtc), antara, pratyantara];
        }

        return [];
    }
}

/// <summary>One sub-period. The level is implied by position in the returned sequence, not stored.</summary>
public sealed record VimshottariSubPeriod(string Lord, Instant StartUtc, Instant EndUtc);
