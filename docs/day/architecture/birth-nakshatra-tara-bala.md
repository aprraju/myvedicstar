# Optional birth Nakshatra in Find a Good Time

The optional `nakshatra` query parameter represents the person's **birth** Nakshatra, not a requested astronomical star.
It applies to `good-times`, `good-times/explanation`, `day-summaries` and `good-times/best` under `/api/v1/muhurta`.
For example: `GET /api/v1/muhurta/good-times?latitude=17.385&longitude=78.4867&date=2026-10-01&activity=business&nakshatra=Rohini`.

Populate the picker using authenticated `GET /api/v1/muhurta/nakshatras?language=en&tradition=telugu`.
The response contains `language` and `nakshatras`, ordered from Ashwini (1) to Revati (27).
Each item has `key`, localized `name` and `order`. Display `name` and send `key` back as `nakshatra`;
never send the translated display label. Language and tradition use the existing localization behavior.

## Approved policy

Count forward inclusively from the birth star to the active star in the existing 27-star catalog, wrapping at Revati.
The Tara position is `((active - birth + 27) % 27) % 9 + 1` with star numbers 1–27.
Positions 2 (Sampat), 4 (Kshema), 6 (Sadhana), 8 (Mitra) and 9 (Parama Mitra) are compatible.
Position 1 (Janma) caps suitability at Mixed; 3 (Vipat), 5 (Pratyak) and 7 (Naidhana) cap it at Caution.
The user explicitly approved these application caps. They add no scoring weights, bonuses, remedies or activity exceptions.

The existing evaluator still calculates all Panchanga values and favorable/unfavorable periods, scores by the activity
and tradition profile, and applies exclusions and conflict caps. Tara Bala applies a further cap after those rules;
compatible Tara never promotes a status, score or restricted interval. Existing minimum durations and ordering remain.
Nakshatra transitions already cut the evaluation into elementary intervals. The personal reason includes
`birth=Rohini;active=...;tara=...`, so adjacent intervals cannot merge across a change in this personal context.
The response shape is unchanged; existing reason fields carry compatibility details.
No Chandra Bala is computed: birth rashi and approved Chandra Bala rules are absent.

## Validation and compatibility

Omitted, null, empty and whitespace-only input use the unchanged standard calculation and cache key.
All 27 enum names are accepted case-insensitively, with spaces, hyphens or underscores between words.
Explicit aliases include Aswini, Krithika, Mrigashirsha, Mrigasira, Arudra, Aslesha, Jyestha, Moola,
Sravana, Dhanistha and Satabhisha. Unknown names, numeric enum values and Abhijit return HTTP 400
validation ProblemDetails keyed by `nakshatra`; Abhijit is not in the existing 27-star catalog.

Personal evaluation keys append `:birth-nakshatra:{CanonicalName}:tara-v1` to the standard key.
The location, zone, date, activity, tradition, rule and schema versions, cache expiry and miss coalescing remain intact.
AI explanation inputs include the personal reason subject, separating explanation caches by birth-star context.
The standard algorithm version remains unchanged because results without this parameter are unchanged.

Reference for the inclusive nine-Tara cycle: [B. V. Raman, Muhurtha](https://www.panchanga.lv/wp-content/uploads/2020/06/Muhurta_Raman.pdf).
The Mixed/Caution caps are the approved product policy rather than a claim that every tradition follows identical rules.
