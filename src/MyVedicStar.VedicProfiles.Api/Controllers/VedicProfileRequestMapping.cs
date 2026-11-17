using MyVedicStar.API.Contracts;
using MyVedicStar.VedicProfiles.Shared;

namespace MyVedicStar.API.Controllers;

/// <summary>Wire-to-domain parsing only. Unparseable enum or time values surface as a validation failure
/// (400) from the Business validator rather than a silently coerced value.</summary>
internal static class VedicProfileRequestMapping
{
    public static VedicProfileDetails ToDetails(this VedicProfileRequest request) => Build(request.Name,
        request.DateOfBirth, request.BirthTimeMode, request.BirthTime, request.BirthTimeFrom, request.BirthTimeTo,
        request.TimeAccuracyNote, request.LocationName, request.Latitude, request.Longitude, request.TimezoneId,
        request.Gender, request.Occupation, request.Relationship, request.CustomRelationship);

    public static VedicProfileDetails ToDetails(this ReplaceVedicProfileRequest request) => Build(request.Name,
        request.DateOfBirth, request.BirthTimeMode, request.BirthTime, request.BirthTimeFrom, request.BirthTimeTo,
        request.TimeAccuracyNote, request.LocationName, request.Latitude, request.Longitude, request.TimezoneId,
        request.Gender, request.Occupation, request.Relationship, request.CustomRelationship);

    private static VedicProfileDetails Build(string? name, DateOnly dateOfBirth, string birthTimeMode,
        string? birthTime, string? birthTimeFrom, string? birthTimeTo, string? timeAccuracyNote,
        string locationName, decimal latitude, decimal longitude, string timezoneId, string gender,
        string? occupation, string? relationship, string? customRelationship) =>
        new(name, dateOfBirth, ParseMode(birthTimeMode), ParseTime(birthTime), ParseTime(birthTimeFrom),
            ParseTime(birthTimeTo), timeAccuracyNote, locationName, latitude, longitude, timezoneId, gender,
            occupation, ParseRelationship(relationship), customRelationship);

    private static BirthTimeMode ParseMode(string value) => Enum.TryParse<BirthTimeMode>(value, true, out var mode) ? mode : (BirthTimeMode)(-1);
    // Relationship is optional on create: an omitted value means the profile is the
    // account owner's own profile. A supplied but unrecognised value is still rejected.
    private static Relationship ParseRelationship(string? value) =>
        string.IsNullOrWhiteSpace(value) ? Relationship.Self
        : Enum.TryParse<Relationship>(value, true, out var relationship) ? relationship
        : (Relationship)(-1);
    // TryParseExact throws on a null input, so an omitted time would become an opaque 500.
    // Absent (null/blank) means "no time supplied"; a malformed value still fails validation.
    private static TimeOnly? ParseTime(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null
        : TimeOnly.TryParseExact(value, "HH:mm:ss", out var time) ? time
        : throw new MyVedicStar.Shared.Exceptions.ValidationException(MyVedicStar.VedicProfiles.Shared.VedicProfileErrors.InvalidTimes);
}
