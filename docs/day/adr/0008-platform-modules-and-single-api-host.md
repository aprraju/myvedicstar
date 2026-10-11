# ADR-0008: Shared platform modules and a single API host

- Status: Accepted
- Date: 2026-09-29
- Supersedes: parts of ADR-0001 (project layout); ADR-0001's composition principle still applies.

## Context

The platform will add features beyond the Panchang calendar: marriage and other Muhurtas, Kundali, festivals. Those
features need the capabilities that were built inside the Panchang projects:

- astronomy (sunrise, Sun and Moon positions, ayanamsa, transition search);
- location and time zone resolution;
- language negotiation, content catalogs and date/time display formatting;
- the list of regional traditions;
- caching with local-day expiry.

Leaving them in `MyVedicDay.Panchang.*` would force new features to depend on Panchang internals.
Moving them into `MyVedicDay.Shared` would break its rule of holding only small cross-cutting primitives.

## Decision

Organize the solution as a **modular monolith**:

| Kind | Projects |
|------|----------|
| Host | `MyVedicDay.Api`: one deployable host; controllers and contracts grouped per domain |
| Feature domain | `MyVedicDay.Panchang.{Business,Calculation,Data}`; later `MyVedicDay.Muhurta.*`, `MyVedicDay.Kundali.*` |
| Platform | `MyVedicDay.Astronomy.Calculation`, `MyVedicDay.Location.{Business,Integration}`, `MyVedicDay.Localization.{Business,Data}`, `MyVedicDay.Traditions.Business`, `MyVedicDay.Caching` |
| Shared | `MyVedicDay.Shared` (exceptions) |

Rules, enforced by `LayerDependencyTests`:

1. The host references only Business modules and Shared.
2. Platform modules never reference a feature domain.
3. A feature may use platform modules. It may also use another feature's **calculation contract** (for example
   Muhurta uses `IPanchangCalculator`), but never another feature's business internals or display facades.
4. Calculation projects stay free of I/O, web and persistence frameworks.

Further decisions:

- **Content:** content is contributed per module. Each module embeds `{module}-content.{language}.json` and registers
  it with `AddContentSource`, and the Localization module combines them. Content categories are owned per module.
- **Traditions:** the tradition list and names are shared (`Traditions` module). Each feature maps a tradition to its
  own rules (Panchang: `panchang-traditions.json`); a tradition without a Panchang profile uses the `all` profile.
- **Registration:** platform registrations are idempotent, so every feature's `Add{Feature}Business` can register the
  platform modules it needs.
- **Routes:** feature endpoints live under the feature (`/api/v1/panchang/...`, including `/panchang/glossary` and
  `/panchang/traditions`). Shared endpoints are `/api/v1/traditions` and `/api/v1/locations`.
- **Configuration:** settings moved from `Panchang:` to the owning module: `Location:CoordinateDecimalPlaces` and
  `Traditions:DefaultTradition`.

## Consequences

- A new feature is new projects plus one `Add{Feature}Business` line in `Program.cs`. It reuses location, time zone,
  language, display format, traditions, caching and astronomy without touching Panchang.
- One process to deploy and operate. A module can later be split into its own service behind the same facade if
  scale requires it.
- API changes (no external clients yet):
  - `/api/v1/glossary` → `/api/v1/panchang/glossary`;
  - `/api/v1/traditions` now returns the shared list (names and regions); Panchang display lists moved to
    `/api/v1/panchang/traditions`;
  - the OpenAPI title is now "MyVedicDay API".
- Test projects follow the modules: `Astronomy.Tests.Calculation`, `Location.Tests.Unit`,
  `Panchang.Tests.{Unit,Calculation}` and `Api.Tests.Integration`, which also holds the dependency rules.
