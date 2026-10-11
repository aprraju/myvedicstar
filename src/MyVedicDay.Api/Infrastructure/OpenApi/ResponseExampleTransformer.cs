using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using MyVedicDay.Api.Contracts.Festivals;
using MyVedicDay.Api.Contracts.Locations;
using MyVedicDay.Api.Contracts.Muhurta;
using MyVedicDay.Api.Contracts.Panchang;
using MyVedicDay.Api.Contracts.Traditions;
using MyVedicDay.Festivals.Business;
using MyVedicDay.Location;
using MyVedicDay.Muhurta.Business;
using MyVedicDay.Panchang.Business;
using MyVedicDay.Traditions;

namespace MyVedicDay.Api.Infrastructure.OpenApi;

/// <summary>
/// Attaches realistic example bodies to success and error responses. The glossary example is produced from the
/// configured content, so it always matches what the API returns.
/// </summary>
internal sealed class ResponseExampleTransformer : IOpenApiOperationTransformer
{
    private const string LocationExample = """
        {
          "latitude": 17.39, "longitude": 78.49, "timeZone": "Asia/Kolkata", "source": "Coordinates",
          "place": { "city": "Hyderabad", "state": "Telangana", "country": "India", "countryCode": "IN",
                     "displayName": "Hyderabad, Telangana, India" }
        }
        """;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    // Real output for the example day (Hyderabad, 2026-09-29, tradition "all"), windows abridged.
    private const string GuidanceExample = """
        {
          "rating": "Mixed",
          "ratingName": "Mixed",
          "icon": "🟡",
          "headline": "🟡 Mixed Day",
          "summary": "Today's Panchang factors are mixed; choose timings carefully for important activities. Tithi Krishna Tritiya and Yoga Harshana are supportive. Tuesday, Nakshatra Bharani and Karana Vishti (Bhadra) call for caution. Best time: 06:46–08:30 IST, 10:36–12:06 IST, 13:36–15:06 IST. Favorable: Abhijit Muhurta 11:42–12:30 IST, Amrit Kalam 03:06(+1)–04:36(+1) IST. Avoid for new beginnings: Varjyam 05:15–06:46 IST, Dur Muhurtam 08:30–09:18 IST, Yamagandam 09:06–10:36 IST, Rahu Kalam 15:06–16:36 IST, Varjyam 18:04–19:35 IST, Dur Muhurtam 22:54–23:42 IST. Use caution: Gulika Kalam 12:06–13:36 IST. For spiritual practice: Brahma Muhurta 04:30–05:18 IST. Check the recommended and traditionally avoided time periods below before beginning an important activity.",
          "factors": [
            {
              "limb": "tithi",
              "limbName": "Tithi",
              "key": "KrishnaTritiya",
              "name": "Krishna Tritiya",
              "rating": "Favorable",
              "ratingName": "Favorable",
              "reason": "Tithi Krishna Tritiya: Favorable. Jaya Tithi (3rd, 8th, 13th), traditionally associated with success."
            },
            {
              "limb": "vara",
              "limbName": "Vara",
              "key": "Tuesday",
              "name": "Tuesday",
              "rating": "Caution",
              "ratingName": "Caution",
              "reason": "Vara Tuesday: Caution. A harsh (Krura) weekday, traditionally treated with caution for auspicious beginnings."
            },
            {
              "limb": "nakshatra",
              "limbName": "Nakshatra",
              "key": "Bharani",
              "name": "Bharani",
              "rating": "Caution",
              "ratingName": "Caution",
              "reason": "Nakshatra Bharani: Caution. A fierce (Ugra) Nakshatra, traditionally treated with caution for auspicious beginnings."
            },
            {
              "limb": "yoga",
              "limbName": "Yoga",
              "key": "Harshana",
              "name": "Harshana",
              "rating": "Favorable",
              "ratingName": "Favorable",
              "reason": "Yoga Harshana: Favorable. A benefic Yoga, traditionally supportive."
            },
            {
              "limb": "karana",
              "limbName": "Karana",
              "key": "Vishti",
              "name": "Vishti (Bhadra)",
              "rating": "Avoid",
              "ratingName": "Traditionally Avoided",
              "reason": "Karana Vishti (Bhadra): Traditionally Avoided. Vishti (Bhadra) Karana, traditionally avoided for auspicious beginnings."
            }
          ],
          "bestWindows": [
            {
              "start": "2026-09-29T06:46:44+05:30",
              "end": "2026-09-29T08:30:06+05:30",
              "startDisplay": "29/09/2026 06:46:44 IST",
              "endDisplay": "29/09/2026 08:30:06 IST"
            },
            {
              "start": "2026-09-29T10:36:13+05:30",
              "end": "2026-09-29T12:06:19+05:30",
              "startDisplay": "29/09/2026 10:36:13 IST",
              "endDisplay": "29/09/2026 12:06:19 IST"
            },
            {
              "start": "2026-09-29T13:36:24+05:30",
              "end": "2026-09-29T15:06:29+05:30",
              "startDisplay": "29/09/2026 13:36:24 IST",
              "endDisplay": "29/09/2026 15:06:29 IST"
            }
          ],
          "favorableWindows": [
            {
              "kind": "AbhijitMuhurta",
              "name": "Abhijit Muhurta",
              "classification": "Favorable",
              "start": "2026-09-29T11:42:17+05:30",
              "end": "2026-09-29T12:30:20+05:30",
              "startDisplay": "29/09/2026 11:42:17 IST",
              "endDisplay": "29/09/2026 12:30:20 IST"
            },
            {
              "kind": "AmritKalam",
              "name": "Amrit Kalam",
              "classification": "HighlyFavorable",
              "start": "2026-09-30T03:06:07+05:30",
              "end": "2026-09-30T04:36:20+05:30",
              "startDisplay": "30/09/2026 03:06:07 IST",
              "endDisplay": "30/09/2026 04:36:20 IST"
            }
          ],
          "spiritualWindows": [
            {
              "kind": "BrahmaMuhurta",
              "name": "Brahma Muhurta",
              "classification": "Favorable",
              "start": "2026-09-29T04:30:10+05:30",
              "end": "2026-09-29T05:18:04+05:30",
              "startDisplay": "29/09/2026 04:30:10 IST",
              "endDisplay": "29/09/2026 05:18:04 IST",
              "overlaps": [
                { "kind": "Varjyam", "name": "Varjyam", "classification": "Avoid", "start": "2026-09-29T05:15:36+05:30", "end": "2026-09-29T05:18:04+05:30", "startDisplay": "29/09/2026 05:15:36 IST", "endDisplay": "29/09/2026 05:18:04 IST" }
              ],
              "overlapMessage": "Overlaps Varjyam (05:15–06:46 IST): avoid 05:15–05:18 IST for new beginnings."
            }
          ],
          "cautionWindows": [
            {
              "kind": "GulikaKalam",
              "name": "Gulika Kalam",
              "classification": "Caution",
              "start": "2026-09-29T12:06:19+05:30",
              "end": "2026-09-29T13:36:24+05:30",
              "startDisplay": "29/09/2026 12:06:19 IST",
              "endDisplay": "29/09/2026 13:36:24 IST"
            }
          ],
          "avoidWindows": [
            {
              "kind": "Varjyam",
              "name": "Varjyam",
              "classification": "Avoid",
              "start": "2026-09-29T05:15:36+05:30",
              "end": "2026-09-29T06:46:44+05:30",
              "startDisplay": "29/09/2026 05:15:36 IST",
              "endDisplay": "29/09/2026 06:46:44 IST"
            },
            {
              "kind": "DurMuhurtam",
              "name": "Dur Muhurtam",
              "classification": "Avoid",
              "start": "2026-09-29T08:30:06+05:30",
              "end": "2026-09-29T09:18:09+05:30",
              "startDisplay": "29/09/2026 08:30:06 IST",
              "endDisplay": "29/09/2026 09:18:09 IST"
            }
          ],
          "mixedWindows": [
            {
              "start": "2026-09-29T05:15:36+05:30",
              "end": "2026-09-29T05:18:04+05:30",
              "startDisplay": "29/09/2026 05:15:36 IST",
              "endDisplay": "29/09/2026 05:18:04 IST",
              "rating": "Mixed",
              "ratingName": "Mixed",
              "icon": "🟡",
              "favorablePeriods": [
                {
                  "kind": "BrahmaMuhurta",
                  "name": "Brahma Muhurta",
                  "classification": "Favorable",
                  "start": "2026-09-29T04:30:10+05:30",
                  "end": "2026-09-29T05:18:04+05:30",
                  "startDisplay": "29/09/2026 04:30:10 IST",
                  "endDisplay": "29/09/2026 05:18:04 IST",
                  "overlaps": [
                    { "kind": "Varjyam", "name": "Varjyam", "classification": "Avoid", "start": "2026-09-29T05:15:36+05:30", "end": "2026-09-29T05:18:04+05:30", "startDisplay": "29/09/2026 05:15:36 IST", "endDisplay": "29/09/2026 05:18:04 IST" }
                  ],
                  "overlapMessage": "Overlaps Varjyam (05:15–06:46 IST): avoid 05:15–05:18 IST for new beginnings."
                }
              ],
              "unfavorablePeriods": [
                {
                  "kind": "Varjyam",
                  "name": "Varjyam",
                  "classification": "Avoid",
                  "start": "2026-09-29T05:15:36+05:30",
                  "end": "2026-09-29T06:46:44+05:30",
                  "startDisplay": "29/09/2026 05:15:36 IST",
                  "endDisplay": "29/09/2026 06:46:44 IST"
                }
              ]
            }
          ],
          "methodology": "Daily guidance summarizes the five Panchanga limbs (Tithi, Vara, Nakshatra, Yoga, Karana) using this app's methodology. It is not a universal traditional rule and not a Muhurta for a specific ceremony such as a marriage or housewarming.",
          "rulesVersion": "2026.1-provisional"
        }
        """;

    private static readonly JsonNode PanchangExample = JsonNode.Parse($$"""
        {
          "location": {{LocationExample}},
          "tradition": {
            "key": "all", "name": "All timings", "description": "Every timing the app calculates, from all traditions.",
            "display": [ "RahuKalam", "Yamagandam", "GulikaKalam", "DurMuhurtam", "Varjyam", "AmritKalam", "AbhijitMuhurta", "BrahmaMuhurta", "Choghadiya" ]
          },
          "calculationMethod": "drik",
          "calculationVersion": "1.1.0",
          "localToday": "2026-09-29",
          "localTodayDisplay": "29/09/2026",
          "displayFormat": { "datePattern": "dd/MM/yyyy", "timePattern": "HH:mm:ss", "countryCode": "IN", "example": "29/09/2026 06:05:58 IST" },
          "generatedAt": "2026-09-29T01:11:03+00:00",
          "language": "en",
          "glossary": {},
          "days": [
            {
              "date": "2026-09-29",
              "dateDisplay": "29/09/2026",
              "weekday": "Tuesday",
              "weekdayName": "Tuesday",
              "sunrise": "2026-09-29T06:05:58+05:30",
              "sunset": "2026-09-29T18:06:40+05:30",
              "nextSunrise": "2026-09-30T06:06:07+05:30",
              "sunriseDisplay": "29/09/2026 06:05:58 IST",
              "sunsetDisplay": "29/09/2026 18:06:40 IST",
              "nextSunriseDisplay": "30/09/2026 06:06:07 IST",
              "tithis": [
                { "key": "KrishnaTritiya", "number": 18, "name": "Krishna Tritiya", "start": "2026-09-28T19:13:46+05:30", "end": "2026-09-29T17:10:28+05:30", "startDisplay": "28/09/2026 19:13:46 IST", "endDisplay": "29/09/2026 17:10:28 IST" },
                { "key": "KrishnaChaturthi", "number": 19, "name": "Krishna Chaturthi", "start": "2026-09-29T17:10:28+05:30", "end": "2026-09-30T14:55:42+05:30", "startDisplay": "29/09/2026 17:10:28 IST", "endDisplay": "30/09/2026 14:55:42 IST" }
              ],
              "nakshatras": [
                { "key": "Ashwini", "number": 1, "name": "Ashwini", "start": "2026-09-28T10:16:15+05:30", "end": "2026-09-29T09:03:28+05:30", "startDisplay": "28/09/2026 10:16:15 IST", "endDisplay": "29/09/2026 09:03:28 IST" },
                { "key": "Bharani", "number": 2, "name": "Bharani", "start": "2026-09-29T09:03:28+05:30", "end": "2026-09-30T07:36:46+05:30", "startDisplay": "29/09/2026 09:03:28 IST", "endDisplay": "30/09/2026 07:36:46 IST" }
              ],
              "yogas": [
                { "key": "Vyaghata", "number": 13, "name": "Vyaghata", "start": "2026-09-28T08:53:44+05:30", "end": "2026-09-29T06:13:02+05:30", "startDisplay": "28/09/2026 08:53:44 IST", "endDisplay": "29/09/2026 06:13:02 IST" },
                { "key": "Harshana", "number": 14, "name": "Harshana", "start": "2026-09-29T06:13:02+05:30", "end": "2026-09-30T03:20:13+05:30", "startDisplay": "29/09/2026 06:13:02 IST", "endDisplay": "30/09/2026 03:20:13 IST" }
              ],
              "karanas": [
                { "key": "Vanija", "number": 7, "name": "Vanija", "start": "2026-09-28T19:13:46+05:30", "end": "2026-09-29T06:13:58+05:30", "startDisplay": "28/09/2026 19:13:46 IST", "endDisplay": "29/09/2026 06:13:58 IST" },
                { "key": "Vishti", "number": 8, "name": "Vishti (Bhadra)", "start": "2026-09-29T06:13:58+05:30", "end": "2026-09-29T17:10:28+05:30", "startDisplay": "29/09/2026 06:13:58 IST", "endDisplay": "29/09/2026 17:10:28 IST" }
              ],
              "auspiciousPeriods": [
                { "kind": "BrahmaMuhurta", "name": "Brahma Muhurta", "classification": "Favorable", "start": "2026-09-29T04:30:10+05:30", "end": "2026-09-29T05:18:04+05:30", "startDisplay": "29/09/2026 04:30:10 IST", "endDisplay": "29/09/2026 05:18:04 IST",
                  "overlaps": [ { "kind": "Varjyam", "name": "Varjyam", "classification": "Avoid", "start": "2026-09-29T05:15:36+05:30", "end": "2026-09-29T05:18:04+05:30", "startDisplay": "29/09/2026 05:15:36 IST", "endDisplay": "29/09/2026 05:18:04 IST" } ],
                  "overlapMessage": "Overlaps Varjyam (05:15–06:46 IST): avoid 05:15–05:18 IST for new beginnings." },
                { "kind": "AbhijitMuhurta", "name": "Abhijit Muhurta", "classification": "Favorable", "start": "2026-09-29T11:42:17+05:30", "end": "2026-09-29T12:30:20+05:30", "startDisplay": "29/09/2026 11:42:17 IST", "endDisplay": "29/09/2026 12:30:20 IST" },
                { "kind": "AmritKalam", "name": "Amrit Kalam", "classification": "HighlyFavorable", "start": "2026-09-30T03:06:07+05:30", "end": "2026-09-30T04:36:20+05:30", "startDisplay": "30/09/2026 03:06:07 IST", "endDisplay": "30/09/2026 04:36:20 IST" }
              ],
              "inauspiciousPeriods": [
                { "kind": "Varjyam", "name": "Varjyam", "classification": "Avoid", "start": "2026-09-29T05:15:36+05:30", "end": "2026-09-29T06:46:44+05:30", "startDisplay": "29/09/2026 05:15:36 IST", "endDisplay": "29/09/2026 06:46:44 IST" },
                { "kind": "DurMuhurtam", "name": "Dur Muhurtam", "classification": "Avoid", "start": "2026-09-29T08:30:06+05:30", "end": "2026-09-29T09:18:09+05:30", "startDisplay": "29/09/2026 08:30:06 IST", "endDisplay": "29/09/2026 09:18:09 IST" },
                { "kind": "Yamagandam", "name": "Yamagandam", "classification": "Avoid", "start": "2026-09-29T09:06:08+05:30", "end": "2026-09-29T10:36:13+05:30", "startDisplay": "29/09/2026 09:06:08 IST", "endDisplay": "29/09/2026 10:36:13 IST" },
                { "kind": "GulikaKalam", "name": "Gulika Kalam", "classification": "Caution", "start": "2026-09-29T12:06:19+05:30", "end": "2026-09-29T13:36:24+05:30", "startDisplay": "29/09/2026 12:06:19 IST", "endDisplay": "29/09/2026 13:36:24 IST" },
                { "kind": "RahuKalam", "name": "Rahu Kalam", "classification": "Avoid", "start": "2026-09-29T15:06:29+05:30", "end": "2026-09-29T16:36:35+05:30", "startDisplay": "29/09/2026 15:06:29 IST", "endDisplay": "29/09/2026 16:36:35 IST" },
                { "kind": "DurMuhurtam", "name": "Dur Muhurtam", "classification": "Avoid", "start": "2026-09-29T22:54:33+05:30", "end": "2026-09-29T23:42:36+05:30", "startDisplay": "29/09/2026 22:54:33 IST", "endDisplay": "29/09/2026 23:42:36 IST" }
              ],
              "choghadiya": [
                { "kind": "ChoghadiyaRog", "name": "Rog", "classification": "Avoid", "start": "2026-09-29T06:05:58+05:30", "end": "2026-09-29T07:36:03+05:30", "startDisplay": "29/09/2026 06:05:58 IST", "endDisplay": "29/09/2026 07:36:03 IST" },
                { "kind": "ChoghadiyaUdveg", "name": "Udveg", "classification": "Avoid", "start": "2026-09-29T07:36:03+05:30", "end": "2026-09-29T09:06:08+05:30", "startDisplay": "29/09/2026 07:36:03 IST", "endDisplay": "29/09/2026 09:06:08 IST" },
                { "kind": "ChoghadiyaChal", "name": "Chal", "classification": "Neutral", "start": "2026-09-29T09:06:08+05:30", "end": "2026-09-29T10:36:13+05:30", "startDisplay": "29/09/2026 09:06:08 IST", "endDisplay": "29/09/2026 10:36:13 IST" }
              ]
            }
          ]
        }
        """)!;

    private static readonly JsonNode ValidationProblemExample = JsonNode.Parse("""
        {
          "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
          "title": "One or more validation errors occurred.",
          "status": 400,
          "errors": { "place": [ "No matching city was found. Enter the place as \"City, State\" or \"City, State, Country\"." ] },
          "traceId": "4bf92f3577b34da6a3ce929d0e0e4736",
          "correlationId": "web-7f3c2a"
        }
        """)!;

    private static readonly JsonNode UnprocessableProblemExample = JsonNode.Parse("""
        {
          "type": "https://tools.ietf.org/html/rfc4918#section-11.2",
          "title": "The request cannot be fulfilled.",
          "status": 422,
          "detail": "The Panchang cannot be calculated for this location and date because the sun does not rise or set.",
          "traceId": "4bf92f3577b34da6a3ce929d0e0e4736",
          "correlationId": "web-7f3c2a"
        }
        """)!;

    public async Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        var successType = context.Description.SupportedResponseTypes
            .FirstOrDefault(response => response.StatusCode == StatusCodes.Status200OK)?.Type;

        if (successType == typeof(PanchangResponse))
        {
            var example = PanchangExample.DeepClone();
            example["glossary"] = (await GetGlossaryExampleAsync(context, cancellationToken))["sections"]!.DeepClone();
            example["days"]![0]!["guidance"] = JsonNode.Parse(GuidanceExample);
            example["days"]![0]!["festivals"] = (await GetFestivalsExampleAsync(context, "all", new DateOnly(2026, 9, 29), 1, cancellationToken))["festivals"]!.DeepClone();
            example["days"]![0]!["goodTime"] = (await GetDaySummariesExampleAsync(context, new DateOnly(2026, 9, 29), 1, cancellationToken))["days"]![0]!.DeepClone();
            SetExample(operation, "200", example);
        }
        else if (successType == typeof(MuhurtaNakshatrasResponse))
        {
            using var scope = context.ApplicationServices.CreateScope();
            var result = await scope.ServiceProvider.GetRequiredService<IMuhurtaFacade>().GetNakshatrasAsync(null, null, cancellationToken);
            SetExample(operation, "200", JsonSerializer.SerializeToNode(new MuhurtaNakshatrasResponse(result.Language,
                result.Nakshatras.Select(star => new MuhurtaNakshatraResponse(star.Key, star.Name, star.Order)).ToArray()), SerializerOptions)!);
        }
        else if (successType == typeof(MuhurtaActivitiesResponse))
        {
            using var scope = context.ApplicationServices.CreateScope();
            var activities = await scope.ServiceProvider.GetRequiredService<IMuhurtaFacade>().GetActivitiesAsync(null, null, cancellationToken);
            SetExample(operation, "200", JsonSerializer.SerializeToNode(activities.ToResponse(), SerializerOptions)!);
        }
        else if (successType == typeof(GoodTimesResponse))
        {
            using var scope = context.ApplicationServices.CreateScope();
            var goodTimes = await scope.ServiceProvider.GetRequiredService<IMuhurtaFacade>().GetGoodTimesAsync(
                new MuhurtaQuery(new LocationQuery(17.385, 78.4867), "telugu"), new GoodTimesRequest(new DateOnly(2026, 10, 1), "business"), cancellationToken);
            SetExample(operation, "200", JsonSerializer.SerializeToNode(goodTimes.ToResponse(), SerializerOptions)!);
        }
        else if (successType == typeof(GoodTimeExplanationResponse))
        {
            using var scope = context.ApplicationServices.CreateScope();
            var explanation = await scope.ServiceProvider.GetRequiredService<IMuhurtaFacade>().GetExplanationAsync(
                new MuhurtaQuery(new LocationQuery(17.385, 78.4867), "telugu"), new GoodTimesRequest(new DateOnly(2026, 10, 1), "business"), cancellationToken);
            SetExample(operation, "200", JsonSerializer.SerializeToNode(explanation.ToResponse(), SerializerOptions)!);
        }
        else if (successType == typeof(BestGoodTimesResponse))
        {
            using var scope = context.ApplicationServices.CreateScope();
            var best = await scope.ServiceProvider.GetRequiredService<IMuhurtaFacade>().GetBestGoodTimesAsync(
                new MuhurtaQuery(new LocationQuery(17.385, 78.4867), "telugu"), new BestGoodTimesRequest("business"), cancellationToken);
            SetExample(operation, "200", JsonSerializer.SerializeToNode(best.ToResponse(), SerializerOptions)!);
        }
        else if (successType == typeof(DaySummariesResponse))
        {
            SetExample(operation, "200", await GetDaySummariesExampleAsync(context, new DateOnly(2026, 10, 1), 7, cancellationToken));
        }
        else if (successType == typeof(FestivalsResponse))
        {
            SetExample(operation, "200", await GetFestivalsExampleAsync(context, "telugu", new DateOnly(2026, 10, 1), 45, cancellationToken));
        }
        else if (successType == typeof(GlossaryResponse))
        {
            SetExample(operation, "200", await GetGlossaryExampleAsync(context, cancellationToken));
        }
        else if (successType == typeof(TraditionsResponse))
        {
            SetExample(operation, "200", await GetTraditionsExampleAsync(context, cancellationToken));
        }
        else if (successType == typeof(PanchangTraditionsResponse))
        {
            SetExample(operation, "200", await GetPanchangTraditionsExampleAsync(context, cancellationToken));
        }
        else if (successType == typeof(LocationResponse))
        {
            SetExample(operation, "200", JsonNode.Parse(LocationExample)!);
        }

        SetExample(operation, "400", ValidationProblemExample);
        SetExample(operation, "422", UnprocessableProblemExample);
    }

    private static async Task<JsonNode> GetGlossaryExampleAsync(OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        using var scope = context.ApplicationServices.CreateScope();
        var glossary = await scope.ServiceProvider.GetRequiredService<IGlossaryFacade>().GetGlossaryAsync(null, null, cancellationToken);
        return JsonSerializer.SerializeToNode(glossary.ToResponse(), SerializerOptions)!;
    }

    /// <summary>Real festivals for Hyderabad, so the example always matches the configured rules.</summary>
    private static async Task<JsonNode> GetFestivalsExampleAsync(
        OpenApiOperationTransformerContext context, string tradition, DateOnly start, int days, CancellationToken cancellationToken)
    {
        using var scope = context.ApplicationServices.CreateScope();
        var festivals = await scope.ServiceProvider.GetRequiredService<IFestivalFacade>()
            .GetRangeAsync(new FestivalQuery(new LocationQuery(17.385, 78.4867), tradition), start, days, cancellationToken);
        return JsonSerializer.SerializeToNode(festivals.ToResponse(), SerializerOptions)!;
    }

    /// <summary>Real General-activity summaries for Hyderabad, so the example always matches the configured rules.</summary>
    private static async Task<JsonNode> GetDaySummariesExampleAsync(
        OpenApiOperationTransformerContext context, DateOnly start, int days, CancellationToken cancellationToken)
    {
        using var scope = context.ApplicationServices.CreateScope();
        var summaries = await scope.ServiceProvider.GetRequiredService<IMuhurtaFacade>()
            .GetDaySummariesAsync(new MuhurtaQuery(new LocationQuery(17.385, 78.4867)), start, days, null, null, cancellationToken);
        return JsonSerializer.SerializeToNode(summaries.ToResponse(), SerializerOptions)!;
    }

    private static async Task<JsonNode> GetTraditionsExampleAsync(OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        using var scope = context.ApplicationServices.CreateScope();
        var traditions = await scope.ServiceProvider.GetRequiredService<ITraditionFacade>().GetTraditionsAsync(null, cancellationToken);
        return JsonSerializer.SerializeToNode(traditions.ToResponse(), SerializerOptions)!;
    }

    private static async Task<JsonNode> GetPanchangTraditionsExampleAsync(OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        using var scope = context.ApplicationServices.CreateScope();
        var traditions = await scope.ServiceProvider.GetRequiredService<IPanchangTraditionFacade>().GetTraditionsAsync(null, cancellationToken);
        return JsonSerializer.SerializeToNode(traditions.ToResponse(), SerializerOptions)!;
    }

    private static void SetExample(OpenApiOperation operation, string statusCode, JsonNode example)
    {
        if (operation.Responses?.TryGetValue(statusCode, out var response) != true || response?.Content is null)
        {
            return;
        }

        foreach (var mediaType in response.Content.Values)
        {
            mediaType.Example = example.DeepClone();
        }
    }
}
