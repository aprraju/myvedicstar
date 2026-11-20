using System.Globalization;
using System.Text.RegularExpressions;
using MyVedicStar.Astronomy.Core;

namespace MyVedicStar.Astronomy.Reference;

/// <summary>
/// Fetches reference vectors from JPL Horizons and turns them into rows in the frame the engine publishes.
/// </summary>
/// <remarks>
/// <para><b>The request this class sends, and why each parameter is fixed:</b></para>
/// <list type="bullet">
/// <item><c>CENTER='500@399'</c> - geocentric. <c>CENTER='399'</c> silently resolves to a <b>topocentric</b> site and
/// cost 245 arcseconds of lunar parallax before anyone noticed.</item>
/// <item><c>REF_PLANE='ECLIPTIC'</c> - the only ecliptic product offered. Horizons has no ecliptic-<i>of-date</i>
/// output, so the J2000-to-date rotation is applied on this side (see <see cref="EclipticOfDate"/>).</item>
/// <item><c>OUT_UNITS='KM-S'</c> - one declared unit. AU and KM mixed silently look like a 1.5e8 scale error.</item>
/// </list>
/// <para><b>Every response is header-checked before a single number is read.</b> The request is what we believed; the
/// header is what happened. <see cref="HorizonHeaderAssertions"/> is the control, and a response failing it is
/// discarded rather than reinterpreted.</para>
/// </remarks>
public sealed partial class HorizonsClient(HttpClient http) : IHorizonsClient
{
    /// <summary>The service endpoint. Fixed so a fetch is reproducible from the artifact alone.</summary>
    public const string Endpoint = "https://ssd.jpl.nasa.gov/api/horizons.api";

    /// <summary>The body identifiers this client knows how to request.</summary>
    /// <remarks>Explicit rather than derived: an unknown body must fail rather than be guessed into the URL.</remarks>
    private static readonly Dictionary<string, string> BodyCommand = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Moon"] = "301",
        ["Sun"] = "10",
    };

    [GeneratedRegex(@"X =\s*(-?[0-9.]+E[+-][0-9]+)\s+Y =\s*(-?[0-9.]+E[+-][0-9]+)\s+Z =\s*(-?[0-9.]+E[+-][0-9]+)",
        RegexOptions.CultureInvariant)]
    private static partial Regex VectorLine { get; }

    /// <inheritdoc />
    public async Task<HorizonsValue> FetchAsync(HorizonsQuery query, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!BodyCommand.TryGetValue(query.Body, out var command))
            throw new HorizonsUnavailableException("unmapped-body",
                $"'{query.Body}' has no Horizons identifier in this client. Add it deliberately rather than guessing a "
                + "command code; a wrong code returns a real ephemeris for the wrong body.");

        var start = ToHorizonsDate(query.InstantTt);
        // One step later, so START < STOP. The first vector is the requested instant.
        var stop = ToHorizonsDate(new TtInstant(query.InstantTt.JulianDay + 1.0));
        var uri = BuildUri(command, start, stop);

        string text;
        try
        {
            text = await http.GetStringAsync(uri, token).ConfigureAwait(false);
        }
        catch (HttpRequestException failure)
        {
            // A 400 is NOT an empty result. Reading one as absence is how APPARENT='N' looked like a missing capability.
            throw new HorizonsUnavailableException("unreachable",
                $"Request failed: {failure.Message}. A non-200 response is an error, never 'no data'.");
        }

        // Split on CR/LF rather than one escaped literal: an escaped two-character literal here made the whole
        // response a single "line", so every header check failed against a perfectly correct response.
        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        // Horizons reports request errors as a 200 with an error body. Without this the body would be parsed as a
        // response with no header and no vector, and the failure would surface as "header mismatch" - blaming the
        // request's parameters for a problem in the request's shape.
        var errorLine = lines.FirstOrDefault(line => line.StartsWith("API ERROR", StringComparison.OrdinalIgnoreCase)
            || line.Contains("Bad dates", StringComparison.OrdinalIgnoreCase)
            || line.Contains("No ephemeris", StringComparison.OrdinalIgnoreCase));
        if (errorLine is not null)
            throw new HorizonsUnavailableException("bad-request", errorLine.Trim());

        // Header first: if the frame, site, units or time scale are not what we asked for, the numbers below are
        // answering a different question and must not be read.
        var problems = HorizonHeaderAssertions.Check(lines);
        if (problems.Count > 0)
            throw new HorizonsUnavailableException("header-mismatch", string.Join(" | ", problems));

        var vector = ParseFirstVector(lines)
            ?? throw new HorizonsUnavailableException("no-vector",
                "The response contained no vector line. Empty output is an error, not a zero.");

        var (longitude, latitude) = EclipticOfDate.ToLongitudeLatitude(vector, query.InstantTt);
        if (query.Quantity == HorizonsQuantity.Apparent)
            throw new HorizonsUnavailableException("apparent-not-a-reference",
                "Apparent longitude is not available as a reference here: Horizons' APPARENT parameter selects "
                + "atmospheric refraction, not aberration, and ObsEcLon is apparent-only. Apparent is a cross-check "
                + "with a stated offset, never the reference.");

        return new HorizonsValue(longitude, latitude,
            new HorizonsProvenance("JPL Horizons", "1.2", "DE441", $"Body {command}", "Earth (399)",
                ObserverTable: "2", Apparent: false, RequestParameters: uri.Query));
    }

    /// <summary>
    /// Builds the request. START and STOP must differ - Horizons rejects an equal pair with
    /// "Bad dates -- start must be earlier than stop", which arrives as a 200 with an error body rather than a failure
    /// status, so it looks like a successful response containing no vectors.
    /// </summary>
    private static Uri BuildUri(string command, string start, string stop) => new(
        $"{Endpoint}?format=text&COMMAND='{command}'&CENTER='500@399'&MAKE_EPHEM='YES'&EPHEM_TYPE='VECTORS'"
        + $"&START_TIME='{start}'&STOP_TIME='{stop}'&STEP_SIZE='1d'&VEC_TABLE='2'&VEC_CORR='LT'"
        + "&REF_PLANE='ECLIPTIC'&REF_SYSTEM='J2000'&OUT_UNITS='KM-S'");

    /// <summary>Formats a TT instant as a Horizons date, stating the TDB/TT equivalence explicitly.</summary>
    /// <remarks>
    /// TT is passed as the input label because Horizons accepts it and the difference from TDB (milliseconds) is far
    /// below every budget here. Recorded rather than assumed: the header assertion checks the returned epoch scale.
    /// </remarks>
    private static string ToHorizonsDate(TtInstant instant)
    {
        var jd = instant.JulianDay;
        // JD 2451545.0 = 2000-01-01 12:00. Horizons accepts 'JD 2451544.5' directly.
        return $"JD{jd.ToString("F6", CultureInfo.InvariantCulture)}";
    }

    private static (double X, double Y, double Z)? ParseFirstVector(string[] lines)
    {
        foreach (var line in lines)
        {
            var match = VectorLine.Match(line);
            if (!match.Success) continue;

            return (double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
                    double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
                    double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture));
        }

        return null;
    }
}

