using MyVedicStar.Astronomy.Core;

namespace MyVedicDay.Astronomy;

/// <summary>
/// Sidereal graha positions from the rich engine (<see cref="IEphemerisEngine"/>), for the <c>spice</c> and <c>compare</c>
/// providers.
/// </summary>
/// <remarks>
/// <para><b>No astronomy in this type.</b> Same property as <see cref="AstronomyEngineEphemerisAdapter"/>: every number
/// comes from <see cref="IEphemerisEngine"/>, so replacing the engine changes every graha. A test asserts that.</para>
/// <para><b>Support is asked for, not assumed.</b> The adapter maps the engine's own supported-body set onto the nine
/// grahas and marks anything the engine does not claim as <c>not-implemented</c> with the engine's reason, rather than
/// emitting a value the engine did not promise.</para>
/// <para><b>Retrograde is a derivative of the same quantity</b> the position came from, computed here rather than asked
/// of the engine, so it cannot disagree with the longitude beside it.</para>
/// </remarks>
internal sealed class AstronomyEngineGrahaPositionsAdapter : IGrahaPositionsProvider
{
    private const double RetrogradeStepDays = 0.5;

    private static readonly IReadOnlyDictionary<Graha, double> MeanDailyMotion = new Dictionary<Graha, double>
    {
        [Graha.Sun] = 0.985647,
        [Graha.Moon] = 13.176396,
        [Graha.Mars] = 0.524021,
        [Graha.Mercury] = 4.092334,
        [Graha.Jupiter] = 0.083086,
        [Graha.Venus] = 1.602130,
        [Graha.Saturn] = 0.033444,
        [Graha.Rahu] = -0.052953,
        [Graha.Ketu] = -0.052953,
    };

    private static readonly IReadOnlyDictionary<Graha, CelestialBody> Bodies = new Dictionary<Graha, CelestialBody>
    {
        [Graha.Sun] = CelestialBody.Sun,
        [Graha.Moon] = CelestialBody.Moon,
        [Graha.Mars] = CelestialBody.Mars,
        [Graha.Mercury] = CelestialBody.Mercury,
        [Graha.Jupiter] = CelestialBody.Jupiter,
        [Graha.Venus] = CelestialBody.Venus,
        [Graha.Saturn] = CelestialBody.Saturn,
        [Graha.Rahu] = CelestialBody.MeanNode,
    };

    private readonly IEphemerisEngine _engine;

    public AstronomyEngineGrahaPositionsAdapter(IEphemerisEngine engine) =>
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));

    public GrahaSupport GetSupport()
    {
        var supported = AstronomyCapabilities.SupportedBodies;
        return new(Enum.GetValues<Graha>().Select(graha =>
        {
            // Ketu is the descending node: defined, not separately listed by the engine.
            var body = graha == Graha.Ketu ? CelestialBody.MeanNode : Bodies[graha];
            var computable = supported.Contains(body);
            return new GrahaSupportEntry(graha, computable,
                computable ? null : $"{GrahaReasonCodes.NotImplemented}: the engine does not cover {body}.");
        }).ToArray());
    }

    public IReadOnlyList<GrahaPosition> GetSiderealPositions(double julianDayUt, double ayanamsaDegrees)
    {
        var support = GetSupport();
        return Enum.GetValues<Graha>().Select(graha =>
        {
            var entry = support.Of(graha);
            if (!entry.Computable)
                return new GrahaPosition(graha, null, null, null, null, MeanDailyMotion[graha],
                    GrahaReasonCodes.NotImplemented, entry.Reason);

            var tropical = TropicalLongitude(graha, julianDayUt);
            var sidereal = AngleMath.NormalizeDegrees(tropical - ayanamsaDegrees);
            var sign = (int)Math.Floor(sidereal / 30d) % 12;
            return new GrahaPosition(graha, sidereal, sign, sidereal - (sign * 30d),
                IsRetrograde(graha, julianDayUt), MeanDailyMotion[graha], GrahaReasonCodes.Validated, null);
        }).ToArray();
    }

    internal double TropicalLongitude(Graha graha, double julianDayUt) => graha switch
    {
        Graha.Ketu => AngleMath.NormalizeDegrees(
            _engine.GetTropicalLongitude(CelestialBody.MeanNode, julianDayUt) + 180d),
        _ => _engine.GetTropicalLongitude(Bodies[graha], julianDayUt),
    };

    internal bool IsRetrograde(Graha graha, double julianDayUt)
    {
        if (graha == Graha.Sun)
            return false;
        if (graha is Graha.Rahu or Graha.Ketu)
            return true;

        var derivative = AngleMath.NormalizeSignedDegrees(
            TropicalLongitude(graha, julianDayUt + RetrogradeStepDays)
            - TropicalLongitude(graha, julianDayUt - RetrogradeStepDays));
        return derivative < 0;
    }
}
