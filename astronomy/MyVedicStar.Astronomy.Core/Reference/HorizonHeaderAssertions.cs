namespace MyVedicStar.Astronomy.Reference;

/// <summary>
/// The response-header facts a fetched value must agree with before it may be used as a reference.
/// </summary>
/// <remarks>
/// <para><b>Assert the header, never the request.</b> The request is what you believed; the header is what happened.
/// Two silent-default failures in one session proved this the expensive way:</para>
/// <list type="bullet">
/// <item><c>APPARENT='N'</c> is not a legal value - the parameter selects atmospheric refraction
/// (<c>AIRLESS</c>/<c>REFRACTED</c>), not aberration. The request returned HTTP 400 and the empty body read as "no
/// result", so a bad parameter looked like a missing capability.</item>
/// <item><c>CENTER='399'</c> does not mean geocentric - it silently resolves to a <b>topocentric</b> site (in our case
/// Kushiro, 144.59E 43.078N). The request looked right, the response looked plausible, and the longitude was 245
/// arcseconds out - the Moon's parallax from that site.</item>
/// </list>
/// <para>Both produced a correct-looking request and a plausible-looking answer. A response header that is checked
/// against an expected value turns each of them into a refusal.</para>
/// </remarks>
public static class HorizonHeaderAssertions
{
    /// <summary>The centre-site value a geocentric reference requires.</summary>
    /// <remarks>
    /// Horizons reports <c>BODY CENTER</c> with geodetic <c>0.0, 0.0, 0.0</c> for <c>CENTER='500@399'</c>. Any other value
    /// means the vector is topocentric and is not comparable with a geocentric engine series.
    /// </remarks>
    public const string RequiredCenterSiteName = "BODY CENTER";

    /// <summary>The reference frame the rows are expressed in.</summary>
    /// <remarks>Horizons reports the literal string <c>Ecliptic of J2000.0</c> for the vector product used.</remarks>
    public const string RequiredReferenceFrame = "Ecliptic of J2000.0";

    /// <summary>
    /// Checks the header facts that determine what a fetched vector means.
    /// </summary>
    /// <param name="header">The response header lines, verbatim.</param>
    /// <returns>One refusal message per missing or unexpected fact; empty when the header is acceptable.</returns>
    /// <remarks>
    /// Returns all problems rather than throwing on the first: a run that is wrong in two ways should say both, so it is
    /// fixed once rather than twice.
    /// </remarks>
    public static IReadOnlyList<string> Check(IEnumerable<string> header)
    {
        ArgumentNullException.ThrowIfNull(header);

        var problems = new List<string>();

        if (!Contains(header, "Center-site name:", RequiredCenterSiteName))
        {
            problems.Add(
                $"Center-site name is not '{RequiredCenterSiteName}'. A topocentric vector is not comparable with a "
                + "geocentric engine series; send CENTER='500@399' and assert this header rather than trusting the "
                + "request.");
        }

        if (!Contains(header, "Reference frame", RequiredReferenceFrame))
            problems.Add($"Reference frame is not '{RequiredReferenceFrame}'.");

        return problems;
    }

    /// <summary>True when some header line starts with <paramref name="label"/> and contains <paramref name="expected"/>.</summary>
    public static bool Contains(IEnumerable<string> header, string label, string expected) =>
        header.Any(line =>
            line.TrimStart().StartsWith(label, StringComparison.OrdinalIgnoreCase)
            && line.Contains(expected, StringComparison.Ordinal));
}

