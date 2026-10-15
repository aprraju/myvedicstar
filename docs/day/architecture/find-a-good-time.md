# Find a Good Time — Muhurta Recommendation Architecture

- **Status: Accepted; M0–M4 implemented** (backend complete 2026-10-01; web and mobile UI in `myvedicstar-ui` complete 2026-10-02). Decisions D1–D5 taken as recommended; see ADR-0011, ADR-0012, ADR-0013.
- Date: 2026-10-01
- Scope: the MyVedicDay API (this repository) and the My Vedic Star web and mobile apps (`myvedicstar-ui`).
- Builds on: ADR-0006 (traditions), ADR-0008 (modules), ADR-0009 (daily guidance is not Muhurta), ADR-0010 (festivals).

---

## 1. Requirements analysis

### What is asked

A user picks an activity: Business, Travel, Property, Vehicle, Agreement, Education, Puja / Spiritual, Wedding,
Housewarming, General, or Other (free text). The backend evaluates that day's Panchang for the activity, the
location and the tradition, and returns:

- time segments, each with a resolved status (HighlyFavorable, Favorable, Mixed, Caution, Avoid);
- reason codes and conflict codes for each segment;
- the qualifying Good Times;
- an explicit "no good time" result when none qualifies.

The Home card, the calendar tiles, the daily timeline and mobile must all use this same evaluation. Activities and
rules are configuration. Traditions influence the rules. Generative AI is integrated with a switchable provider.

### Conflicts found, and the proposed resolution (§36)

| # | Conflict | Why it matters | Proposal |
|---|----------|----------------|----------|
| C1 | "Utilize generative AI to generate the best results" vs "the backend rules engine is authoritative" (§5, §16) | LLM output is not deterministic and can invent times or statuses. If an LLM chose or ranked Good Times, the same request could give different answers on Home and on the calendar, violating §11, and could promote a conflicting period, violating the acceptance criteria. | **The AI never decides status, times or ranking.** The deterministic engine selects the results. The AI (a) writes the "Why this time?" narrative from the engine's structured result, and (b) maps "Other" free text to a known activity. Its output is validated against the result and replaced by template text on any mismatch. |
| C2 | §5 resolves Abhijit + Dur Muhurtam to **Caution / Conflict**; §8 and §17 resolve Amrit Kalam + Rahu Kalam to **Mixed** | The two examples disagree. | A **configurable conflict matrix** (§5 says "severity must be determined by the backend rules engine"). The defaults are in §6.4, and either outcome can be configured per activity and tradition. |
| C3 | "Copilot" as an AI provider | GitHub Copilot and Microsoft 365 Copilot are products, not model APIs a backend can call. | Microsoft's route is **Azure OpenAI / Azure AI Foundry**. Claude is also available on Microsoft Foundry. **Needs confirmation.** |
| C4 | The calendar tile status must come from "the same resolved evaluation" (§11) | The Panchang `guidance.rating` today is a limb-only day rating (ADR-0009). A tile could show "Mixed" while the day headline says "Favorable Day". | **Decision needed** (§15, D3). |
| C5 | Home's Good Time is derived **in the UI today**: `domain-models/day-summary.ts` picks `guidance.bestWindows` and computes overlaps client-side (`overlapsWith`) | This violates §10, §16 and "no UI component may promote…". `bestWindows` ignores limb and activity rules. | The backend returns `current` and `next` qualifying segments. The UI deletes its derivation. `guidance.bestWindows` is deprecated once the UI has migrated. |
| C6 | §6 maps Avoid to **red** | The UI maps `Avoid` to the `caution` tone today (`day-dashboard.ts`, `day-summary.ts`). | The UI gets a five-status design-token map shared by web and mobile. |
| C7 | Wedding and Housewarming Muhurta traditionally need Lagna (ascendant) suitability, month restrictions (Chaturmas, Adhika), Guru and Shukra combustion, and personal factors (Tara and Chandra Bala of the persons involved) | The engine has the Sun and Moon only (ADR-0002), no Lagna, no planets, and no birth data. Calling a period "Highly Favorable for a Wedding" without these could mislead users about a major life event. | **Decision needed** (§15, D2). Recommended: ship with the factors available, cap the result at Favorable, add reason `PERSONALIZED_MUHURTA_RECOMMENDED`, and show the disclaimer prominently. |
| C8 | `score: 82` in §17 | ADR-0009 deliberately shows no percentages. | Return `score` (0–100) for ordering only, documented as **not for display as a percentage**. |
| C9 | Times as `"16:27"` strings (§17) | The API convention is ISO 8601 instants with offset plus `…Display` strings (ADR-0007). Bare times break across midnight and DST. | Follow the convention: `start`, `end`, `startDisplay`, `endDisplay`. |

---

## 2. Proposed solution architecture

A new feature domain, as ADR-0008 and ADR-0009 foresaw, plus one new platform integration module for AI:

```text
myvedicstar-ui (web + mobile) ── renders status, reasons, conflicts; no astrology rules
        │  HTTPS/JSON
        ▼
MyVedicDay.Api ── MuhurtaController (thin) ──► IMuhurtaFacade
                  PanchangController ─────────► IPanchangFacade + IFestivalFacade + IMuhurtaFacade (day summary)
        │
        ▼
MyVedicDay.Muhurta.Business
   MuhurtaFacade ─► ActivityRulesProvider (activity × tradition → resolved rule profile)
                ─► DailyMuhurtaService (cache-aside, coalescing)
                ─► IGoodTimeExplainer   ── TemplateExplainer (default)
                                        └─ AiGoodTimeExplainer (decorator, validated, falls back to template)
                ─► IActivityInterpreter ── AiActivityInterpreter ("Other" free text → activity key)
        │
        ├──► MyVedicDay.Muhurta.Calculation (pure)
        │       SegmentTimelineBuilder → SegmentEvaluator (rule interpreter) → ConflictResolver → GoodTimeSelector
        │       uses IPanchangCalculator (Panchang calculation contract) + ILunisolarCalendar (Astronomy, see §6.6)
        ├──► MyVedicDay.Muhurta.Data
        │       muhurta-activities.json (versioned rules), muhurta-content.{lang}.json, muhurta-ai-prompts.json
        └──► MyVedicDay.Ai.Integration (platform)
                IStructuredTextGenerator ─► IChatClient (Microsoft.Extensions.AI)
                  /Anthropic  (official `Anthropic` NuGet, IChatClient)
                  /OpenAI     (Microsoft.Extensions.AI.OpenAI)
                  /AzureOpenAI (Azure OpenAI / Foundry, managed identity)
```

Patterns, each used where it pays:

- **Facade:** `IMuhurtaFacade`.
- **Strategy:** explainers, and rule-evaluator extensions for rules that data cannot express.
- **Factory:** the AI client selected by configuration.
- **Adapter:** one per AI provider.
- **Decorator:** the AI explainer over the template explainer, and resilience around the providers.
- **Repository:** rules and prompts.

---

## 3. Visual Studio projects

| Project | Kind | References |
|---------|------|------------|
| `MyVedicDay.Muhurta.Calculation` | feature, pure | Panchang.Calculation (contract), Astronomy.Calculation, Shared |
| `MyVedicDay.Muhurta.Data` | feature | Localization.Data, Shared |
| `MyVedicDay.Muhurta.Business` | feature | Muhurta.Calculation, Muhurta.Data, Ai.Integration, Location, Localization, Traditions, Caching, Shared |
| `MyVedicDay.Ai.Integration` | platform | Shared, Microsoft.Extensions.AI, provider SDKs. **Never references a feature.** |
| `MyVedicDay.Muhurta.Tests.Calculation` | tests | spec examples (§5 of the requirements), conflict matrix, reference days |
| `MyVedicDay.Muhurta.Tests.Unit` | tests | rules and content consistency, tradition overrides, caching, AI guardrails (fake `IChatClient`) |
| `MyVedicDay.Ai.Tests.Unit` | tests | provider factory and configuration validation |

`LayerDependencyTests` gets one rule row per new assembly. `Muhurta.Calculation` joins the "no web, persistence or
cache frameworks" check.

## 4. Dependency diagram

```text
Api ──► Muhurta.Business ──► Muhurta.Calculation ──► Panchang.Calculation ──► Astronomy.Calculation
  │            │        └──► Muhurta.Data ──► Localization.Data
  │            ├──► Ai.Integration            (platform; no feature references)
  │            └──► Location / Localization / Traditions / Caching (platform)
  ├──► Panchang.Business      (no reference to Muhurta.* — the host composes)
  └──► Festivals.Business
```

## 5. Layer responsibilities

- **API:** routing, binding, validation, and composing facades (as for festivals). No evaluation logic.
- **Muhurta.Business:**
  - resolve location, tradition and activity, including "Other";
  - resolve the rule profile;
  - cache-aside, and localize labels;
  - explain (template or AI);
  - build the day summaries for the calendar and Home.
- **Muhurta.Calculation:** build the segment timeline, evaluate the rules, resolve conflicts, select qualifying
  segments, and produce reason and conflict codes. Deterministic and pure.
- **Muhurta.Data:** versioned rule profiles, localized labels (reasons, conflicts, activities, messages), versioned
  AI prompts.
- **Ai.Integration:** the provider-neutral structured-generation port; provider adapters; timeouts, retries,
  circuit breaker; redaction-safe logging.

---

## 6. Calculation architecture

### 6.1 Segment timeline

For the selected local date, the window runs from sunrise to the next sunrise. Each activity profile limits the
allowed part of it: `Daytime` for Business, `Any` for Puja.

Boundaries come from:

- sunrise and sunset;
- every period the tradition's Muhurta profile considers (Rahu Kalam, Yamagandam, Gulika, Dur Muhurtam, Varjyam,
  Abhijit, Amrit Kalam, Brahma Muhurta, and Choghadiya where the profile uses it). The Panchang day reports the
  Brahma Muhurta before its own sunrise, outside every window. The evaluator therefore adds the one before the next
  sunrise, at the same fraction of the night, so `Any`-window profiles such as Puja can use it;
- every Tithi, Nakshatra, Yoga and Karana transition.

Between consecutive boundaries every input is constant, so each elementary segment is evaluated exactly. This
generalizes the overlap logic now in `PeriodOverlapAnnotator` and `GuidanceWindowAnalyzer`.

### 6.2 Evaluation per segment (rule interpreter, no `if/else` per activity)

1. **Limb factors.** Each activity rates the values in force: Tithi groups (Nanda, Bhadra, Jaya, Rikta, Purna),
   Nakshatra groups (Dhruva, Mridu, Kshipra, Chara, Ugra, Tikshna, Mishra), Yoga, Karana (Vishti) and Vara.
   - Each factor is `Supportive`, `Neutral`, `Adverse` or `Excluded`, and becomes a positive or negative reason code.
   - `Excluded` caps the segment, e.g. Vishti Karana caps a Wedding at Avoid.
2. **Favorable periods** add positive reasons and raise the base: `ABHIJIT_MUHURTA`, `AMRIT_KALAM`, `BRAHMA_MUHURTA`,
   `CHOGHADIYA_LABH`…
3. **Unfavorable periods** carry a per-activity **severity**: `Block` (Avoid), `Caution` (cap at Caution) or
   `Downgrade` (one level).
4. **Base status:** a weighted score of the factors, mapped to thresholds, the same mechanism as the daily-guidance
   rules.

### 6.3 Conflict resolution

When a favorable and an unfavorable period are both active in a segment, the **conflict matrix** decides:
`(activity, tradition, unfavorable kind[, favorable kind]) → Mixed | Caution | Avoid`. The segment can never come
out above that result, and the overlap is reported as a `conflict` with the unfavorable period's full times and
the overlap's times. Segments with no favorable period simply take the unfavorable severity.

### 6.4 Default conflict matrix (provisional; for astrologer review)

| Unfavorable period | Overlaps a favorable period | Alone |
|--------------------|-----------------------------|-------|
| Rahu Kalam | Mixed (Wedding, Housewarming: Avoid) | Avoid |
| Yamagandam | Mixed | Avoid |
| Dur Muhurtam | Caution | Avoid |
| Varjyam | Mixed (Telugu: Caution) | Avoid |
| Gulika Kalam | Mixed | Caution |
| Choghadiya Rog / Kaal / Udveg (Choghadiya traditions) | Caution | Caution |

Applied to the requirement examples:

- Abhijit 16:00–16:48 with Dur Muhurtam 16:06–16:27 gives **16:00–16:06 Favorable, 16:06–16:27 Caution
  (conflict), 16:27–16:48 Favorable**.
- Amrit Kalam with Rahu Kalam gives **Mixed** with conflict `RAHU_KALAM`.

### 6.5 Good Time selection (§9: never manufactured)

A segment, after merging adjacent segments with identical status, reasons and conflicts, qualifies when **all** of
these hold:

- its status is HighlyFavorable or Favorable;
- it has no conflict of Caution or Block severity;
- it lies inside the allowed window;
- it lasts at least the profile minimum (default 24 min, one muhurta).

Qualifying segments are ranked by score, then duration. When none qualifies, the result is `goodTimes: []` with
`noGoodTimeReason` (e.g. `NO_QUALIFYING_PERIOD`, or `DAY_EXCLUDED_FOR_ACTIVITY` such as Vishti all day for a
Wedding). **A Mixed or Caution segment is never promoted.**

### 6.6 Shared lunisolar calendar

Wedding and Housewarming need the lunar month (Chaturmas, Adhika exclusion). `LunisolarCalendar` is currently
internal to `Festivals.Calculation`. **Proposal:** move it to `Astronomy.Calculation` as a public
`ILunisolarCalendar` (Amanta month, Adhika, Sankranti), used by both Festivals and Muhurta. This is a small refactor
with its own ADR. Neither feature may depend on the other's internals.

### 6.7 Traditions

A Muhurta tradition profile (`muhurta-activities.json → traditions`) partially overrides the defaults. It is
independent of the Panchang display profile:

- **Gujarati and North Indian:** Business and Travel use Choghadiya (Labh, Amrit and Shubh positive; Rog, Kaal and
  Udveg conflict).
- **Telugu:** Varjyam is a strong conflict; Amrita Kalam is a strong positive.
- **Tamil:** Rahu Kalam, Yamagandam and Kuligai drive conflicts. Gowri Panchangam / Nalla Neram can be added once a
  reference table exists (open item in ADR-0006).

## 7. Caching architecture

- Entries are cached per `muhurta:{lat}:{lon}:{tz}:{date}:{activity}:{traditionProfileHash}:{rulesVersion}:{calcVersion}`,
  with local-midnight expiry (ADR-0003). Evaluation costs a few milliseconds on top of the Panchang days.
- Day summaries for a month are cached per day and reused, as for Panchang and festivals.
- AI explanations are cached separately per evaluation hash, language, prompt version and provider/model, so a
  repeated view never calls the LLM again.
- "Other" text is classified first. The cache key uses the **resolved activity key**, never the free text.
- Muhurta, like Festivals, calculates Panchang days through `IPanchangCalculator`, so a cold request computes them a
  third time (a few ms per day). If metrics show this matters, add a cached `IPanchangCalculator` decorator in a
  small Panchang caching project (separate ADR).

## 8. Database design

Nothing relational in this phase: rules and prompts are versioned JSON, as for guidance and festivals.
Personalization (saved activities, birth Nakshatra for Tara Bala) needs the Authorize service's user identity and a
PII review, and is deferred.

## 9. API contracts

All endpoints are anonymous, like Panchang. They take the existing location parameters plus `tradition` and
`language`.

| Endpoint | Purpose |
|----------|---------|
| `GET /api/v1/muhurta/activities` | Activity catalog for the cards: `key`, `name`, `description`, `icon` (icon key), `order`, `requiresInput` (`other`), `rulesCompleteness` (`full` / `partial`). The UI renders cards from this list, so a new activity needs **no UI release** (unknown icon keys fall back to a generic icon). |
| `GET /api/v1/muhurta/good-times?date=&activity=&otherText=` | Full evaluation of one day (below). |
| `GET /api/v1/muhurta/day-summaries?start=&days=&activity=general&otherText=` | Calendar tiles and the Find a Good Time month view: per day `status`, `bestGoodTime`, `goodTimeCount`. |
| `GET /api/v1/muhurta/good-times/best?activity=&otherText=&count=3&days=30` | "Next best times": from now, at most one option per day (that day's highest-ranked Good Time that has not ended), Highly Favorable before Favorable, then soonest. Each option's `segment` is the same segment (same `id`) as `good-times` returns for its date. Only Good Times; empty with `noGoodTimeMessage` when none qualifies. Search stops early once `count` Highly Favorable days are found. Configured by `Muhurta:BestSearchDefaultDays` (30), `BestSearchMaxDays` (90), `BestMaxCount` (10). |
| `GET /api/v1/muhurta/good-times/explanation?…` | Optional AI narrative (§11). Separate, so LLM latency never delays the recommendation. |
| Panchang days | Gain `goodTime` (the General day summary, composed in the host like `festivals`), so the month view stays one call. |

`good-times` response (abridged):

```json
{
  "activity": { "key": "business", "name": "Business", "requested": "business", "rulesCompleteness": "full" },
  "date": "2026-10-01",
  "location": { "...": "as today" },
  "tradition": { "key": "gujarati", "name": "Gujarati" },
  "rulesVersion": "2026.1-provisional",
  "dayStatus": { "status": "Favorable", "statusName": "Favorable", "icon": "🟢" },
  "now": { "segmentId": "s7", "status": "Mixed" },
  "next": { "segmentId": "s9", "status": "Favorable" },
  "goodTimes": [ { "segmentId": "s9", "rank": 1 } ],
  "noGoodTimeReason": null,
  "segments": [
    {
      "id": "s9",
      "start": "2026-10-01T16:27:00-05:00", "end": "2026-10-01T17:16:00-05:00",
      "startDisplay": "10/01/2026 16:27:00 CDT", "endDisplay": "10/01/2026 17:16:00 CDT",
      "status": "Favorable", "statusName": "Favorable", "score": 82, "isGoodTime": true,
      "reasons": [
        { "type": "positive", "code": "FAVORABLE_NAKSHATRA", "label": "Favorable Nakshatra", "detail": "Rohini" },
        { "type": "positive", "code": "NO_RAHU_KALAM_CONFLICT", "label": "No Rahu Kalam conflict" }
      ],
      "conflicts": []
    },
    {
      "id": "s7",
      "status": "Mixed", "isGoodTime": false,
      "reasons": [ { "type": "positive", "code": "AMRIT_KALAM", "label": "Amrit Kalam" } ],
      "conflicts": [
        { "code": "RAHU_KALAM", "kind": "RahuKalam", "severity": "Mixed", "label": "Overlaps Rahu Kalam",
          "start": "…14:49…", "end": "…16:18…", "overlapStart": "…14:58…", "overlapEnd": "…16:18…" }
      ]
    }
  ],
  "disclaimer": "This provides guidance based on today's Panchang. For important life events…"
}
```

- `status` values: `HighlyFavorable`, `Favorable`, `Mixed`, `Caution`, `Avoid`. These are the existing
  `GuidanceRating` names, so one vocabulary covers the whole app.
- Codes are stable UPPER_SNAKE strings, and labels are localized content. The UI branches on `status`, `isGoodTime`
  and codes, **never on text**.
- `now` and `next` are computed by the backend from the request instant in the location's time zone. This replaces
  the client-side "good time now" logic (C5).

## 10. Authentication design

The endpoints stay anonymous, consistent with Panchang, and are protected by the existing rate limiter. The UI
currently routes `/good-time` behind login (`ProtectedRoute`), which is a product choice the API does not require.
The AI explanation endpoint gets its own, stricter rate-limit policy because of cost. Personalized Muhurta (future)
will require the Authorize service's token (`docs/prompts/vediczora-authorize.md`).

## 11. Generative AI integration

**Role (C1):**

- explanation narrative;
- "Other" → activity classification;
- (future) conversational Q&A grounded in the same evaluation.

The AI **never** affects status, times, ranking or Good Time qualification.

**Port and providers.** `IStructuredTextGenerator.GenerateAsync<T>(AiRequest, JsonSchema, CancellationToken)` sits
over `Microsoft.Extensions.AI.IChatClient`. Developers switch providers by configuration, with no code change:

```json
"Ai": {
  "Enabled": false,
  "Provider": "Anthropic",            // Anthropic | OpenAI | AzureOpenAI | None
  "Model": "claude-opus-5-5",         // per provider; e.g. an OpenAI model id or an Azure deployment name
  "MaxOutputTokens": 2000,
  "Anthropic":   { "ApiKey": "<Key Vault / user-secrets>" },
  "OpenAI":      { "ApiKey": "<Key Vault / user-secrets>" },
  "AzureOpenAI": { "Endpoint": "https://….openai.azure.com", "Deployment": "…", "UseManagedIdentity": true }
}
```

- **Anthropic:** the official `Anthropic` NuGet package, which implements `IChatClient`. Default model
  `claude-opus-5-5`. Structured outputs (`OutputConfig.Format` JSON schema). Handle the `refusal` stop reason and
  enable the server-side refusal fallback.
- **OpenAI:** `Microsoft.Extensions.AI.OpenAI`.
- **Azure OpenAI / Foundry:** the Microsoft route the "Copilot" requirement most likely means (C3). Uses managed
  identity, and Claude can also run there.
- `Provider: None` or `Enabled: false` means the template explainer only. This is the default, so the feature works
  fully without AI.

**Guardrails:**

1. **Input:** the structured evaluation only. No coordinates beyond the city name, no user identifiers, no free text
   except the "Other" label (≤ 80 characters, sanitized).
2. **Output:** schema-constrained JSON `{ summary, segmentNotes[{ segmentId, text }] }`.
3. **Validation:**
   - every `segmentId` must exist;
   - every time mentioned must appear in the input;
   - the text must not call a non-qualifying segment good (checked with the reason codes).

   On any failure, refusal, timeout or open circuit breaker, the template text is used and logged with a reason
   code.
4. **Prompts:** versioned data (`muhurta-ai-prompts.json`). `promptVersion` and `provider` / `model` are returned
   for traceability.
5. **Operations:**
   - resilience via `Microsoft.Extensions.Http.Resilience`: timeout, retry on transient errors only, circuit breaker;
   - logging per §12: provider, operation, duration, tokens and outcome, with no prompt bodies by default;
   - metrics: latency, fallback rate, cost.
6. **Before enabling in production:** an eval set (~50 representative evaluations) with automated checks of the
   validation rules plus spot review, run per provider and model change.

## 12. Observability design

- Activities: `muhurta.evaluate`, `muhurta.explain`, `ai.generate`.
- Metrics:
  - evaluation latency, cache hit ratio;
  - share of days with no Good Time, per activity;
  - AI latency, fallback rate by reason, and tokens per provider.
- Logs carry activity, tradition, `rulesVersion`, cache hit/miss, provider and duration, with the location rounded
  per §6.

## 13. Security architecture

- AI keys come from Key Vault, user-secrets or managed identity, never from `appsettings`.
- "Other" text is length-limited, sanitized, never logged in full, and passed to the LLM as data in a delimited
  field. It is treated as untrusted (prompt-injection safe) and cannot change instructions because output is
  schema-validated.
- Per-endpoint rate limits, with the AI endpoint strictest.
- No PII to providers. Data-processing terms are reviewed per provider before enabling.

## 14. Testing strategy

- **Calculation:**
  - the requirement examples exactly: Abhijit plus Dur Muhurtam splits into three segments; Amrit plus Rahu is Mixed;
  - one case for each conflict row;
  - "never promote Mixed, Caution or Avoid" as a property test over a year of days × activities × traditions;
  - "no Good Time" days;
  - DST and cross-midnight segments.
- **Unit:**
  - every activity and tradition rule parses, and every code has a label;
  - tradition overrides and caching;
  - AI guardrails with a fake `IChatClient`: hallucinated time, unknown segment, refusal and timeout all fall back
    to the template;
  - provider factory selection.
- **Integration:**
  - endpoints, validation and OpenAPI examples;
  - Panchang days' `goodTime` equals `day-summaries` and `good-times` for General (§11 consistency).
- **UI (`myvedicstar-ui`):**
  - contract (zod) tests;
  - activity cards as an accessible radio group (axe and keyboard);
  - request de-duplication and abort;
  - error state keeps the selection;
  - Home, calendar and timeline render backend status only;
  - Playwright and React Native Testing Library.
- **Domain review:** the reference astrologer reviews the rule profiles and conflict matrix (`2026.1-provisional`)
  before release.

## 15. Decisions (accepted 2026-10-01 as recommended)

| # | Decision | Recommendation |
|---|----------|----------------|
| D1 | AI role | Explanation and "Other" classification only; the deterministic engine decides (C1). |
| D2 | Wedding and Housewarming without Lagna, planets or personal data | Ship with available factors, cap at Favorable, mark `rulesCompleteness: partial` with reason `PERSONALIZED_MUHURTA_RECOMMENDED`. Alternatively, hide them until Lagna and planetary positions exist. Update: Guru and Shukra Asta (Jupiter and Venus combustion) are now checked (ADR-0011); Lagna and personal factors are still missing. |
| D3 | Calendar tile and Home status (C4) | Tiles and Home use the **General Muhurta day status**. `guidance.rating` stays the limb rating on the detail page, labelled "Day factors". |
| D4 | "Copilot" provider (C3) | Azure OpenAI / Azure AI Foundry. |
| D5 | UI work in `myvedicstar-ui` | Implement in that repository in phase M4, following its own architecture docs. |

## 16. NuGet packages

| Package | Purpose |
|---------|---------|
| `Microsoft.Extensions.AI` (+ `.Abstractions`) | `IChatClient` port |
| `Anthropic` | official Claude SDK (implements `IChatClient`) |
| `Microsoft.Extensions.AI.OpenAI` | OpenAI adapter |
| `Azure.AI.OpenAI`, `Azure.Identity` | Azure OpenAI with managed identity |
| `Microsoft.Extensions.Http.Resilience` | timeout, retry, circuit breaker |

Nothing new is needed for the calculation projects.

## 17. Implementation phases

| Phase | Content | Exit criterion |
|-------|---------|----------------|
| M0 | Approve this document; ADR-0011 (Muhurta module and rules engine), ADR-0012 (provider-neutral AI, explanation-only), ADR-0013 (shared lunisolar calendar) | Decisions D1–D5 recorded |
| M1 | `Muhurta.Calculation` and `.Data` (all activities, conflict matrix, tradition overrides), `.Business` facade and cache; move `LunisolarCalendar` | Calculation and unit tests green, including the requirement examples |
| M2 | API: `activities`, `good-times`, `day-summaries`, `goodTime` on Panchang days; OpenAPI; deprecation notice on `guidance.bestWindows` | Integration and consistency tests green |
| M3 | `Ai.Integration` (Anthropic, OpenAI, Azure OpenAI), explainer and "Other" classifier, guardrails, explanation endpoint, eval set; disabled by default | Guardrail tests green; eval passes for each enabled provider |
| M4 | `myvedicstar-ui`: contracts, api-client, activity cards, results, disclaimer, Home and calendar switched to backend status, Avoid shown red, client-side overlap and good-time logic removed | UI checks, a11y and e2e green on web and mobile |
| M5 | Astrologer review of rule profiles; reference dataset tests | Rules version leaves `provisional` |
