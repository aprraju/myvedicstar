namespace MyVedicDay.Panchang.Calculation.Models;

/// <summary>Time periods derived from sunrise, sunset, weekday and Nakshatra.</summary>
public enum PanchangPeriodKind
{
    BrahmaMuhurta = 1,
    AbhijitMuhurta,
    AmritKalam,
    RahuKalam,
    Yamagandam,
    GulikaKalam,
    DurMuhurtam,
    Varjyam,
    ChoghadiyaAmrit,
    ChoghadiyaShubh,
    ChoghadiyaLabh,
    ChoghadiyaChal,
    ChoghadiyaUdveg,
    ChoghadiyaKaal,
    ChoghadiyaRog,
}

/// <summary>Traditional classification of a time period.</summary>
public enum PeriodNature
{
    Auspicious = 1,
    Inauspicious,
    Neutral,
}

/// <summary>Family of a time period, used to present related periods together.</summary>
public enum PeriodGroup
{
    /// <summary>Muhurta-style windows: Rahu Kalam, Yamagandam, Gulika, Dur Muhurtam, Varjyam, Amrit Kalam, Brahma and Abhijit Muhurta.</summary>
    Muhurta = 1,

    /// <summary>The eight day and eight night Choghadiya segments.</summary>
    Choghadiya,
}
