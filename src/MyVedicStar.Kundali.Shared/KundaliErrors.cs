using MyVedicStar.Shared.Exceptions;

namespace MyVedicStar.Kundali.Shared;

public static class KundaliErrors
{
    public const string NotFound = "Kundali was not found.";
    public const string ProfileNotFound = "Vedic profile was not found.";
    public const string AlreadyRunning = "A Kundali generation is already running for this profile.";
    public const string StaleSnapshot = "The profile's birth details changed. Regenerate the Kundali.";
    public const string InvalidConfig = "The calculation settings are invalid or unsupported.";
    public const string InvalidBirthDate = "The birth date is outside the supported range for Kundali calculations.";
    public const string EngineUnavailable = "The calculation engine is temporarily unavailable. Try again.";
    public const string InvalidGenerationRequest = "The generation request is invalid.";
}

/// <summary>Map centrally to HTTP 409; carries no persistence detail and no personal input.</summary>
public sealed class KundaliConflictException(string publicMessage) : MyVedicStarException(publicMessage);

/// <summary>The engine could not produce a supported result for a capability it was asked for. This is a refusal,
/// never an approximation (ADR-0016 section 2).</summary>
public sealed class KundaliUnsupportedCapabilityException(string publicMessage) : MyVedicStarException(publicMessage);
