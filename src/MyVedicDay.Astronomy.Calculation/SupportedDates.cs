namespace MyVedicDay.Astronomy;

/// <summary>
/// Years for which the ephemeris and ΔT model are verified. A property of the algorithms, not a deployment setting:
/// every module validates requested dates against it.
/// </summary>
public static class SupportedDates
{
    public const int MinYear = 1900;
    public const int MaxYear = 2100;
}
