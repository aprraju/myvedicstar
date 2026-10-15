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

   Three options express the classical tie-breaks:
   - `avoidBhadra`: the window counts only where Bhadra (Vishti Karana, the first half of Purnima) does not prevail.
   - `sunriseDayMinimumFraction`: when no day's window is fully covered (Bhadra-free), the day at whose sunrise the
     element prevails is chosen if the element lasts at least this fraction of that daytime. Examples: three Muhurtas
     (0.2) for Raksha Bandhan and Vijayadashami, three Praharas (0.75) for Holika Dahan.
   - `occurrence: Last`: a solar-month rule keeps only the month's last occurrence. Onam, Karthigai Deepam and Thai
     Pusam fall on the later Nakshatra when it occurs twice in Chingam, Karthigai or Thai. To decide this, the engine
     calculates the rest of the solar month only when such a festival falls in the requested range.

   Two further options:
   - the observance time `Moonrise`: the moonrise between the day's sunrise and the next (Sankashti Chaturthi, Karwa
     Chauth). A day without a moonrise has no window. Moonrise is searched only for the days a matching Tithi touches.
   - `ekadashi: Smarta | Vaishnava` replaces the day choice for Ekadashi fasts. Smarta: the Ekadashi at sunrise, moved
     a day earlier when Dwadashi ends before the next sunrise (Parana must fall within Dwadashi). Vaishnava: the last
     day whose Arunodaya (96 minutes before sunrise) lies within the Ekadashi, or else the following day. The
     `VaishnavaEkadashi` observance is hidden when it falls on a Smarta Ekadashi day. Vaikuntha Ekadashi keeps the
     plain sunrise rule, which matches the Tirumala and Srirangam dates (23 December 2023, 30 December 2025).

   Rules with `avoidBhadra` or `sunriseDayMinimumFraction` first take the first day whose whole window the element
   covers. Other rules keep the plain "most of the window" choice. Applying the full-coverage step everywhere would
   move Janmashtami 2021 off the published date: Ashtami covers Nishita on both nights, and the real tie-break, Rohini,
   is not modelled.
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
  and Diwali after the Adhika month; Makar Sankranti 2026). Rules version `2026.2-provisional` adds Holika Dahan and
  Holi 2023 and 2026, Raksha Bandhan 2021–2024, Vijayadashami 2022 and 2024, Janmashtami 2021, and the last-occurrence
  cases Onam 2024, Karthigai Deepam 2024 and Thai Pusam 2025.
- Known simplifications, to review with the reference astrologer (rules version `2026.2-provisional`):
  - Bhadra is handled through the generic options above, not the full Bhadra Mukha and Bhadra Puchha rules.
  - Ekadashi conventions follow the classical rules above, checked against four 2025 Ekadashis (Nirjala,
    Devshayani, Prabodhini, Mokshada). Mahadwadashi cases and regional Vaishnava calendars (ISKCON, Sri Vaishnava)
    are not modelled separately.
  - Solar months begin at sunset of the Sankranti day (the Tamil convention).
  - A Nakshatra that occurs twice in one solar month gives both days, except for rules marked `occurrence: Last`.
  - Akshaya Tritiya 2026 comes out as 20 April (Tritiya covers more of that Purvahna). Some almanacs give 19 April,
    which has not been verified.
  - Kshaya months are not modelled.
