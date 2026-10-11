# ADR-0013: Lunar month and Sankranti move to the shared astronomy module

- Status: Accepted
- Date: 2026-10-01

## Context

The festival module (ADR-0010) introduced `LunisolarCalendar`: the Amanta lunar month with Adhika detection, and
the Sankranti search. It is internal to `Festivals.Calculation`. Muhurta needs the same calendar to exclude
Chaturmas or Adhika months for weddings and housewarmings. ADR-0008 forbids one feature from using another
feature's internals.

## Decision

Move it to `MyVedicDay.Astronomy.Calculation` as the public `ILunisolarCalendar`, together with `LunarMonth`,
`Rashi` and `Sankranti` (namespace `MyVedicDay.Astronomy`), registered by `AddAstronomy()`. Festivals and Muhurta
both use it.

## Consequences

- One implementation of lunar months for every feature. Festival behaviour and tests are unchanged apart from
  namespaces.
- `Astronomy.Calculation` now also holds calendrical astronomy derived from Sun and Moon positions. It stays pure.
