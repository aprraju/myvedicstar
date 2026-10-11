# ADR-0005: Localized content catalogs, language-neutral cache, glossary per response

- Status: Accepted
- Date: 2026-09-29

## Context

Each Panchang item needs a meaning, an app-friendly description, a one-line message and, for periods, a UI
classification (Avoid, Caution, Favorable, Highly Favorable). This text must be editable without code changes, and
the app will become multilingual (Telugu, Hindi, Tamil and others). Until now, English names were hard-coded in the
calculation engine and baked into cached days.

## Decision

1. **Content is data.** Per-language JSON catalogs (`panchang-content.{language}.json`) with one entry shape
   (`name`, `meaning`, `description`, `message`, `icon`) keyed `category.id`, served by `IPanchangContentRepository`
   in the Data layer. They are embedded by default, and a configured directory can override them field by field.
   The calculation engine no longer contains display names.
2. **The cache is language-neutral.** Cached days (`DailyPanchangData`) hold stable keys only, and `PanchangLocalizer`
   applies names, classifications and the glossary after the cache. The cache key gained a data-schema segment (`d2`).
3. **Glossary once per response.** Explanations are returned once in `glossary`, and items reference them by `kind`
   or `key`. They are also available at `GET /api/v1/glossary`.
4. **Negotiated language with per-field fallback** to the default language. `Localization:SupportedLanguages` gates
   which languages are offered, so a partial translation can be staged before it is enabled.
5. **Classification is configuration** (`Panchang:PeriodClassifications`), validated at startup. Its labels and icons
   are content.

## Consequences

- Adding a language or rewording a meaning means editing a JSON file; there is no recalculation or cache flush, and
  one cached day serves every language.
- Year responses stay compact.
- Clients must join items to the glossary by `kind`/`key`. This is documented in OpenAPI and shown in the examples.
- A completeness test ensures the default-language catalog covers every value, period, element and term.
- The Data layer changed its embedded-resource setup: `WithCulture="false"` is required so `.en.json` is not moved
  into a satellite assembly.
