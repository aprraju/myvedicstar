using MyVedicStar.Astronomy.Core;
using MyVedicStar.Astronomy.Reference;

namespace MyVedicStar.Astronomy.Tests;

/// <summary>
/// api#140. The live round trip: ask Horizons, run the header checks, rotate to date.
/// </summary>
/// <remarks>
/// <para><b>Opt-in, not part of the default suite.</b> These call the real service, so they are a different kind of test
/// from the rest: they can fail for reasons that have nothing to do with this repository. Gated behind an environment
/// variable so CI stays hermetic, and so a network outage is never mistaken for a code defect.</para>
/// <para><b>What they prove that the structural tests cannot:</b> that the request we send is actually accepted, that
/// the header checks pass against a real response rather than a transcribed one, and that the numbers come out where
/// the independent arithmetic puts them.</para>
/// </remarks>
public sealed class HorizonsLiveFetchTests
{
    private static bool LiveEnabled =>
        string.Equals(Environment.GetEnvironmentVariable("MYVEDICSTAR_LIVE_HORIZONS"), "1", StringComparison.Ordinal);

    private static HorizonsClient Client() => new(new HttpClient { Timeout = TimeSpan.FromSeconds(30) });

    [Fact]
    public async Task The_live_geocentric_request_is_accepted_and_passes_the_header_checks()
    {
        if (!LiveEnabled) return;  // opt-in: see the class remarks

        var value = await Client().FetchAsync(
            new HorizonsQuery("Moon", new TtInstant(2451544.5), HorizonsQuantity.Geometric));

        // The provenance is what makes the row re-issuable; if these are blank the artifact is not reproducible.
        Assert.Equal("JPL Horizons", value.Provenance.Service);
        Assert.Equal("1.2", value.Provenance.ApiVersion);
        Assert.Contains("DE441", value.Provenance.EphemerisSource, StringComparison.Ordinal);
        Assert.Contains("500@399", value.Provenance.RequestParameters, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_live_longitude_matches_the_independently_computed_value()
    {
        if (!LiveEnabled) return;  // opt-in: see the class remarks

        // Geocentric Moon at 2000-Jan-01 00:00 TDB. Vector X=-3.176111005821861E+05, Y=-2.418751255175011E+05,
        // Z=3.655582540462588E+04 km. atan2(Y,X) = 217.2909017 deg in the ECLIPTIC OF J2000 frame.
        var value = await Client().FetchAsync(
            new HorizonsQuery("Moon", new TtInstant(2451544.5), HorizonsQuantity.Geometric));

        // The client rotates J2000 -> of date. At this instant precession is ~0.0193 deg (~70 arcsec), so the row
        // longitude is the J2000 value PLUS that rotation - asserted as a relationship rather than a magic number.
        var expectedJ2000 = 217.2909017;
        var precessionDegrees = EclipticOfDate.PrecessionArcseconds(2451544.5) / 3600.0;
        var expectedOfDate = expectedJ2000 + precessionDegrees;

        Assert.Equal(expectedOfDate, value.LongitudeDegrees, 4);
    }

    [Fact]
    public async Task An_unmapped_body_is_refused_rather_than_guessed_into_the_url()
    {
        if (!LiveEnabled) return;  // opt-in: see the class remarks

        // A guessed command code returns a real ephemeris for the wrong body - worse than an error.
        var failure = await Assert.ThrowsAsync<HorizonsUnavailableException>(() =>
            Client().FetchAsync(new HorizonsQuery("Pluto", new TtInstant(2451544.5), HorizonsQuantity.Geometric)));

        Assert.Equal("unmapped-body", failure.Reason);
    }

    [Fact]
    public async Task Apparent_is_refused_as_a_reference_rather_than_silently_differing()
    {
        if (!LiveEnabled) return;  // opt-in: see the class remarks

        var failure = await Assert.ThrowsAsync<HorizonsUnavailableException>(() =>
            Client().FetchAsync(new HorizonsQuery("Moon", new TtInstant(2451544.5), HorizonsQuantity.Apparent)));

        Assert.Equal("apparent-not-a-reference", failure.Reason);
    }
}

