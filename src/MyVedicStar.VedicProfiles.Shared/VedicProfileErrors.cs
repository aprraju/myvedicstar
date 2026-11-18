using MyVedicStar.Shared.Exceptions;

namespace MyVedicStar.VedicProfiles.Shared;

public static class VedicProfileErrors
{
    public const string InvalidDetails = "Vedic profile details are invalid or exceed technical limits.";
    public const string InvalidTimes = "Choose valid second-precision local times for the selected birth time mode.";
    /// <summary>Distinct from <see cref="InvalidTimes"/> so a client can say the window must not wrap past midnight
    /// rather than reporting a generic malformed-input error. Owner decision #16: reject, never clamp.</summary>
    public const string BirthWindowCrossesMidnight =
        "An approximate birth time window must not cross midnight. Enter the start time before the end time, on the same day.";
    public const string InvalidDate = "Date of birth must not be in the future.";
    public const string InvalidLocation = "Choose valid coordinates and an IANA timezone.";
    public const string InvalidRelationship = "Choose a supported relationship; custom relationship is required only for Other.";
    public const string InvalidRevision = "A positive revision is required.";
    public const string NotFound = "Vedic profile was not found.";
    public const string StaleRevision = "The Vedic profile has changed. Reload it and try again.";
    public const string DuplicateSelf = "An account may have only one Self Vedic profile.";
}

/// <summary>Map centrally to HTTP 409; no persistence details or personal input are included.</summary>
public sealed class VedicProfileConflictException(string publicMessage) : MyVedicStarException(publicMessage);
