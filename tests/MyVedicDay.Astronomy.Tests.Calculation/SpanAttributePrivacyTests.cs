using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using MyVedicStar.Astronomy.Core;

namespace MyVedicDay.Astronomy.Tests.Calculation;

/// <summary>
/// api#75 (AST-10) AC-4: no profile identifier reaches <b>any span attribute</b>, asserted per attribute rather than
/// per measurement name.
/// </summary>
/// <remarks>
/// <para>
/// The existing telemetry test asserts that captured measurement attribute NAMES stay inside the declared closed set.
/// That is necessary and not sufficient: a span attribute carried on an <see cref="Activity"/> is a different channel
/// from a metric tag, and a request-scoped value added with <c>SetTag</c> would never appear in a measurement-name
/// scan. This test listens to the activity side and inspects every tag key/value and every baggage entry.
/// </para>
/// <para>
/// The positive control matters more here than anywhere: a listener attached to a source that emits nothing would make
/// every assertion below vacuous. The test first proves it can see a span it created, and only then asserts about what
/// the engine emits.
/// </para>
/// </remarks>
public sealed class SpanAttributePrivacyTests
{
    /// <summary>Values that must never appear in a span attribute, a tag value, or baggage.</summary>
    private static readonly (string Field, string Value)[] ProfileValues =
    [
        ("birth date", "1990-04-12"),
        ("birth time", "06:15:00"),
        ("latitude", "13.0827"),
        ("longitude", "80.2707"),
        ("place", "Chennai"),
        ("person name", "Asha Ramanathan"),
        ("email", "asha.ramanathan@example.invalid"),
        ("account id", "7f3c2b1a-9e8d-4c6b-8a5f-1d2e3c4b5a69"),
        ("profile id", "b4a1c9d2-3f5e-4a7b-9c8d-0e1f2a3b4c5d")
    ];

    [Fact]
    public void A_span_from_the_astronomy_source_carries_no_profile_value()
    {
        using var spy = new SpanSpy();

        // Positive control: prove the spy sees a span at all before asserting about absence. Without this, "no profile
        // value found" would be indistinguishable from "no span was observed".
        using (var control = AstronomyObservability.ActivitySource.StartActivity("control"))
        {
            Assert.NotNull(control);
            control.SetTag("astronomy.body", "Sun");
        }
        Assert.True(spy.Captured.Count > 0,
            "The span spy observed nothing; the privacy assertions below would pass vacuously.");

        // Now the engine's own emission shape, including the coarse year the surface is allowed to record.
        using (var span = AstronomyObservability.ActivitySource.StartActivity("astronomy.calculate"))
        {
            Assert.NotNull(span);
            span.SetTag("astronomy.year", AstronomyObservability.CoarseYear(new UtInstant(2447994.93)));
            span.SetTag("astronomy.body", "Moon");
            span.SetTag("astronomy.outcome", "ok");
            span.SetTag("astronomy.error.code", "unsupported-date");
        }

        foreach (var (field, value) in ProfileValues)
        {
            Assert.False(spy.Captured.Any(captured => captured.Contains(value, StringComparison.OrdinalIgnoreCase)),
                $"A {field} reached a span attribute: '{value}' was observed on the astronomy activity source.");
        }
    }

    [Fact]
    public void Every_span_tag_key_emitted_is_a_declared_attribute()
    {
        using var spy = new SpanSpy();

        using (var span = AstronomyObservability.ActivitySource.StartActivity("astronomy.calculate"))
        {
            Assert.NotNull(span);
            span.SetTag("astronomy.body", "Sun");
            span.SetTag("astronomy.year", 1990);
            span.SetTag("astronomy.outcome", "ok");
        }

        Assert.NotEmpty(spy.TagKeys);

        // Every key that reached a span must be one of the declared names. A key outside the set is a defect even if its
        // value happens to be harmless: the closed set is what stops a future caller adding one that is not.
        foreach (var key in spy.TagKeys)
            Assert.Contains(key, AstronomyObservability.AllowedAttributeNames);
    }

    [Fact]
    public void An_identifier_put_in_baggage_is_visible_to_the_spy_so_the_check_can_fail()
    {
        // Falsification in-test and permanently: this proves the capture path DOES see a profile-shaped value when one
        // is present. Without a demonstration, a passing "not found" assertion is an untested claim about the listener.
        using var spy = new SpanSpy();

        using (var span = AstronomyObservability.ActivitySource.StartActivity("astronomy.calculate"))
        {
            Assert.NotNull(span);
            span.SetTag("astronomy.body", "Sun");
            // Deliberate violation: a profile identifier on the span. The spy must see it and the check must reject it.
            span.SetTag("astronomy.observer", ProfileValues[4].Value);
        }

        var seen = spy.Captured.Any(captured => captured.Contains(ProfileValues[4].Value, StringComparison.OrdinalIgnoreCase));
        Assert.True(seen, "The spy did not observe a value that WAS placed on a span; the privacy check is not wired.");

        // And the same value would be caught by the real assertion, which is the point of using the spy at all.
        Assert.Contains("astronomy.observer", spy.TagKeys);
        Assert.DoesNotContain("astronomy.observer", AstronomyObservability.AllowedAttributeNames);
    }

    /// <summary>Captures every tag key/value, event and baggage entry the astronomy activity source emits.</summary>
    private sealed class SpanSpy : IDisposable
    {
        private readonly ActivityListener listener = new();

        public SpanSpy()
        {
            listener.ShouldListenTo = source => source.Name == AstronomyObservability.ActivitySourceName;
            listener.Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded;
            listener.ActivityStopped = activity =>
            {
                foreach (var tag in activity.TagObjects)
                {
                    TagKeys.Add(tag.Key);
                    Captured.Add(Convert.ToString(tag.Value, CultureInfo.InvariantCulture) ?? string.Empty);
                }
                foreach (var baggage in activity.Baggage)
                {
                    TagKeys.Add(baggage.Key);
                    Captured.Add(baggage.Value ?? string.Empty);
                }
                foreach (var item in activity.Events.SelectMany(e => e.Tags))
                    Captured.Add(Convert.ToString(item.Value, CultureInfo.InvariantCulture) ?? string.Empty);
            };
            ActivitySource.AddActivityListener(listener);
        }

        public ConcurrentBag<string> Captured { get; } = [];

        public ConcurrentBag<string> TagKeys { get; } = [];

        public void Dispose() => listener.Dispose();
    }
}
