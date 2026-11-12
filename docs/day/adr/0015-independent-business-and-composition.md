# ADR-0015: Separate module contracts and composition from Business

- Status: Accepted for the authorized architecture refactor
- Date: 2026-11-12
- Supersedes: ADR-0001 composition policy and the corresponding composition policy in ADR-0008

## Context

Business registration previously referenced concrete Data and Integration projects.
This conflicts with the approved dependency-inversion rules. Star Shared also
registered its cache provider, and Settings Data reused Authorize Data configuration.

## Decision

Move cross-layer persistence/integration contracts into module Shared projects.
Keep facade interfaces and internal business implementations in Business. Move
provider composition to MyVedicStar.Infrastructure.DependencyInjection, which the
hosts invoke. Extract shared PostgreSQL configuration and data-source ownership to
MyVedicStar.Infrastructure.Data. Keep persistence queries in their owning modules.
Move Star cache provider registration out of Shared into composition.

Existing namespaces are retained where needed to minimize consumer churn. Moving a
type between assemblies is an assembly-level change; all solution projects are
rebuilt and deployed together. This is not a promise of binary compatibility for
independently compiled consumers.

### Module HTTP ownership

The executable hosts remain `MyVedicStar.API` and `MyVedicDay.Api`. HTTP controllers
now live in Star Authorize/Settings/Email API libraries and Day
Location/Traditions/Panchang/Festivals/Muhurta API libraries. Hosts explicitly add
these application parts. Shared Day HTTP response mapping and language negotiation
live in `MyVedicDay.Api.Common`, not in Business or Shared domain projects.

GeoNames provider contracts reside in `GeoNames.Shared`; `GeoNames.Business`
exposes location lookup use cases. `GeoNames.Api` supplies the standalone Day HTTP
adapter. The combined host replaces that compatibility adapter with the Star
Authorize API adapter, preserving its additional address endpoints. The provider
assembly remains `GeoNames` for deployment compatibility but no Business assembly
references it.

Reference-catalog caching runs inside Business facades, not controllers. Cache
key fields, language/tradition normalization and two-day TTL are preserved. The
cache helper assembly moves, so its deployment-version component invalidates old
reference entries, as intended on deployment; calculation cache policies are unchanged.

## Preservation

No route, authentication policy, JSON schema, business calculation, database schema,
or cache policy change is intended. Email HTTP operations now use IEmailFacade;
the existing validation and delivery service remains internal behind that facade.

## Verification and limitations

Day layer tests and combined-host module boundary tests guard dependency inversion.
Behavioral tests remain required alongside these structural tests. The combined
production entry point remains MyVedicStar.API. Module libraries are deployed together with that host; the new API DLLs are required
by the published-host smoke test.
