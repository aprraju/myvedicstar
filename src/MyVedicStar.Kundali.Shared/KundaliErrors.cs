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

    /// <summary>KUN-17: a report request named a section this contract does not define. The message names the section id
    /// so a caller can correct it, and it never echoes free text the caller supplied as a code.</summary>
    public const string UnknownReportSection = "The report request names a section that is not defined.";

    /// <summary>KUN-17: the section is defined but this variant does not render it - a distinct fact from "unknown", and
    /// refused rather than silently dropped, because a short report that quietly omits a requested section is a
    /// document the caller cannot trust.</summary>
    public const string UnsupportedReportSection = "The report variant does not render the requested section.";

    /// <summary>KUN-17: no sections were requested. An empty selection is a caller error, never "everything".</summary>
    public const string EmptyReportSelection = "The report request must name at least one section.";

    /// <summary>KUN-17: the report does not exist, is not owned by the caller, or its profile was deleted. One message
    /// for all three so existence is not leaked to a caller who does not own the report.</summary>
    public const string ReportNotFound = "The report was not found.";

    /// <summary>The profile was edited while a result was being produced; the result was discarded rather than shown
    /// against inputs it no longer describes (KUN-22).</summary>
    public const string ProfileChanged = "The profile changed while the Kundali was being prepared. Reload to see current results.";

    /// <summary>A background result arrived for a revision the profile has already left; it is discarded (KUN-22).</summary>
    public const string ObsoleteResult = "This result was computed for an earlier profile revision and was discarded.";
    /// <summary>KUN-13: an alias subscription named an event type or channel this build does not produce.</summary>
    public const string InvalidAlertSubscription =
        "The alert subscription is invalid: unknown event type or unsupported delivery channel.";

    /// <summary>KUN-13: the requested timezone is not a resolvable IANA zone.</summary>
    public const string InvalidTimezone = "The timezone is invalid or unsupported.";
}

/// <summary>Map centrally to HTTP 409; carries no persistence detail and no personal input.</summary>
public sealed class KundaliConflictException(string publicMessage) : MyVedicStarException(publicMessage);

/// <summary>The engine could not produce a supported result for a capability it was asked for. This is a refusal,
/// never an approximation (ADR-0016 section 2).</summary>
public sealed class KundaliUnsupportedCapabilityException(string publicMessage) : MyVedicStarException(publicMessage);