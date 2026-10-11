> This guide was migrated from the original Day repository. Run its commands from the combined repository root.
> Shared hosting and deployment are documented in [the repository README](../../README.md).

# MyVedicDay

Location-aware Vedic Panchang platform. Phase 1 provides a REST API that calculates the daily Panchang (sunrise,
sunset, Tithi, Nakshatra, Yoga, Karana, Rahu Kalam, Yamagandam, Gulika Kalam, Dur Muhurtam, Varjyam, Abhijit
Muhurat, Amrit Kalam, Brahma Muhurta) for any coordinates, with DST-safe time zones and daily caching.

Architecture and design decisions: [docs/architecture](architecture/README.md) and [docs/adr](adr).

## Solution layout

A modular monolith: one API host, feature domains, and shared platform modules
([ADR-0008](adr/0008-platform-modules-and-single-api-host.md)).

| Kind | Projects |
|------|----------|
| Host | `MyVedicDay.Api` (controllers per domain, OpenAPI, cross-cutting HTTP concerns) |
| Feature: Panchang | `MyVedicDay.Panchang.Business`, `.Calculation`, `.Data` |
| Feature: Festivals | `MyVedicDay.Festivals.Business`, `.Calculation`, `.Data` |
| Feature: Muhurta (Find a Good Time) | `MyVedicDay.Muhurta.Business`, `.Calculation`, `.Data` |
| Platform | `MyVedicDay.Astronomy.Calculation`, `MyVedicDay.Location.Business` + `.Integration`, `MyVedicDay.Localization.Business` + `.Data`, `MyVedicDay.Traditions.Business`, `MyVedicDay.Caching`, `MyVedicDay.Ai.Integration` |
| Shared | `MyVedicDay.Shared` (exceptions) |

New features (Muhurta, Kundali, …) add `MyVedicDay.{Feature}.{Business,Calculation,Data}` on top of the platform
modules and one `Add{Feature}Business` line in `Program.cs`.

## Run

Requires the .NET 10 SDK and, for the private `MyVedicStar.ApiProtection` package, a GitHub personal access token
with the `read:packages` scope in `GITHUB_TOKEN` (see [nuget.config](nuget.config)):

```bash
export GITHUB_TOKEN=<token>        # PowerShell: $env:GITHUB_TOKEN = "<token>"
dotnet run --project src/MyVedicDay.Api --launch-profile http
curl "http://localhost:5275/api/v1/panchang/today?latitude=17.385&longitude=78.4867"
curl "http://localhost:5275/api/v1/panchang/today?place=Austin,%20Texas,%20USA"
```

API documentation (on in every environment by default; set `ApiDocumentation__Enabled=false` to hide it):

- Swagger UI: `http://localhost:5275/swagger` — overview, location modes, time semantics, every parameter with
  examples, example responses and error codes; *Try it out* is enabled.
- OpenAPI 3.1 document: `http://localhost:5275/openapi/v1.json`.

Without configuration the API uses an in-process cache. To use Redis locally:

```bash
docker compose up -d redis
Redis__ConnectionString=localhost:6379 dotnet run --project src/MyVedicDay.Api --launch-profile http
```

## Endpoints

| Endpoint | Description |
|----------|-------------|
| `GET /api/v1/panchang/today` | Current local date at the location |
| `GET /api/v1/panchang/tomorrow` | Next local date |
| `GET /api/v1/panchang/ten-days` | Ten days from today |
| `GET /api/v1/panchang/{yyyy-MM-dd}` | One date |
| `GET /api/v1/panchang/range?start=&days=` | Up to 366 days |
| `GET /api/v1/panchang/month/{year}/{month}` | Calendar month |
| `GET /api/v1/panchang/year/{year}` | Calendar year |
| `GET /api/v1/festivals/upcoming?days=30` | Festivals and observances from local today (1–90 days, default 30) |
| `GET /api/v1/muhurta/activities` | Activities for "What are you planning?" (cards, icons, default `general`) |
| `GET /api/v1/muhurta/good-times?activity=&date=` | One day evaluated for an activity: segments, statuses, reasons, conflicts, Good Times |
| `GET /api/v1/muhurta/good-times/explanation` | "Why this time?" narrative (AI when enabled and valid, else template) |
| `GET /api/v1/muhurta/day-summaries?start=&days=&activity=&otherText=` | Day status per date for calendar tiles and the Find a Good Time month view (also `goodTime` on every Panchang day) |
| `GET /api/v1/muhurta/good-times/best?activity=&count=3&days=30` | Next best Good Times: at most one per day, Highly Favorable before Favorable, then soonest |
| `GET /api/v1/locations/resolve` | Resolve a place or name the browser position (no calculation) |
| `GET /api/v1/panchang/glossary` | Meanings, descriptions, messages and classification labels |
| `GET /api/v1/panchang/traditions` | The Panchang timings each tradition shows |
| `GET /api/v1/traditions` | Shared tradition list (names and regions) for a tradition picker |

Location, one of:

- `place=City, State, Country` (e.g. `Austin, Texas, USA`)
- `place=City, State` plus the browser's `latitude`/`longitude`, which supply the country
- only the browser's `latitude`/`longitude`

Optional: `timezone` (IANA zone, validated but the location's own zone wins), `tradition` (default `all`),
`language` (e.g. `te`).

Place data © [GeoNames](https://www.geonames.org), [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/);
regenerate with `dotnet run tools/build-geonames-places.cs`.

## Daily guidance

Every day includes `guidance`: a rating from the five Panchanga limbs, a headline and summary, and time windows for the
selected tradition. For example:

> **🟡 Mixed Day**. Today's Panchang factors are mixed; choose timings carefully for important activities. Tithi
> Krishna Tritiya and Yoga Harshana are supportive. Tuesday, Nakshatra Bharani and Karana Vishti (Bhadra) call for
> caution. Best time: 06:46–08:30 IST, 10:36–12:06 IST, 13:36–15:06 IST. Favorable: Abhijit Muhurta 11:42–12:30 IST.
> Avoid for new beginnings: Rahu Kalam 15:06–16:36 IST, …

Windows (Rahu Kalam, Varjyam…) never change the day rating. Periods come from different Panchanga rules and can
overlap: Amrit Kalam (Nakshatra-based, favorable) 14:58–16:27 and Rahu Kalam (weekday/daylight-based, avoided for new
beginnings) 14:49–16:18 overlap 14:58–16:18. Neither cancels the other, so that stretch is listed in `mixedWindows`
(🟡 Mixed) with both periods. The favorable period itself also carries `overlaps` and an `overlapMessage`, e.g. "Overlaps
Rahu Kalam (14:49–16:18 IST): avoid 14:58–16:18 IST for new beginnings." This is the app's own methodology, **not a Muhurta**:
activity-specific questions ("a good day for a marriage?") are for the planned Muhurta module.

- **Rules:** `src/MyVedicDay.Panchang.Data/Content/panchang-guidance-rules.json` (versioned) holds weights, score
  bands, a rating per Tithi, Nakshatra, Yoga, Karana and weekday, and per-tradition overrides. A file of the same name
  in `Content:Directory` replaces it. The initial ratings are provisional.
- **Wording:** `guidanceRatings.*`, `guidanceText.*` and `guidanceGroups.*` in the Panchang content catalog.

## Find a Good Time

Pick an activity (Business, Travel, Property, Vehicle, Agreement, Education, Puja / Spiritual, Wedding, Housewarming,
General, or Other with a description) and the backend evaluates the day for it
([design](architecture/find-a-good-time.md), [ADR-0011](adr/0011-muhurta-module-and-rules-engine.md)).

- **Rules engine.** The day is cut wherever an input changes (every Muhurta boundary, every Tithi, Nakshatra, Yoga and
  Karana change). Each segment gets a status (`HighlyFavorable`, `Favorable`, `Mixed`, `Caution`, `Avoid`) from the
  activity's rules and a conflict matrix. Abhijit 16:00–16:48 with Dur Muhurtam 16:06–16:27 gives Favorable /
  Caution / Favorable; Amrit Kalam with Rahu Kalam is Mixed.
- **Only `isGoodTime: true` is a Good Time**: Highly Favorable or Favorable, no conflict, at least 24 minutes. When none
  qualifies, `goodTimes` is empty with a reason; nothing is promoted.
- **One evaluation everywhere.** Panchang days carry `goodTime` (General) for the calendar and Home; `day-summaries`
  returns the same values. Clients never derive Good Times.
- **Data-driven.** `src/MyVedicDay.Muhurta.Data/Content/muhurta-activities.json` (versioned, provisional) holds value
  groups, defaults, the activities and per-tradition overrides (Choghadiya for Gujarati business, Varjyam for Telugu).
  New activities are data plus content. Wedding and Housewarming are `partial` (no Lagna or personal charts) and
  capped at Favorable.
- **Generative AI (optional, [ADR-0012](adr/0012-provider-neutral-generative-ai.md)).** Explains the result and maps
  "Other" text to an activity; it never decides statuses or times. Switch provider in configuration:

  ```bash
  dotnet user-secrets --project src/MyVedicDay.Api set "Ai:Enabled" "true"
  dotnet user-secrets --project src/MyVedicDay.Api set "Ai:Provider" "Anthropic"     # or OpenAI, AzureOpenAI
  dotnet user-secrets --project src/MyVedicDay.Api set "Ai:Anthropic:ApiKey" "<key>" # model: Ai:Anthropic:Model (claude-opus-5-5)
  ```

  AI text is used only when it mentions no time or segment outside the evaluation and calls nothing but a Good Time
  good; otherwise the deterministic template is returned with `fallbackReason`. Prompts are versioned data
  (`muhurta-ai-prompts.json`).

## Festivals

`GET /api/v1/festivals/upcoming` lists the next 30 days of festivals (`days` 1–90) for the selected `tradition`. Every
Panchang day also carries `festivals`, so the today, ten-day, month and year views show a festival on its day.

- **Dates come from the location.** A festival falls on the day its Tithi or Nakshatra prevails during the
  traditional observance time: Madhyahna for Ganesh Chaturthi, Pradosha for Diwali, Nishita for Janmashtami. Solar
  festivals follow the Sankranti. Lunar months are Amanta, and Adhika months are detected (2026 has Adhika Jyeshtha).
- **Traditions choose the festivals and their names.** `telugu` lists Ugadi, Vinayaka Chavithi and Mukkoti Ekadashi;
  `tamil` lists Puthandu, Thai Pongal and Karthigai Deepam; `north-indian` lists Karwa Chauth and Holi; `all` lists
  everything.
- **Categories:** `festival`, or `observance` for recurring vrata (Ekadashi, Pradosham, Purnima…). A named festival
  hides the generic observance on the same day.
- **Rules:** `src/MyVedicDay.Festivals.Data/Content/festival-rules.json` (versioned, provisional). Names are in
  `festivals-content.{language}.json`, with regional names keyed `{tradition}/festivals.{key}`. See
  [ADR-0010](adr/0010-festivals-feature-module.md) for the method and its known simplifications.

## Panchang traditions

Location drives the calculation; `tradition` drives what is shown. Optional, default `all`:

| `tradition` | Shows |
|-------------|-------|
| `all` | every timing, including Choghadiya |
| `telugu` | Varjyam, Durmuhurtham, Rahu Kalam, Yamagandam, Gulika, Amrita Kalam, Abhijit Muhurtham, Brahma Muhurtham |
| `tamil` | Rahu Kalam, Yamagandam, Kuligai, Amirtha Kalam, Abhijit Muhurtham, Brahma Muhurtham |
| `kannada` | Rahu Kalam, Yamagandam, Gulika, Durmuhurtha, Amrita Kala, Abhijit Muhurtha, Brahma Muhurtha |
| `malayalam` | Rahu Kalam, Yamagandam, Gulika, Amrutha Kalam, Abhijit Muhurtham, Brahma Muhurtham |
| `north-indian` | Rahu Kaal, Choghadiya, Abhijit Muhurat, Amrit Kaal, Brahma Muhurat |
| `gujarati` | Choghadiya, Rahu Kaal, Abhijit Muhurat, Amrit Kaal, Brahma Muhurat |
| `marathi` | Rahu Kaal, Abhijit Muhurat, Choghadiya, Amrit Kaal, Brahma Muhurat |

`GET /api/v1/traditions` lists the traditions (shared by every feature; `src/MyVedicDay.Traditions.Business/Content/traditions.json`).
`GET /api/v1/panchang/traditions` shows Panchang's profile of each: `src/MyVedicDay.Panchang.Data/Content/panchang-traditions.json`.
Files with the same names in `Content:Directory` replace them without a rebuild. Regional names are content entries
such as `tamil/periods.GulikaKalam`.

## Date and time display

Every time is returned twice: ISO 8601 for programs (`sunrise`, `start`, `end`) and a display string in the
location's format for people (`sunriseDisplay`, `startDisplay`, `endDisplay`, `dateDisplay`):

| Location | Example |
|----------|---------|
| United States | `09/28/2026 07:22:45 CDT` (`CST` in winter) |
| India | `28/09/2026 06:05:56 IST` |
| Germany | `28.09.2026 07:10:31 CEST` |
| Japan | `2026/09/28 05:33:26 JST` |

The country (from the matched city, else the time zone) selects the date pattern: `DateTimeDisplay:CountryDatePatterns`
first (US and IN are set explicitly), otherwise the country's standard convention, otherwise `DefaultDatePattern`.
Time is `TimePattern` (24-hour by default). Zones without a letter abbreviation show `UTC±hh:mm`.

## Languages, meanings and classifications

Every response includes a `glossary` (once, not per item) with the `name`, `meaning`, `description`, one-line
`message` and `icon` of every period, element (Tithi, Nakshatra, Yoga, Karana), term and classification. Each period
carries a `classification`: `HighlyFavorable`, `Favorable`, `Caution` or `Avoid`. `GET /api/v1/panchang/glossary` returns
the glossary alone.

- **Language:** `?language=te` or the `Accept-Language` header. The chosen language is returned in `language` and
  `Content-Language`; untranslated fields fall back to the default language.
- **Text:** each module has its own catalog, e.g. `src/MyVedicDay.Panchang.Data/Content/panchang-content.{language}.json`
  and `src/MyVedicDay.Traditions.Business/Content/traditions-content.{language}.json`. To change text without
  rebuilding, put files with the same names in the folder set by `Content:Directory`; they override field by field.
- **Adding a language:** add `{module}-content.te.json` files (built in or in `Content:Directory`), then add `te` to
  `Localization:SupportedLanguages`. They can start with only some entries.
- **Classifications:** `Panchang:PeriodClassifications` in configuration; unknown or missing period kinds fail at
  startup.

## Test

```bash
dotnet test
```

| Project | Covers |
|---------|--------|
| `MyVedicDay.Astronomy.Tests.Calculation` | Meeus worked examples, sunrise/sunset against published times |
| `MyVedicDay.Panchang.Tests.Calculation` | Panchang elements, periods and Choghadiya against known events and rules |
| `MyVedicDay.Location.Tests.Unit` | Geocoding, place/coordinate resolution, time zones |
| `MyVedicDay.Panchang.Tests.Unit` | Panchang business logic, caching, localization, traditions, display formats |
| `MyVedicDay.Festivals.Tests.Calculation` | Festival dates against published dates, lunar month and Adhika, Sankranti |
| `MyVedicDay.Festivals.Tests.Unit` | Rules and content consistency, tradition filtering and names, supersedes, caching |
| `MyVedicDay.Muhurta.Tests.Calculation` | Requirement conflict examples to the minute, never-promote invariants over real seasons |
| `MyVedicDay.Muhurta.Tests.Unit` | Rules/content for every activity × tradition, Other mapping, now/next, summaries, caching, AI guardrails |
| `MyVedicDay.Ai.Tests.Unit` | Provider selection, timeout, circuit breaker, invalid output, configuration validation |
| `MyVedicDay.Caching.Tests.Unit` | Cache failures degrade to a miss, Redis vs in-process registration, readiness probe |
| `MyVedicDay.Api.Tests.Integration` | HTTP API end to end, OpenAPI document, exception → Problem Details mapping, per-environment settings, module dependency rules |

## Configuration

For reusable Azure Container Apps deployment automation, see [deployment/README.md](deployment/README.md).

Settings files in `src/MyVedicDay.Api`:

- `appsettings.json` holds every setting with its shared default. Nothing tunable is hard-coded in the code.
- `appsettings.Development.json` overrides it locally: debug logging, a `dev` cache key prefix, higher rate limits
  and the local UI origins for CORS.
- `appsettings.Production.json` overrides it in production (`ASPNETCORE_ENVIRONMENT=Production`): production log
  levels, a `prod` cache key prefix, and the trusted proxy subnet of the Container Apps environment.

Secrets (Redis connection string, AI keys) and per-deployment values (CORS origins, Application Insights) come from
environment variables or Key Vault, never from these files. Every section is validated at startup.

| Section | Keys |
|---------|------|
| `Location` | `CoordinateDecimalPlaces`, `NearestCityMaxDistanceKm` (coordinates are named after a city this close), `CountryHintMaxDistanceKm` (place search prefers the country of a city this close) |
| `Traditions` | `DefaultTradition` |
| `Panchang` | `MaxRangeDays`, `PastDateCacheLifetime`, `MaxCacheLifetime`, `PeriodClassifications` |
| `Redis` | `ConnectionString` (supply from a secret store), `InstanceName` |
| `ApiProtection` | Shared edge protection ([ADR-0014](adr/0014-shared-api-protection-library.md)): `RateLimiting` (`Global`, `PublicRead`, `GoodTime`, `ExpensiveAstrology`, … each `PermitLimit`/`WindowSeconds`), `RequestSize:StandardPostBytes`, `TrustedProxies` (IPs/CIDRs whose `X-Forwarded-For` is trusted), `HealthCheckPaths` (never rate limited), `UserIdClaim`, `IssuerClaim` |
| `Muhurta` | `DefaultActivity`, `MaxRangeDays`, `BestDefaultCount`, `BestSearchDefaultDays`, `BestSearchMaxDays`, `BestMaxCount`, `OtherTextMaxLength`, `OtherTextMinimumConfidence` (AI match threshold for "Other"), `PastDateCacheLifetime`, `MaxCacheLifetime` |
| `Festivals` | `MaxUpcomingDays`, `MaxRangeDays`, `PastDateCacheLifetime`, `MaxCacheLifetime` |
| `Ai` | `Enabled`, `Provider` (`None`, `Anthropic`, `OpenAI`, `AzureOpenAI`), `Model`, `MaxOutputTokens`, `Anthropic:Model`, `Anthropic:ApiKey`, `OpenAI:ApiKey`, `AzureOpenAI:Endpoint`/`Deployment`/`ApiKey` (keys from user-secrets or Key Vault). Resilience uses `ApiProtection:Timeouts:IntegrationCallMs`, `CircuitBreaker` and `Bulkhead`. |
| `Cors` | `AllowedOrigins` |
| `ApiDocumentation` | `Enabled` (Swagger UI / OpenAPI outside Development; default `true`) |
| `Localization` | `DefaultLanguage`, `SupportedLanguages` |
| `DateTimeDisplay` | `DefaultDatePattern`, `TimePattern`, `DeriveFromCountryConventions`, `CountryDatePatterns` |
| `Content` | `Directory` (optional content overrides / additional languages) |
| `Observability` | `ServiceName`, `OtlpEndpoint` |
| Deployment | `APPLICATIONINSIGHTS_CONNECTION_STRING` is supplied by Azure deployment automation, not `appsettings.json`. |
