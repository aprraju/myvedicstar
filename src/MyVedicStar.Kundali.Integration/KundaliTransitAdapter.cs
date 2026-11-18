using MyVedicStar.Kundali.Business;
using MyVedicStar.Kundali.Calculation;

namespace MyVedicStar.Kundali.Integration;

/// <summary>
/// Adapts <see cref="TransitCalculator"/> to the business transit port. Mapping and delegation only: no astronomy and no
/// policy, so a change in the engine cannot be absorbed here.
/// </summary>
/// <remarks>
/// The calculator is constructed from the host's graha port and ayanamsa, which means this adapter inherits the same
/// publication gate: a graha whose longitude is withheld cannot produce a transit date. That is the intended
/// consequence - the engine can compute, seven bodies cannot yet be claimed, so Saturn and Jupiter transit dates are
/// reported as unsupported until their references land, rather than derived from an unproven position.
/// </remarks>
internal sealed class KundaliTransitAdapter(TransitCalculator calculator, IKundaliCalculationEngine natalEngine)
    : IKundaliCalculationEngine
{
    /// <summary>Days either side of the reference instant for ingress windows. Saturn stays in a sign for roughly two
    /// and a half years and Jupiter for about one, so two years each way always contains the surrounding ingress.</summary>
    private const int WindowDays = 730;

    public KundaliEngineResult Calculate(KundaliEngineInput input) => natalEngine.Calculate(input);

    public KundaliEngineDashaTimeline CalculateDashaTimeline(KundaliDashaQuery query) =>
        natalEngine.CalculateDashaTimeline(query);

    public KundaliEngineTransits CalculateTransits(KundaliTransitQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var instant = NodaTime.Instant.FromDateTimeOffset(query.At.ToUniversalTime());
        var gaps = new List<KundaliEngineGap>();

        // Saturn drives both the ingress list and Sade Sati; Jupiter is independent. Separate blocks so a withheld
        // Jupiter position does not discard a valid Saturn answer - each gap is reported on its own.
        var (saturnSign, saturnWindows) = GrahaTransits("Saturn", instant, gaps);
        var (jupiterSign, jupiterWindows) = GrahaTransits("Jupiter", instant, gaps);

        var sadeSati = BuildSadeSati(query, instant, gaps);

        return new KundaliEngineTransits(saturnSign, saturnWindows, jupiterSign, jupiterWindows, sadeSati, gaps);
    }

    private (string Sign, IReadOnlyList<KundaliSignWindow> Windows) GrahaTransits(string graha, NodaTime.Instant instant,
        List<KundaliEngineGap> gaps)
    {
        try
        {
            var sign = MyVedicStar.Kundali.Shared.KundaliSigns.NameOf(calculator.TransitSign(graha, instant));
            var windows = calculator.WindowsAround(graha, instant, WindowDays)
                .Select(window => new KundaliSignWindow(window.Sign, window.StartUtc, window.EndUtc)).ToArray();
            return (sign, windows);
        }
        catch (InvalidOperationException)
        {
            // The engine withholds a longitude whose accuracy is not yet claimed. A transit date derived from it would
            // be an unproven number presented as a dated event.
            gaps.Add(new KundaliEngineGap(graha, KundaliEngineReasons.UnvalidatedReference));
            return (string.Empty, []);
        }
    }

    /// <summary>
    /// Sade Sati measured from the natal Moon. The natal result comes from the natal engine, not from a second
    /// calculation: reading the Moon at the reference instant would answer a different and meaningless question.
    /// </summary>
    private KundaliEngineSadeSati? BuildSadeSati(KundaliTransitQuery query, NodaTime.Instant instant,
        List<KundaliEngineGap> gaps)
    {
        var natal = natalEngine.Calculate(query.Input);
        if (natal.SiderealMoonLongitude is not { } moonLongitude)
        {
            // The natal Moon is itself withheld; its reason already names the cause (normally an inexact birth time).
            gaps.Add(new KundaliEngineGap("SadeSati", KundaliEngineReasons.RequiresExactBirthTime));
            return null;
        }

        try
        {
            return calculator.SadeSati(moonLongitude, instant) is { } interval
                ? new KundaliEngineSadeSati(interval.MoonSign, interval.StartUtc, interval.EndUtc, interval.IsPartial,
                    interval.Phases.Select(phase => new KundaliEngineSadeSatiPhase(phase.Phase, phase.Sign,
                        phase.StartUtc, phase.EndUtc)).ToArray())
                : null;
        }
        catch (InvalidOperationException)
        {
            gaps.Add(new KundaliEngineGap("SadeSati", KundaliEngineReasons.UnvalidatedReference));
            return null;
        }
    }
}
