namespace MyVedicDay.Panchang.Business.Models;

/// <summary>
/// How a time period should be presented to users, from most to least favorable.
/// Assigned per period kind in configuration (<c>Panchang:PeriodClassifications</c>).
/// </summary>
public enum PeriodClassification
{
    HighlyFavorable = 1,
    Favorable,
    Neutral,
    Caution,
    Avoid,
}
