using MyVedicDay.Astronomy;
using MyVedicDay.Panchang.Calculation.Models;
using NodaTime;

namespace MyVedicDay.Panchang.Calculation.Traditions;

/// <summary>Whether a Muhurta belongs to the daytime (sunrise→sunset) or night (sunset→next sunrise).</summary>
internal enum DayHalf
{
    Day = 1,
    Night,
}

/// <summary>One of the 15 Muhurtas of the day or night half (1-based).</summary>
internal readonly record struct MuhurtaSlot(DayHalf Half, int Number);

/// <summary>
/// Rule tables for a tradition. Values vary between regional Panchangs and must be validated against the
/// reference almanac of the tradition before being relied on.
/// </summary>
internal sealed record PanchangTraditionRules
{
    public required string Key { get; init; }

    public required SunriseDefinition SunriseDefinition { get; init; }

    /// <summary>1-based eighth of the daytime occupied by Rahu Kalam, per weekday.</summary>
    public required IReadOnlyDictionary<IsoDayOfWeek, int> RahuKalamSegments { get; init; }

    /// <summary>1-based eighth of the daytime occupied by Yamagandam, per weekday.</summary>
    public required IReadOnlyDictionary<IsoDayOfWeek, int> YamagandamSegments { get; init; }

    /// <summary>1-based eighth of the daytime occupied by Gulika Kalam, per weekday.</summary>
    public required IReadOnlyDictionary<IsoDayOfWeek, int> GulikaKalamSegments { get; init; }

    /// <summary>Dur Muhurtam slots per weekday.</summary>
    public required IReadOnlyDictionary<IsoDayOfWeek, IReadOnlyList<MuhurtaSlot>> DurMuhurtamSlots { get; init; }

    /// <summary>Weekdays on which Abhijit Muhurat is not considered auspicious.</summary>
    public required IReadOnlySet<IsoDayOfWeek> AbhijitExcludedWeekdays { get; init; }

    /// <summary>Start of Varjyam (Tyajyam) in ghatis from the start of each Nakshatra, indexed by Nakshatra (Ashwini first).</summary>
    public required IReadOnlyList<double> VarjyamStartGhatis { get; init; }

    /// <summary>Start of Amrit Kalam in ghatis from the start of each Nakshatra, indexed by Nakshatra (Ashwini first).</summary>
    public required IReadOnlyList<double> AmritKalamStartGhatis { get; init; }

    /// <summary>Length of Varjyam and Amrit Kalam in ghatis, where a Nakshatra lasts 60 ghatis.</summary>
    public required double NakshatraSubPeriodGhatis { get; init; }

    /// <summary>Cyclic order of the daytime Choghadiya.</summary>
    public required IReadOnlyList<PanchangPeriodKind> ChoghadiyaDaySequence { get; init; }

    /// <summary>Cyclic order of the night-time Choghadiya.</summary>
    public required IReadOnlyList<PanchangPeriodKind> ChoghadiyaNightSequence { get; init; }

    /// <summary>First daytime Choghadiya per weekday.</summary>
    public required IReadOnlyDictionary<IsoDayOfWeek, PanchangPeriodKind> ChoghadiyaDayStart { get; init; }

    /// <summary>First night-time Choghadiya per weekday.</summary>
    public required IReadOnlyDictionary<IsoDayOfWeek, PanchangPeriodKind> ChoghadiyaNightStart { get; init; }
}
