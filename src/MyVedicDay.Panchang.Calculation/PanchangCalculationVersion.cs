namespace MyVedicDay.Panchang.Calculation;

/// <summary>
/// Version of the calculation algorithms and rule tables. Part of every cache key, so changing an algorithm
/// or rule table never serves results calculated by the previous version.
/// </summary>
public static class PanchangCalculationVersion
{
    public const string Current = "1.2.0";
}
