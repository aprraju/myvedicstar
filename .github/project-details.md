# MyVedicStar Project Details

## Overview
MyVedicStar is a modular .NET solution that combines the MyVedicDay Panchang and Muhurta platform with the MyVedicStar authorization, identity, settings, and shared-host capabilities. The repository is organized as a modular monolith with separate project boundaries for business, calculation, data, integration, caching, shared contracts, and hosting responsibilities.

## Current product architecture
- Production host entry point: `src/MyVedicStar.API/MyVedicStar.API.csproj`
- Standalone Day API: `src/MyVedicDay.Api`
- Shared platform and domain modules live under `src/`
- The solution retains separate responsibilities for business logic, calculations, data access, integrations, and shared infrastructure

## Main solution responsibilities

### Host and integration projects
- `MyVedicStar.API` — combined host for authorization, GeoNames, and shared hosting behavior
- `MyVedicDay.Api` — standalone Day API with its own controllers and run profile
- `SharedApi` — shared host/API infrastructure support

### Domain feature projects
- `MyVedicDay.Panchang.Business`, `.Calculation`, `.Data`
- `MyVedicDay.Festivals.Business`, `.Calculation`, `.Data`
- `MyVedicDay.Muhurta.Business`, `.Calculation`, `.Data`

### Platform and support projects
- `MyVedicDay.Astronomy.Calculation`
- `MyVedicDay.Location.Business` and `.Integration`
- `MyVedicDay.Localization.Business` and `.Data`
- `MyVedicDay.Traditions.Business`
- `MyVedicDay.Caching`
- `MyVedicDay.Ai.Integration`
- `GeoNames`

### Identity and settings modules
- `MyVedicStar.Authorize.Business`, `.Data`, `.Integration`
- `MyVedicStar.Settings.Business`, `.Data`, `.Integration`
- `MyVedicStar.Email.Business`, `.Integration`

### Shared infrastructure
- `MyVedicDay.Shared`
- `MyVedicStar.Shared`
- `MyVedicStar.Caching`

## Current product scope
The repository currently reflects a concrete implementation baseline centered on:
- location-aware daily Panchang calculations
- Muhurta / Good Time evaluation
- festival and observance lookups
- location resolution and GeoNames-backed place lookup
- user identity, profile, login/logout flows, and session auditing
- shared hybrid caching with in-memory L1 and optional PostgreSQL L2
- deployment and configuration support for combined host execution

## Current API model
The project preserves existing endpoint contracts and compatibility patterns, including:
- `POST /api/v1/auth/login`
- `POST /api/v1/auth/logout`
- `POST /api/v1/auth/userinfo`
- anonymous GeoNames routes under `/api/v1/locations`
- Panchang and Muhurta endpoints in the Day API contract

The host keeps a single OpenAPI/Swagger surface for the combined API, while the Day API remains as a separate project and run configuration.

## Identity and auditing model
Identity behavior is documented in `docs/auth-auditing.md`. The current implementation includes:
- bearer-token-based authentication flows
- DB-backed user registration and profile synchronization
- login/logout auditing in PostgreSQL
- session identity hashing rather than storing raw tokens or credentials
- support for managed identity or password-based database configuration
- profile and display name synchronization patterns with provider pending flags

## Caching model
The hybrid cache architecture is documented in `docs/hybrid-cache.md`. The active design includes:
- bounded native in-memory L1 cache
- optional PostgreSQL-backed L2 cache
- typed and versioned cache keys
- invalidation and cache-key strategy tied to calculation version and inputs
- safe degradation when cache failures occur
- no active Redis dependency in the current implementation

## Deployment and configuration model
The repository supports:
- `dotnet run --project src/MyVedicStar.API --launch-profile shared`
- `dotnet run --project src/MyVedicDay.Api --launch-profile http`
- both `MyVedicDay.sln` and `MyVedicStar.slnx` as relevant validation surfaces
- `MyVedicStar.Deployment.slnx` for deploy-time project composition

Published defaults are namespaced under `apis/day` and `apis/star`, with env variables and command-line options overriding defaults. GeoNames and other shared settings are configured in application settings and can be overridden per environment.

## Documentation and source-of-truth files
Key repo references:
- [README.md](../README.md)
- [docs/day/README.md](../docs/day/README.md)
- [docs/auth-auditing.md](../docs/auth-auditing.md)
- [docs/hybrid-cache.md](../docs/hybrid-cache.md)
- [.github/copilot-instructions.md](copilot-instructions.md)
- [.github/business-requirements/README.md](./business-requirements/README.md)

## Business requirements
The project now includes module-level business requirements derived from the current implementation and documentation. See the per-module files in [.github/business-requirements](./business-requirements/README.md):
- [Panchang business requirements](./business-requirements/panchang.md)
- [Festivals business requirements](./business-requirements/festivals.md)
- [Muhurta business requirements](./business-requirements/muhurta.md)
- [Location business requirements](./business-requirements/location.md)
- [Authorization business requirements](./business-requirements/authorization.md)
- [Settings business requirements](./business-requirements/settings.md)
- [Email business requirements](./business-requirements/email.md)
- [Caching business requirements](./business-requirements/caching.md)

## Important boundary note
This file reflects the current repository state and implementation baseline. It does not describe the full future product roadmap as a committed requirement set. Several major modules and product visions described in broader SRS-style documents remain future or proposed work rather than supported, implemented, or tested project scope.

## Relationship to broader product vision
The repository is aligned to a real, working modular architecture for:
- Panchang calculations
- location-aware daily time computations
- Muhurta / good-time evaluation
- identity and audit capability
- secure config and deployment patterns

It is not yet a complete implementation of a full-marketplace astrology platform spanning all future modules such as Kundali, Rashi, compatibility, AI agents, or broader admin/partner experiences.
