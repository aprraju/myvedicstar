# ADR-0009: Daily guidance as a configurable methodology, separate from Muhurta

- Status: Accepted
- Date: 2026-09-29

## Context

Users want a simple answer: "is today a good day?" The five Panchanga limbs (Tithi, Vara, Nakshatra, Yoga, Karana) are
calculated identically for every tradition. How they are *interpreted*, and which time periods matter, differs by
tradition.

A single weighted formula ("Nakshatra 30 %, Tithi 25 %…") is not a universal Vedic rule. Presenting "today is 92 %
good according to Vedic astrology" would misrepresent it. Activity-specific decisions (marriage, Griha Pravesh,
naming) are a separate discipline, **Muhurta**, involving the person's Nakshatra, Tara Bala, Chandra Bala, Lagna and
ceremony-specific rules.

## Decision

1. **Daily guidance, not a verdict.** Each day carries `guidance`, containing:
   - a rating label (Highly Favorable / Favorable / Mixed / Caution / Traditionally Avoided);
   - a localized headline and summary;
   - each limb's rating with its traditional reason;
   - time windows.

   No percentage is shown. An internal score only selects the label.
2. **Two independent evaluations.** The day rating comes from the five limbs. The time windows come from the periods
   the tradition shows: best windows are daytime free of caution and avoid periods, plus favorable, spiritual, caution
   and avoid windows. Windows never change the day rating.

   Periods come from different Panchanga rules and can overlap, e.g. Amrit Kalam (Nakshatra-based, favorable)
   14:58–16:27 and Rahu Kalam (weekday/daylight-based, avoided for new beginnings) 14:49–16:18. Neither cancels the
   other: both are reported with their full times, and the overlap 14:58–16:18 is reported as a **mixed window**
   listing the favorable and unfavorable periods involved. The favorable period itself also carries a note naming
   the times to avoid within it. The app does not decide which rule wins.
3. **The methodology is data.** `panchang-guidance-rules.json` (versioned) holds:
   - weights and score bands;
   - a rating and traditional group for every value;
   - which value of a limb is rated (`PredominantDaytime` by default, because a Yoga or Karana that ends minutes
     after sunrise should not decide the day; `Sunrise` is available);
   - best-window rules;
   - partial overrides per tradition.

   The wording is content (`guidanceRatings.*`, `guidanceText.*`, `guidanceGroups.*`), so it is translatable. Every
   result carries `rulesVersion` and a methodology note.
4. **Layering:**

   ```
   IPanchangCalculator (common astronomy)
     → tradition profile (which periods are shown)
     → IDailyGuidanceEvaluator (daily guidance)
     → future IMuhurtaEvaluator (activity-specific)
   ```

   The guidance evaluator runs after the cache, like localization, so it adds no recalculation and no cache entries.
5. **No activity parameter on daily guidance.** "Is this a good day for marriage?" is answered by a future Muhurta
   module: `MyVedicDay.Muhurta.*` with Marriage, Griha Pravesh, Travel and other evaluators. It builds on
   `IPanchangCalculator` and the shared modules (ADR-0008), never on the daily score.

## Consequences

- Users see guidance and concrete windows instead of a number presented as authoritative.
- The initial ratings (`2026.1-provisional`) follow classical groupings:
  - Tithi: Nanda, Bhadra, Jaya, Rikta, Purna;
  - Nakshatra: Dhruva, Mridu, Kshipra, Chara, Mishra, Ugra, Tikshna;
  - Yoga: the nine malefic Yogas;
  - Karana: Vishti.

  They must be reviewed with the reference astrologer. Changes are data edits plus a version bump.
- Traditions can diverge in interpretation without code changes, and switching tradition never changes the Tithi or
  Nakshatra.
