# ADR-0006: Panchang traditions are presentation profiles over a shared calculation

- Status: Accepted
- Date: 2026-09-29

## Context

Regional Panchang traditions agree on the five limbs (Tithi, Vara, Nakshatra, Yoga, Karana) but emphasize different
daily timings. For example:

- Telugu Panchangam: Varjyam, Durmuhurtham, Rahu Kalam, Amrita Kalam.
- Tamil: Rahu Kalam, Yamagandam, Kuligai.
- North and West India: Choghadiya, Rahu Kaal, Abhijit Muhurat.

Users should choose their tradition independently of where they are: a Telugu family in Dallas expects Telugu
conventions computed for Dallas.

Until now, the API parameter `tradition` selected the *calculation* rules (`drik`).

## Decision

1. `tradition` now means the **regional presentation tradition**: `all` (default), `telugu`, `tamil`, `kannada`,
   `malayalam`, `north-indian`, `gujarati` or `marathi`. It is never derived from location.
2. Each tradition is a **profile** in `panchang-traditions.json`: `{ key, calculationMethod, display[] }`, where
   `display` lists period kinds or the `Choghadiya` group in order of emphasis. The file is embedded, and a copy in
   `Content:Directory` can replace or add profiles.
3. The former `drik` value becomes the profile's **calculation method**. It is internal, and the cache is keyed by it.
   All current traditions use `drik`, so every tradition shares one calculation per location and date.
4. **Regional names** are content keyed `{tradition}/periods.{kind}` (Kuligai, Durmuhurtham, Rahu Kaal), with
   per-field and per-language fallback to the generic names.
5. **Choghadiya** is computed by the engine for every day. The period model gained a group (`Muhurta` or
   `Choghadiya`) and a `Neutral` classification (for Chal). The API returns Choghadiya as a separate list, only for
   traditions that display it.

## Consequences

- Switching tradition never recalculates. Adding or adjusting a tradition is a data change.
- Responses carry `tradition { key, name, description, display }` and `calculationMethod`.
  `GET /api/v1/traditions` feeds a tradition picker.
- `tradition=drik` is no longer accepted. There were no external clients.
- The calculation version was bumped to 1.1.0 and the cache data schema to `d3`.
- Open items: Tamil Nalla Neram (needs a reference table); review of the provisional `malayalam` and `marathi`
  profiles; traditions whose calculations differ (for example Amanta vs Purnimanta month names) will add a
  calculation method and point their profiles at it.
