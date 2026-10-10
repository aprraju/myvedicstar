# Vedic Astrology Platform — Visual Studio Architecture & Development Instructions

Act as a **Principal .NET / Azure Solution Architect and Senior Software Engineer**.

I am building a production-grade, scalable **Indian Vedic Astrology platform** using Visual Studio and modern .NET.

The first major capability is a location-aware **Vedic Panchang Calendar**, but the architecture must support future modules such as:

- Kundali / Birth Chart
- Rashi
- Nakshatra
- Lagna
- Navamsa
- Dasha / Antardasha
- Gochar / Planetary Transits
- Muhurta
- Horoscope
- Compatibility
- Festival Calendar
- AI-based astrology explanations

Design the solution as an **enterprise-grade modular application** where business logic, Panchang calculations, integrations, persistence, API concerns, and shared infrastructure are clearly separated.

Do not compromise these architecture rules for implementation convenience.

---

# 1. Core Architecture Principles

Use:

- Latest stable .NET / ASP.NET Core
- C#
- Dependency Injection
- Async/await where appropriate
- SOLID principles
- Clean separation of concerns
- Gang of Four design patterns where they provide real value
- Repository Pattern
- Facade Pattern
- Strategy Pattern where calculations vary
- Factory Pattern where object/provider creation varies
- Adapter Pattern for external providers
- Decorator Pattern where appropriate for logging/caching
- Dependency Inversion
- Interface Segregation
- Strong typing
- Nullable reference types
- Centralized exception handling
- Structured logging
- In-memory caching behind an abstraction
- OpenAPI / Swagger
- Unit and integration testing

Avoid unnecessary abstraction and do not introduce a design pattern simply to say that a pattern is being used.

---

# 2. Solution Naming Standard

The Visual Studio Solution represents the Product.

Naming convention:

`Product.Domain.Type`

Where:

- Product = Product/Application name
- Domain = Functional domain
- Type = API, Business, Calculation, Integration, Data, Shared, Tests, etc.

Example using a placeholder product name `MyPanchang`:

MyPanchang.sln

Projects:

MyPanchang.Panchang.Api  
MyPanchang.Panchang.Business  
MyPanchang.Panchang.Calculation  
MyPanchang.Panchang.Data  
MyPanchang.Panchang.Integration  
MyPanchang.Shared  
MyPanchang.Panchang.Tests.Unit  
MyPanchang.Panchang.Tests.Integration  
MyPanchang.Panchang.Tests.Calculation

Future domains should follow the same pattern:

MyPanchang.Kundali.Business  
MyPanchang.Kundali.Calculation  
MyPanchang.Kundali.Data

Do not place unrelated functionality into one large project simply for convenience.

---

# 3. Dependency Rules

Project dependencies must flow in a controlled direction.

The API must NOT directly access:

- repositories
- database contexts
- Panchang calculation classes
- external integrations
- cache implementation
- internal business services

The API communicates with the Business layer through its **public Facade**.

Conceptually:

API
 |
 v
Business Facade
 |
 +-------------------+
 |                   |
 v                   v
Internal Business   Calculation Engine
Services
 |
 +-------------------+
 |                   |
 v                   v
Data               Integrations

Shared contains cross-cutting contracts/utilities that genuinely need to be shared.

Avoid circular dependencies.

---

# 4. Controller / API Layer

Project:

`Product.Panchang.Api`

Controllers must remain thin.

A Controller is responsible ONLY for HTTP/API concerns.

Allowed responsibilities:

- HTTP routing
- Request binding
- Request validation coordination
- Authentication
- Authorization
- API input sanitization
- Calling the Business Facade
- Mapping API request/response contracts where required
- Returning HTTP responses
- Swagger/OpenAPI documentation

Do NOT put business rules in Controllers.

Do NOT perform Panchang calculations in Controllers.

Do NOT access repositories directly from Controllers.

Do NOT access DbContext directly from Controllers.

Do NOT call external integrations directly from Controllers.

Do NOT implement caching logic directly in Controllers.

---

# 5. Global Error Handling

Prefer centralized ASP.NET Core exception handling middleware / `IExceptionHandler` rather than repetitive try/catch blocks inside every Controller action.

Business and lower layers should throw meaningful custom exceptions.

Examples:

ValidationException  
BusinessRuleException  
ResourceNotFoundException  
CalculationException  
IntegrationException  
DataAccessException  
CacheException

Map exceptions centrally to appropriate HTTP status codes and RFC 7807 Problem Details responses.

Never expose:

- stack traces
- database details
- connection strings
- internal service names
- credentials
- implementation details

to API consumers.

Controllers should not contain repetitive exception-handling code.

---

# 6. API Logging

Use structured logging.

For every API request, capture appropriate operational information such as:

- UTC request timestamp
- Local request timestamp when useful
- Correlation ID / Trace ID
- HTTP method
- Endpoint
- Response status
- Request duration
- Client IP where justified
- User-Agent
- Device/client information when reliably available
- Application version
- Requested Panchang location
- Latitude/longitude only at an appropriate privacy-safe precision when required
- Requested timezone
- Cache hit/miss
- Calculation duration
- External integration duration
- Authentication status where appropriate

Do NOT log unnecessarily:

- passwords
- access tokens
- refresh tokens
- API keys
- authentication headers
- cookies
- secrets
- full personal birth details
- sensitive personal information
- unnecessary precise location

Implement redaction and structured logging.

Use correlation IDs across:

API → Business → Calculation → Data → Integration.

---

# 7. Business Logic Layer

Project:

`Product.Panchang.Business`

The Business Layer owns application/business orchestration.

## Facade Rule

All functionality exposed outside the Business Layer must go through a public Facade/interface.

Example:

`IPanchangFacade`

Possible operations:

GetTodayAsync()

GetTomorrowAsync()

GetDateAsync()

GetDateRangeAsync()

GetTenDaysAsync()

GetMonthAsync()

GetYearAsync()

The API should depend primarily on the Facade contract.

Internal implementation classes should be marked `internal` wherever practical.

Examples:

`PanchangFacade`  
`PanchangOrchestrator`  
`CalendarRangeBuilder`  
`LocationResolver`  
`TimezoneResolver`

Do not expose internal business implementation unnecessarily.

---

# 8. Method Responsibility

Apply Command-Query Separation.

A method should have one clear purpose.

Queries retrieve information.

Commands change state.

Avoid methods that unexpectedly retrieve data, modify unrelated state, write data, send notifications, and perform other unrelated operations simultaneously.

Method names must communicate their intent clearly.

Examples:

GetPanchangAsync()

CalculatePanchangAsync()

SavePanchangAsync()

GetCachedPanchangAsync()

SetCachedPanchangAsync()

Avoid ambiguous methods such as:

Process()

HandleEverything()

DoWork()

ExecuteStuff()

---

# 9. Panchang Calculation Engine

Create a dedicated project:

`Product.Panchang.Calculation`

This project contains astronomical and Panchang calculations.

The Controller must NEVER call it directly.

Flow:

Controller
→ IPanchangFacade
→ Business orchestration
→ Calculation abstraction
→ Panchang Calculation Engine

Define a contract similar to:

`IPanchangCalculator`

The calculation engine should support:

- Sunrise
- Sunset
- Tithi
- Nakshatra
- Yoga
- Karana
- Rahu Kalam
- Yamagandam
- Gulika Kalam
- Varjyam
- Dur Muhurat
- Abhijit Muhurat
- Good/Auspicious periods
- Tithi transitions
- Nakshatra transitions

Every time-based result must correctly represent:

- StartDateTime
- EndDateTime
- TimeZone
- UTC equivalent where appropriate

Calculations must use:

- Date
- Latitude
- Longitude
- Timezone
- Sunrise/sunset
- Required astronomical data

Do not mix UI formatting with astronomical calculations.

---

# 10. Configurable Panchang Rules

Separate astronomical facts from traditional Panchang interpretation.

For example:

Astronomical calculation
→ lunar/solar longitude
→ Tithi determination
→ Panchang rules
→ regional/traditional interpretation
→ API representation

Do not hard-code regional conventions throughout the calculation classes.

Use Strategy Pattern where appropriate.

Examples:

`IPanchangTraditionStrategy`

Possible future implementations:

`DrikPanchangStrategy`

`TeluguPanchangStrategy`

`TamilPanchangStrategy`

`NorthIndianPanchangStrategy`

The exact supported traditions will be decided later, but the architecture must allow them.

---

# 11. Integration Layer

Project:

`Product.Panchang.Integration`

All external system communication belongs here.

Separate integrations by provider and integration type.

Example:

Integration
  /AI
      /OpenAI
      /Claude
  /Location
      /Google
      /AzureMaps
  /Astronomy
      /ProviderA
  /Messaging
  /Events
  /Email

Each external provider must be hidden behind an interface/adapter.

For example:

`IAiProvider`

`ILocationProvider`

`IGeocodingProvider`

`IAstronomyProvider`

The Business Layer should not know provider-specific HTTP details.

---

# 12. Integration Logging

External integrations should log:

- Correlation ID
- Provider
- Operation
- Request start
- Duration
- HTTP status
- Success/failure
- Retry count

Do not log secrets or sensitive request/response data.

If request/response bodies need logging, implement configurable redaction.

Use resilient HTTP patterns including:

- timeout
- cancellation
- retry where appropriate
- circuit breaker where appropriate

Do not blindly retry non-transient failures.

---

# 13. Data Layer

Project:

`Product.Panchang.Data`

Use the Repository Pattern.

Do not expose DbContext directly outside the Data Layer.

Separate persistence concerns by aggregate/entity.

Possible repositories:

`IPanchangRepository`

`ILocationRepository`

`IUserPreferenceRepository`

`ICalendarConfigurationRepository`

Repositories should contain persistence operations, not business rules.

Use EF Core where relational persistence is appropriate.

Support migrations and proper indexes.

All database calls should be asynchronous where appropriate and accept CancellationToken.

---

# 14. Caching Architecture

Use an in-memory cache (`IMemoryCache`) per API instance.

Caching must be behind an abstraction.

The shared cache contract is `ICacheStore` (`MyVedicStar.Shared.Caching`), registered with
`services.AddMyVedicStarCaching(configuration)`. Its current implementation, `MemoryCacheStore`, uses a
private, size-limited `MemoryCache`; do not use the shared `IMemoryCache` service directly.

Each domain wraps `ICacheStore` in its own abstraction that owns key building and expiration rules.

Example:

`IPanchangCache`

The Business Layer must not depend on the cache implementation (`MemoryCache`, or any future provider).

Consequences of an in-memory cache, which callers must accept:

- each API instance has its own cache; entries are not shared between replicas
- entries are lost on restart and deployment
- cached data must therefore be derivable (recalculable), never authoritative state

`ICacheStore` is asynchronous so a distributed cache (for example Redis) can replace the implementation
later without changing callers, if replica count or calculation cost makes per-instance caching insufficient.

Cache Panchang primarily at:

Location + Timezone + LocalDate + CalculationVersion + Tradition

Conceptual key:

`panchang:{locationKey}:{timezone}:{localDate}:{tradition}:{calculationVersion}`

Normalize latitude/longitude appropriately so insignificant coordinate differences do not create millions of redundant cache entries.

---

# 15. Daily Cache Expiration

Cache expiration must be based on the LOCATION'S timezone.

Never expire all Panchang entries according to:

- server midnight
- UTC midnight
- one globally configured timezone

Determine:

Selected location  
→ IANA timezone  
→ next local midnight  
→ convert next local midnight to UTC  
→ use resulting duration/instant for expiration

This must correctly handle DST transitions.

A Panchang request for India and one for Texas may therefore have different UTC expiration times.

---

# 16. Cache-Aside Pattern

Use cache-aside behavior.

For example:

API Request

→ Business Facade

→ Check cache

If HIT:
→ Return cached Panchang

If MISS:
→ Execute calculation
→ Optionally persist appropriate calculated data
→ Store cache entry
→ Return result

Prevent cache stampedes when many requests simultaneously request the same uncached location/date.

`ICacheStore.GetOrCreateAsync` coalesces concurrent misses for the same key within an instance, so each
instance runs the calculation once per key. Instances do not coordinate with each other.

---

# 17. Multi-Day Caching

Do NOT cache unnecessarily duplicated giant objects for:

Today  
10 Days  
Month  
Year

Prefer caching daily Panchang objects.

For a 10-day request:

Check cache for each requested date.

Reuse available entries.

Calculate only missing dates.

Combine daily objects into the API response.

Apply similar logic for month/year queries.

---

# 18. Shared Project

Project:

`Product.Shared`

Shared contains functionality genuinely used across multiple projects.

Examples:

- Cross-layer DTOs only when truly required
- Constants
- Custom exceptions
- Common interfaces
- Extensions
- Date/time utilities
- Timezone utilities
- Correlation utilities
- Result primitives
- Shared validation primitives
- Common HTTP client functionality
- Common serialization functionality

Do NOT turn Shared into a dumping ground.

Before adding something to Shared, determine whether it belongs to a specific domain/layer.

---

# 19. Static Classes

Use static classes only where they are appropriate.

Good candidates:

- constants
- pure utility functions
- extension methods
- deterministic helpers without dependencies or mutable state

Do NOT make business services, repositories, integrations, calculation engines, or cache services static merely for convenience.

Prefer Dependency Injection for services requiring dependencies, configuration, logging, testing, or replacement.

---

# 20. DTO Rules

Layer-specific DTOs remain inside their owning layer.

Examples:

API request/response models
→ API project

Integration request/response models
→ Integration project/provider folder

Persistence models
→ Data project

Calculation models
→ Calculation project

Only DTOs/contracts genuinely shared by multiple architectural boundaries should be placed in Shared or a dedicated Contracts project.

Do not reuse database entities as API response models.

---

# 21. Authentication and Authorization

Design authentication using standards such as:

- OAuth 2.0
- OpenID Connect
- JWT access tokens

Keep authentication provider details configurable.

Support future:

- Google authentication
- Apple authentication
- Microsoft authentication
- Email/password or passwordless authentication

Use policy-based authorization where appropriate.

Never implement custom cryptography or token algorithms when established frameworks exist.

---

# 22. API Design

Use RESTful APIs initially.

Example:

`GET /api/v1/panchang/today`

`GET /api/v1/panchang/tomorrow`

`GET /api/v1/panchang/{date}`

`GET /api/v1/panchang/range`

`GET /api/v1/panchang/month/{year}/{month}`

`GET /api/v1/panchang/year/{year}`

Location may be supplied through appropriate query parameters or a location identifier.

Example request concept:

`GET /api/v1/panchang/today?latitude=...&longitude=...&timezone=...`

Do not blindly trust a timezone supplied by the client.

Validate coordinates and timezone identifiers.

Use API versioning.

Document APIs through Swagger/OpenAPI.

---

# 23. API Response Standards

Return consistent response structures.

Include appropriate metadata such as:

- Requested date
- Location
- Timezone
- Panchang tradition/version where relevant
- Calculation version
- Panchang data

Do NOT expose internal exception details.

Use Problem Details for errors.

---

# 24. Location Architecture

Support:

1. Browser/device geolocation with explicit user permission
2. Manual city/location search
3. Saved user location
4. Appropriate IP-derived approximate location as an optional fallback

Never assume IP geolocation is exact.

Convert selected location into:

- Latitude
- Longitude
- IANA timezone

Use the selected location for Panchang calculations.

---

# 25. Date and Time Standards

Internally prefer:

- UTC instants for absolute timestamps
- IANA timezone identifiers for geographic timezone rules
- local calendar date where Panchang calculation requires it

Avoid relying only on fixed UTC offsets.

Handle:

- DST
- midnight boundaries
- cross-midnight Panchang periods
- ambiguous local times
- invalid local times
- leap years

Consider using NodaTime where its types provide safer timezone/date handling.

---

# 26. Frontend Architecture

Design a modern responsive web application.

Recommended options should be evaluated before implementation.

Possible technologies:

- React / Next.js
- Angular
- Blazor

Frontend should support:

- Today
- Tomorrow
- 10 Days
- Month
- Year
- Location selection
- Automatic location detection
- Calendar navigation
- Panchang detail cards
- Mobile responsive design
- Timezone display
- Loading/error states
- Localization

Keep frontend calculation logic minimal.

The authoritative Panchang calculations belong on the backend.

---

# 27. Observability

Implement structured logging, metrics, and distributed tracing.

Support OpenTelemetry.

If deployed to Azure, consider:

- Application Insights
- Azure Monitor
- Log Analytics

Important metrics include:

- Request count
- Error rate
- Response latency
- Panchang calculation latency
- Cache hit ratio
- Cache miss ratio
- Database latency
- External provider latency
- Calculation failures

Propagate correlation/trace identifiers through all layers.

---

# 28. Security

Follow OWASP recommendations.

Implement:

- Input validation
- Output encoding
- Authentication
- Authorization
- Rate limiting
- HTTPS
- Secure headers
- Secret management
- Request size limits
- Dependency vulnerability scanning
- Secure logging
- PII minimization
- API abuse protection

Use managed identities and secret-management platforms such as Azure Key Vault when deployed to Azure.

Never commit secrets to source control.

---

# 29. Scalability and High Availability

Design APIs to be stateless.

Do not depend on server-local memory for authoritative shared state. The in-memory cache is allowed
because it holds only recalculable data (see section 14).

Support:

API instances
→ Load Balancer / API Gateway
→ Database
→ Integration providers

The application should be horizontally scalable.

Design for:

- multiple API instances, each with its own in-memory cache
- transient failures
- graceful degradation
- health checks
- readiness checks
- dependency failures
- rolling deployments

---

# 30. Testing Architecture

Create separate testing projects where appropriate.

## Unit Tests

Test individual:

- business rules
- services
- strategies
- cache-key generation
- timezone conversion
- validation
- exception behavior

## Calculation Tests

Panchang calculations require specialized tests.

Test known/reference cases for:

- Tithi
- Nakshatra
- sunrise
- sunset
- Rahu Kalam
- Varjyam
- Dur Muhurat
- transitions

Test multiple locations:

- India
- United States
- Europe
- Australia

Test timezone edge cases and DST transitions.

Use reference ephemeris/calculation datasets where appropriate.

## Integration Tests

Test:

API → Business → Cache/Data

and controlled integration boundaries.

External providers should be mockable through interfaces.

---

# 31. Coding Standards

Apply these rules to EVERY:

- solution
- project
- layer
- module
- namespace
- class
- interface
- method

Classes must have one primary responsibility.

Methods must perform one coherent operation.

Prefer small composable components.

Avoid:

- God classes
- giant services
- giant Controllers
- duplicate code
- hidden dependencies
- service locator
- unnecessary static state
- circular dependencies
- business logic inside Controllers
- business logic inside repositories
- database entities leaking into API contracts

Use CancellationToken throughout asynchronous call chains where appropriate.

Use `ConfigureAwait` only according to modern .NET application requirements rather than mechanically adding it everywhere.

---

# 32. Dependency Injection

All significant services should be registered through DI.

Organize registration through layer-specific extension methods.

Example:

`services.AddPanchangBusiness(...)`

`services.AddPanchangCalculation(...)`

`services.AddPanchangData(...)`

`services.AddPanchangIntegrations(...)`

`services.AddPanchangCaching(...)`

Keep `Program.cs` clean.

---

# 33. Configuration

Use strongly typed Options Pattern.

Examples:

`CachingOptions`

`DatabaseOptions`

`PanchangOptions`

`LocationOptions`

`IntegrationOptions`

`ObservabilityOptions`

Validate important configuration during application startup.

Support environment-specific configuration.

Never hard-code credentials or environment-specific URLs.

---

# 34. Documentation

Every public contract should have meaningful XML documentation where useful.

Swagger should describe:

- endpoints
- parameters
- response types
- error responses
- authentication requirements

Architecture decisions that materially affect the system should be captured as Architecture Decision Records (ADRs).

---

# 35. Implementation Rule for Visual Studio AI Agent

Do NOT attempt to generate the entire production application in one uncontrolled step.

Before writing implementation code, produce:

1. Requirements analysis
2. Proposed solution architecture
3. Visual Studio solution/project structure
4. Dependency diagram
5. Layer responsibilities
6. Panchang calculation architecture
7. Caching architecture
8. Database design
9. API contracts
10. Authentication design
11. Observability design
12. Security architecture
13. Testing strategy
14. NuGet package recommendations
15. Implementation phases

Wait for architecture approval before generating the full implementation.

Once architecture is approved, implement incrementally.

For every implementation phase:

1. State which project is being modified.
2. Explain the responsibility being implemented.
3. Create interfaces/contracts first where appropriate.
4. Implement the functionality.
5. Register dependencies.
6. Add configuration.
7. Add validation.
8. Add structured logging.
9. Add exception handling.
10. Add unit tests.
11. Add integration tests where appropriate.
12. Ensure the solution compiles.
13. Ensure existing tests continue to pass.
14. Do not weaken architectural boundaries to resolve compilation issues.

---

# 36. Architecture Enforcement

These rules are mandatory throughout development.

If a requested implementation conflicts with the architecture:

1. Identify the conflict.
2. Explain why it violates the architecture.
3. Recommend an architecture-compliant solution.
4. Implement the compliant solution.

Do not silently bypass architectural boundaries.

Most importantly:

**Controller → Facade → Internal Business Services → Calculation/Data/Integration**

The Controller must never bypass the Facade to directly access Calculation, Data, the cache, or external integrations.

The **Panchang Calculation Engine must remain independently testable and isolated from API, UI, database, and external-provider implementation details.**

Build the platform so that Panchang is the first domain, not the last domain the architecture can support.