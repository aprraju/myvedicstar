using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace MyVedicDay.Api.Infrastructure.OpenApi;

/// <summary>
/// Documents the query and route parameters shared across endpoints, with realistic examples, in one place.
/// </summary>
internal sealed class ParameterDocumentationTransformer : IOpenApiOperationTransformer
{
    private static readonly Dictionary<string, (string Description, JsonNode Example)> Parameters =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["place"] = (
                "City to look up: `City, State, Country` or `City, State`. Case, accents and punctuation are ignored. " +
                "States may be given by name or code (`Texas`, `TX`); countries by name, ISO code or common alias " +
                "(`India`, `IN`, `USA`, `UK`). With `City, State` and no country, the browser `latitude`/`longitude` " +
                "(if supplied) select the country. Cities with a population of 5,000 or more are covered.",
                JsonValue.Create("Austin, Texas, USA")),
            ["latitude"] = (
                "Latitude in decimal degrees, −90 to 90 (north positive), typically from the browser Geolocation API. " +
                "Must be sent together with `longitude`. Required unless `place` is given.",
                JsonValue.Create(17.385)),
            ["longitude"] = (
                "Longitude in decimal degrees, −180 to 180 (east positive). Must be sent together with `latitude`. " +
                "Required unless `place` is given.",
                JsonValue.Create(78.4867)),
            ["timeZone"] = (
                "Optional IANA time zone identifier. It is validated, but the zone of the location takes precedence; " +
                "it is used only when no zone can be derived (e.g. mid-ocean).",
                JsonValue.Create("Asia/Kolkata")),
            ["tradition"] = (
                "Panchang tradition (regional convention): `all` (default), `telugu`, `tamil`, `kannada`, `malayalam`, " +
                "`north-indian`, `gujarati`, `marathi`. Selects which timings are shown, their order of emphasis and " +
                "regional names (e.g. Kuligai, Durmuhurtham, Rahu Kaal). It does not change the calculation: timings " +
                "always come from the location. See `GET /api/v1/traditions`.",
                JsonValue.Create("telugu")),
            ["language"] = (
                "Optional response language (BCP 47), e.g. `en`, `te`, `hi-IN`. Takes precedence over the " +
                "`Accept-Language` header. Unsupported languages fall back to the default language; untranslated text " +
                "falls back field by field. The chosen language is returned in `language` and `Content-Language`.",
                JsonValue.Create("en")),
            ["date"] = ("Local calendar date at the location, `yyyy-MM-dd`, between 1900-01-01 and 2100-12-31.", JsonValue.Create("2026-09-29")),
            ["start"] = ("First local date of the range, `yyyy-MM-dd`.", JsonValue.Create("2026-09-29")),
            ["days"] = ("Number of consecutive days, 1–366.", JsonValue.Create(30)),
            ["year"] = ("Gregorian year, 1900–2100.", JsonValue.Create(2026)),
            ["month"] = ("Gregorian month, 1–12.", JsonValue.Create(9)),
            ["activity"] = (
                "Activity key from `GET /api/v1/muhurta/activities`: `business`, `travel`, `property`, `vehicle`, `agreement`, " +
                "`education`, `puja`, `wedding`, `housewarming`, `general` (default) or `other` (requires `otherText`).",
                JsonValue.Create("business")),
            ["otherText"] = (
                "Description of the activity when `activity=other`, up to 80 letters, digits, spaces and simple punctuation. " +
                "It is matched to the closest activity; the response reports which.",
                JsonValue.Create("Opening a bakery")),
            ["nakshatra"] = ("Optional birth Nakshatra (27-star catalog, case-insensitive). Tara Bala uses each interval's active star; Janma caps at Mixed and positions 3, 5, 7 at Use Caution. Empty input preserves standard results. Abhijit is unsupported.", JsonValue.Create("Rohini")),
            ["count"] = ("Number of options to return, 1–10; 3 when omitted.", JsonValue.Create(3)),
        };

    // Display order: route values first, then location, then options.
    private static readonly string[] DisplayOrder =
        ["date", "year", "month", "place", "latitude", "longitude", "timeZone", "tradition", "language", "activity", "otherText", "nakshatra", "start", "days", "count"];

    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        if (operation.Parameters is null)
        {
            return Task.CompletedTask;
        }

        foreach (var parameter in operation.Parameters.OfType<OpenApiParameter>())
        {
            if (parameter.Name is null || !Parameters.TryGetValue(parameter.Name, out var documentation))
            {
                continue;
            }

            // Binding is case-insensitive; document the camelCase names clients should send.
            parameter.Name = Parameters.Keys.First(key => key.Equals(parameter.Name, StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrWhiteSpace(parameter.Description))
            {
                parameter.Description = documentation.Description;
            }

            parameter.Example ??= documentation.Example.DeepClone();
        }

        var ordered = operation.Parameters
            .OrderBy(parameter => Array.FindIndex(DisplayOrder, name => name.Equals(parameter.Name, StringComparison.OrdinalIgnoreCase)) is var index and >= 0
                ? index
                : DisplayOrder.Length)
            .ToList();
        operation.Parameters.Clear();
        foreach (var parameter in ordered)
        {
            operation.Parameters.Add(parameter);
        }

        return Task.CompletedTask;
    }
}
