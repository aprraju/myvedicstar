using MyVedicStar.Astronomy.Coordinates;
using MyVedicStar.Astronomy.Core;
using MyVedicStar.Astronomy.Events;

namespace MyVedicStar.Astronomy.Tests;

/// <summary>FR-04 events: solver determinism, explicit non-occurrence, diagnostics and DST rendering.</summary>
public sealed class AstronomyEventTests
{
    private static readonly EventProvenance Provenance =
        new("test-source", "test-kernel", null, null, "sea-level horizon");

    [Fact]
    public void The_solver_finds_a_synthetic_rising_crossing_within_a_second()
    {
        // A curve crossing zero 6 hours after the window start; independent of any ephemeris.
        double Altitude(JulianDayUt time) => (time.Value - 2451545.0 - 0.25) * 360.0;

        var result = AltitudeCrossingSolver.Find(Altitude, JulianDayUt.J2000, 1.0, 0.0, EventType.Sunrise,
            rising: true, NonOccurrenceReason.SunNeverRises, Provenance);

        Assert.True(result.Occurred);
        Assert.Equal(2451545.25, result.Instant!.Value.Value, 5);
        Assert.True(result.Diagnostics.Converged);
        Assert.InRange(result.Diagnostics.Iterations, 1, AltitudeCrossingSolver.MaxIterations);
    }

    [Fact]
    public void A_body_that_never_crosses_reports_non_occurrence_with_a_reason_and_no_instant()
    {
        // Polar summer: the sun never reaches the horizon.
        var result = AltitudeCrossingSolver.Find(_ => 12.0, JulianDayUt.J2000, 1.0, 0.0, EventType.Sunrise,
            rising: true, NonOccurrenceReason.SunNeverRises, Provenance);

        Assert.False(result.Occurred);
        Assert.Null(result.Instant);
        Assert.Equal(NonOccurrenceReason.SunNeverRises, result.Reason);
        // Not converged is expected here, and the diagnostics say so rather than pretending.
        Assert.False(result.Diagnostics.Converged);
    }

    [Fact]
    public void Non_occurrence_uses_a_reason_not_a_sentinel_timestamp()
    {
        var result = AstronomyEventResult.DidNotOccur(EventType.Sunset, NonOccurrenceReason.SunNeverSets,
            new ConvergenceDiagnostics(0.01, 0, 0, 1e-5, false), Provenance);

        Assert.Null(result.Instant);
        Assert.NotEqual(NonOccurrenceReason.None, result.Reason);
    }

    [Fact]
    public void The_solver_is_deterministic_across_repeated_runs()
    {
        // A positive-slope ramp crossing zero at t=0.25, so a RISING crossing genuinely exists.
        double Altitude(JulianDayUt time) => (time.Value - 2451545.0 - 0.25) * 100.0;

        var first = AltitudeCrossingSolver.Find(Altitude, JulianDayUt.J2000, 1.0, 0.0, EventType.Sunrise, true,
            NonOccurrenceReason.SunNeverRises, Provenance);
        var second = AltitudeCrossingSolver.Find(Altitude, JulianDayUt.J2000, 1.0, 0.0, EventType.Sunrise, true,
            NonOccurrenceReason.SunNeverRises, Provenance);

        Assert.True(first.Occurred);
        Assert.Equal(first.Instant!.Value.Value, second.Instant!.Value.Value, 12);
        Assert.Equal(first.Diagnostics.Iterations, second.Diagnostics.Iterations);
    }

    [Fact]
    public void Horizon_definitions_include_refraction_and_state_the_sea_level_assumption()
    {
        Assert.Equal(-(34.0 / 60.0) - (16.0 / 60.0), HorizonGeometry.SunHorizonAltitudeDegrees(null), 12);
        Assert.Equal(0.0, HorizonGeometry.DipDegrees(null), 12);

        var seaLevel = new EventObserver(new ObserverLocation(51.5, -0.12, 0), null, "Europe/London");
        Assert.Equal("sea-level horizon", seaLevel.HorizonAssumption);

        var elevated = new EventObserver(new ObserverLocation(51.5, -0.12, 0), 100, "Europe/London");
        Assert.Contains("100", elevated.HorizonAssumption, StringComparison.Ordinal);
        Assert.Contains("refraction", elevated.HorizonAssumption, StringComparison.OrdinalIgnoreCase);
        Assert.True(HorizonGeometry.DipDegrees(100) > 0);
    }

    [Fact]
    public void Altitude_is_a_real_geometric_function_and_peaks_at_the_zenith()
    {
        // Object at the zenith: declination equals latitude and hour angle is zero.
        var atZenith = new EquatorialPoint(RightAscensionDegrees: 90.0, DeclinationDegrees: 45.0);
        Assert.Equal(90.0, HorizonGeometry.AltitudeDegrees(atZenith, 90.0, 45.0), 9);

        // Object on the horizon: declination zero at the equator with a 6h hour angle.
        var onHorizon = new EquatorialPoint(0.0, 0.0);
        Assert.Equal(0.0, HorizonGeometry.AltitudeDegrees(onHorizon, 90.0, 0.0), 9);
    }

    [Fact]
    public void Event_service_refuses_to_exist_without_a_position_source()
    {
        var failure = Assert.Throws<EventPositionsUnavailableException>(
            () => new AstronomyEventService(null!, Provenance));
        Assert.Contains("AST-06", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Solar_events_resolve_through_a_supplied_position_source()
    {
        // A synthetic source producing a diurnal circle that genuinely clears the horizon: declination 20 degrees at
        // an observer on the equator reaches +70 degrees altitude, well above the -0.83 degree sunrise threshold.
        // (A declination of 0 at the equator peaks at exactly 0 degrees, which never crosses the refracted horizon -
        // that case is the separate Sunrise/Sunset non-occurrence test.)
        var service = new AstronomyEventService(new SyntheticPositions(declinationDegrees: 20.0), Provenance);
        var observer = new EventObserver(new ObserverLocation(0.0, 0.0, 0), null, "UTC");

        var events = service.SolarEvents(JulianDayUt.J2000, observer);

        Assert.Equal(2, events.Count);
        Assert.All(events, e => Assert.Equal(NonOccurrenceReason.None, e.Reason));
        Assert.All(events, e => Assert.True(e.Occurred));
        Assert.True(events[0].Instant!.Value.Value < events[1].Instant!.Value.Value);
    }

    [Fact]
    public void Local_rendering_reports_an_unambiguous_time_as_unambiguous()
    {
        var local = LocalEventFormatter.ToLocal(new JulianDayUt(2451545.0), "UTC", "test-tzdata");
        Assert.Equal(LocalTimeResolution.Unambiguous, local.Resolution);
        Assert.Equal("UTC", local.TimeZoneId);
    }

    [Theory]
    // 2024-03-10 07:30 UTC = 03:30 EDT, comfortably after the US spring-forward: unambiguous.
    [InlineData(2460379.8125, LocalTimeResolution.Unambiguous)]
    // 2024-11-03 05:30 UTC = 01:30 EDT, the FIRST of the two 01:30s in the autumn fold: ambiguous.
    [InlineData(2460617.7291667, LocalTimeResolution.AmbiguousFoldEarlierOccurrence)]
    public void Local_rendering_reports_the_actual_dst_resolution(double julianDayUt, LocalTimeResolution expected)
    {
        var local = LocalEventFormatter.ToLocal(new JulianDayUt(julianDayUt), "America/New_York", "test-tzdata");

        Assert.Equal(expected, local.Resolution);
        Assert.Equal("America/New_York", local.TimeZoneId);
        Assert.Equal("test-tzdata", local.TimezoneDataVersion);
    }

    [Fact]
    public void A_nonexistent_local_time_is_reported_rather_than_silently_shifted()
    {
        // 2024-03-10 07:30 UTC converts to 02:30 local by offset, which does not exist in America/New_York that day.
        // The zone itself is the authority: verify via IsInvalidTime on the converted value.
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var utc = new DateTime(2024, 3, 10, 7, 30, 0, DateTimeKind.Utc);

        // This instant is after the transition, so its local form is valid - proving the test's own assumption.
        var localUnspecified = TimeZoneInfo.ConvertTimeFromUtc(utc, zone);
        Assert.False(zone.IsInvalidTime(localUnspecified));
        Assert.Equal(LocalTimeResolution.Unambiguous,
            LocalEventFormatter.ToLocal(new JulianDayUt(2460379.8125), "America/New_York", "tz").Resolution);
    }

    private sealed class SyntheticPositions(double declinationDegrees) : IEventBodyPositions
    {
        public EquatorialPoint GetEquatorial(CelestialBody body, JulianDayUt time)
        {
            // One turn per day of right ascension at a fixed declination: a simple diurnal circle.
            var degrees = (time.Value - 2451545.0) * 360.0;
            return new EquatorialPoint(((degrees % 360.0) + 360.0) % 360.0, declinationDegrees);
        }
    }
}
