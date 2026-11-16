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
        string occupation, string relationship, string? customRelationship) =>
        new(name, dateOfBirth, ParseMode(birthTimeMode), ParseTime(birthTime), ParseTime(birthTimeFrom),
            ParseTime(birthTimeTo), timeAccuracyNote, locationName, latitude, longitude, timezoneId, gender,
            occupation, ParseRelationship(relationship), customRelationship);

    private static BirthTimeMode ParseMode(string value) => Enum.TryParse<BirthTimeMode>(value, true, out var mode) ? mode : (BirthTimeMode)(-1);
    private static Relationship ParseRelationship(string value) => Enum.TryParse<Relationship>(value, true, out var relationship) ? relationship : (Relationship)(-1);
    private static TimeOnly? ParseTime(string? value) => TimeOnly.TryParseExact(value, "HH:mm:ss", out var time) ? time : null;
}
