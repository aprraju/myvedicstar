# MyVedicDay — Panchang Platform Architecture

Status: **Phase 1 implemented (backend)**. This document is the design baseline. Changes that materially affect
it are recorded as ADRs in [`docs/adr`](../adr).

---

## 1. Requirements

### Functional (Phase 1)

| # | Requirement | Where it is met |
|---|-------------|-----------------|
| F1 | Panchang for Today, Tomorrow, 10 days, a month, a year, any date or date range | `IPanchangFacade`, `PanchangController` |
| F2 | Per day: date, weekday, sunrise, sunset, Tithi, Nakshatra, Yoga, Karana | `PanchangCalculator` |
| F3 | Rahu Kalam, Yamagandam, Gulika Kalam, Dur Muhurtam, Varjyam | `RuleBasedPanchangTraditionStrategy` |
| F4 | Good times: Abhijit Muhurat (where applicable), Amrit Kalam, Brahma Muhurta | same |
| F5 | Every period has a start **and** end; Tithi/Nakshatra transitions are listed in order | `ElementPeriod`, API `tithis[]` / `nakshatras[]` |
| F6 | Periods crossing midnight carry their true dates | all times are absolute instants rendered with offset |
| F7 | Calculations use actual latitude/longitude, not just country or zone | `SunEventCalculator` |
| F8 | Manual location selection | any coordinates can be passed on every request |

### Non-functional

- **Accuracy:** Tithi/Nakshatra transitions and sunrise within ±1–2 minutes of published almanacs (verified in tests).
- **Time zones:** IANA zones, UTC instants internally, DST-safe; never fixed offsets.
- **Caching:** daily entries keyed by location + local date + calculation method + calculation version; expire at the
  location's next local midnight.
- **Scalability:** stateless API instances behind a load balancer, shared Redis.
- **Extensibility:** Panchang is the first domain; Kundali, Dasha, Muhurta and others follow the same layering.

### Explicitly deferred

Frontend, user accounts/saved locations, authentication, festival calendar, city autocomplete. See §13.

---

## 2. Solution architecture

The solution is a **modular monolith**: one deployable API host, feature **domains** (Panchang today; Muhurta, Kundali
and others later), and shared **platform modules** that every domain builds on
([ADR-0008](../adr/0008-platform-modules-and-single-api-host.md)).

```
                      Browser / mobile
                             │
               ┌─────────────▼──────────────┐
               │ MyVedicDay.Api (host)      │  HTTP, validation, Problem Details, rate limiting, correlation IDs,
               │ Controllers/{Panchang,     │  request logging, OpenAPI + Swagger UI, health checks
               │   Locations, Traditions}   │
               └──┬─────────┬─────────┬─────┘
       facades only│         │         │
   ┌───────────────▼──┐ ┌────▼──────┐ ┌▼─────────────┐   future: Muhurta.Business, Kundali.Business …
   │ Panchang.Business│ │ Location. │ │ Traditions.  │
   │ (facade, cache-  │ │ Business  │ │ Business     │
   │  aside, present.)│ └────┬──────┘ └──────┬───────┘
   └─┬──────┬─────────┘      │               │
     │      │         Location.Integration   │
     │      │         (GeoNames, GeoTimeZone)│
     │      ▼                                ▼
     │  Panchang.Data ─────────────►  Localization.Business ─► Localization.Data (content repository)
     │  (content, tradition profiles)
     ▼
  Panchang.Calculation ─► Astronomy.Calculation            Caching (Redis / in-memory, coalescing, local-day expiry)

                    every module ──► MyVedicDay.Shared (exceptions)
```

| Kind | Projects | Rule |
|------|----------|------|
| Host | `MyVedicDay.Api` | References Business modules (facades) and Shared only. |
| Feature domain | `MyVedicDay.Panchang.{Business,Calculation,Data}` | May use platform modules; never another domain's internals. |
| Platform | `Astronomy.Calculation`, `Location.{Business,Integration}`, `Localization.{Business,Data}`, `Traditions.Business`, `Caching` | Never reference a feature domain. |
| Shared | `MyVedicDay.Shared` | Exceptions and small primitives only. |

The rules are enforced by `LayerDependencyTests` (in `MyVedicDay.Api.Tests.Integration`), which lists the MyVedicDay
assemblies each assembly may reference and fails if any MyVedicDay assembly has no rule. Each feature registers the
platform modules it needs in its `Add{Feature}Business` call ([ADR-0001](../adr/0001-business-layer-composes-lower-layers.md));
platform registrations are idempotent, so a new feature adds one line to `Program.cs`.

### Projects

| Project | Responsibility | Public surface |
|---------|----------------|----------------|
| `MyVedicDay.Api` | HTTP host; controllers and contracts per domain | endpoints |
| `MyVedicDay.Panchang.Business` | Panchang orchestration, cache-aside, presentation per tradition | `IPanchangFacade`, `IGlossaryFacade`, `IPanchangTraditionFacade`, `PanchangOptions` |
| `MyVedicDay.Panchang.Calculation` | Panchang elements, periods, calculation methods; pure | `IPanchangCalculator`, `IPanchangTraditionCatalog`, models |
| `MyVedicDay.Panchang.Data` | Panchang content catalog, tradition profiles | `ITraditionProfileRepository`, `AddPanchangData` |
| `MyVedicDay.Festivals.Business` | festival orchestration, cache-aside, tradition filtering and names | `IFestivalFacade`, `FestivalOptions` |
| `MyVedicDay.Festivals.Calculation` | festival dates: lunar month, Adhika, Sankranti, observance windows; pure | `IFestivalCalculator`, models |
| `MyVedicDay.Festivals.Data` | festival rules and content catalog | `IFestivalRulesRepository`, `AddFestivalData` |
| `MyVedicDay.Muhurta.Business` | Find a Good Time: activities, rule profiles per tradition, cache-aside, explanations | `IMuhurtaFacade`, `MuhurtaOptions` |
| `MyVedicDay.Muhurta.Calculation` | segment timeline, rule interpreter, conflict resolution, Good Time selection; pure | `IMuhurtaCalculator`, models, `ReasonCodes` |
| `MyVedicDay.Muhurta.Data` | activity rules, content catalog, AI prompts | `IMuhurtaRulesRepository`, `IMuhurtaPromptRepository`, `AddMuhurtaData` |
| `MyVedicDay.Ai.Integration` | provider-neutral generative AI (Anthropic, OpenAI, Azure OpenAI) with timeout and circuit breaker | `IStructuredTextGenerator`, `AiOptions` |
| `MyVedicDay.Astronomy.Calculation` | Sun/Moon positions, sunrise/sunset, ayanamsa, transition search; pure | `IEphemeris`, `ISunEventCalculator`, `LahiriAyanamsa`, `AngularCrossingFinder`, `JulianDay`, `GeoCoordinate` |
| `MyVedicDay.Location.Business` | place/coordinate resolution, time zone, coordinate rounding | `ILocationResolver`, `ILocationFacade`, `LocationQuery`, `ResolvedLocation`, `LocationOptions` |
| `MyVedicDay.Location.Integration` | geocoding and coordinate → zone adapters | `IGeocodingProvider`, `ITimeZoneLookupProvider` |
| `MyVedicDay.Localization.Business` | language negotiation, content with fallback, date/time display | `ILocalizedContentProvider`, `IDateTimeDisplayFormatter`, `LocalizationOptions`, `DateTimeDisplayOptions` |
| `MyVedicDay.Localization.Data` | content repository combining every module's catalogs | `IContentRepository`, `AddContentSource`, `ContentOptions` |
| `MyVedicDay.Traditions.Business` | the shared list of regional traditions | `ITraditionRegistry`, `ITraditionFacade`, `TraditionsOptions` |
| `MyVedicDay.Caching` | distributed object cache, coalescing, local-day expiry | `IDistributedObjectCache`, `InFlightRequestCoalescer<T>`, `LocalDayCacheExpiration`, `RedisOptions` |
| `MyVedicDay.Shared` | exceptions shared by all modules | exception types |

**Adding a feature (e.g. marriage Muhurta):** create `MyVedicDay.Muhurta.{Business,Calculation,Data}`.
- `Muhurta.Calculation` uses `Astronomy.Calculation` and Panchang's *calculation* contract (`IPanchangCalculator`)
  for Tithi and Nakshatra as data, never the Panchang display facade.
- `Muhurta.Data` contributes `muhurta-content.{language}.json` (its own content categories) and
  `muhurta-traditions.json` (its rules per shared tradition).
- `Muhurta.Business` uses `ILocationResolver`, `ILocalizedContentProvider`, `ITraditionRegistry` and
  `IDistributedObjectCache`.
- Add `AddMuhurtaBusiness` to `Program.cs`, controllers under `Controllers/Muhurta`, and a rule row in
  `LayerDependencyTests`.

---

## 3. Panchang calculation approach

The engine separates **astronomical facts** from **traditional interpretation**:

```
Ephemeris (IEphemeris)          Sun & Moon tropical longitude, Sun RA/Dec          ← astronomy
   │
   ├─ SunEventCalculator         sunrise / sunset for lat, lon, date, zone           ← astronomy + tradition's sunrise definition
   ├─ PanchangElementCalculator  Tithi, Nakshatra, Yoga, Karana + exact transitions  ← astronomy + tradition's ayanamsa
   │
IPanchangTraditionStrategy      Rahu/Yama/Gulika, Muhurtas, Varjyam, Amrit Kalam     ← tradition (rule tables)
```

### Astronomical layer

| Quantity | Method | Accuracy |
|----------|--------|----------|
| Solar longitude, RA/Dec | Meeus ch. 25 + nutation (ch. 22) | ~0.01° |
| Lunar longitude | Meeus ch. 47 (truncated ELP-2000/82, 59 terms) | ~10″ |
| ΔT (TT − UT) | Espenak–Meeus polynomials | a few seconds, 1900–2100 |
| Sidereal time | Meeus 12.4 | < 0.1 s |
| Sunrise/sunset | hour-angle iteration to 0.1 s on the apparent Sun | < 1 min vs almanacs |
| Transitions | Newton iteration on the angle function to 0.1 s | < 1 min vs published new/full moons |

Definitions:

- **Tithi** = ⌊(λ☾ − λ☉) / 12°⌋ + 1 (1–30). Independent of ayanamsa.
- **Karana** = half-Tithi (6°): 1 = Kimstughna, 2–57 cycle Bava…Vishti, 58–60 Shakuni, Chatushpada, Naga.
- **Nakshatra** = ⌊(λ☾ − ayanamsa) / 13°20′⌋ + 1.
- **Yoga** = ⌊(λ☉ + λ☾ − 2·ayanamsa) / 13°20′⌋ + 1.
- A **Panchang day** runs sunrise → next sunrise. Element lists start with the element prevailing at sunrise and
  include every transition before the next sunrise, each with exact start and end instants.

The ephemeris sits behind `IEphemeris`, so a Swiss Ephemeris or JPL adapter can replace the analytical series
without touching anything else ([ADR-0002](../adr/0002-analytical-ephemeris.md)).

### Traditional layer — where conventions differ

| Convention | Options | Phase 1 (Drik) default | Configured in |
|------------|---------|------------------------|---------------|
| Sunrise definition | upper limb + refraction / disc centre ± refraction | upper limb + refraction | `SunriseDefinition` |
| Ayanamsa | Lahiri, Raman, KP, Yukteshwar, True Chitra… | Lahiri (mean) | `IAyanamsa` |
| Rahu/Yamagandam/Gulika segment per weekday | standard 8-part tables | standard | `PanchangTraditionRules` |
| Dur Muhurtam slots | varies by almanac | Sun 14; Mon 9, 12; Tue 4 + night 7; Wed 8; Thu 6, 12; Fri 4, 9; Sat 1, 2 | same |
| Abhijit on Wednesday | excluded / included | excluded | same |
| Varjyam ghati table | notable regional differences (e.g. Ardra 11 vs 21, Mula 20 vs 56) | see `DrikPanchangStrategy` | same |
| Amrit Kalam ghati table | regional differences | see `DrikPanchangStrategy` | same |
| Day boundary | sunrise (civil date labels the day) | sunrise → next sunrise | engine |
| Month system (future) | Amanta / Purnimanta | — | future strategy |

> **Validation required before launch:** the Varjyam and Amrit Kalam ghati tables and the Dur Muhurtam slots must be
> checked against the reference almanac chosen for each tradition. Changing any table requires bumping
> `PanchangCalculationVersion.Current`, which invalidates every cache entry.

The engine also computes **Choghadiya**: the day (sunrise→sunset) and night (sunset→next sunrise) are each split
into 8 segments. The day sequence is Udveg, Chal, Labh, Amrit, Kaal, Shubh, Rog, and the night sequence is Shubh,
Amrit, Chal, Rog, Kaal, Labh, Udveg. Each starts at a weekday-specific segment set by the rule tables.

A different *calculation method* (for example a KP or Surya Siddhanta ayanamsa) derives from
`RuleBasedPanchangTraditionStrategy` or implements `IPanchangTraditionStrategy`. It is internal, and regional
traditions refer to it by key (§3a).

### 3a. Panchang traditions: calculation vs presentation

**Location drives the calculation; tradition drives the presentation.** A Telugu family in Dallas wants Telugu
conventions computed for Dallas, so tradition is never inferred from GPS.

```
latitude/longitude/zone ─► calculation method (drik) ─► all timings, cached once per location and date
tradition (telugu, tamil, …) ─► which timings to show, emphasis order, regional names ─► response
```

| Tradition | Shows, in order of emphasis | Regional names |
|-----------|-----------------------------|----------------|
| `all` (default) | every timing, including Choghadiya | — |
| `telugu` | Varjyam, Durmuhurtham, Rahu Kalam, Yamagandam, Gulika, Amrita Kalam, Abhijit Muhurtham, Brahma Muhurtham | Durmuhurtham, Amrita Kalam, Abhijit Muhurtham, Brahma Muhurtham |
| `tamil` | Rahu Kalam, Yamagandam, Kuligai, Amirtha Kalam, Abhijit Muhurtham, Brahma Muhurtham | Kuligai, Amirtha Kalam, Abhijit Muhurtham, Brahma Muhurtham |
| `kannada` | Rahu Kalam, Yamagandam, Gulika, Durmuhurtha, Amrita Kala, Abhijit Muhurtha, Brahma Muhurtha | Durmuhurtha, Amrita Kala, Abhijit Muhurtha, Brahma Muhurtha |
| `malayalam` | Rahu Kalam, Yamagandam, Gulika, Amrutha Kalam, Abhijit Muhurtham, Brahma Muhurtham | Amrutha Kalam, Abhijit Muhurtham, Brahma Muhurtham |
| `north-indian` | Rahu Kaal, Choghadiya, Abhijit Muhurat, Amrit Kaal, Brahma Muhurat | Rahu Kaal, Abhijit Muhurat, Amrit Kaal, Brahma Muhurat |
| `gujarati` | Choghadiya, Rahu Kaal, Abhijit Muhurat, Amrit Kaal, Brahma Muhurat | Rahu Kaal, Abhijit Muhurat, Amrit Kaal, Brahma Muhurat |
| `marathi` | Rahu Kaal, Abhijit Muhurat, Choghadiya, Amrit Kaal, Brahma Muhurat | Rahu Kaal, Abhijit Muhurat, Amrit Kaal, Brahma Muhurat |

- **The tradition list is shared** (`Traditions` module, `traditions.json`); Panchang maps each tradition to a
  profile, and a tradition without one uses the `all` profile.
- **Profiles are data.** `Panchang.Data/Content/panchang-traditions.json` holds `{ key, calculationMethod, display[] }`, where
  `display` contains period kinds or the `Choghadiya` group. `panchang-traditions.json` in `Content:Directory`
  replaces profiles by key or adds new ones without a redeploy.
- **Regional names are content.** Keys `{tradition}/periods.{kind}` (e.g. `tamil/periods.GulikaKalam` = Kuligai)
  override the generic name per field and per language.
- **Traditions share calculations.** The cache key uses the calculation method, not the tradition, so switching
  tradition never recalculates.
- **Pending review:** the `malayalam` and `marathi` profiles are provisional. Tamil *Nalla Neram* is not implemented
  yet because its per-weekday tables differ between almanacs; it will be added as a period kind once a reference is
  chosen.

---

### 3b. Festivals

A separate feature module (ADR-0010) built on the Panchang calculation contract:

```
IPanchangCalculator (Tithi, Nakshatra, sun events) ─┐
LunisolarCalendar (Amanta month, Adhika, Sankranti) ─┼─► IFestivalCalculator ─► IFestivalFacade ─► API
festival-rules.json (rules, traditions, supersedes) ─┘   (pure)                  (cache, tradition, names)
```

- A Tithi or Nakshatra rule picks the day on which the element covers most of its observance window (Sunrise,
  Arunodaya, Purvahna, Madhyahna, Aparahna, Sunset, Pradosha, Nishita). Month-specific rules skip Adhika months.
- A Sankranti rule picks the first day whose observance time is at or after the Sankranti. Offsets give the eve or
  the day after (Bhogi, Kanuma, Holi).
- Daily entries are cached per location without tradition or language. The tradition then filters them, applies
  `supersedes`, and localizes regional names.
- Panchang days carry `festivals`: `PanchangController` composes `IPanchangFacade` and `IFestivalFacade`, because
  feature business layers never depend on each other.

## 4. Location and time zone

The client specifies a location in one of three ways:

| Input | Resolution |
|-------|------------|
| `place=City, State, Country` | geocoded within that country |
| `place=City, State` + browser `latitude`/`longitude` | country inferred from the browser position (nearest city within 300 km); if the city isn't there, searched globally, so a traveller can look up home |
| browser `latitude`/`longitude` only | used directly; labelled with the nearest city within 50 km |

1. A place always determines the coordinates; browser coordinates only disambiguate it. Browser coordinates require
   the user's explicit Geolocation permission.
2. Search order for `City, X` without a country: browser country → anywhere (primary names before historic alternate
   names, then by population) → `X` read as a country ("Hyderabad, India").
3. Geocoding uses an embedded GeoNames extract: 69,750 cities with population ≥ 5,000, plus state and country names,
   ISO codes and common aliases such as "USA", "UK" and "Bangalore". It is offline, with no key and no per-request
   cost, behind `IGeocodingProvider` ([ADR-0004](../adr/0004-offline-geonames-geocoding.md)).
   `GET /api/v1/locations/resolve` lets the UI confirm the match ("Austin, Texas, United States") before loading
   the calendar. **Attribution "Place data © GeoNames (CC BY 4.0)" must be shown in the UI.**
4. Coordinates are validated and rounded to `CoordinateDecimalPlaces` (default 2 ≈ 1.1 km). Rounding changes sunrise
   by at most a few seconds, bounds the cache key space and minimizes stored precision. A place search and a browser
   position in the same cell share cache entries.
5. The time zone is **derived from the coordinates** (`GeoTimeZone`, offline). A client-supplied `timezone` is
   validated but only used when no zone can be derived. The API never trusts it blindly.
6. "Today" is `clock.Now` in the location's zone, never the server's date.

IP-based geolocation is a possible future fallback in the Integration layer, clearly labelled as approximate.

---

## 5. Data model

Phase 1 has no relational store: Panchang is deterministic and recomputable, so the distributed cache is sufficient.
Planned schema for later phases (EF Core, SQL Server / PostgreSQL):

| Table | Key columns | Notes |
|-------|-------------|-------|
| `Locations` | Id, Name, CountryCode, Latitude, Longitude, TimeZoneId | seeded city catalogue for search; index on normalized name |
| `UserProfiles` | Id, ExternalSubjectId, DisplayName, PreferredTradition, Locale | ExternalSubjectId from the identity provider |
| `SavedLocations` | Id, UserProfileId, LocationId or coordinates, Label, IsDefault | |
| `BirthDetails` | Id, UserProfileId, BirthInstantUtc, BirthTimeZoneId, Latitude, Longitude | encrypted at rest; sensitive personal data |
| `FestivalDefinitions` | Id, Tradition, RuleType, RuleParameters | rules evaluated against daily Panchang |

Database entities stay in `.Data` and are never returned by the API.

---

## 6. Cache design

**Granularity:** one entry per location + local date ([ADR-0003](../adr/0003-daily-cache-entries-with-local-midnight-expiry.md)).

```
panchang:{lat:F2}:{lon:F2}:{ianaZone}:{localDate}:{calculationMethod}:{calculationVersion}:d{dataSchemaVersion}
e.g.  myvedicday:panchang:17.39:78.49:Asia/Kolkata:2026-09-28:drik:1.1.0:d3
```

- **Cache-aside:** `DailyPanchangService` reads all requested keys concurrently, returns hits, and calculates only
  the misses. A 10-day, month or year request reuses any days already cached.
- **Expiry:** an entry for local date *D* expires at `zone.AtStartOfDay(D + 1)`, converted to UTC. That is DST-safe
  and handles zones that skip midnight. So today's entry expires at the location's next local midnight, and
  Hyderabad and Austin entries expire at different UTC instants. Dates already over get a short lifetime (1 h), and
  all entries are capped at 35 days.
- **Day change:** the key contains the local date, so after local midnight a new key is used. Yesterday's entry can
  never be served as today's, and a hit is also verified against the requested date.
- **Stampede protection:** concurrent misses for the same key share one calculation (`InFlightRequestCoalescer`).
  Across instances the worst case is one calculation per instance per key (about 1 ms each). A distributed lock would
  cost more than it saves.
- **Failure handling:** cache read/write failures are logged and degrade to calculation. Readiness reports
  *Degraded*, not *Unhealthy*. Calculation failures are never cached.
- **Language-neutral entries:** cached days hold stable keys only; names, glossary and classifications are
  applied per request (§7), so one entry serves every language and configuration changes apply immediately.
- **Invalidation:** bump `PanchangCalculationVersion.Current` when algorithms or rule tables change.

---

## 7. Content, localization and classifications

Display text is **data, not code**, and is separate from both calculation and caching:

```
Calculation (enums) ─► Business cache (stable keys, language-neutral) ─► PanchangLocalizer ─► localized response
                                                                              ▲
                              IContentRepository (Localization.Data) ──┘  {module}-content.{language}.json
```

- **One entry shape for everything:** `{ name, meaning, description, message, icon }`, keyed `category.id`:

  | Category | Examples | Fields used |
  |----------|----------|-------------|
  | `periods` | `periods.RahuKalam` | name, meaning, description, message |
  | `elements` | `elements.tithi` | name, meaning, description |
  | `terms` | `terms.sunrise`, `terms.vara` | name, meaning, description |
  | `classifications` | `classifications.Avoid` | name, icon |
  | value names | `tithi.ShuklaPanchami`, `nakshatra.Rohini`, `yoga.Siddhi`, `karana.Bava`, `weekday.Tuesday` | name |

  Adding a new kind of item (for example `festivals.Diwali` or `rashi.Mesha` for Kundali) is a new category of
  entries: no schema change.
- **Glossary once per response:** the `periods`, `elements`, `terms` and `classifications` categories are returned as
  `glossary` once per response, and items reference them by `kind`/`key`. A year response would otherwise repeat
  the same text about 3,600 times. `GET /api/v1/panchang/glossary` serves it alone for client-side caching.
- **Languages:** requested by `?language=` then `Accept-Language`, matched exactly (`te-IN`) or by base language
  (`te`) against `Localization:SupportedLanguages`, otherwise `Localization:DefaultLanguage`. Fallback is **per field**:
  a translation may supply only names at first; meanings come from the default language until translated.
  The response carries `language`, `Content-Language` and `Vary: Accept-Language`.
- **Configurable without redeploy:** built-in catalogs are embedded in `.Data`; files in `Content:Directory` override
  them field by field or add languages (read at startup). A database or CMS can replace the repository behind
  `IContentRepository`. Each module contributes its own `{module}-content.{language}.json` catalogs
  (`AddContentSource`); content categories are owned per module.
- **Classifications:** `HighlyFavorable`, `Favorable`, `Caution`, `Avoid`, assigned per period kind in
  `Panchang:PeriodClassifications` (defaults: Amrit Kalam highly favorable; Brahma and Abhijit favorable; Gulika
  caution; Rahu, Yamagandam, Dur Muhurtam and Varjyam avoid). Startup validation rejects unknown or missing kinds.
  Labels and icons are content (`classifications.*`), so they are translatable. A per-tradition override is a natural
  extension if traditions disagree.
- **Guarded by tests:** the default-language catalog must contain a name for every Tithi, Nakshatra, Yoga, Karana and
  weekday, and a name, meaning and description for every period, element and term.

Place names from GeoNames are English only; localized place names would come from GeoNames alternate names or an
online geocoder.

### 7b. Daily guidance

Every day carries `guidance`: simple consumer guidance, **not** a universal rule and **not** a Muhurta
([ADR-0009](../adr/0009-daily-guidance-is-not-muhurta.md)).

```
IPanchangCalculator ─► tradition profile ─► IDailyGuidanceEvaluator ─► (future) IMuhurtaEvaluator per activity
 (common astronomy)     (periods shown)      (day rating + windows)      Marriage, Griha Pravesh, Travel…
```

- **Day rating** from the five limbs (the value in force for most of the daytime), using the versioned
  `panchang-guidance-rules.json`:
  - limb weights (default Nakshatra 30, Tithi 25, Yoga 20, Karana 15, Vara 10);
  - score bands (85/70/50/30);
  - a rating and traditional group for all 102 values;
  - per-tradition overrides.

  Labels: Highly Favorable, Favorable, Mixed, Caution, Traditionally Avoided. No percentage is exposed.
- **Time windows** from the tradition's periods: `bestWindows` (longest daytime stretches free of caution and avoid
  periods, at least 45 min, up to 3), `favorableWindows`, `spiritualWindows` (Brahma Muhurta), `cautionWindows` and
  `avoidWindows`. Windows never change the day rating.
- **Mixed windows:** periods come from independent rules (Amrit Kalam from the Nakshatra, Rahu Kalam from the weekday
  and daylight) and can overlap. Every stretch where a favorable period overlaps a caution or avoid period is listed in
  `mixedWindows` (🟡 Mixed) with the periods involved; contiguous stretches are merged. Periods keep their full times,
  and best windows still exclude them. Each favorable period also carries `overlaps` (the overlapping caution/avoid
  periods and the overlap times) and a localized `overlapMessage` (`periodOverlap.*` templates), added by
  `PeriodOverlapAnnotator` after localization, so it appears in `auspiciousPeriods` and the guidance windows alike.
- **Text:** headline, summary and per-limb reasons come from content templates (`guidanceRatings.*`, `guidanceText.*`,
  `guidanceGroups.*`), so wording and translations are data.
- **Evaluation** runs after the cache, like localization. It adds no recalculation or cache entries.
- **Pending review:** the ratings (`2026.1-provisional`) follow classical groupings and must be reviewed with the
  reference astrologer.

### 7a. Date and time display

Times are stored and returned as ISO 8601 instants with offset. Each also gets a **display string** in the location's
format, `{date} {time} {zone abbreviation}`, e.g. `09/28/2026 07:22:45 CDT` (US) or `28/09/2026 06:05:56 IST` (India)
([ADR-0007](../adr/0007-location-formatted-display-times.md)).

- **Country:** the matched or nearest city's country, else the time zone's country (tz `zone.tab`).
- **Date pattern:** `DateTimeDisplay:CountryDatePatterns` (US `MM/dd/yyyy`, IN `dd/MM/yyyy`), else the country's CLDR
  short-date convention (the most common among its non-English cultures, normalized to `dd`, `MM`, `yyyy`), else
  `DefaultDatePattern`.
- **Time and zone:** `TimePattern` (24-hour by default). The abbreviation comes from the tz database for that
  instant, so it is DST-correct (CST/CDT); zones with numeric abbreviations show `UTC±hh:mm`.
- Formatting runs after the cache, like localization, so it never affects cached data.

## 8. API design

Base path `/api/v1/panchang`. Location parameters (see §4): `place` ("City, State[, Country]") and/or
`latitude` + `longitude` (browser location); at least one is required. Optional: `timezone` (IANA),
`tradition` (default `all`, see §3a), `language` (see §7).

| Method & path | Returns |
|---------------|---------|
| `GET /today` | current local date |
| `GET /tomorrow` | next local date |
| `GET /ten-days` | 10 days from local today |
| `GET /{date}` | one date (`yyyy-MM-dd`) |
| `GET /range?start=&days=` | 1–366 days |
| `GET /month/{year}/{month}` | calendar month |
| `GET /year/{year}` | calendar year |
| `GET /api/v1/muhurta/activities`, `/good-times`, `/good-times/explanation`, `/good-times/best`, `/day-summaries` | Find a Good Time (see [design](find-a-good-time.md)); `goodTime` on every Panchang day |
| `GET /api/v1/festivals/upcoming?days=` | festivals from local today, 1–90 days (default 30); also in every day's `festivals` |
| `GET /api/v1/locations/resolve` | resolved coordinates, time zone and matched/nearest city (no calculation) |
| `GET /api/v1/traditions` | shared tradition list with localized names and regions |
| `GET /api/v1/panchang/traditions` | Panchang timings each tradition shows (`display`) |
| `GET /api/v1/panchang/glossary?tradition=` | Panchang glossary for a tradition and language |
| `GET /health/live`, `/health/ready` | liveness, readiness (cache) |

Response (abridged):

```json
{
  "location": { "latitude": 17.39, "longitude": 78.49, "timeZone": "Asia/Kolkata", "source": "Coordinates",
                "place": { "city": "Hyderabad", "state": "Telangana", "country": "India", "countryCode": "IN",
                           "displayName": "Hyderabad, Telangana, India" } },
  "tradition": { "key": "telugu", "name": "Telugu", "display": [ "Varjyam", "DurMuhurtam", "RahuKalam", "…" ] },
  "calculationMethod": "drik", "calculationVersion": "1.1.0",
  "localToday": "2026-09-29", "generatedAt": "2026-09-29T01:11:03Z",
  "days": [{
    "date": "2026-09-29", "weekday": "Tuesday",
    "sunrise": "2026-09-29T06:05:58+05:30", "sunset": "2026-09-29T18:06:40+05:30",
    "nextSunrise": "2026-09-30T06:06:07+05:30",
    "tithis": [
      { "number": 18, "name": "Krishna Tritiya",   "start": "2026-09-28T19:13:46+05:30", "end": "2026-09-29T17:10:28+05:30" },
      { "number": 19, "name": "Krishna Chaturthi", "start": "2026-09-29T17:10:28+05:30", "end": "2026-09-30T14:55:42+05:30" }
    ],
    "nakshatras": [ ... ], "yogas": [ ... ], "karanas": [ ... ],
    "auspiciousPeriods":   [ { "kind": "AbhijitMuhurta", "name": "Abhijit Muhurat", "start": "...", "end": "..." } ],
    "inauspiciousPeriods": [ { "kind": "RahuKalam", "name": "Rahu Kalam", "start": "...", "end": "..." } ]
  }]
}
```

The UI renders `tithis[0]` as "Krishna Tritiya — until 5:10 PM" and `tithis[1]` as "Next: Krishna Chaturthi — from
5:10 PM". Times carry the offset in force at that instant, so DST and cross-midnight periods are unambiguous.

**Errors** are RFC 7807 Problem Details with `traceId` and `correlationId`: 400 validation, 422 business rule
(e.g. polar day), 429 rate limit, 503 dependency, 500 unexpected. Stack traces and internals are never returned.

Versioning is by URL segment (`v1`). Adopting `Asp.Versioning` is planned for when a v2 is needed.

**Documentation:** Swagger UI at `/swagger` and the OpenAPI 3.1 document at `/openapi/v1.json`, on in every
environment by default (`ApiDocumentation:Enabled`, opt-out; rate-limited outside Development because generating the
document runs real calculations for its examples). Content comes from controller XML comments (summary, remarks,
parameters) plus transformers in `Api/Infrastructure/OpenApi`: API overview and tags, shared parameter descriptions
and examples, example success and error bodies. `ApiDocumentationTests` fails the build if an operation, parameter or
response loses its documentation. Swagger UI gets a relaxed Content-Security-Policy; the API keeps
`default-src 'none'`.

---

## 9. UI design (Phase 2)

Recommended: **React + TypeScript (Vite or Next.js)** as a responsive PWA, calling this API. It has the largest
ecosystem for calendar and date components, supports offline caching of fetched days, and SSR if SEO matters.
Blazor WebAssembly is the alternative if the team prefers a single C# stack. This decision is still open.

- **Views:** Today (detail cards), Tomorrow, 10-day list, Month grid, Year overview.
- **Location:** "Use my location" (Geolocation API, explicit permission), manual search, saved locations. Show the
  resolved city and time zone prominently.
- **Day card:** sunrise/sunset; Tithi and Nakshatra as "current → until time; next → from time"; good and
  inauspicious periods as time ranges with a date suffix when they fall on another day ("12:40 AM, Sep 30").
- **Day change:** compare `localToday` with the device clock in the location's zone; refetch on change, on
  visibility change and on focus. The server's key-per-local-date guarantees a fresh day.
- **Loading and error states:** skeleton cards, Problem Details `title`/`detail` surfaced to the user, retry on 503.
- **Localization:** names and glossary text are localized server-side (§7). The UI sends `Accept-Language` (or a
  language picker's `?language=`), uses `key`/`kind` for logic and `name` for display, colours periods by
  `classification`, and shows `glossary.periods[kind].message` inline with `meaning` in a detail sheet.
- **No calculations in the browser:** the backend is authoritative.

---

## 10. Cross-cutting concerns

**Authentication.** `MyVedicStar.ApiProtection` validates configured Entra External ID, Google, Apple and Facebook
tokens and normalizes provider claims. MVC endpoints require the package's API-scope policy, except the anonymous
GET actions for today's Panchang and location resolution. Health probes and enabled documentation remain anonymous. Provider secrets come
from user-secrets or Key Vault.

**Error handling.** Lower layers throw `Shared` exceptions, and `GlobalExceptionHandler` maps them to Problem
Details. The expensive explanation action handles its package deadline locally as HTTP 503; client cancellation
continues to the centralized handler.

**Logging and monitoring.**
- JSON console logs with scopes (trace ID, span ID, correlation ID).
- One entry per request: method, path, status, duration, user agent, app version. The query string is not logged
  because it holds coordinates.
- The Business layer logs time zone, calculation method, cache hits and misses; calculation latency is logged at Debug level.
- OpenTelemetry traces and metrics: ASP.NET Core instrumentation plus the `MyVedicDay.Panchang` meter
  (`panchang.cache.hits`, `panchang.cache.misses`, `panchang.calculation.duration`). Exported via OTLP when
  `Observability:OtlpEndpoint` is set; on Azure, use the Azure Monitor exporter for Application Insights.

**Security (OWASP).**
- Input validated at the API (DataAnnotations) and again in Business.
- Edge protection from the shared `MyVedicStar.ApiProtection` library (ADR-0014): trusted proxies
  (`ApiProtection:TrustedProxies`), so limits see the real client IP behind the ingress; sliding-window rate limits
  (global, plus `public-read`, `good-time` and `astrology-expensive` per endpoint group); 64 KB standard request bodies
  and 256 KB for future POST calculations marked with the package's `AllowCalculationRequestSize` metadata.
- HTTPS and HSTS; security headers (nosniff, DENY framing, CSP `default-src 'none'`).
- CORS allow-list; health probes (`ApiProtection:HealthCheckPaths`) are never rate limited.
- Secrets (the Redis connection string) come from environment or Key Vault, never appsettings.
- PII minimization: coordinates are rounded and not logged.

**Scalability and availability.**
- Stateless API; all shared state lives in Redis.
- Calculation is CPU-only, at roughly 1 ms per day.
- Horizontal scaling behind a load balancer or API gateway; liveness and readiness probes for rolling deployments.
- Graceful degradation when Redis is down.

---

## 11. Testing strategy

| Project | Scope |
|---------|-------|
| `Tests.Calculation` | Meeus worked examples (Sun, Moon, sidereal time), Lahiri at J2000, published sunrise/sunset for London, New York, Sydney and Austin (±2 min), the 2024-04-08 new moon and 2024-04-23 full moon (±2 min), contiguity and duration invariants, weekday rules, polar day |
| `Tests.Unit` | cache key format and normalization, local-midnight expiry incl. DST, coalescing, location/time zone resolution, facade date logic, cache reuse for overlapping ranges, layer-dependency rules |
| `Tests.Integration` | API → Business → Calculation → cache through `WebApplicationFactory`: every endpoint, DST offsets, client time zone override, validation (400), polar day (422), correlation and security headers, health |

**Next:** add a reference dataset (for example 3 years × 6 locations exported from the chosen reference Panchang)
as a data-driven calculation test, especially for Varjyam and Amrit Kalam.

---

## 12. Technology

| Area | Choice |
|------|--------|
| Runtime | .NET 10, ASP.NET Core controllers |
| Dates and zones | NodaTime (IANA tzdb) |
| Coordinate → zone | GeoTimeZone (offline) |
| Geocoding | Embedded GeoNames extract (offline, CC BY 4.0), `tools/build-geonames-places.cs` |
| Cache | Redis via `Microsoft.Extensions.Caching.StackExchangeRedis` (in-memory fallback for development) |
| Telemetry | OpenTelemetry (OTLP) |
| API docs | `Microsoft.AspNetCore.OpenApi` (OpenAPI 3.1) + Swashbuckle Swagger UI (`/swagger`) |
| Tests | xUnit, `Microsoft.AspNetCore.Mvc.Testing`, NodaTime.Testing |
| Future precision | Swiss Ephemeris adapter (check the AGPL/commercial licence) or JPL DE440 |

## 13. Implementation phases

| Phase | Scope | Status |
|-------|-------|--------|
| 1 | Calculation engine, Business facade, daily caching, REST API, observability, tests | **done** |
| 1b | Validate rule tables against the reference almanac; reference-dataset tests; `ForwardedHeaders`; Docker image | next |
| 2 | Frontend (Today/Tomorrow/10-day/Month/Year, location detection, localization) | |
| 3 | Authentication, user profiles, saved locations (EF Core + database) | |
| 4 | City autocomplete endpoint; optional online geocoder (Azure Maps / Google) behind `IGeocodingProvider` for towns under 5,000 people | |
| 5 | Tamil Nalla Neram and Gowri Panchangam; validate Malayalam and Marathi profiles; review daily-guidance ratings; Amanta/Purnimanta months; festival calendar | |
| 5b | Muhurta module (`MyVedicDay.Muhurta.*`): `IMuhurtaEvaluator` per activity (Marriage, Griha Pravesh, Travel, Business…) with Tara Bala, Chandra Bala, Lagna | |
| 6 | Kundali: planetary positions, Lagna, Navamsa, Dasha; then Gochar, compatibility, AI explanations | |
