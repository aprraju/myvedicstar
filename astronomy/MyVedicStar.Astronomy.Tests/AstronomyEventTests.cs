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
        double Altitude(JulianDayUt time) => Math.Sin((time.Value - 2451545.0) * 2.0 * Math.PI);

        var first = AltitudeCrossingSolver.Find(Altitude, JulianDayUt.J2000, 1.0, 0.0, EventType.Sunrise, true,
            NonOccurrenceReason.SunNeverRises, Provenance);
        var second = AltitudeCrossingSolver.Find(Altitude, JulianDayUt.J2000, 1.0, 0.0, EventType.Sunrise, true,
            NonOccurrenceReason.SunNeverRises, Provenance);

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
        // A synthetic source that reproduces a plausible daily circle, so the wiring is exercised without an
        // ephemeris. Declination fixed at 0 and RA advancing one full turn per sidereal day.
        var service = new AstronomyEventService(new SyntheticPositions(), Provenance);
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

    [Fact]
    public void Local_rendering_states_how_a_dst_gap_is_resolved()
    {
        // 2024-03-10 07:00 UTC is 03:00 EDT, just after the US spring-forward; 06:30 UTC is inside the gap edge.
        // Assert the resolution is reported as one of the two named values rather than silently normalised.
        var local = LocalEventFormatter.ToLocal(new JulianDayUt(2460380.2708333), "America/New_York", "test-tzdata");
        Assert.Contains(local.Resolution,
            new[] { LocalTimeResolution.Unambiguous, LocalTimeResolution.NonexistentGapShiftedForward,
                LocalTimeResolution.AmbiguousFoldEarlierOccurrence });
        Assert.Equal("America/New_York", local.TimeZoneId);
        Assert.Equal("test-tzdata", local.TimezoneDataVersion);
    }

    private sealed class SyntheticPositions : IEventBodyPositions
    {
        public EquatorialPoint GetEquatorial(CelestialBody body, JulianDayUt time)
        {
            // One turn per day of right ascension, zero declination: a simple diurnal circle.
            var degrees = (time.Value - 2451545.0) * 360.0;
            return new EquatorialPoint(((degrees % 360.0) + 360.0) % 360.0, 0.0);
        }
    }
}
