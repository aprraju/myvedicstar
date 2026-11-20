using MyVedicStar.Astronomy.Reference;

namespace MyVedicStar.Astronomy.Tests;

/// <summary>
/// api#140. The header assertions, tested against the two real response headers that produced the confusion.
/// </summary>
/// <remarks>
/// Both header sets below are transcribed from live Horizons responses, not invented. A test written against an
/// imagined header would pass while the real one still slipped through.
/// </remarks>
public sealed class HorizonHeaderAssertionTests
{
    /// <summary>
    /// My own query, <c>CENTER='399'</c>. Note what looks correct and what is not: target and centre body are right,
    /// only the site reveals the vector is topocentric.
    /// </summary>
    private static readonly string[] TopocentricHeader =
    [
        "Target body name: Moon (301)                      {source: DE441}",
        "Center body name: Earth (399)                     {source: DE441}",
        "Center-site name: Kushiro",
        "Center geodetic : 144.59, 43.0781806, .132201     {E-lon(deg),Lat(deg),Alt(km)}",
        "Output units    : AU-D",
        "Reference frame : Ecliptic of J2000.0",
        "2451544.500000000 = A.D. 2000-Jan-01 00:00:00.0000 TDB",
    ];

    private static readonly string[] GeocentricHeader =
    [
        "Target body name: Moon (301)                      {source: DE441}",
        "Center body name: Earth (399)                     {source: DE441}",
        "Center-site name: BODY CENTER",
        "Center geodetic : 0.0, 0.0, 0.0                   {E-lon(deg),Lat(deg),Alt(km)}",
        "Output units    : KM-S",
        "Reference frame : Ecliptic of J2000.0",
        "2451544.500000000 = A.D. 2000-Jan-01 00:00:00.0000 TDB",
    ];

    [Fact]
    public void A_topocentric_header_is_refused_even_though_target_and_centre_body_look_correct()
    {
        var problems = HorizonHeaderAssertions.Check(TopocentricHeader);

        // Target body and centre body both agree, so a check that only looked at those would pass this header. The
        // site is the line that reveals the 245 arcsecond parallax; the units differ too (AU-D vs KM-S), and both are
        // reported.
        Assert.Contains(problems, p => p.Contains("Center-site name", StringComparison.Ordinal)
            && p.Contains("500@399", StringComparison.Ordinal));
    }

    [Fact]
    public void A_geocentric_header_passes()
    {
        Assert.Empty(HorizonHeaderAssertions.Check(GeocentricHeader));
    }

    [Fact]
    public void The_wrong_units_are_refused_by_name()
    {
        // The topocentric header is wrong in two ways: the site AND the units. Both must be reported, because a
        // vector in AU compared against one in KM looks like a scale error rather than a unit error.
        var problems = HorizonHeaderAssertions.Check(TopocentricHeader);

        Assert.Equal(2, problems.Count);
        Assert.Contains(problems, p => p.Contains("Center-site name", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("Output units", StringComparison.Ordinal));
    }

    [Fact]
    public void A_missing_time_scale_on_the_epoch_lines_is_refused()
    {
        var header = GeocentricHeader
            .Select(line => line.EndsWith(" TDB", StringComparison.Ordinal) ? line[..^4] + " UT" : line)
            .ToArray();

        var problems = HorizonHeaderAssertions.Check(header);

        Assert.Single(problems);
        Assert.Contains("time scale", problems[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_wrong_reference_frame_is_reported_separately_from_the_site()
    {
        // A run wrong in two ways should say both, so it is fixed once rather than twice.
        var header = TopocentricHeader
            .Select(line => line.StartsWith("Reference frame", StringComparison.Ordinal)
                ? "Reference frame : Ecliptic and mean equinox of date"
                : line)
            .ToArray();

        var problems = HorizonHeaderAssertions.Check(header);

        // Asserted by presence, not by count: the header is already wrong in other ways, and pinning the total would
        // make this test fail every time a new named assertion is added rather than when the frame is wrong.
        Assert.Contains(problems, p => p.Contains("Center-site name", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("Reference frame", StringComparison.Ordinal));
    }

    [Fact]
    public void An_empty_header_is_refused_rather_than_treated_as_acceptable()
    {
        // A 400 response has no header. Reading that as "nothing wrong" is exactly how APPARENT='N' looked like an
        // empty result instead of an error.
        var problems = HorizonHeaderAssertions.Check([]);

        // Every named assertion must fire, and there is more than one, so an empty header can never read as clean.
        Assert.NotEmpty(problems);
        Assert.True(problems.Count > 2, "An empty header should trip every named assertion, not just a couple.");
    }

    [Fact]
    public void The_required_values_match_the_live_geocentric_response()
    {
        Assert.Equal("BODY CENTER", HorizonHeaderAssertions.RequiredCenterSiteName);
        Assert.Equal("Ecliptic of J2000.0", HorizonHeaderAssertions.RequiredReferenceFrame);
        Assert.True(HorizonHeaderAssertions.Contains(GeocentricHeader, "Center-site name:",
            HorizonHeaderAssertions.RequiredCenterSiteName));
    }
}

