using MyVedicDay.Astronomy;
using MyVedicStar.Astronomy.Core;
using MyVedicDay.Astronomy;
using MyVedicStar.Kundali.Calculation;
using NodaTime;

namespace MyVedicStar.Kundali.Tests.Unit;

/// <summary>
/// KUN-13 transit windows and Sade Sati, asserted on a controlled graha source so the arithmetic, the boundary rule and
/// the phase naming are measured rather than the ephemeris.
/// </summary>
/// <remarks>
/// The stub moves a graha at an exact rate, so the instant of an ingress is known in closed form and the bisection can
/// be checked to the second against a value the test computes independently. Asserting against the real analytical
/// ephemeris would test Meeus, not this class.
/// </remarks>
public sealed class TransitCalculatorTests
{
    /// <summary>2000-01-01T12:00Z, the J2000 epoch, so a day count is arithmetic rather than calendar reasoning.</summary>
    private static readonly Instant Epoch = Instant.FromUtc(2000, 1, 1, 12, 0);

    private const double DegreesPerDay = 0.1;

    private static TransitCalculator Calculator(double startLongitude) =>
        new(new LinearGrahaProvider(startLongitude, DegreesPerDay), new ZeroAyanamsa());

    [Fact]
    public void A_window_starts_where_the_graha_enters_the_sign()
    {
        // Start at 29.5 degrees: 5.1 days to reach 30 degrees (Taurus) at 0.1 deg/day.
        var calculator = Calculator(startLongitude: 29.5);

        var windows = calculator.WindowsAround("Saturn", Epoch, 60);

        // 29.5 degrees to 30.0 is 0.5 degrees at 0.1 deg/day: 5.0 days, not 5.1 - the boundary is where the sign
        // changes, and 30.0 already belongs to the next sign.
        var taurus = windows.Single(window => window.Sign == "Taurus");
        var expected = Epoch.ToDateTimeOffset().AddDays(5.0);
        Assert.True(Math.Abs((taurus.StartUtc - expected).TotalSeconds) <= 2.0,
            $"Ingress at {taurus.StartUtc:O}, expected {expected:O}.");
    }

    [Fact]
    public void Windows_tile_without_gaps_or_overlaps()
    {
        // The half-open rule is what makes a boundary instant belong to exactly one window; if it were broken, a
        // consumer asking "which sign is Saturn in" at a boundary would get two answers or none.
        var windows = Calculator(startLongitude: 10).WindowsAround("Jupiter", Epoch, 200);

        for (var index = 1; index < windows.Count; index++)
            Assert.Equal(windows[index - 1].EndUtc, windows[index].StartUtc);

        Assert.All(windows, window => Assert.True(window.EndUtc > window.StartUtc));
    }

    [Fact]
    public void A_fast_graha_is_refused_rather_than_computed_and_filtered()
    {
        var calculator = Calculator(0);
        Assert.Throws<ArgumentOutOfRangeException>(() => calculator.WindowsAround("Mars", Epoch, 10));
    }

    [Fact]
    public void An_out_of_range_span_is_refused()
    {
        var calculator = Calculator(0);
        Assert.Throws<ArgumentOutOfRangeException>(() => calculator.WindowsAround("Saturn", Epoch, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => calculator.WindowsAround("Saturn", Epoch, 10_000));
    }

    [Fact]
    public void Sade_Sati_phases_are_named_from_the_natal_moon_sign()
    {
        // Moon at 45 degrees is Taurus. Saturn just after 30 degrees is in Aries, the sign BEFORE Taurus, which is the
        // rising phase. A stub that placed Saturn over the Moon's own sign would be the peak.
        var calculator = Calculator(startLongitude: 31);
        var interval = calculator.SadeSati(moonSiderealLongitude: 45, Epoch);

        Assert.NotNull(interval);
        Assert.Equal("Taurus", interval!.MoonSign);
        Assert.Contains(interval.Phases, phase => phase.Phase == SadeSatiPhase.Rising);
    }

    [Fact]
    public void Sade_Sati_reports_the_peak_when_saturn_is_over_the_moon_sign()
    {
        // Moon at 45 (Taurus); Saturn at 46 is in Taurus, so the phase at the reference instant is the peak.
        var calculator = Calculator(startLongitude: 46);
        var interval = calculator.SadeSati(45, Epoch);

        Assert.NotNull(interval);
        var covering = interval!.Phases.Single(phase => phase.StartUtc <= Epoch.ToDateTimeOffset()
                                                       && Epoch.ToDateTimeOffset() < phase.EndUtc);
        Assert.Equal(SadeSatiPhase.Peak, covering.Phase);
    }

    [Fact]
    public void A_withheld_longitude_never_becomes_a_transit_date()
    {
        // The publication gate must reach this far: a transit date derived from an unvalidated longitude would present
        // an unproven number as a dated event.
        var calculator = new TransitCalculator(new WithholdingGrahaProvider(), new ZeroAyanamsa());

        var exception = Assert.Throws<InvalidOperationException>(() => calculator.TransitSign("Saturn", Epoch));
        Assert.Contains("unvalidated", exception.Message);
    }

    private sealed class ZeroAyanamsa : IAyanamsa
    {
        public double GetDegrees(double julianDayUt) => 0;
    }

    /// <summary>One graha advancing at a constant sidereal rate; every other graha parked.</summary>
    private sealed class LinearGrahaProvider(double startLongitude, double degreesPerDay) : IGrahaPositionsProvider
    {
        public GrahaSupport GetSupport() => new(Enum.GetValues<Graha>()
            .Select(graha => new GrahaSupportEntry(graha, true, null)).ToArray());

        public IReadOnlyList<GrahaPosition> GetSiderealPositions(double julianDayUt, double ayanamsaDegrees)
        {
            var days = julianDayUt - JulianDay.J2000;
            return Enum.GetValues<Graha>().Select(graha =>
            {
                var longitude = Normalize(startLongitude + (degreesPerDay * days));
                return new GrahaPosition(graha, longitude, (int)(longitude / 30), longitude % 30, false,
                    degreesPerDay, GrahaReasonCodes.Validated, null);
            }).ToArray();
        }

        private static double Normalize(double degrees)
        {
            var wrapped = degrees % 360d;
            return wrapped < 0 ? wrapped + 360d : wrapped;
        }
    }

    /// <summary>Computes nothing: every graha is withheld exactly as the shipped reference set does.</summary>
    private sealed class WithholdingGrahaProvider : IGrahaPositionsProvider
    {
        public GrahaSupport GetSupport() => new(Enum.GetValues<Graha>()
            .Select(graha => new GrahaSupportEntry(graha, true, null)).ToArray());

        public IReadOnlyList<GrahaPosition> GetSiderealPositions(double julianDayUt, double ayanamsaDegrees) =>
            Enum.GetValues<Graha>().Select(graha => new GrahaPosition(graha, null, null, null, null, 1d,
                GrahaReasonCodes.UnvalidatedReference,
                "No independent reference value is checked in for " + graha)).ToArray();
    }
}
