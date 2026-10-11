using Microsoft.Extensions.Options;
using MyVedicDay.Panchang.Calculation.Models;

namespace MyVedicDay.Panchang.Business.Configuration;

/// <summary>
/// Validates that every period kind has exactly one known classification.
/// </summary>
internal sealed class PanchangOptionsValidator : IValidateOptions<PanchangOptions>
{
    public ValidateOptionsResult Validate(string? name, PanchangOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();
        var kinds = Enum.GetNames<PanchangPeriodKind>();

        var unknown = options.PeriodClassifications.Keys.Where(key => !kinds.Contains(key, StringComparer.OrdinalIgnoreCase)).ToArray();
        if (unknown.Length > 0)
        {
            failures.Add($"{nameof(PanchangOptions.PeriodClassifications)} has unknown period kinds: {string.Join(", ", unknown)}.");
        }

        var missing = kinds.Where(kind => !options.PeriodClassifications.ContainsKey(kind)).ToArray();
        if (missing.Length > 0)
        {
            failures.Add($"{nameof(PanchangOptions.PeriodClassifications)} is missing: {string.Join(", ", missing)}.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
