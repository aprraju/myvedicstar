namespace MyVedicStar.VedicProfiles.Shared;

public enum BirthTimeMode { Known, Approximate, Unknown }
public enum Relationship { Self, Father, Mother, Wife, Husband, Son, Daughter, Other }

/// <summary>Local birth details. All times are second-precision wall-clock values without conversion.
/// Known requires all three times equal; Approximate requires both endpoints and null BirthTime;
/// Unknown requires all times null. Gender is intentionally free text pending owner-approved values.</summary>
public sealed record VedicProfileDetails(
    string? Name, DateOnly DateOfBirth, BirthTimeMode BirthTimeMode,
    TimeOnly? BirthTime, TimeOnly? BirthTimeFrom, TimeOnly? BirthTimeTo,
    string? TimeAccuracyNote, string LocationName, decimal Latitude, decimal Longitude,
    string TimezoneId, string Gender, string Occupation, Relationship Relationship,
    string? CustomRelationship);

/// <summary>Immutable owned resource; account identifiers are deliberately not exposed.</summary>
public sealed record VedicProfile(Guid Id, VedicProfileDetails Details, long Revision);

public sealed record VedicProfileSummary(Guid Id, string Name, Relationship Relationship,
    string? CustomRelationship, long Revision);

/// <summary>Technical storage/request bounds, not additional business requiredness rules.</summary>
public static class VedicProfileLimits
{
    public const int Name = 256;
    public const int LocationName = 512;
    public const int TimezoneId = 128;
    public const int Gender = 128;
    public const int Occupation = 256;
    public const int CustomRelationship = 256;
    public const int TimeAccuracyNote = 2000;
}
