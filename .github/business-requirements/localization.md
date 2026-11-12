# Localization Business Requirements

## Module scope
This module supplies localized labels, descriptions, glossary terms, and other language-dependent content consumed by API features and client applications.

## Business objectives
The system shall present feature content in the user's supported language without changing calculation values, rule outcomes, identifiers, or data provenance.

## API requirements
1. Endpoints that support localization shall accept an optional language tag and otherwise negotiate from `Accept-Language`.
2. Explicit supported language input takes precedence over header preferences. Unsupported or unavailable translations shall use the documented fallback language rather than fail an otherwise valid calculation.
3. Responses that negotiate language shall identify the selected language in the response contract and, where implemented, the `Content-Language` header.
4. Glossary content shall provide localized meanings/descriptions/messages and classification labels for the selected tradition. It shall remain reference content and shall not change the underlying Panchang/Muhurta result.
5. Stable keys, codes, timestamps, and machine-readable classifications shall not be localized; display labels may be.

## UI consumer requirements
1. Web shall use the user's selected language preference where supported; mobile shall derive its initial language preference from device locales. Both shall pass supported preferences to APIs that localize their responses.
2. The UI shall render the language actually selected/fallback by the API and use client translations for application chrome. It shall not assume that requesting a language guarantees every API string is translated.
3. Cached localized responses shall be separated by language and tradition wherever those inputs affect content.
4. Incomplete translations shall fall back to the default language at the content-field level where supported; the UI shall not display missing strings, raw localization keys, or reinterpret status codes based on translated text.

## Business rules
1. Localization is a presentation/content concern and must not modify astronomy, date boundaries, activity ranking, or business classifications.
2. Language negotiation must be deterministic and consistent across the response, its metadata, and the client-visible content.

## Non-functional requirements
1. Language tags shall be validated and bounded; content catalogs shall support adding translations without changing stable API identifiers.
2. Localization/reference lookups may use bounded shared caching keyed by all inputs that affect the response.
