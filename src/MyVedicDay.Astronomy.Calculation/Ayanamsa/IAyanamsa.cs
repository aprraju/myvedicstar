namespace MyVedicDay.Astronomy;

/// <summary>
/// Precession offset between the tropical and sidereal zodiacs. Traditions differ in the ayanamsa they use,
/// which shifts every Nakshatra and Yoga transition.
/// </summary>
public interface IAyanamsa
{
    /// <summary>Ayanamsa in degrees, to subtract from a tropical (mean equinox of date) longitude.</summary>
    double GetDegrees(double julianDayUt);
}
