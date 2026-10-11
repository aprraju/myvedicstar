# ADR-0011: Muhurta feature module with a data-driven rules engine and conflict resolution

- Status: Accepted
- Date: 2026-10-01
- Design: [Find a Good Time](../architecture/find-a-good-time.md)

## Context

"Find a Good Time" recommends time segments for an activity: Business, Travel, Wedding, General and others. A
period must not be called good just because it lies inside one favorable Muhurta. Overlaps with unfavorable
periods must be resolved first. Home, the calendar, the timeline and mobile must show the same result, and
activities must be addable without redesigning the UI or the engine. ADR-0009 reserved activity-specific
evaluation for a Muhurta module.

## Decision

1. **New feature domain `MyVedicDay.Muhurta.{Calculation,Data,Business}`** (ADR-0008).
   - `Muhurta.Calculation` is pure and uses Panchang's calculation contract (`IPanchangCalculator`) and the shared
     `ILunisolarCalendar` (ADR-0013).
2. **One rule interpreter, many profiles.** Each activity is a profile in `muhurta-activities.json` (versioned),
   built from shared defaults plus partial per-activity overrides plus partial per-tradition overrides. A profile
   holds:
   - limb factor effects (Supportive, Neutral, Adverse, Excluded) for Tithi, Nakshatra, Yoga, Karana and Vara,
     given per value or per traditional group;
   - limb weights and score thresholds;
   - favorable periods with their score bonus;
   - unfavorable periods with a severity *alone* and *when overlapping a favorable period* (the conflict matrix);
   - the evaluation window (daytime or all day), the minimum Good Time length, an optional status cap, and excluded
     lunar months.

   No activity is a code branch.
3. **Exact segmentation.** The day is cut at sunrise and sunset, at every considered period boundary and at every
   Tithi, Nakshatra, Yoga and Karana change. Each elementary segment is evaluated, and identical neighbours
   (same status, reasons and conflicts) are merged.
4. **Status** uses `HighlyFavorable, Favorable, Mixed, Caution, Avoid`, the existing guidance vocabulary:
   - the limb score maps to a base status;
   - an excluded limb caps the segment at Avoid;
   - every active unfavorable period caps it at its severity;
   - the profile cap applies last.
5. **Good Time** means status HighlyFavorable or Favorable, no conflict, inside the window, and at least the
   minimum length. Nothing else is ever returned as a Good Time. A day without one returns an empty list and a
   reason code.
6. **Structured output only.** The result carries statuses, reason codes, conflict codes and ISO times. Labels are
   localized content, and clients never parse text.
7. **Calendar and Home** use the General activity's day status (decision D3). The limb-only `guidance.rating`
   remains the "day factors" rating on the detail view.
8. **Wedding and Housewarming** are evaluated with the available factors only (no Lagna, planets or personal data).
   They are capped at Favorable and marked `rulesCompleteness: partial` (decision D2).

## Consequences

- A new activity or a rule change is a data edit plus a rules-version bump. The UI renders activities from
  `GET /api/v1/muhurta/activities`.
- All provisional values (`2026.1-provisional`) need review by the reference astrologer.
- The conflict matrix makes both requirement examples expressible: Abhijit + Dur Muhurtam resolves to Caution, and
  Amrit Kalam + Rahu Kalam to Mixed.
