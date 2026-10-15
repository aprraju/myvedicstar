using MyVedicDay.Astronomy;
using MyVedicDay.Muhurta.Calculation.Models;
using MyVedicDay.Panchang.Calculation.Models;
using NodaTime;

namespace MyVedicDay.Muhurta.Calculation;

/// <summary>
/// Evaluates one Panchang day for one activity profile (ADR-0011):
/// <list type="number">
/// <item><description>cut the evaluation window at every considered period boundary and every Tithi, Nakshatra, Yoga
/// and Karana change, so every input is constant within a segment;</description></item>
/// <item><description>score each segment from the limb effects plus the best favorable-period bonus;</description></item>
/// <item><description>cap it by excluded values, by every active unfavorable period (its severity alone or when
/// overlapping a favorable period), by an excluded month, and by the profile's maximum status;</description></item>
/// <item><description>merge identical neighbours and select the Good Times (Highly Favorable or Favorable, no
/// conflict, long enough). Nothing else is ever a Good Time.</description></item>
/// </list>
/// </summary>
internal static class MuhurtaDayEvaluator
{
    /// <param name="day">The calculated Panchang day.</param>
    /// <param name="profile">The activity's resolved rules.</param>
    /// <param name="lunarMonth">The lunar month at sunrise, when the profile restricts months.</param>
    /// <param name="birthNakshatra">The user's birth Nakshatra, for Tara Bala; null to skip it.</param>
    /// <param name="combustPlanets">Planets of the profile's combustion rules that are combust at sunrise; they rule the day out.</param>
    public static MuhurtaEvaluation Evaluate(
        PanchangDay day,
        ActivityRuleProfile profile,
        (LunarMonth Month, bool IsAdhika)? lunarMonth,
        Nakshatra? birthNakshatra = null,
        IReadOnlyList<Planet>? combustPlanets = null)
    {
        ArgumentNullException.ThrowIfNull(day);
        ArgumentNullException.ThrowIfNull(profile);

        var windowStart = day.Sun.Sunrise;
        var windowEnd = profile.Window == EvaluationWindow.Daytime ? day.Sun.Sunset : day.Sun.NextSunrise;

        var favorable = WithClosingBrahmaMuhurta(day)
            .Where(period => profile.FavorablePeriods.TryGetValue(period.Kind, out var rule)
                && !rule.ExcludedWeekdays.Contains(day.Weekday)
                && Overlaps(period.Start, period.End, windowStart, windowEnd))
            .ToArray();
        var unfavorable = day.Periods
            .Where(period => profile.UnfavorablePeriods.ContainsKey(period.Kind) && Overlaps(period.Start, period.End, windowStart, windowEnd))
            .ToArray();

        IReadOnlyList<(string Code, string Subject)> dayExclusions =
        [
            .. GetMonthExclusion(profile, lunarMonth) is { } monthExclusion ? [monthExclusion] : Array.Empty<(string, string)>(),
            .. (combustPlanets ?? []).Select(planet => (ReasonCodes.CombustPlanet, planet.ToString())),
        ];

        var boundaries = Boundaries(day, favorable, unfavorable, windowStart, windowEnd);
        var elementary = boundaries
            .Zip(boundaries.Skip(1))
            .Select(pair => EvaluateSegment(day, profile, favorable, unfavorable, dayExclusions, pair.First, pair.Second, birthNakshatra))
            .ToArray();

        var segments = Merge(elementary)
            .Select(segment => Finish(segment, profile, day))
            .ToArray();

        var goodTimes = segments
            .Select((segment, index) => (segment, index))
            .Where(entry => entry.segment.IsGoodTime)
            .OrderByDescending(entry => entry.segment.Score)
            .ThenByDescending(entry => entry.segment.End - entry.segment.Start)
            .ThenBy(entry => entry.segment.Start)
            .Select(entry => entry.index)
            .ToArray();

        return new MuhurtaEvaluation(
            day.Date,
            DayStatus(segments, goodTimes),
            segments,
            goodTimes,
            goodTimes.Length > 0 ? null : NoGoodTimeReason(segments, dayExclusions),
            lunarMonth?.Month);
    }

    /// <summary>
    /// The day's periods plus the Brahma Muhurta that closes this Panchang day. The Panchang reports the Brahma Muhurta
    /// before the day's sunrise, which no evaluation window reaches; the one before the next sunrise belongs to the
    /// night evaluated here. It is placed at the same fraction of this night as the reported one of the previous night,
    /// so it follows the tradition's rule.
    /// </summary>
    private static IEnumerable<TimePeriod> WithClosingBrahmaMuhurta(PanchangDay day)
    {
        foreach (var period in day.Periods)
        {
            yield return period;
        }

        if (day.Periods.FirstOrDefault(period => period.Kind == PanchangPeriodKind.BrahmaMuhurta) is not { } brahma)
        {
            yield break;
        }

        var previousNight = day.Sun.Sunrise - day.Sun.PreviousSunset;
        var night = day.Sun.NextSunrise - day.Sun.Sunset;
        yield return brahma with
        {
            Start = day.Sun.NextSunrise - (night * ((day.Sun.Sunrise - brahma.Start) / previousNight)),
            End = day.Sun.NextSunrise - (night * ((day.Sun.Sunrise - brahma.End) / previousNight)),
        };
    }

    private static (string Code, string Subject)? GetMonthExclusion(ActivityRuleProfile profile, (LunarMonth Month, bool IsAdhika)? lunarMonth)
    {
        if (lunarMonth is not { } month)
        {
            return null;
        }

        if (profile.ExcludeAdhikaMonth && month.IsAdhika)
        {
            return (ReasonCodes.AdhikaMasa, month.Month.ToString());
        }

        return profile.ExcludedLunarMonths.Contains(month.Month) ? (ReasonCodes.ExcludedLunarMonth, month.Month.ToString()) : null;
    }

    private static Instant[] Boundaries(
        PanchangDay day, IReadOnlyList<TimePeriod> favorable, IReadOnlyList<TimePeriod> unfavorable, Instant windowStart, Instant windowEnd)
    {
        var instants = new SortedSet<Instant> { windowStart, windowEnd };
        IEnumerable<(Instant Start, Instant End)> spans = favorable.Concat(unfavorable).Select(period => (period.Start, period.End))
            .Concat(day.Tithis.Select(period => (period.Start, period.End)))
            .Concat(day.Nakshatras.Select(period => (period.Start, period.End)))
            .Concat(day.Yogas.Select(period => (period.Start, period.End)))
            .Concat(day.Karanas.Select(period => (period.Start, period.End)));

        foreach (var (start, end) in spans)
        {
            foreach (var instant in new[] { start, end })
            {
                if (instant > windowStart && instant < windowEnd)
                {
                    instants.Add(instant);
                }
            }
        }

        return instants.ToArray();
    }

    private static Elementary EvaluateSegment(
        PanchangDay day,
        ActivityRuleProfile profile,
        IReadOnlyList<TimePeriod> favorable,
        IReadOnlyList<TimePeriod> unfavorable,
        IReadOnlyList<(string Code, string Subject)> dayExclusions,
        Instant start,
        Instant end,
        Nakshatra? birthNakshatra)
    {
        var middle = start + ((end - start) / 2);
        var reasons = new List<MuhurtaReason>();

        var values = new (Limb Limb, string Value)[]
        {
            (Limb.Tithi, ValueAt(day.Tithis, middle)),
            (Limb.Vara, day.Weekday.ToString()),
            (Limb.Nakshatra, ValueAt(day.Nakshatras, middle)),
            (Limb.Yoga, ValueAt(day.Yogas, middle)),
            (Limb.Karana, ValueAt(day.Karanas, middle)),
        };

        double weighted = 0;
        double totalWeight = 0;
        var excluded = false;
        foreach (var (limb, value) in values)
        {
            var effect = profile.Factors.TryGetValue(limb, out var effects) && effects.TryGetValue(value, out var configured)
                ? configured
                : FactorEffect.Neutral;

            switch (effect)
            {
                case FactorEffect.Excluded:
                    excluded = true;
                    reasons.Add(new MuhurtaReason(ReasonType.Negative, ReasonCodes.ForLimb(effect, limb), value));
                    continue;
                case FactorEffect.Supportive:
                    reasons.Add(new MuhurtaReason(ReasonType.Positive, ReasonCodes.ForLimb(effect, limb), value));
                    break;
                case FactorEffect.Adverse:
                    reasons.Add(new MuhurtaReason(ReasonType.Negative, ReasonCodes.ForLimb(effect, limb), value));
                    break;
            }

            var weight = profile.Weights.GetValueOrDefault(limb);
            weighted += weight * profile.EffectScores[effect];
            totalWeight += weight;
        }

        var activeFavorable = favorable.Where(period => Overlaps(period.Start, period.End, start, end)).OrderBy(period => period.Start).ToArray();
        reasons.AddRange(activeFavorable.Select(period => new MuhurtaReason(ReasonType.Positive, ReasonCodes.ForPeriod(period.Kind), period.Kind.ToString())));
        var bonus = activeFavorable.Select(period => profile.FavorablePeriods[period.Kind].Bonus).DefaultIfEmpty(0).Max();

        var baseScore = totalWeight > 0 ? weighted / totalWeight : profile.EffectScores[FactorEffect.Neutral];
        var score = (int)Math.Clamp(Math.Round(baseScore + bonus), 0, 100);

        var status = profile.Thresholds
            .OrderByDescending(threshold => threshold.MinScore)
            .FirstOrDefault(threshold => score >= threshold.MinScore)?.Status ?? MuhurtaStatus.Avoid;
        if (excluded)
        {
            status = MuhurtaStatus.Avoid;
        }

        var activeUnfavorable = unfavorable.Where(period => Overlaps(period.Start, period.End, start, end)).OrderBy(period => period.Start).ToArray();
        foreach (var period in activeUnfavorable)
        {
            var rule = profile.UnfavorablePeriods[period.Kind];
            status = Worse(status, activeFavorable.Length > 0 ? rule.WithFavorable : rule.Alone);
        }

        foreach (var (code, subject) in dayExclusions)
        {
            status = MuhurtaStatus.Avoid;
            reasons.Add(new MuhurtaReason(ReasonType.Negative, code, subject));
        }

        if (profile.MaximumStatus is { } maximum)
        {
            status = Worse(status, maximum);
        }

        if (birthNakshatra is { } birth)
        {
            var active = Enum.Parse<Nakshatra>(ValueAt(day.Nakshatras, middle));
            var tara = TaraBala.Position(birth, active);
            var cap = TaraBala.MaximumStatus(tara);
            if (cap is { } maximumTaraStatus) status = Worse(status, maximumTaraStatus);
            // The subject explicitly distinguishes personal input from the astronomical star and prevents
            // merging otherwise identical neighbours across a Nakshatra transition.
            reasons.Add(new MuhurtaReason(cap is null ? ReasonType.Positive : ReasonType.Negative,
                cap is null ? "COMPATIBLE_TARA_BALA" : tara == 1 ? "JANMA_TARA_BALA" : "ADVERSE_TARA_BALA",
                $"birth={birth};active={active};tara={tara}"));
        }

        return new Elementary(start, end, status, score, reasons, activeUnfavorable, activeFavorable.Length > 0);
    }

    /// <summary>Joins neighbours with the same status, score, reasons and active unfavorable periods.</summary>
    private static List<Elementary> Merge(IReadOnlyList<Elementary> segments)
    {
        var merged = new List<Elementary>();
        foreach (var segment in segments)
        {
            if (merged.Count > 0 && merged[^1].IsSameAs(segment))
            {
                merged[^1] = merged[^1] with { End = segment.End };
            }
            else
            {
                merged.Add(segment);
            }
        }

        return merged;
    }

    private static MuhurtaSegment Finish(Elementary segment, ActivityRuleProfile profile, PanchangDay day)
    {
        var conflicts = segment.Unfavorable
            .Select(period =>
            {
                var rule = profile.UnfavorablePeriods[period.Kind];
                return new MuhurtaConflict(
                    period.Kind,
                    ReasonCodes.ForPeriod(period.Kind),
                    segment.WithFavorable ? rule.WithFavorable : rule.Alone,
                    segment.WithFavorable,
                    period.Start,
                    period.End,
                    period.Start > segment.Start ? period.Start : segment.Start,
                    period.End < segment.End ? period.End : segment.End);
            })
            .ToArray();

        var isGoodTime = segment.Status <= MuhurtaStatus.Favorable
            && conflicts.Length == 0
            && segment.End - segment.Start >= Duration.FromMinutes(profile.MinimumMinutes);

        var reasons = segment.Reasons;
        if (isGoodTime)
        {
            // "No Rahu Kalam conflict": the day's unfavorable periods this Good Time avoids.
            reasons = reasons
                .Concat(day.Periods
                    .Select(period => period.Kind)
                    .Where(profile.UnfavorablePeriods.ContainsKey)
                    .Distinct()
                    .Order()
                    .Select(kind => new MuhurtaReason(ReasonType.Positive, ReasonCodes.NoConflict(kind), kind.ToString())))
                .ToArray();
        }

        return new MuhurtaSegment(segment.Start, segment.End, segment.Status, segment.Score, isGoodTime, reasons, conflicts);
    }

    private static MuhurtaStatus DayStatus(IReadOnlyList<MuhurtaSegment> segments, IReadOnlyList<int> goodTimes)
    {
        if (goodTimes.Count > 0)
        {
            return goodTimes.Select(index => segments[index].Status).Min();
        }

        // Without a Good Time the day is never better than Mixed.
        return segments.Count == 0 ? MuhurtaStatus.Avoid : Worse(segments.Min(segment => segment.Status), MuhurtaStatus.Mixed);
    }

    private static string NoGoodTimeReason(IReadOnlyList<MuhurtaSegment> segments, IReadOnlyList<(string Code, string Subject)> dayExclusions) =>
        dayExclusions.Count > 0 || segments.All(segment => segment.Reasons.Any(reason => reason.Code.StartsWith("EXCLUDED_", StringComparison.Ordinal)))
            ? NoGoodTimeReasons.DayExcludedForActivity
            : NoGoodTimeReasons.NoQualifyingPeriod;

    private static string ValueAt<TElement>(IReadOnlyList<ElementPeriod<TElement>> periods, Instant instant)
        where TElement : struct, Enum =>
        (periods.FirstOrDefault(period => period.Start <= instant && instant < period.End) ?? periods[^1]).Element.ToString();

    private static bool Overlaps(Instant start, Instant end, Instant windowStart, Instant windowEnd) => start < windowEnd && windowStart < end;

    private static MuhurtaStatus Worse(MuhurtaStatus first, MuhurtaStatus second) => first > second ? first : second;

    private sealed record Elementary(
        Instant Start,
        Instant End,
        MuhurtaStatus Status,
        int Score,
        IReadOnlyList<MuhurtaReason> Reasons,
        IReadOnlyList<TimePeriod> Unfavorable,
        bool WithFavorable)
    {
        public bool IsSameAs(Elementary other) =>
            Status == other.Status
            && Score == other.Score
            && WithFavorable == other.WithFavorable
            && Reasons.SequenceEqual(other.Reasons)
            && Unfavorable.SequenceEqual(other.Unfavorable);
    }
}
