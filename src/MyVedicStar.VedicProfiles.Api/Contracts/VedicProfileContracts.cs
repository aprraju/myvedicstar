namespace MyVedicStar.API.Contracts;

/// <summary>Create or replace payload. Account id, resource id and revision are never caller-supplied here.</summary>
public sealed record VedicProfileRequest(
    string? Name, DateOnly DateOfBirth, string BirthTimeMode,
    string? BirthTime, string? BirthTimeFrom, string? BirthTimeTo,
    string? TimeAccuracyNote, string LocationName, decimal Latitude, decimal Longitude,
    string TimezoneId, string Gender, string Occupation, string Relationship,
    string? CustomRelationship);

/// <summary>Replace payload adds the current revision required for optimistic concurrency.</summary>
public sealed record ReplaceVedicProfileRequest(
    string? Name, DateOnly DateOfBirth, string BirthTimeMode,
    string? BirthTime, string? BirthTimeFrom, string? BirthTimeTo,
    string? TimeAccuracyNote, string LocationName, decimal Latitude, decimal Longitude,
    string TimezoneId, string Gender, string Occupation, string Relationship,
    string? CustomRelationship, long Revision);

public sealed record VedicProfileSummaryResponse(Guid Id, string Name, string Relationship,
    string? CustomRelationship, long Revision)
{
    public static VedicProfileSummaryResponse From(MyVedicStar.VedicProfiles.Shared.VedicProfileSummary value) =>
        new(value.Id, value.Name, value.Relationship.ToString(), value.CustomRelationship, value.Revision);
}

public sealed record VedicProfileResponse(
    Guid Id, string Name, DateOnly DateOfBirth, string BirthTimeMode,
    string? BirthTime, string? BirthTimeFrom, string? BirthTimeTo,
    string? TimeAccuracyNote, string LocationName, decimal Latitude, decimal Longitude,
    string TimezoneId, string Gender, string Occupation, string Relationship,
    string? CustomRelationship, long Revision)
{
    public static VedicProfileResponse From(MyVedicStar.VedicProfiles.Shared.VedicProfile value) =>
        new(value.Id, value.Details.Name ?? "", value.Details.DateOfBirth, value.Details.BirthTimeMode.ToString(),
            Format(value.Details.BirthTime), Format(value.Details.BirthTimeFrom), Format(value.Details.BirthTimeTo),
            value.Details.TimeAccuracyNote, value.Details.LocationName, value.Details.Latitude, value.Details.Longitude,
            value.Details.TimezoneId, value.Details.Gender, value.Details.Occupation,
            value.Details.Relationship.ToString(), value.Details.CustomRelationship, value.Revision);

    private static string? Format(TimeOnly? value) => value?.ToString("HH:mm:ss");
}
