namespace MyVedicDay.Astronomy;

/// <summary>
/// How sunrise and sunset are defined. Traditions differ here, and every sunrise-based period
/// (Rahu Kalam, Muhurtas, the Tithi prevailing at sunrise) inherits the choice.
/// </summary>
public enum SunriseDefinition
{
    /// <summary>Upper limb on the horizon with standard refraction (−0°50′). Matches civil almanacs.</summary>
    UpperLimbWithRefraction = 1,

    /// <summary>Centre of the disc on the horizon with standard refraction (−0°34′).</summary>
    DiscCentreWithRefraction,

    /// <summary>Centre of the disc on the geometric horizon, no refraction. Used by some traditional almanacs.</summary>
    DiscCentreWithoutRefraction,
}
