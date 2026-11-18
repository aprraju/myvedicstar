using MyVedicStar.Astronomy.Core;

namespace MyVedicDay.Astronomy;

/// <summary>
/// Turns a provider's raw graha positions into what the product is allowed to publish.
/// </summary>
/// <remarks>
/// <para><b>This is where the validation gate lives.</b> A provider can compute all nine grahas; publication additionally
/// requires a checked-in independent reference for that graha. Without one the longitude is withheld with
/// <see cref="GrahaReasonCodes.UnvalidatedReference"/> and the computed value is not returned at all - publishing it
/// beside an invented tolerance is the failure mode this type exists to prevent.</para>
/// <para>Retrograde flags and mean daily motion are still published for a withheld body: they follow from its motion
/// rather than from an unvalidated absolute position, and suppressing them would throw away a fact the engine does
/// have. The convention text says so.</para>
/// </remarks>
public sealed class GrahaPositionsResolver(IGrahaPositionsProvider provider)
{
    private readonly IGrahaPositionsProvider _provider = provider ?? throw new ArgumentNullException(nameof(provider));

    /// <summary>
    /// Builds the publishable result.
    /// </summary>
    /// <param name="julianDayUt">Instant, Julian Day UT.</param>
    /// <param name="ayanamsaDegrees">Ayanamsa of date. The caller resolves it; the resolver never picks one.</param>
    /// <param name="conventions">Frame and ayanamsa description, supplied by the caller so the naming has one origin.</param>
    /// <param name="references">Checked-in independent references. An empty set withholds every longitude.</param>
    public GrahaPositionsResult Resolve(double julianDayUt, double ayanamsaDegrees, GrahaConventions conventions,
        IEnumerable<PlanetaryReference> references)
    {
        ArgumentNullException.ThrowIfNull(conventions);
        ArgumentNullException.ThrowIfNull(references);

        var tolerances = PlanetaryReferences.PerBodyToleranceArcseconds(references);
        var raw = _provider.GetSiderealPositions(julianDayUt, ayanamsaDegrees);

        var positions = raw.Select(position => Publish(position, tolerances)).ToArray();
        return new GrahaPositionsResult(positions, conventions, BuildEnvelope(tolerances));
    }

    /// <summary>The envelope for the current reference set, without computing anything.</summary>
    public GrahaAccuracyEnvelope BuildEnvelope(IEnumerable<PlanetaryReference> references) =>
        BuildEnvelope(PlanetaryReferences.PerBodyToleranceArcseconds(references));

    private static GrahaPosition Publish(GrahaPosition position, IReadOnlyDictionary<string, double> tolerances)
    {
        if (position.SiderealLongitude is null)
            return position;

        if (tolerances.ContainsKey(position.Graha.ToString()))
            return position;

        // Computable, but unproven: withhold the number, keep the reason, and say exactly what is missing.
        return position with
        {
            SiderealLongitude = null,
            Sign = null,
            DegreeWithinSign = null,
            Status = GrahaReasonCodes.UnvalidatedReference,
            Reason = $"No independent reference value is checked in for {position.Graha}, so no tolerance can be defended."
        };
    }

    private static GrahaAccuracyEnvelope BuildEnvelope(IReadOnlyDictionary<string, double> tolerances)
    {
        var budget = ParityGate.PlanetaryToleranceArcseconds.ToString("0.###",
            System.Globalization.CultureInfo.InvariantCulture);
        var basis = tolerances.Count == 0
            ? "No planetary reference value is checked in. Sun and Moon tolerances are inherited from the shipped "
              + "parity fixtures; no graha tolerance is asserted for Mars, Mercury, Jupiter, Venus, Saturn, Rahu or Ketu. "
              + "The 1 arcsecond figure below is the provider-vs-provider parity budget, not an accuracy claim."
            : "Per-body tolerances are the published precision of each checked-in independent reference in "
              + "astronomy/planetary-references.json; a graha absent from that file has no asserted tolerance and its "
              + $"longitude is withheld. The {budget} arcsecond figure below is the provider-vs-provider parity budget, "
              + "not an accuracy claim.";

        return new GrahaAccuracyEnvelope(new Dictionary<string, double>(tolerances, StringComparer.Ordinal), budget, basis);
    }
}
