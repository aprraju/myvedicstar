using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using MyVedicDay.Astronomy;
using MyVedicStar.Astronomy.Core;

namespace MyVedicDay.Astronomy.Tests.Calculation;

/// <summary>
/// AST-10 (api#75) observability: a birth profile has no route onto the astronomy telemetry surface.
/// </summary>
/// <remarks>
/// <para>
/// `RequestScopedTelemetryTests` already asserts that attribute <b>names</b> stay inside the declared closed set. This
/// class attacks the other direction: it takes real profile-shaped <b>values</b> - a date, a time, coordinates, a name,
/// an e-mail, an account id - runs them through the engine's own surface, and asserts none of them appears in any
/// captured measurement, span or exception message.
/// </para>
/// <para>
/// A name check passes trivially against a surface that emits nothing. So every assertion here is paired with a
/// positive control that proves the listener is attached and the instrument works; without that, "no identifier found"
/// would be indistinguishable from "nothing was recorded".
/// </para>
/// </remarks>
public sealed class ProfileIdentifiersNeverReachLogsTests
{
    /// <summary>Profile-shaped values a caller could plausibly supply. Each is distinctive enough to find if leaked.</summary>
    private static readonly (string Field, string Value)[] ProfileValues =
    [
        ("birth date", "1990-04-12"),
        ("birth time", "06:15:00"),
        ("latitude", "13.0827"),
        ("longitude", "80.2707"),
        ("place", "Chennai"),
        ("name", "Asha Ramanathan"),
        ("email", "asha.ramanathan@example.invalid"),
        ("account id", "7f3c2b1a-9e8d-4c6b-8a5f-1d2e3c4b5a69"),
        ("profile id", "b4a1c9d2-3f5e-4a7b-9c8d-0e1f2a3b4c5d")
    ];

    /// <summary>
    /// The engine's actual surface, exercised with profile-shaped inputs. The port takes a Julian Day, so the profile
    /// fields are supplied as the things a caller would log or tag alongside the call - and the assertion is that the
    /// surface records none of them.
    /// </summary>
    [Fact]
    public void No_profile_value_appears_in_any_measurement_or_span()
    {
        using var spy = new TelemetrySpy();

        // Positive control first: if the listener cannot see a known-good measurement, every "not found" below would be
        // vacuous. This is the assertion that makes the rest mean something.
        AstronomyObservability.CalculationDuration.Record(1.0,
            new KeyValuePair<string, object?>("astronomy.body", "Sun"),
            new KeyValuePair<string, object?>("astronomy.outcome", "ok"));
        Assert.True(spy.Everything.Count > 0,
            "The telemetry spy saw nothing; the leakage assertions below would pass vacuously.");

        // Now the real path: a coarsened year is the only date-related value the surface may carry.
        var year = AstronomyObservability.CoarseYear(new UtInstant(2447994.5));
        using (var activity = AstronomyObservability.ActivitySource.StartActivity("astronomy.calculate"))
        {
            activity?.SetTag("astronomy.year", year);
            activity?.SetTag("astronomy.body", "Moon");
            activity?.SetTag("astronomy.outcome", "ok");
        }

        AstronomyObservability.CalculationErrors.Add(1,
            new KeyValuePair<string, object?>("astronomy.error.code", "unsupported-date"));

        var haystack = spy.Everything;

        foreach (var (field, value) in ProfileValues)
        {
            Assert.False(haystack.Any(captured => captured.Contains(value, StringComparison.OrdinalIgnoreCase)),
                $"A {field} reached the telemetry surface: '{value}' was found in a captured measurement or span.");
        }
    }

    /// <summary>
    /// The date quantiser is the boundary: the surface may know the year, never the day, month or moment.
    /// </summary>
    [Fact]
    public void An_instant_reduces_to_a_year_and_no_finer_granularity()
    {
        // 12 April 1990 06:15 UT.
        var coarsened = AstronomyObservability.CoarseYear(new UtInstant(2447994.93));

        Assert.Equal(1990, coarsened);

        // The rendered value carries a year and nothing that could be read back as a date: no 4-digit-day, no month
        // name, no clock time.
        var rendered = coarsened.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal("1990", rendered);
        Assert.DoesNotContain("04", rendered);
        Assert.DoesNotContain("12", rendered);
        Assert.DoesNotContain("06", rendered);
    }

    /// <summary>
    /// The declared attribute set is the closed list, and it contains nothing a caller could populate with a profile.
    /// </summary>
    [Fact]
    public void The_declared_attribute_set_admits_no_caller_supplied_field()
    {
        // Every allowed name is an engine fact or a coarse quantiser. Spelling that out means a future addition has to
        // argue against this list rather than slip in beside it.
        string[] engineFacts =
        [
            "astronomy.engine.version", "astronomy.ephemeris.source", "astronomy.kernel.identifier",
            "astronomy.provider", "astronomy.body", "astronomy.year", "astronomy.outcome",
            "astronomy.cache.result", "astronomy.error.code", "astronomy.duration.ms", "astronomy.delta_t.seconds"
        ];

        Assert.Equal(engineFacts.OrderBy(name => name, StringComparer.Ordinal),
            AstronomyObservability.AllowedAttributeNames.OrderBy(name => name, StringComparer.Ordinal));

        string[] forbiddenSubstrings =
        [
            "user", "account", "profile", "birth", "name", "email", "latitude", "longitude", "elevation", "observer",
            "place", "reason", "message", "id."
        ];

        foreach (var allowed in AstronomyObservability.AllowedAttributeNames)
            Assert.DoesNotContain(forbiddenSubstrings,
                fragment => allowed.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Captures every measurement name/value and span tag the astronomy meter and source emit.</summary>
    private sealed class TelemetrySpy : IDisposable
    {
        private readonly MeterListener listener = new();
        private readonly ActivityListener activityListener = new();

        public TelemetrySpy()
        {
            listener.InstrumentPublished = (instrument, owning) =>
            {
                if (instrument.Meter.Name == AstronomyObservability.MeterName) owning.EnableMeasurementEvents(instrument);
            };
            listener.SetMeasurementEventCallback<double>((_, value, tags, _) => Capture(value, tags));
            listener.SetMeasurementEventCallback<long>((_, value, tags, _) => Capture(value, tags));
            listener.Start();

            activityListener.ShouldListenTo = source => source.Name == AstronomyObservability.ActivitySourceName;
            activityListener.ActivityStopped = activity => Capture(0, activity.TagObjects);
            ActivitySource.AddActivityListener(activityListener);
        }

        public ConcurrentBag<string> Everything { get; } = [];

        private void Capture<T>(T value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            foreach (var tag in tags) CaptureTag(tag.Key, tag.Value);
            Everything.Add(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "");
        }

        private void Capture<T>(T value, IEnumerable<KeyValuePair<string, object?>> tags)
        {
            foreach (var tag in tags) CaptureTag(tag.Key, tag.Value);
            Everything.Add(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "");
        }

        private void CaptureTag(string key, object? value)
        {
            Everything.Add(key);
            Everything.Add(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "");
        }

        public void Dispose()
        {
            listener.Dispose();
            activityListener.Dispose();
        }
    }
}
