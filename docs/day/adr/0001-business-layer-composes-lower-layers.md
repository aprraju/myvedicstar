# ADR-0001: The Business layer composes Calculation, Data and Integration

- Status: Superseded by ADR-0015 (layout previously superseded by ADR-0008)
- Date: 2026-09-28

## Context

The architecture rules say the API must depend only on the Business Facade and must not access calculation, data or
integration code. They also show `Program.cs` calling `AddPanchangCalculation`, `AddPanchangData` and similar
registration methods directly. That would require the API project to reference every lower-layer project, which
makes their public types compile-visible to controllers.

## Decision

`AddPanchangBusiness(configuration)` registers the Business layer and calls `AddPanchangCalculation`,
`AddPanchangCaching` and `AddPanchangIntegration` itself. The API project references only Business and Shared.

`LayerDependencyTests` asserts that the API assembly does not reference the Calculation, Data or Integration
assemblies.

## Consequences

- Controllers cannot accidentally use a repository, cache or calculator: the boundary holds at compile time as well
  as in tests.
- `Program.cs` stays two lines of service registration.
- Hosts other than the API (such as a background worker) get the full stack from one call.
- Lower layers are still individually registrable for tests.
