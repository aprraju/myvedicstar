using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace MyVedicDay.Api.Infrastructure.OpenApi;

/// <summary>
/// Adds the API overview, conventions and tag descriptions to the OpenAPI document.
/// </summary>
internal sealed class ApiDocumentTransformer(IConfiguration configuration) : IOpenApiDocumentTransformer
{
    public const string PanchangTag = "Panchang";
    public const string LocationsTag = "Locations";
    public const string GlossaryTag = "Glossary";
    public const string TraditionsTag = "Traditions";
    public const string FestivalsTag = "Festivals";
    public const string MuhurtaTag = "Find a Good Time";

    private const string Overview = """
        Location-aware **Vedic Panchang** calendar. Every day includes sunrise and sunset, Tithi, Nakshatra, Yoga and
        Karana with their exact transitions, and the classified periods Rahu Kalam, Yamagandam, Gulika Kalam,
        Dur Muhurtam, Varjyam, Abhijit Muhurat, Amrit Kalam and Brahma Muhurta.

        ## Specifying the location
        Every endpoint accepts one of:

        | Mode | Parameters | Behaviour |
        |------|------------|-----------|
        | City, state, country | `place=Austin, Texas, USA` | Looked up within that country. |
        | City, state + browser location | `place=Hyderabad, Telangana&latitude=…&longitude=…` | The browser position supplies the country; if the city is not there it is searched worldwide. |
        | Browser location | `latitude=17.385&longitude=78.4867` | Used directly; the response names the nearest city (within 50 km). |

        A place always determines the coordinates. Coordinates are rounded to 0.01° (≈ 1.1 km).
        The time zone is derived from the location; a client-supplied `timeZone` is only a fallback.

        ## Dates and times
        * **"Today"** is the current date *at the location*, not the server's date. `localToday` in every response
          lets clients detect a day change.
        * A Panchang day runs from **sunrise to the next sunrise**. Element lists start with the element prevailing at
          sunrise, followed by each transition before the next sunrise.
        * All times are ISO 8601 with the location's UTC offset at that instant, so daylight-saving changes and
          periods that cross midnight are exact. A period's `start`/`end` may fall on the previous or next date.
        * Every time also has a **display string** in the location's local format,
          `{date} {time} {time zone abbreviation}`: `09/28/2026 07:14:52 CDT` in the US,
          `28/09/2026 06:05:58 IST` in India, `28.09.2026 07:10:31 CEST` in Germany. Fields end in `Display`
          (`sunriseDisplay`, `startDisplay`, `endDisplay`, `dateDisplay`); `displayFormat` states the pattern and the
          country it came from. The abbreviation follows daylight saving (CST in winter, CDT in summer). Show the display
          strings to users; use the ISO 8601 values for sorting and calculations, because abbreviations are ambiguous
          (IST is also used in Ireland and Israel).
        * Supported dates: 1900-01-01 to 2100-12-31.

        ## Daily guidance
        Every day includes `guidance`: simple consumer guidance for the selected tradition.
        * **Day rating** (🟢🟢 Highly Favorable, 🟢 Favorable, 🟡 Mixed, 🟠 Caution, 🔴 Traditionally Avoided) from the
          five Panchanga limbs (Tithi, Vara, Nakshatra, Yoga, Karana), each with its rating and traditional reason in
          `factors`. The limb in force for most of the daytime is rated.
        * **Time windows** from the periods the tradition shows: `bestWindows` (daytime free of caution/avoid periods),
          `favorableWindows`, `spiritualWindows`, `cautionWindows`, `avoidWindows`. Windows never change the day rating:
          a favorable day with Rahu Kalam stays favorable, with Rahu Kalam listed to avoid.
        * **Mixed windows** (`mixedWindows`, 🟡): periods come from different Panchanga rules and can overlap. Amrit
          Kalam (Nakshatra-based, favorable) 14:58–16:27 and Rahu Kalam (weekday/daylight-based, avoided for new
          beginnings) 14:49–16:18 overlap 14:58–16:18; neither cancels the other, so that stretch is Mixed. Each mixed
          window lists the favorable and unfavorable periods involved; the periods keep their full times. The
          favorable period itself (in `auspiciousPeriods` and the guidance windows) carries `overlaps` and an
          `overlapMessage` naming the times to avoid, e.g. "Overlaps Rahu Kalam (14:49–16:18 IST): avoid 14:58–16:18
          IST for new beginnings."
        * A localized `headline` and `summary`.
        * This is the application's own methodology (`rulesVersion`, configurable per tradition), not a universal
          traditional rule, and not a Muhurta for a specific ceremony such as a marriage.

        ## Find a Good Time
        `GET /api/v1/muhurta/good-times?activity=business` evaluates a day for an activity (`GET /api/v1/muhurta/activities`
        lists them: Business, Travel, Property, Vehicle, Agreement, Education, Puja / Spiritual, Wedding, Housewarming,
        General, Other). The backend rules engine is authoritative:
        * The day is cut wherever an input changes; each segment has a resolved `status` (`HighlyFavorable`, `Favorable`,
          `Mixed`, `Caution`, `Avoid`), `reasons` and `conflicts`. Overlapping favorable and unfavorable periods are
          resolved by a configurable conflict matrix (activity × tradition): Abhijit with Dur Muhurtam is Caution, Amrit
          Kalam with Rahu Kalam is Mixed.
        * Only segments with `isGoodTime: true` are Good Times. When none qualifies, `goodTimes` is empty with a reason;
          a Mixed, Caution or Avoid segment is never promoted. Clients must not derive or promote Good Times themselves.
        * The same evaluation drives the calendar and Home: every Panchang day carries `goodTime` (General activity),
          and `GET /api/v1/muhurta/day-summaries` returns it for a range (any activity: a month view of an activity).
        * `GET /api/v1/muhurta/good-times/best` returns the next best Good Times (default 3 within 30 days), at most one
          per day: Highly Favorable before Favorable, then the soonest.
        * Wedding and Housewarming are `partial`: a complete Muhurta also needs the Lagna and personal charts. Results
          are guidance (`rulesVersion` is provisional), not a personalized Muhurta.

        ## Festivals
        `GET /api/v1/festivals/upcoming` lists the festivals of the next `days` (default 30) from today at the location.
        Every Panchang day also carries `festivals`, so today, ten-day, month and year views show them in place.
        * **Location-dependent:** a festival falls on the day its Tithi or Nakshatra prevails during the traditional
          observance time (`timing.observanceTime`: sunrise, Madhyahna for Ganesh Chaturthi, Pradosha for Diwali, Nishita
          for Janmashtami and Maha Shivaratri), so dates can differ between India and the USA. Solar festivals (Makar
          Sankranti, Puthandu, Vishu) follow the Sun's entry into a sign. Lunar months are Amanta, with Adhika months
          detected and skipped.
        * **Tradition-aware:** `tradition` selects which festivals are listed and their regional names: `telugu` shows
          Ugadi, Vinayaka Chavithi and Mukkoti Ekadashi; `tamil` shows Puthandu, Thai Pongal and Karthigai Deepam;
          `north-indian` shows Karwa Chauth, Holi and Dussehra; `all` shows every festival.
        * **Categories:** `festival`, or `observance` for recurring vrata (Ekadashi, Pradosham, Sankashti Chaturthi,
          Purnima, Amavasya, Sankranti). A named festival hides the generic observance on the same day (Vaikuntha
          Ekadashi replaces Ekadashi).
        * `timing` gives what determined the date (the Tithi, Nakshatra or Sankranti with its times) and the observance
          window on that day, e.g. the Madhyahna puja time.
        * The rules are this application's methodology (`rulesVersion`, provisional), not an authoritative almanac.

        ## Panchang tradition
        **Location drives the calculation; tradition drives the presentation.** The optional `tradition` parameter
        (default `all`) selects which timings are shown, in what order of emphasis, and with which regional names:

        | Tradition | Shows |
        |-----------|-------|
        | `all` | Every timing, including Choghadiya |
        | `telugu` | Varjyam, Durmuhurtham, Rahu Kalam, Yamagandam, Gulika, Amrita Kalam, Abhijit Muhurtham, Brahma Muhurtham |
        | `tamil` | Rahu Kalam, Yamagandam, Kuligai, Amirtha Kalam, Abhijit Muhurtham, Brahma Muhurtham |
        | `kannada` | Rahu Kalam, Yamagandam, Gulika, Durmuhurtha, Amrita Kala, Abhijit Muhurtha, Brahma Muhurtha |
        | `malayalam` | Rahu Kalam, Yamagandam, Gulika, Amrutha Kalam, Abhijit Muhurtham, Brahma Muhurtham |
        | `north-indian` | Rahu Kaal, Choghadiya, Abhijit Muhurat, Amrit Kaal, Brahma Muhurat |
        | `gujarati` | Choghadiya, Rahu Kaal, Abhijit Muhurat, Amrit Kaal, Brahma Muhurat |
        | `marathi` | Rahu Kaal, Abhijit Muhurat, Choghadiya, Amrit Kaal, Brahma Muhurat |

        A Telugu family in Dallas uses `tradition=telugu` with Dallas coordinates. `tradition.display` in the response
        gives the emphasis order; `GET /api/v1/traditions` lists the traditions for a picker.

        ## Languages, meanings and classifications
        * Names are localized: pass `language=te` or send `Accept-Language`. The chosen language is returned in
          `language` and the `Content-Language` header; untranslated text falls back to English field by field.
        * Every item has a stable, language-independent key (`key` for Tithi/Nakshatra/Yoga/Karana, `kind` for periods)
          for program logic; `name` is for display.
        * Each response carries a `glossary` once (not per item) with `name`, `meaning`, `description`, a one-line
          `message` and an `icon`: `glossary.periods[kind]`, `glossary.elements.tithi`, `glossary.terms.sunrise`.
        * Every period has a `classification` (`HighlyFavorable`, `Favorable`, `Neutral`, `Caution`, `Avoid`),
          configurable per period; `glossary.classifications[classification]` gives its label and icon (🟢 / ⚪ / 🟠 / 🔴).
        * `GET /api/v1/glossary` returns the glossary alone for help screens or client-side caching.

        ## Errors
        Errors use [RFC 7807 Problem Details](https://www.rfc-editor.org/rfc/rfc7807) (`application/problem+json`)
        with `traceId` and `correlationId`. Send an `X-Correlation-ID` header (letters, digits, `_`, `-`; up to 64
        characters) to trace a request; it is echoed in the response.

        | Status | Meaning |
        |--------|---------|
        | 400 | Invalid parameters, unknown time zone or tradition, or no matching city. |
        | 422 | Valid request but the Panchang is undefined, e.g. no sunrise during polar day or night. |
        | 429 | Rate limit exceeded (per client IP). |
        | 503 | A dependency is temporarily unavailable; retry later. |

        ## Accuracy and conventions
        Calculation method `drik` (all current traditions): Lahiri ayanamsa, upper-limb sunrise with standard refraction. Transition and sunrise
        times are accurate to about a minute. Responses carry `calculationVersion`; results change only when it changes.

        ## Attribution
        Place data © [GeoNames](https://www.geonames.org), licensed [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/).
        Applications displaying place names must show this attribution.
        """;

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info = new OpenApiInfo
        {
            Title = "MyVedicDay API",
            Version = "v1",
            Description = Overview,
        };

        // A single BearerAuth scheme makes Swagger UI show one "Authorize" dialog that reliably
        // injects Authorization: Bearer <token> on every secured request.  Runtime validation is
        // multi-provider (Google, Entra, Apple, Facebook) — the scheme name is a Swagger hint only.
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[SecurityOperationTransformer.BearerSchemeId] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "JWT Bearer token from any supported identity provider: Google, Azure Entra External ID, Apple, or Facebook.",
        };

        foreach (var provider in SecurityOperationTransformer.EnabledProviders(configuration))
        {
            document.Components.SecuritySchemes[provider] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = provider == "Facebook" ? "JWT or opaque access token" : "JWT",
                Description = $"Bearer token from {provider}.",
            };
        }

        document.Tags = new HashSet<OpenApiTag>
        {
            new()
            {
                Name = PanchangTag,
                Description = "Daily Panchang for today, tomorrow, ten days, any date, a date range, a month or a year.",
            },
            new()
            {
                Name = TraditionsTag,
                Description = "Panchang traditions (regional conventions) that choose which timings are shown.",
            },
            new()
            {
                Name = MuhurtaTag,
                Description = "Good times for an activity, with favorable and unfavorable overlaps resolved by the rules engine.",
            },
            new()
            {
                Name = FestivalsTag,
                Description = "Upcoming Hindu festivals and observances at a location, for a regional tradition.",
            },
            new()
            {
                Name = GlossaryTag,
                Description = "Localized meanings, descriptions, messages and classification labels for every Panchang item.",
            },
            new()
            {
                Name = LocationsTag,
                Description = "Resolve a typed place or name the browser's position before loading the calendar.",
            },
        };

        return Task.CompletedTask;
    }
}
