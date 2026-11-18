using MyVedicStar.Shared.Exceptions;
using MyVedicStar.VedicProfiles.Shared;

namespace MyVedicStar.VedicProfiles.Business;

internal sealed class VedicProfileValidator(TimeProvider clock)
{
    public void Validate(VedicProfileDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        if (!RequiredText(details.Name, VedicProfileLimits.Name)
            || !RequiredText(details.LocationName, VedicProfileLimits.LocationName)
            || !RequiredText(details.Gender, VedicProfileLimits.Gender)
            || !OptionalText(details.Occupation, VedicProfileLimits.Occupation)
            || !OptionalText(details.TimeAccuracyNote, VedicProfileLimits.TimeAccuracyNote))
            throw new ValidationException(VedicProfileErrors.InvalidDetails);

        if (details.DateOfBirth > DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime))
            throw new ValidationException(VedicProfileErrors.InvalidDate);
        ValidateTimes(details);
        ValidateRelationship(details);
        ValidateLocation(details);
    }

    private static void ValidateTimes(VedicProfileDetails details)
    {
        var valid = Enum.IsDefined(details.BirthTimeMode)
            && SecondPrecision(details.BirthTime) && SecondPrecision(details.BirthTimeFrom) && SecondPrecision(details.BirthTimeTo)
            && details.BirthTimeMode switch
            {
                BirthTimeMode.Known => details.BirthTime.HasValue
                    && details.BirthTimeFrom == details.BirthTime && details.BirthTimeTo == details.BirthTime,
                // A window must run strictly forward on the clock. Equal bounds are rejected too: a zero-length
                // "approximate" window is an exact time pretending to be uncertain, and the user should pick Exact.
                //
                // Deliberately NOT DST-aware. The validator holds TimeOnly pairs with no date, so it cannot know
                // which day the window falls on and cannot compute elapsed real time; a spring-forward night can
                // legitimately have a 23:30-00:30 window worth zero elapsed minutes. Encoding a date-free
                // approximation of that would be wrong-but-plausible, which is worse than an honest simple rule.
                // The consequence is that such a window is rejected as wrapping - over-strict but explainable.
                // Revisit when the snapshot carries a birth date and real elapsed-time semantics are possible.
                BirthTimeMode.Approximate => details.BirthTime is null
                    && details.BirthTimeFrom.HasValue && details.BirthTimeTo.HasValue
                    && details.BirthTimeTo > details.BirthTimeFrom,
                BirthTimeMode.Unknown => details.BirthTime is null && details.BirthTimeFrom is null && details.BirthTimeTo is null,
                _ => false
            };

        if (!valid)
        {
            // A structurally well-formed approximate window that does not run forward is a specific, correctable
            // mistake, so it gets its own message. Everything else stays the generic invalid-times error so the
            // client is not handed a misleadingly precise diagnosis for a different problem.
            if (details.BirthTimeMode == BirthTimeMode.Approximate
                && details.BirthTime is null
                && SecondPrecision(details.BirthTimeFrom) && SecondPrecision(details.BirthTimeTo)
                && details.BirthTimeFrom.HasValue && details.BirthTimeTo.HasValue
                && details.BirthTimeTo <= details.BirthTimeFrom)
                throw new ValidationException(VedicProfileErrors.BirthWindowCrossesMidnight);

            throw new ValidationException(VedicProfileErrors.InvalidTimes);
        }
    }

    private static void ValidateRelationship(VedicProfileDetails details)
    {
        if (!Enum.IsDefined(details.Relationship)
            || (details.Relationship == Relationship.Other
                ? !RequiredText(details.CustomRelationship, VedicProfileLimits.CustomRelationship)
                : details.CustomRelationship is not null))
            throw new ValidationException(VedicProfileErrors.InvalidRelationship);
    }

    private static void ValidateLocation(VedicProfileDetails details)
    {
        var timezone = details.TimezoneId;
        if (details.Latitude is < -90 or > 90 || details.Longitude is < -180 or > 180
            || !RequiredText(timezone, VedicProfileLimits.TimezoneId)
            || (timezone != "UTC" && !TimeZoneInfo.TryConvertIanaIdToWindowsId(timezone, out _)))
            throw new ValidationException(VedicProfileErrors.InvalidLocation);
        try { _ = TimeZoneInfo.FindSystemTimeZoneById(timezone); }
        catch (TimeZoneNotFoundException) { throw new ValidationException(VedicProfileErrors.InvalidLocation); }
        catch (InvalidTimeZoneException) { throw new ValidationException(VedicProfileErrors.InvalidLocation); }
    }

    private static bool SecondPrecision(TimeOnly? value) => value is null || value.Value.Ticks % TimeSpan.TicksPerSecond == 0;
    private static bool RequiredText(string? value, int max) => !string.IsNullOrWhiteSpace(value) && OptionalText(value, max);
    private static bool OptionalText(string? value, int max) => value is null
        || (value.Length <= max && !value.Any(char.IsControl));
}
