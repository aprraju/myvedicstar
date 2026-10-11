# ADR-0010: Festivals as a feature module with data-driven, tradition-aware rules

- Status: Accepted
- Date: 2026-10-01

## Context

Users want the upcoming festivals for the next 30 days, and want to see a festival on the day it falls in the today,
ten-day and month views. Festival dates depend on:

- **Location:** a festival falls on the day its Tithi or Nakshatra prevails during a traditional observance time
  (Kala). Sunrise and sunset differ by location, so dates can differ between India and the USA.
- **Lunar month:** months are counted from new moon to new moon (Amanta), and an Adhika (intercalary) month moves
  every later festival by a month (2026 has Adhika Jyeshtha).
- **The Sun:** solar festivals (Makar Sankranti, Puthandu, Vishu) follow the Sun's entry into a sign, and Tamil and
  Kerala festivals use solar months.
- **Tradition:** which festivals are observed (Ugadi vs Gudi Padwa, Karwa Chauth, Karthigai Deepam), what they are
  called (Vinayaka Chavithi, Vinayagar Chathurthi, Ganesh Chaturthi), and on which day of Diwali the main celebration
  falls.

ADR-0008 lists festivals as a future feature domain.

## Decision

1. **New feature domain `MyVedicDay.Festivals.{Calculation,Data,Business}`.**
   - `Festivals.Calculation` is pure. It uses `Astronomy.Calculation` and Panchang's calculation contract
     (`IPanchangCalculator`) for Tithi, Nakshatra and sun events (ADR-0008, rule 3). It adds the Amanta lunar month
     with Adhika detection and Sankranti search.
   - `Festivals.Data` holds `festival-rules.json` (versioned) and `festivals-content.{language}.json`.
   - `Festivals.Business` exposes `IFestivalFacade`.
2. **Rules are data.** Each festival has one or more rules:
   - a Tithi or Nakshatra, optionally restricted to an Amanta lunar month (never an Adhika month) or a solar month;
   - an observance time: Sunrise, Arunodaya, Purvahna, Madhyahna, Aparahna, Sunset, Pradosha or Nishita;
   - or a Sankranti, optionally of one sign;
   - optional day offsets (Holi is +1 from Holika Dahan, Bhogi −1 from Makar Sankranti).

   A festival falls on the day the element covers most of the observance window. The earlier day wins a tie (a Tithi
   that prevails at two sunrises). When the element touches no window, the day it covers most wins. A Sankranti
   falls on the first day whose observance time is at or after it.
3. **Traditions** decide *which* festivals are listed (each festival lists its traditions, or `*`; `all` lists every
   festival). Regional names are content (`{tradition}/festivals.{key}`). A festival may *supersede* others on the
   same date, so a named festival hides the generic observance (Vaikuntha Ekadashi hides Ekadashi).
4. **Cache:** daily entries per location, tradition-neutral, with local-midnight expiry (ADR-0003). Missing days
   are calculated together, because a festival's day depends on its neighbours. The key carries the rules version,
   the festival calculation version and the Panchang calculation version.
5. **API:**
   - `GET /api/v1/festivals/upcoming?days=30` (1–90 days).
   - Every Panchang day carries `festivals`. Panchang and Festivals are separate feature modules, and neither
     business layer may depend on the other. The host therefore composes the two facades in `PanchangController`,
     and the response mapper attaches festivals to days by date.

## Consequences

- Adding or correcting a festival, or a tradition's set or names, is a data change plus a rules-version bump.
- The first request for a new location and range calculates its Panchang days a second time for the festival module.
  This takes a few milliseconds per day, and both results are cached. If it matters at scale, a shared per-day
  calculation cache can be introduced behind `IPanchangCalculator`.
- Verified against published dates (2025: Ugadi, Rama Navami, Raksha Bandhan, Ganesh Chaturthi, Navaratri,
  Vijayadashami, Diwali, Maha Shivaratri, Holika Dahan, Holi, Onam, Puthandu; 2026: Raksha Bandhan, Ganesh Chaturthi
  and Diwali after the Adhika month; Makar Sankranti 2026).
- Known simplifications, to review with the reference astrologer (rules version `2026.1-provisional`):
  - Bhadra (Vishti) avoidance is approximated: Raksha Bandhan uses the sunrise Purnima. Holika Dahan uses Pradosha
    without the Bhadra check.
  - Moonrise-based observances (Karwa Chauth, Sankashti Chaturthi) use Pradosha.
  - Ekadashi uses the sunrise rule, without the Vaishnava Dashami-viddha rule.
  - Solar months begin at sunset of the Sankranti day (the Tamil convention).
  - If a Nakshatra occurs twice in one solar month, both days are listed.
  - Kshaya months are not modelled.
