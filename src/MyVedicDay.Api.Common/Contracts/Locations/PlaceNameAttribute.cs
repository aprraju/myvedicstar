using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace MyVedicDay.Api.Contracts.Locations;

/// <summary>
/// Validates a place name: letters in any script (including combining marks, e.g. "São Paulo", "హైదరాబాదు"), digits,
/// spaces and <c>. , ' ( ) -</c>, up to 120 characters.
/// </summary>
/// <remarks>
/// Deliberately not a <see cref="RegularExpressionAttribute"/>: that would publish the pattern in OpenAPI, and
/// <c>\p{L}</c> is not understood by browser JavaScript regular expressions without the <c>u</c> flag, so Swagger UI
/// would reject valid input client-side.
/// </remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed partial class PlaceNameAttribute : ValidationAttribute
{
    public const int MaxLength = 120;

    public PlaceNameAttribute()
        : base("Place may contain letters, digits, spaces and . , ' ( ) - only.")
    {
    }

    public override bool IsValid(object? value) => value is null || (value is string text && Pattern().IsMatch(text));

    [GeneratedRegex(@"^[\p{L}\p{M}0-9 .,'()\-]{1,120}$")]
    private static partial Regex Pattern();
}
