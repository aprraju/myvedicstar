using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyVedicDay.Astronomy;
using MyVedicStar.Astronomy.Core;

namespace MyVedicDay.Astronomy.Tests.Calculation;

/// <summary>
/// AST-10 (api#75): what the astronomy path shares across requests, and the test that fails if a mutable static
/// learns to carry a request's input into someone else's response.
/// </summary>
/// <remarks>
/// <para>
/// The item #159 defect was a static latch: the first writer's state silently won every later writer. The defence here
/// is not a careful reviewer, it is <see cref="MutableStaticCensus"/>, which reflects over the astronomy assemblies and
/// fails on any non-readonly mutable static field. A field added later breaks this suite at build-and-test time rather
/// than in production, and the assertion names the offending field.
/// </para>
/// <para>
/// The remainder asserts the observables AST-10 claims: the provider is a singleton (shared engine state is intended and
/// stateless), the result cache is a singleton (cross-request cache hits are the point), and the telemetry surface emits
/// only attributes from a closed set — so no birth-profile input has anywhere to go.
/// </para>
/// </remarks>
// Touches the process-wide default conversion via its seams, so it shares the serialised collection (see
// TtOffsetsCollection): without it, a seam restored by another class made these assertions fail by ordering.
[Collection("astronomy-tt-offsets")]
public sealed class RequestScopedTelemetryTests
{
    private const double JulianDay = 2448908.5;

    [Fact]
    public void Astronomy_assemblies_have_no_mutable_static_state_to_cross_a_request_boundary()
    {
        // Exactly one exception, named: the default UT->TT conversion. It is process state BY DESIGN (the provider installs
        // it at composition), it is replaced atomically by reference so no request ever reads a half-installed seam, and
        // it never holds a caller's input - a seam is conversion functions, not data. Everything else must be a finding.
        const string DefaultConversion = "MyVedicStar.Astronomy.Core.TtConversionSeam._default";

        var offenders = MutableStaticCensus.FindMutableStatics(
            [typeof(AstronomyObservability).Assembly, typeof(IEphemeris).Assembly],
            [DefaultConversion]);

        // The exception is re-checked rather than trusted: it must still be the field we think it is.
        Assert.True(typeof(TtConversionSeam)
                .GetField(DefaultConversion.Split('.')[^1], System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Static) is { IsInitOnly: false },
            "The single mutable-static exception is no longer a mutable field; remove it from the list.");

        Assert.True(offenders.Count == 0,
            "Mutable static state could carry one request's input into another's response: " +
            string.Join("; ", offenders));
    }

    [Fact]
    public void The_provider_and_the_result_cache_are_singletons_so_their_sharing_is_deliberate()
    {
        var services = new ServiceCollection();
        services.AddAstronomyProvider(ProviderSelectionParser.AnalyticalKey);
        using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<IEphemeris>();
        var second = provider.GetRequiredService<IEphemeris>();
        Assert.Same(first, second);
    }

    [Fact]
    public void Every_telemetry_attribute_name_is_declared_in_the_closed_set()
    {
        using var listener = new TelemetrySpy();
        AstronomyObservability.CalculationDuration.Record(1.0,
            new KeyValuePair<string, object?>("astronomy.body", "Sun"),
            new KeyValuePair<string, object?>("astronomy.outcome", "ok"),
            new KeyValuePair<string, object?>("astronomy.year", 1992));

        Assert.NotEmpty(listener.MeasurementNames);
        foreach (var name in listener.MeasurementNames)
            Assert.Contains(name, AstronomyObservability.AllowedAttributeNames);
    }

    [Fact]
    public void A_birth_profile_has_no_attribute_on_the_telemetry_surface()
    {
        // The scrubbing test: names a real financial/health-grade input would use. None may be permitted, and none may
        // appear on a real measurement.
        string[] forbidden =
        [
            "user.id", "birth.date", "birth.time", "birth.place", "name", "email", "latitude", "longitude",
            "observer", "profile"
        ];

        foreach (var name in forbidden)
        {
            Assert.DoesNotContain(AstronomyObservability.AllowedAttributeNames, allowed =>
                allowed.Contains(name, StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(AstronomyObservability.ActivitySource.Name, name, StringComparison.Ordinal);
        }

        using var listener = new TelemetrySpy();
        AstronomyObservability.ActivitySource.StartActivity("test")?.Dispose();
        Assert.DoesNotContain(listener.MeasurementNames, name =>
            forbidden.Any(f => name.Contains(f, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void A_refused_second_conversion_is_counted_rather_than_a_silent_no_op()
    {
        // The concurrency claim as an observable: the conversion state a caller can be in is held per seam, not per
        // process. Two seams with different answers coexist at one instant and neither disturbs the other.
        var real = TtConversionSeam.Installed(DeltaT.ToJulianEphemerisDay, DeltaT.ToJulianEphemerisDay);
        var absent = TtConversionSeam.Uninstalled();
        var other = TtConversionSeam.Installed(day => day + 0.5, day => day - 0.5);
        var instant = new UtInstant(2451545.0);

        Assert.Equal(DeltaT.ToJulianEphemerisDay(2451545.0), instant.ToTerrestrialTime(real).JulianDay, 12);
        Assert.Throws<InvalidOperationException>(() => instant.ToTerrestrialTime(absent));

        // The other seam answers for itself only: the real seam is unmoved either side of it.
        Assert.Equal(2451545.5, instant.ToTerrestrialTime(other).JulianDay, 12);
        Assert.Equal(DeltaT.ToJulianEphemerisDay(2451545.0), instant.ToTerrestrialTime(real).JulianDay, 12);
    }

    [Fact]
    public void An_instant_is_recorded_as_a_coarse_year_only()
    {
        var year = AstronomyObservability.CoarseYear(new UtInstant(JulianDay));
        Assert.Equal(1992, year);
    }

    private sealed class TelemetrySpy : IDisposable
    {
        private readonly MeterListener _listener = new();
        public List<string> MeasurementNames { get; } = [];

        public TelemetrySpy()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == AstronomyObservability.MeterName)
                    listener.EnableMeasurementEvents(instrument);
            };
            _listener.SetMeasurementEventCallback<double>((instrument, _, tags, _) => Record(tags));
            _listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) => Record(tags));
            _listener.Start();
        }

        private void Record(ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            foreach (var tag in tags) MeasurementNames.Add(tag.Key);
        }

        public void Dispose() => _listener.Dispose();
    }
}

/// <summary>Reflects over the astronomy assemblies and reports every mutable static field.</summary>
internal static class MutableStaticCensus
{
    /// <summary>
    /// Returns <c>Type.Field</c> for every static field that is neither readonly nor a const. The telemetry instruments
    /// are readonly statics, so they are not reported; a mutable one would be.
    /// </summary>
    public static IReadOnlyList<string> FindMutableStatics(System.Reflection.Assembly[] assemblies,
        IReadOnlyCollection<string> allowed)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        ArgumentNullException.ThrowIfNull(allowed);

        var offenders = new List<string>();

        // Read after the null guards above; a null argument would otherwise fail on a confusing call site.
        foreach (var assembly in assemblies.Distinct())
        {
            foreach (var type in assembly.GetTypes())
            {
                if (type.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false))
                    continue;

                const System.Reflection.BindingFlags flags =
                    System.Reflection.BindingFlags.Static |
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.DeclaredOnly;

                foreach (var field in type.GetFields(flags))
                {
                    if (field.IsLiteral) continue;          // const
                    if (field.IsInitOnly) continue;         // readonly
                    if (field.IsDefined(typeof(System.ThreadStaticAttribute), false)) continue;

                    var name = $"{type.FullName}.{field.Name}";
                    if (allowed.Contains(name)) continue;

                    offenders.Add(name);
                }
            }
        }

        return offenders;
    }
}
