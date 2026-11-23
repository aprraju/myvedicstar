using MyVedicStar.Kundali.Shared;
using MyVedicStar.Shared.Exceptions;

namespace MyVedicStar.Kundali.Business;

/// <summary>
/// KUN-17 (api#57) request admission. A report request is refused here, before any store or renderer is touched, so an
/// invalid request costs nothing and cannot half-create a report.
/// </summary>
/// <remarks>
/// <para>
/// Two closed vocabularies are enforced: the variant, and every requested section id. A section that is not defined by
/// <see cref="KundaliReportSections"/> is <b>unknown</b>; one that is defined but not rendered by the requested variant
/// is <b>unsupported</b>. Those are different answers and are kept different, because collapsing them would tell a
/// caller with a typo to stop using a section that exists.
/// </para>
/// <para>
/// The received ids are never echoed into the error message as a code: the message names the offending id inside a
/// sentence, so a caller cannot make the server render arbitrary text by sending it as a section id.
/// </para>
/// </remarks>
public static class KundaliReportRequestValidator
{
    /// <summary>Validates and normalises a request, returning the sections in contract order with duplicates removed.</summary>
    /// <exception cref="ValidationException">Variant unknown, selection empty, or any section unknown/unsupported.</exception>
    public static KundaliReportRequest Validate(KundaliReportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!KundaliReportVariants.All.Contains(request.Variant, StringComparer.Ordinal))
            throw new ValidationException(KundaliErrors.InvalidGenerationRequest);

        var sections = request.Sections ?? [];
        if (sections.Count == 0)
            throw new ValidationException(KundaliErrors.EmptyReportSelection);

        foreach (var section in sections)
        {
            if (!KundaliReportSections.IsDefined(section))
                throw new ValidationException($"{KundaliErrors.UnknownReportSection} Section: '{section}'.");
            if (!KundaliReportSections.IsSupported(request.Variant, section))
                throw new ValidationException($"{KundaliErrors.UnsupportedReportSection} Section: '{section}'.");
        }

        // Normalised to the contract's order and de-duplicated: the stored report must not depend on the order the
        // caller happened to send, or two identical requests produce documents that compare unequal.
        var canonical = KundaliReportSections.For(request.Variant)
            .Where(section => sections.Contains(section, StringComparer.Ordinal))
            .ToArray();

        return request with { Sections = canonical };
    }
}
