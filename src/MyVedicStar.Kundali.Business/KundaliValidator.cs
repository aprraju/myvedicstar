using MyVedicStar.Kundali.Shared;
using MyVedicStar.Shared.Exceptions;
using MyVedicStar.VedicProfiles.Shared;

namespace MyVedicStar.Kundali.Business;

/// <summary>Business validation for Kundali inputs. The capability boundary from ADR-0016 is enforced here, so an
/// unsupported birth date or settings value is refused with a 400 before any engine work or persistence happens.</summary>
internal sealed class KundaliValidator(TimeProvider clock, KundaliEngineVersions versions)
{
    private static readonly string[] ChartStyles = ["North", "South", "East"];

    public void ValidateSnapshot(KundaliInputSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!KundaliCapabilities.IsSupportedBirthDate(snapshot.DateOfBirth))
            throw new ValidationException(KundaliErrors.InvalidBirthDate);
        if (string.IsNullOrWhiteSpace(snapshot.TimezoneId) || !TimeZoneExists(snapshot.TimezoneId))
            throw new ValidationException(KundaliErrors.InvalidConfig);
        if (!Enum.IsDefined(snapshot.BirthTimeMode) || snapshot.ProfileRevision <= 0)
            throw new ValidationException(KundaliErrors.InvalidGenerationRequest);
        if (snapshot.Latitude is < -90 or > 90 || snapshot.Longitude is < -180 or > 180)
            throw new ValidationException(KundaliErrors.InvalidConfig);
    }

    /// <summary>A section payload bound. Without this an unbounded write could exhaust storage or time out a
    /// transaction midway and leave a revision looking complete when it is not.</summary>
    public void ValidateSectionPayload(string section, string payloadJson)
    {
        if (!KundaliSections.All.Contains(section, StringComparer.Ordinal))
            throw new ValidationException(KundaliErrors.InvalidGenerationRequest);
        if (payloadJson is null || System.Text.Encoding.UTF8.GetByteCount(payloadJson) > KundaliLimits.SectionPayloadBytes)
            throw new ValidationException(KundaliErrors.InvalidGenerationRequest);
    }

    public void ValidateChartStyle(string chartStyle)
    {
        // Chart style is presentation-only, but an unknown value would render wrongly; the three Indian styles are
        // the supported set, matching the approved defaults.
        if (!ChartStyles.Contains(chartStyle, StringComparer.Ordinal))
            throw new ValidationException(KundaliErrors.InvalidConfig);
    }

    /// <summary>Provenance must be fully populated: a blank version would make every stored result unfalsifiable.</summary>
    public void ValidateProvenance(KundaliProvenance provenance)
    {
        if (string.IsNullOrWhiteSpace(provenance.EngineVersion) || string.IsNullOrWhiteSpace(provenance.EphemerisVersion)
            || string.IsNullOrWhiteSpace(provenance.RuleSetVersion) || string.IsNullOrWhiteSpace(provenance.TimezoneDataVersion)
            || string.IsNullOrWhiteSpace(provenance.Ayanamsa))
            throw new ValidationException(KundaliErrors.InvalidConfig);
    }

    private static bool TimeZoneExists(string timezoneId)
    {
        try { _ = TimeZoneInfo.FindSystemTimeZoneById(timezoneId); return true; }
        catch (TimeZoneNotFoundException) { return false; }
        catch (InvalidTimeZoneException) { return false; }
    }
}

/// <summary>Engine, ephemeris, timezone-data and rule-set versions. Every persisted result carries these; a change is
/// a new version, never an in-place edit, so past results stay explainable (ADR-0016 Consequences).</summary>
public sealed record KundaliEngineVersions(
    string Engine, string Ephemeris, string RuleSet, string TimezoneData)
{
    /// <summary>Placeholder versions until the KUN-20 engine lands; deliberately explicit so a preview build cannot
    /// be mistaken for a released calculation.</summary>
    public sealed class Defaults : KundaliEngineVersions
    {
        public static readonly Defaults Value = new();
        private Defaults() : base("kundali-foundation-0", "none", "foundation-0", "host") { }
    }
}
