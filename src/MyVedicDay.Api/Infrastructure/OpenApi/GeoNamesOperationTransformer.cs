using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace MyVedicDay.Api.Infrastructure.OpenApi;

internal sealed class GeoNamesOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        var path = context.Description.RelativePath;
        if (path is not ("api/v1/locations/search" or "api/v1/locations/postal" or "api/v1/locations/reverse" or "api/v1/locations/timezone"))
            return Task.CompletedTask;
        var action = path.Split('/')[^1];
        operation.OperationId = "GeoNames" + char.ToUpperInvariant(action[0]) + action[1..];
        operation.Summary = action switch
        {
            "search" => "Search worldwide cities with GeoNames.",
            "postal" => "Find places by international postal code.",
            "reverse" => "Find the nearest place for exact coordinates.",
            _ => "Resolve the IANA timezone for exact coordinates.",
        };
        operation.Description = operation.Summary + " Uses the server-side GeoNames account; disabled or unavailable lookups return 503. Quota exhaustion returns 429 with Retry-After. Coordinates retain their supplied precision.";
        foreach (var parameter in operation.Parameters?.OfType<OpenApiParameter>() ?? [])
        {
            (string Description, JsonNode? Example) documentation = parameter.Name switch
            {
                "text" => (action == "postal" ? "Postal code, 2–10 characters; country is optional." : "City prefix or search text, 2–120 characters.", JsonValue.Create(action == "postal" ? "78701" : "Austin")),
                "country" => ("Optional uppercase ISO country code; omitted searches worldwide.", JsonValue.Create("US")),
                "language" => ("Optional GeoNames language code for place labels.", JsonValue.Create("en")),
                "maxRows" => ("Maximum suggestions, 1–8; default 8.", JsonValue.Create(8)),
                "latitude" => ("Required exact latitude, −90 to 90.", JsonValue.Create(30.2672)),
                "longitude" => ("Required exact longitude, −180 to 180.", JsonValue.Create(-97.7431)),
                _ => ("Location lookup parameter.", JsonValue.Create("")),
            };
            parameter.Description = documentation.Description;
            parameter.Example = documentation.Example;
        }
        var place = JsonNode.Parse("""
        {"id":"4671654","city":"Austin","region":"Texas","country":"United States","countryCode":"US","postalCode":null,"latitude":30.2672,"longitude":-97.7431,"source":"city"}
        """)!;
        JsonNode example = action switch
        {
            "timezone" => new JsonObject { ["timeZone"] = "America/Chicago" },
            "reverse" => new JsonObject { ["place"] = place },
            "postal" => new JsonObject
            {
                ["results"] = new JsonArray(new JsonObject
                {
                    ["id"] = "postal:US:78701:30.2672:-97.7431",
                    ["city"] = "Austin",
                    ["region"] = "Texas",
                    ["country"] = "United States",
                    ["countryCode"] = "US",
                    ["postalCode"] = "78701",
                    ["latitude"] = 30.2672,
                    ["longitude"] = -97.7431,
                    ["source"] = "postal",
                })
            },
            _ => new JsonObject { ["results"] = new JsonArray(place) },
        };
        if (operation.Responses?.TryGetValue("200", out var response) == true && response.Content is not null)
        {
            foreach (var media in response.Content.Values) media.Example = example.DeepClone();
        }
        return Task.CompletedTask;
    }
}
