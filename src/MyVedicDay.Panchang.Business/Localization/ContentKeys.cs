namespace MyVedicDay.Panchang.Business.Localization;

/// <summary>
/// Content key scheme: <c>{category}.{id}</c>, optionally prefixed by a tradition (<c>{tradition}/{category}.{id}</c>)
/// for regional names. Glossary categories carry explanations; name categories carry display names for values.
/// </summary>
internal static class ContentKeys
{
    public const string Periods = "periods";
    public const string Elements = "elements";
    public const string Terms = "terms";
    public const string Classifications = "classifications";

    /// <summary>Shared tradition names (owned by the Traditions module).</summary>
    public const string Traditions = "traditions";

    /// <summary>Panchang-specific tradition descriptions: which timings each tradition shows.</summary>
    public const string PanchangTraditions = "panchangTraditions";

    public const string Tithi = "tithi";
    public const string Nakshatra = "nakshatra";
    public const string Yoga = "yoga";
    public const string Karana = "karana";
    public const string Weekday = "weekday";

    /// <summary>Term explaining the Choghadiya system; shown only by traditions that display Choghadiya.</summary>
    public const string ChoghadiyaTerm = "choghadiya";

    /// <summary>Display entry and period group name for Choghadiya.</summary>
    public const string ChoghadiyaGroup = "Choghadiya";

    /// <summary>Period group of Muhurta-style windows (Rahu Kalam, Abhijit Muhurta…).</summary>
    public const string MuhurtaGroup = "Muhurta";

    /// <summary>Daily-guidance ratings: label, icon, headline (message) and summary sentence (description).</summary>
    public const string GuidanceRatings = "guidanceRatings";

    /// <summary>Traditional group explanations, keyed <c>{limb}.{group}</c> (e.g. <c>tithi.rikta</c>).</summary>
    public const string GuidanceGroups = "guidanceGroups";

    /// <summary>Daily-guidance sentence templates.</summary>
    public const string GuidanceText = "guidanceText";

    /// <summary>
    /// Note on a favorable period overlapped by a caution or avoid period, keyed by the overlapping period's
    /// classification (<c>Caution</c>, <c>Avoid</c>).
    /// </summary>
    public const string PeriodOverlap = "periodOverlap";
}
