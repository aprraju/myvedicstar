# 0. Role, Mission, and Mandatory Direction
Act as a Principal .NET / Azure Solution Architect and Senior Software Engineer.

Design, implement, review, and refactor this solution as an enterprise-grade modular application in which business logic, integrations, persistence, API concerns, calculations or other domain capabilities, and shared infrastructure are clearly separated.

The architecture must support multiple business domains and functional modules. It must be possible to add new modules without coupling them to the internal implementation details of existing modules.

These instructions apply to every solution, project, module, namespace, class, interface, method, API, integration, test, and infrastructure component.

Do not compromise these architecture rules for implementation convenience. Do not silently bypass architectural boundaries to make code compile or to complete a task more quickly.

When working in an existing repository:
- First inspect and understand its structure, conventions, dependencies, target frameworks, and current implementation.
- Reuse existing capabilities where appropriate.
- Do not create duplicate projects, abstractions, utilities, or components without a clear need.
- Do not remove or weaken existing functionality or tests unless the requested change explicitly requires it.
- Identify conflicts between existing code and these standards before making broad changes.

# 1. Core Architecture Principles and Technology Standards
Use the following principles and technologies where appropriate:
- Latest stable, supported .NET and ASP.NET Core versions compatible with the solution's constraints.
- C# and modern language features where they improve clarity, safety, and maintainability.
- Dependency Injection (DI) and Inversion of Control (IoC).
- async / await for I/O-bound asynchronous operations and other operations where asynchronous execution is appropriate.
- SOLID principles.
- Clean separation of concerns and clear architectural boundaries.
- Domain-driven design concepts where they provide meaningful domain boundaries and language.
- Gang of Four design patterns only where they provide real value.
- Repository Pattern for appropriate persistence boundaries.
- Facade Pattern as the public business entry point for each module.
- Strategy Pattern where algorithms, business rules, or processing approaches vary.
- Factory Pattern where object or provider creation varies.
- Adapter Pattern for external providers and incompatible interfaces.
- Decorator Pattern where cross-cutting behavior such as logging, caching, or resilience is appropriate.
- Dependency Inversion.
- Interface Segregation.
- Strong typing and nullable reference types.
- Centralized exception handling.
- Structured logging.
- In-memory caching behind an abstraction, following the dedicated caching architecture in this document.
- OpenAPI / Swagger.
- Unit and integration testing.
Avoid unnecessary abstraction. Do not introduce a design pattern simply to claim that a pattern is being used. Prefer the simplest design that preserves the required boundaries, testability, extensibility, and maintainability.

# 2. API Design and Implementation Standards

## 2.1 General API Standards
- Design RESTful APIs according to established HTTP and REST conventions.
- GraphQL may be used where there is a clear requirement; do not introduce GraphQL merely as an additional technology.
- Use appropriate HTTP methods, status codes, error responses, and API versioning.
- Create comprehensive API documentation with clear endpoint specifications.
- Design efficient request, response, and data contracts.
- Implement authentication, authorization, and security controls.
- Maintain backward compatibility when changing published API contracts wherever practical.
- Use pagination, filtering, and sorting for collection endpoints where appropriate.
- Apply request-size limits, rate limiting, and abuse protection where appropriate.
- Do not expose internal domain entities, persistence models, or implementation details through public API contracts.

## 2.2 Controller and Endpoint Design

Controllers must be thin HTTP adapters. Their responsibilities are limited to:
- HTTP routing and request binding.
- Coordinating API-boundary request validation.
- Performing appropriate API input sanitization.
- Enforcing or invoking authentication and authorization mechanisms.
- Calling the appropriate public Business Facade.
- Mapping API-specific request/response contracts where required.
- Returning HTTP responses and appropriate status codes.
- Providing Swagger/OpenAPI documentation.

Controllers must NOT:
- Contain business rules or business calculations.
- Access repositories directly.
- Access DbContext directly.
- Access the cache directly.
- Call external integrations directly.
- Implement persistence or transaction logic.
- Orchestrate internal business services.
- Contain provider-specific integration logic.

All business operations must go through the module's public Facade in its Business project.

Each controller action and method must perform one coherent responsibility—nothing unrelated, nothing redundant. Keep methods small, focused, readable, and testable. Extract reusable or complex logic into the appropriate layer rather than allowing controller methods to grow.

## 2.3 API Versioning and Routing

- API routes must be versioned.
- Use a consistent route convention such as v1/api/[controller] or the repository's established versioned route convention.
- Apply the same versioning policy consistently across all controllers.
- Do not mix versioned and unversioned routes without an explicit architectural reason.
- Keep API versioning independent from internal business implementation versions.

Example:
[ApiController]
[Route("v1/api/[controller]")]
public sealed class CustomerController : ControllerBase
{
}

## 2.4 Request Validation and Sanitization

- Validate required parameters, route values, query parameters, request bodies, and supported formats at the API boundary.
- Sanitize untrusted input where sanitization is appropriate.
- Do not confuse sanitization with business validation.
- Business rules and domain invariants must also be enforced within the Business layer; API validation must never be the only protection for business correctness.
- Use dedicated validators for complex or reusable validation rules.
- Keep controller-specific validation helpers private.
- Prefer a validation framework such as FluentValidation when appropriate and consistent with the existing solution.
- Return consistent, consumer-readable validation errors.
- Never rely solely on client-side validation.

## 2.5 Swagger / OpenAPI Documentation

Every public API endpoint must be accurately and comprehensively documented.

Document, as applicable:
- Endpoint purpose and description.
- HTTP method and route.
- Route, query, and header parameters.
- Request DTO/schema and field descriptions.
- Required and optional fields.
- Success response DTO/schema.
- Relevant HTTP status codes.
- Validation error responses.
- Business error responses.
- Authentication and authorization requirements.
- Examples of requests and responses where useful.

Swagger documentation must match the actual implementation and must not advertise response types or status codes that the endpoint does not return.

## 2.6 API Response Standards

- Return consistent, consumer-friendly response structures.
- Include appropriate metadata where useful, such as correlation/trace identifiers or pagination metadata.
- Use the correct HTTP status code for success, validation failure, missing resources, conflicts, authentication failures, authorization failures, and unexpected errors.
- Use RFC 7807 / RFC 9457 Problem Details conventions, as supported by the selected ASP.NET Core version and project standards.
- Do not expose internal exception details, stack traces, database information, secrets, or implementation details.
- Do not create multiple competing error response formats without a documented reason.

# 3. Modular / Domain-Based Solution Architecture

## 3.1 Domain and Module Organization

Organize the solution by business domains or functional modules. Each module must maintain its own:
1. API project.
2. Business project.
3. Data project.
4. Integration project.
5. Shared project.

Each module owns its business capabilities, data access implementation, integrations, and module-specific shared contracts.

Do not combine unrelated domains into one large project for convenience. Avoid unnecessary dependencies between modules. A module must not access another module's internal business implementation or database directly.

##  3.2 Mandatory Project Structure
The following is the standard logical structure. Replace Module1, Module2, and Module3 with actual business domain names.

Solution
│
├── Modules
│   ├── Module1
│   │   ├── Module1.API
│   │   ├── Module1.Business
│   │   ├── Module1.Data
│   │   ├── Module1.Integration
│   │   └── Module1.Shared
│   │
│   ├── Module2
│   │   ├── Module2.API
│   │   ├── Module2.Business
│   │   ├── Module2.Data
│   │   ├── Module2.Integration
│   │   └── Module2.Shared
│   │
│   └── Module3
│       ├── Module3.API
│       ├── Module3.Business
│       ├── Module3.Data
│       ├── Module3.Integration
│       └── Module3.Shared
│
├── Common
│   ├── Common.Utilities
│   ├── Common.Logging
│   ├── Common.Security
│   ├── Common.Messaging
│   └── Common.Observability
│
└── Infrastructure
    ├── Infrastructure.Configuration
    └── Infrastructure.DependencyInjection

Only create a Common or Infrastructure project when it contains genuinely reusable cross-module capabilities. Do not create empty or speculative projects. Keep the logical separation even if the repository uses a slightly different physical folder arrangement.

## 3.3 Project Responsibilities

For illustration, Module1 may represent a Customer domain. Apply the same responsibilities to every module.

### 3.3.1 Module1.API

- Contains API controllers and HTTP-related functionality.
- Exposes versioned REST endpoints.
- Handles request binding, API-boundary validation coordination, sanitization, authentication/authorization integration, and HTTP response mapping.
- Uses the public Facade interfaces exposed by Module1.Business.
- Provides Swagger/OpenAPI documentation.
- Must not contain business logic.
- Must not access repositories, DbContext, cache implementations, or external integrations directly.
- Must not reference or expose internal Business implementation classes.

### 3.3.2 Module1.Business

- Contains business logic and domain-specific processing.
- Exposes business capabilities through public Facade interfaces and required public entry points.
- Actual business implementation classes and internal services must be internal unless there is a documented reason for a type to be public.
- Internal business services must not be directly accessible to other projects.
- Coordinates data access and integration through abstractions.
- Owns business orchestration and business-level validation.
- Must not depend on API controllers, HTTP context, or HTTP-specific implementations.
- Must not depend on concrete Data or Integration implementations.
- Must not expose repositories or internal services through its Facade.
- Must not put persistence implementation or external-provider implementation details into business rules.

### 3.3.3 Module1.Data

- Contains database-related implementations.
- Implements repository and persistence abstractions.
- Handles database operations, EF Core configurations, queries, mappings, and transactions.
- Owns persistence-specific entities and configurations unless a particular domain model is intentionally shared by an approved design.
- Must not contain business logic.
- Must not expose database entities outside appropriate boundaries.
- Must not depend on the API project.
- Must not be accessed directly by Controllers or other modules.

### 3.3.4 Module1.Integration

- Contains external-system and cross-module integration implementations.
- Supports REST APIs, messaging, eventing, queues, and third-party integrations as required.
- Implements canonical data transformations where appropriate.
- Keeps external-system-specific models, SDKs, and dependencies isolated from Business and API projects.
- Exposes integration capabilities through well-defined abstractions.
- Handles provider-specific communication, serialization, authentication, timeouts, retries, and other transport concerns.
- Must not contain core business rules.
- Must not depend on the API project.
- Must not expose provider-specific implementation details to consumers of the Business Facade.

### 3.3.5 Module1.Shared

Contains module-specific shared objects and contracts required across architectural boundaries, including as appropriate:
- Module DTOs that genuinely need to be shared across projects, including request, response, and transfer contracts.
- Business exceptions and domain-specific exception types that must be referenced across project boundaries.
- Shared enums and constants.
- Shared interfaces and contracts where appropriate.
- Canonical data contracts where appropriate.
- Common validation contracts and reusable module-specific primitives.
- Result types or other module-level contracts where they are genuinely shared.

Shared must not contain business implementation, persistence implementation, API controllers, provider SDK models, or unrelated utilities.

Do not create separate DTO, Exceptions, or Facade projects. The Facade interfaces and Facade implementation belong in Module1.Business. Module-specific shared DTOs, exceptions, enums, constants, and contracts belong in Module1.Shared when they need to be shared across project boundaries.

Layer-specific models must remain in their owning project. For example, an API-only request model belongs in API; an external provider request model belongs in Integration; and a database persistence entity belongs in Data. Do not move every model into Shared.

### 3.4 Business Facade Implementation Rules

The Module1.Business project must expose only the required public Facade interfaces and necessary public entry points. The Facade is the sole business entry point used by the API layer.

A recommended internal organization is:
Module1.Business
│
├── Facade
│   ├── IModule1Facade.cs
│   └── Module1Facade.cs
│
├── Services
│   ├── Module1Service.cs
│   └── Module1ValidationService.cs
│
├── Domain
│   ├── Module1Entity.cs
│   └── Module1Rules.cs
│
├── Interfaces
│   ├── IModule1Repository.cs
│   └── IModule1IntegrationService.cs
│
└── DependencyInjection
    └── BusinessServiceRegistration.cs

This is an internal organization example, not a requirement to create every folder or class. Add only what the module needs.

Mandatory rules:

- IModule1Facade must be public.
- Module1Facade may be public when required by DI registration or composition; otherwise keep its implementation internal where feasible. The API should depend on the public Facade interface, not on internal implementation types.
- Internal business services must be internal.
- Internal implementation classes must not be directly exposed to other projects.
- Only the Facade should provide the business entry point to the API layer.
- Use dependency injection to resolve internal implementations.
- Do not expose repositories, data access objects, integration clients, or internal business services through the Facade.
- Facade methods should represent meaningful business capabilities/use cases, not simply mirror every repository method.
- Keep the Facade focused on delegating/orchestrating business operations; do not turn it into a large class containing all business logic.

Example:
public interface ICustomerFacade
{
    Task<CustomerResponseDto> GetCustomerAsync(
        Guid customerId,
        CancellationToken cancellationToken);
}

public sealed class CustomerFacade : ICustomerFacade
{
    private readonly CustomerService _customerService;

    public CustomerFacade(CustomerService customerService)
    {
        _customerService = customerService;
    }

    public Task<CustomerResponseDto> GetCustomerAsync(
        Guid customerId,
        CancellationToken cancellationToken)
    {
        return _customerService.GetCustomerAsync(
            customerId,
            cancellationToken);
    }
}

internal sealed class CustomerService
{
    // Actual business implementation remains internal to the Business project.
}

Register internal implementations within the Business project using its dependency injection registration method. The API composition root should register the module's Business services through the supported registration extension without directly constructing internal services.
3.5 Project Dependency Rules
Maintain strict dependency boundaries.
- API references Business and the module Shared project only as needed for public contracts.
- Business references the module Shared project.
- Data references the module Shared project and the appropriate abstraction project(s).
- Integration references the module Shared project and the appropriate abstraction project(s).
- Business must not depend on concrete Data or Integration implementations.
- Data and Integration must not depend on API.
- No module may directly access another module's internal business implementation or database.
- Cross-module communication must happen through explicitly defined contracts and integration interfaces, or through published APIs/events when modules are separately deployed.
- Use dependency injection and composition to connect implementations.
- Ensure actual project references do not introduce circular dependencies.
- Do not add project references merely to make an implementation shortcut possible.
- Every method should do one thing—nothing less and nothing more.
- Configuration must not be hard-coded; place it in configuration/settings and validate it.
- Define reusable constants and user-facing messages centrally in an appropriate Shared or localization resource boundary rather than scattering hard-coded values.
- The initial application language may be English, but design user-facing messages and resources so that multiple languages can be supported in the future.

Important abstraction placement rule: Where repository or integration interfaces are required by Business, define abstractions at an appropriate boundary—commonly inside Business as ports owned by the consuming business layer, or in Shared only when the contract genuinely needs to be shared across projects. Let Data and Integration implement those abstractions. Choose the placement that preserves dependency inversion and avoids circular references. Do not force all interfaces into Shared.

A typical dependency direction is:
API ───────────────► Business ───────────────► Shared
                         ▲
                         │ implements Business-owned ports
Data ────────────────────┘

                         ▲
                         │ implements Business-owned ports
Integration ─────────────┘

The composition root (API host or dedicated composition project) wires Business abstractions to Data and Integration implementations. Business must not reference concrete Data/Integration projects.

For separately deployed microservices, do not use project references across service boundaries. Communicate through versioned APIs, messages, or events. The module project structure can be used within a service boundary; deployment boundaries must be explicit.

## 3.6 Module Communication and Integration Boundaries

- A module must not query another module's database or access its repositories.
- A module must not call another module's internal service class.
- Use an explicit contract, Facade, API, or integration boundary for module-to-module interaction.
- For in-process modular applications, expose only intentional public contracts and compose implementations through DI.
- For independently deployed services, use service APIs or asynchronous messaging/events.
- Avoid synchronous chains of service calls where they create tight runtime coupling or cascading failures.
- Use canonical contracts at integration boundaries where multiple systems or providers represent equivalent information differently.
- Transform provider-specific or external models into canonical/module-owned contracts inside the Integration layer.
- Keep Business unaware of the transport technology, provider SDK, wire format, or external system's internal schema.
- Support synchronous and asynchronous communication as required, including REST, messaging, queues, and events.
- Version integration contracts and plan for backward compatibility.

# 4. Solution and Project Naming Standards

The Visual Studio solution represents the product/application.

Use the naming convention:

Product.Module.Type

Where:
- Product = product/application name.
- Module = functional domain.
- Type = API, Business, Calculation, Integration, Data, Shared, Tests, and so on.
Example using the generic product name Ecommerce and the Customer module:
Ecommerce.sln

Projects:
Ecommerce.Customer.API
Ecommerce.Customer.Business
Ecommerce.Customer.Data
Ecommerce.Customer.Integration
Ecommerce.Customer.Shared

Ecommerce.Order.API
Ecommerce.Order.Business
Ecommerce.Order.Data
Ecommerce.Order.Integration
Ecommerce.Order.Shared

Ecommerce.Tests.Unit
Ecommerce.Tests.Integration

Ecommerce.Common.Utilities
Ecommerce.Common.Logging
Ecommerce.Common.Security
Ecommerce.Common.Messaging
Ecommerce.Common.Observability

Ecommerce.Infrastructure.Configuration
Ecommerce.Infrastructure.DependencyInjection

Additional projects such as Calculation, Worker, Contracts, or module-specific test projects may be introduced when justified by a distinct responsibility and approved architecture. Do not create separate Facade, DTO, or Exceptions projects.

Do not place unrelated functionality into one large project simply for convenience. Keep naming consistent across project names, namespaces, assemblies, folders, and test projects.

# 5. Database Architecture and Optimization

- Design normalized database schemas with appropriate relationships, keys, constraints, and indexes.
- Denormalize only when there is a measured or well-understood performance or read-model requirement.
- Optimize queries using query analysis, execution plans, appropriate indexing, and caching strategies.
- Avoid N+1 queries, unbounded result sets, unnecessary eager loading, and inefficient materialization.
- Use asynchronous database APIs for I/O-bound operations.
- Implement database migrations and version control for schema changes.
- Design for data integrity with validation, transactions, and consistency checks.
- Use transactions only where required and keep their scope appropriately short.
- Define transaction boundaries explicitly.
- Recommend database technologies based on use-case requirements, operational constraints, scalability, consistency, and cost.
- Use EF Core appropriately; do not assume every query must use EF Core when a more suitable data-access approach is justified.
- Keep EF Core configurations, mappings, and database-specific code in Data.
- Do not leak database entities or DbContext outside the Data boundary.
- Avoid storing secrets or sensitive values in plain text.
- Consider connection pooling, command timeouts, retry behavior, and database capacity.
- Measure before applying database optimizations; document important trade-offs.

# 6. Global Error Handling and Exception Standards

Prefer centralized ASP.NET Core exception handling middleware or IExceptionHandler rather than repetitive try/catch blocks inside every Controller action.

Business and lower layers should throw meaningful custom exceptions that represent the failure at the appropriate abstraction level.

Examples include:
- ValidationException
- BusinessRuleException
- ResourceNotFoundException
- CalculationException or another domain-operation-specific exception where relevant
- IntegrationException
- DataAccessException
- CacheException

Place module-specific exception types in the module's Shared project when they must be referenced across project boundaries. Keep implementation-only exceptions internal to the project that owns them.

Rules:
- Map exceptions centrally to appropriate HTTP status codes and Problem Details responses.
- Preserve the original exception as the inner exception when wrapping it.
- Do not swallow exceptions silently.
- Do not use exceptions for ordinary control flow.
- Log exceptions once at an appropriate boundary; avoid duplicate logging at every layer.
- Include correlation/trace identifiers in error responses where appropriate.
- Return safe, actionable error messages to API consumers.
- Never expose stack traces, database details, connection strings, internal service names, credentials, tokens, or implementation details to API consumers.
- Controllers must not contain repetitive exception-handling code.
- Do not catch an exception unless the code can meaningfully handle it, translate it, add useful context, or perform required cleanup.

# 7. API Logging

Use structured logging with consistent event names, fields, and appropriate log levels.

For every API request, capture appropriate operational information such as:
- UTC request timestamp.
- Local request timestamp when useful and unambiguous.
- Correlation ID / Trace ID.
- HTTP method.
- Endpoint/route template.
- Response status.
- Request duration.
- Client IP only where justified and privacy-compliant.
- User-Agent.
- Device/client information when reliably available and useful.
- Application version.
- Relevant, non-sensitive business context needed for diagnostics.
- Requested location or other domain input only when operationally necessary and privacy-safe.
- Latitude/longitude only at an appropriate privacy-safe precision and only when required.
- Requested timezone where relevant.
- Cache hit/miss where relevant.
- Calculation or processing duration where relevant.
- External integration duration where relevant.
- Authentication status where appropriate.

Do NOT log unnecessarily or in plain text:
- Passwords.
- Access tokens.
- Refresh tokens.
- API keys.
- Authentication headers.
- Cookies.
- Secrets.
- Full personal details or sensitive user-submitted data.
- Unnecessary precise location.
- Payment or other regulated information.

Implement structured redaction and data minimization. Logging must follow applicable privacy, security, and retention requirements.

Use correlation IDs / trace context across the complete request flow, for example:

API → Business → Calculation/Processing → Data → Integration

Do not trust arbitrary client-supplied correlation identifiers without validation. Use standard trace-context propagation where possible.

# 8. Integration Logging and Resilience

External integrations should log appropriate operational details, including:
- Correlation ID / Trace ID.
- Provider/system.
- Operation name.
- Request start time.
- Duration.
- HTTP status or messaging outcome.
- Success/failure.
- Retry count.
- Timeout/cancellation outcome where useful.

Do not log secrets or sensitive request/response data. If request/response bodies need to be logged for diagnostics, make logging configurable and implement field-level redaction.

Use resilient HTTP and messaging patterns, including as appropriate:
- Explicit timeouts.
- Cancellation support.
- Retry for transient failures only.
- Circuit breaker where appropriate.
- Bulkhead or concurrency limits where appropriate.
- Fallback or graceful degradation where appropriate.
- Idempotency for operations that may be retried.
- Dead-letter handling and poison-message handling for messaging.

Do not blindly retry non-transient failures. Respect provider rate limits and Retry-After guidance where applicable. Use bounded retries with backoff and jitter. Ensure retries do not create duplicate side effects.

Keep resilience policies appropriate to the operation and provider. Do not retry non-idempotent operations unless an idempotency mechanism makes retry safe.

# 9. Caching Architecture

Use an in-memory cache (IMemoryCache) per API/application instance where appropriate.

All cache access must be behind an abstraction. Business and domain code must not depend directly on IMemoryCache or a particular cache provider.

The cache architecture must follow these project-specific requirements:
- Use the dedicated bounded L1 cache and optional PostgreSQL L2 cache.
- Do not reintroduce a bespoke memory store or Redis as a replacement for this architecture.
- Domain code owns normalized, versioned cache keys and cache validity rules.
- Calculation engines and pure domain algorithms must remain free of cache I/O.
- L1 is local to each process and is lost on restart.
- PostgreSQL L2 retains valid results according to the defined persistence/expiration policy.
- Native single-flight/coalescing is per process, not distributed.
- Cache only safe, derivable data.
- Never cache tokens, login responses, user profiles, authorization decisions, or other security-sensitive/user-specific data unless a separately reviewed design explicitly permits it.
- Follow docs/hybrid-cache.md for policies, bounded tag metadata, failure behavior, tests, and database-first rollout. Treat that document as the detailed implementation reference when it exists in the repository; inspect it before changing cache behavior.
- Do not invent a different cache architecture or introduce Redis without an explicit architecture change request and approval.

# 9.1 Cache Abstraction
- Define an appropriate cache abstraction, such as ICacheStore, so consumers are not coupled to the cache implementation.
- Keep cache provider implementations in the appropriate infrastructure/data boundary.
- Support asynchronous cache operations where required.
- Define expiration, invalidation, key normalization, and failure behavior explicitly.
- Keep cache keys deterministic, normalized, collision-resistant, and versioned.
- Avoid unbounded cache growth; enforce memory limits, entry limits, or an appropriate eviction policy.
- Do not store mutable shared objects that can be modified by callers.
- Ensure cached values are safe to reuse across requests and tenants.
- Treat cache failure as a dependency failure with a defined fallback policy; do not let a cache outage cause unsafe behavior.
# 9.2 Cache-Aside Pattern

Use cache-aside behavior.

Typical flow:
API Request
    ↓
Business Facade
    ↓
Business operation requests cache through abstraction
    ↓
Check cache
    ├── HIT  → Return cached result
    │
    └── MISS → Execute business/calculation operation
                 ↓
               Optionally persist appropriate calculated data
                 ↓
               Store cache entry
                 ↓
               Return result
Prevent cache stampedes when many requests simultaneously request the same uncached key.

ICacheStore.GetOrCreateAsync should coalesce concurrent misses for the same key within an application instance, so each instance performs the underlying calculation or retrieval once per key during the coalescing window. Multiple instances do not coordinate through native in-process single-flight. Do not describe this as distributed locking.

L2/database coordination must follow the approved hybrid-cache design. Do not add a distributed lock unless the architecture is explicitly reviewed and approved.

# 10. Shared Project Standards

Each module may have a project named:

Product.Module.Shared

Shared contains functionality genuinely required across multiple projects within that module.

Examples:
- Cross-layer DTOs only when genuinely required by multiple project boundaries.
- Module-level constants.
- Custom exceptions that cross project boundaries.
- Common interfaces/contracts that are intentionally shared.
- Result primitives.
- Shared validation primitives.
- Canonical contracts for the module where appropriate.

Rules:
- Do NOT turn Shared into a dumping ground.
- Before adding something to Shared, determine whether it belongs to a specific domain or layer.
- Keep business implementation out of Shared.
- Keep persistence entities, API-only models, and provider-specific models in their owning projects unless an explicit contract requires otherwise.
- Avoid placing generic utilities in every module's Shared project; use Common only when genuinely cross-module.
- Do not create separate DTO, Exceptions, or Facade projects.

# 11. Common Project Standards

Common projects contain cross-cutting capabilities genuinely reusable across multiple modules.

Potential examples:
- Extensions.
- Date/time utilities.
- Timezone utilities.
- Correlation and trace-context utilities.
- Common HTTP client functionality.
- Common serialization functionality.
- Shared logging abstractions or configuration.
- Messaging abstractions/utilities.
- Security primitives that are truly cross-cutting.
- Observability utilities.

Rules:
- Do NOT turn Common into a dumping ground.
- Before adding something to Common, determine whether it belongs to a specific domain/module or layer.
- Do not create a Common abstraction for a single use case without a clear reuse or architectural need.
- Avoid circular dependencies between Common, Infrastructure, and module projects.
- Common must not depend on individual module implementations.

# 12. Static Class Standards

Use static classes only where appropriate.

Good candidates include:
- Constants.
- Pure utility functions.
- Extension methods.
- Deterministic helpers without dependencies or mutable state.

Do NOT make business services, repositories, integrations, calculation engines, or cache services static merely for convenience.

Prefer Dependency Injection for services requiring dependencies, configuration, logging, testing, lifecycle management, or replacement.

Avoid mutable static state. Static classes must not become a hidden service locator or global state container.

# 13. DTO and Model Placement Rules
Layer-specific DTOs and models remain inside their owning layer.

Examples:
- API-only request/response models → API project.
- Integration request/response models → Integration project/provider folder.
- Persistence models/entities/configurations → Data project.
- Calculation/processing-specific models → the relevant calculation or processing component within the module, or a dedicated project only when justified.
- Module contracts genuinely shared across project boundaries → module Shared project.

Additional rules:
- Do not reuse database entities as API response models.
- Do not expose provider-specific external models to Business or API.
- Do not place every DTO in Shared by default.
- Avoid unnecessary mapping layers, but map explicitly when crossing boundaries with different responsibilities or lifecycles.
- Keep public contracts stable and version them when necessary.
- Do not create a separate DTO project or Exceptions project.

# 14. Authentication and Authorization

Design authentication using established standards and frameworks, such as:
- OAuth 2.0.
- OpenID Connect.
- JWT access tokens where appropriate.
Rules:
- Keep authentication provider details configurable and isolated from business logic.
- Support future identity providers as required, such as Google, Apple, Microsoft, email/password, or passwordless authentication.
- Use policy-based and claims-based authorization where appropriate.
- Enforce authorization at the API boundary and, where required, within business operations to protect non-HTTP entry points.
- Follow least-privilege principles.
- Validate token issuer, audience, signature, expiry, and other required claims.
- Use established identity libraries and providers.
- Never implement custom cryptography or token algorithms when established, reviewed frameworks exist.
- Never log tokens, credentials, or authentication headers.
- Do not treat authentication as authorization; a successfully authenticated user is not automatically permitted to perform every operation.

# 15. Location and Contextual Input Architecture

Where a module requires a user's location or other contextual input, support appropriate acquisition methods, such as:

1. Browser/device geolocation with explicit user permission.
2. Manual city/location search.
3. Saved user location.
4. Approximate IP-derived location as an optional fallback.

Rules:
- Never assume IP geolocation is exact.
- Clearly distinguish user-selected, device-provided, saved, and IP-derived values.
- Obtain permission where required.
- Minimize storage and logging of precise location data.
- Convert the selected location into the canonical values required by the relevant domain operation, such as latitude, longitude, and IANA timezone when applicable.
- Validate coordinates, timezone identifiers, and other contextual inputs.
- Use the selected and validated context consistently throughout the relevant business operation.
- Do not hard-code location or timezone assumptions.
This section is conditional: implement location functionality only for modules that require it. Do not introduce location-specific dependencies into unrelated modules.

# 16. System Reliability and Scalability
- Implement comprehensive error handling with appropriate logging and monitoring.
- Design fault-tolerant systems with graceful degradation and circuit breakers where appropriate.
- Create scalable architectures that can handle increasing load and data volume.
- Implement appropriate caching strategies at application, database, or CDN layers when justified by the use case and consistent with the dedicated cache architecture.
- Design for horizontal and vertical scaling.
- Identify critical dependencies and define behavior when they become unavailable.
- Use timeouts and cancellation to prevent resource exhaustion.
- Avoid unbounded queues, unbounded concurrency, and unbounded in-memory collections.
- Design background processing with retry, idempotency, and dead-letter handling where appropriate.
- Define recovery and data-consistency expectations for partial failures.
- Prefer stateless API instances.
- Use health checks and readiness checks that reflect the application's ability to serve traffic.
- Support safe rolling deployments and backward-compatible schema/API changes.

# 17. Monitoring and Observability

Implement comprehensive observability using:

- Structured logs.
- Metrics.
- Distributed tracing.
- Health checks.
- Error tracking.
- Alerts and dashboards.
Support OpenTelemetry for vendor-neutral instrumentation where appropriate.

If deployed to Azure, consider:
- Application Insights.
- Azure Monitor.
- Log Analytics.

Important metrics include, as relevant:
- Request count.
- Error rate.
- Response latency (including percentiles).
- Business operation or calculation latency.
- Cache hit ratio.
- Cache miss ratio.
- Database latency.
- External provider latency.
- Integration failures.
- Retry and timeout counts.
- Queue depth and message processing latency.
- Background job success/failure.
- Resource utilization and saturation.

Propagate correlation and trace identifiers through all layers and external calls where supported. Use consistent metric names and avoid high-cardinality metric labels such as raw user IDs, request IDs, or arbitrary input values.

Health endpoints should distinguish liveness from readiness. Do not make liveness depend on every downstream service. Readiness should reflect whether the instance can safely accept the work it is expected to handle.

# 18. Security Standards

Follow current OWASP recommendations and secure development practices.

Implement, as appropriate:
- Input validation.
- Output encoding.
- Authentication.
- Authorization.
- Rate limiting.
- HTTPS.
- Secure HTTP headers.
- Secret management.
- Request-size limits.
- Dependency vulnerability scanning.
- Secure logging and redaction.
- PII minimization.
- API abuse protection.
- Protection against injection, cross-site scripting, CSRF, SSRF, insecure deserialization, and other relevant threats.
- Secure configuration and least-privilege access.
- Encryption in transit and at rest where required.

Use managed identities and secret-management platforms such as Azure Key Vault when deployed to Azure.

Never commit secrets, credentials, certificates, private keys, connection strings, or environment-specific sensitive values to source control.

Do not hard-code credentials or environment-specific URLs. Use secure configuration providers and validate required settings at startup.

Security must be considered at API, Business, Data, Integration, messaging, and infrastructure boundaries—not only in Controllers.

# 19. Scalability and High Availability

Design APIs to be stateless.

Do not depend on server-local memory for authoritative shared state. The approved in-memory cache is allowed because it stores only safe, recalculable data and follows the cache rules in Section 9.

A typical deployment topology may look like:
Clients
   ↓
Load Balancer / API Gateway
   ↓
Multiple API/Application Instances
   ├── Business
   ├── Local bounded L1 cache
   ├── Data access
   └── Integration clients
          ↓
      Database / Approved L2 cache / External providers

Design for:
- Multiple API instances, each with its own in-memory L1 cache.
- Transient failures.
- Graceful degradation.
- Health checks.
- Readiness checks.
- Dependency failures.
- Rolling deployments.
- Safe startup and shutdown.
- Horizontal scaling without reliance on process-local authoritative state.
- Backward-compatible deployment and database migration strategies.

Do not assume in-process synchronization or single-flight coordinates across application instances.

# 20. Testing Architecture

Create separate testing projects where appropriate. Tests must validate both behavior and architectural boundaries.

# 20.1 Unit Tests

Use sociable testing techniques wherever possible: test meaningful collaborations between real objects while replacing only true external dependencies or boundaries.

Test, as appropriate:
- Business rules.
- Business services.
- Facade behavior.
- Strategies and factories.
- Cache-key generation and normalization.
- Timezone/date conversions where relevant.
- Validation.
- Exception behavior.
- Mapping logic.
- Resilience policy behavior.
- Edge cases and boundary conditions.
Tests must be deterministic, isolated from production services, and independent of execution order.

# 20.2 Integration Tests
Test end-to-end interactions across relevant application boundaries, such as:
- API → Business → Cache/Data.
- Business → Data implementations.
- Business → Integration adapters.
- Serialization and contract compatibility.
- Authentication and authorization behavior.
- Database migrations and transaction behavior where appropriate.
External providers should be replaceable or mockable through interfaces. Use controlled test environments, fakes, or test containers where appropriate. Do not make normal automated tests depend on unstable live third-party services.

# 20.3 Additional Test Expectations
- Add regression tests for defects.
- Test failure paths, timeouts, cancellation, retries, and partial failures.
- Test authorization and validation failures, not only successful requests.
- Verify that API responses do not leak internal exception details.
- Verify cache expiration, invalidation, boundedness, and per-instance single-flight behavior.
- Add architecture/dependency tests where they help enforce project boundaries.
- Ensure tests are meaningful; do not write tests that merely duplicate implementation details.
- Do not weaken or delete tests simply to make a build pass.

# 21. Coding Standards and Best Practices

Apply these rules to EVERY solution, project, layer, module, namespace, class, interface, and method.
- Classes must have one primary responsibility.
- Methods must perform one coherent operation.
- Prefer small, composable components.
- Use descriptive names that communicate intent.
- Keep public APIs small and intentional.
- Prefer immutability for value objects and data contracts where practical.
- Use nullable reference types and handle nullability intentionally.
- Use appropriate access modifiers; default to the least visibility needed.
- Dispose resources correctly and use IDisposable / IAsyncDisposable appropriately.
- Use CancellationToken throughout asynchronous call chains where appropriate.
- Avoid blocking asynchronous code with .Result, .Wait(), or equivalent sync-over-async patterns.
- Use ConfigureAwait according to modern .NET application requirements rather than mechanically adding it everywhere.
- Avoid premature optimization; measure performance and profile before making optimization decisions.
- Use analyzers, formatting, and compiler warnings to maintain quality.
Avoid:
- God classes.
- Giant services.
- Giant Controllers.
- Duplicate code.
- Hidden dependencies.
- Service locator.
- Unnecessary static state.
- Circular dependencies.
- Business logic inside Controllers.
- Business logic inside repositories.
- Database entities leaking into API contracts.
- Direct external-provider calls from Controllers or core business rules.
- Hard-coded configuration, credentials, URLs, constants, or user-facing messages.
- Swallowed exceptions.
- Unbounded retries, concurrency, or memory growth.
- Unnecessary project references and abstractions.

# 22. Dependency Injection Standards
- Register all significant services through DI.
- Organize registrations through layer-specific extension methods.
- Keep Program.cs clean and focused on application composition.
- Register module Business Facades and their internal services through the Business project's registration extension.
- Register Data and Integration implementations in their respective registration extensions.
- Wire Business-owned abstractions to Data/Integration implementations at the composition root.
- Do not make Business reference concrete Data or Integration projects merely to register them.
- Choose service lifetimes deliberately: Transient, Scoped, or Singleton according to the service's state, dependencies, and thread-safety.
- Do not inject scoped services into singletons.
- Avoid capturing scoped services in long-lived objects.
- Ensure singleton services are thread-safe and do not retain request-specific state.
- Avoid building nested service providers or resolving dependencies manually from IServiceProvider except in justified framework/composition scenarios.
- Validate DI registrations at startup where supported and useful.

# 23. Configuration and Secrets Management
- Use the strongly typed Options Pattern for application and integration configuration.
- Validate important configuration during application startup.
- Support environment-specific configuration.
- Never hard-code credentials or environment-specific URLs.
- Keep configuration separate from business logic.
- Use secure secret providers; use Azure Key Vault and managed identities when deployed to Azure.
- Do not store secrets in ordinary appsettings files committed to source control.
- Define clear configuration ownership and names for each module/provider.
- Fail fast with a clear startup/configuration error when required settings are missing or invalid.
- Do not log secret values when reporting configuration errors.
- Make timeouts, retry limits, cache policies, and provider endpoints configurable where appropriate, with safe defaults and validation.

# 24. Documentation Standards
- Every public contract should have meaningful XML documentation where useful.
- Public APIs must have Swagger/OpenAPI documentation.
- Swagger should describe endpoints, parameters, request schemas, response types, error responses, and authentication requirements.
- Keep README and setup instructions accurate.
- Document configuration keys and required environment variables without exposing secret values.
- Document local development, build, test, and deployment steps.
- Record architecture decisions that materially affect the system as Architecture Decision Records (ADRs).
- Document important integration contracts, failure behavior, retry policies, and operational dependencies.
- Keep documentation synchronized with implementation; update it when behavior or contracts change.

# 25. Implementation Workflow for the Visual Studio / GitHub Copilot AI Agent

Do NOT attempt to generate the entire production application in one uncontrolled step.

## 25.1 Before Writing Implementation Code

First produce and review:

1. Requirements analysis, including assumptions and open questions.
2. Proposed solution architecture.
3. Visual Studio solution/project structure.
4. Project dependency diagram.
5. Layer and module responsibilities.
6. Business Facade and internal business implementation design.
7. Calculation/processing architecture where relevant to the domain.
8. Caching architecture.
9. Database design.
10. API contracts.
11. Authentication and authorization design.
12. Observability design.
13. Security architecture.
14. Testing strategy.
15. NuGet package recommendations with reasons.
16. Implementation phases and dependencies between phases.
17. Risks, trade-offs, and migration considerations.
Wait for architecture approval before generating the full implementation.

## 25.2 Incremental Implementation

Once architecture is approved, implement incrementally by module and capability.

For every implementation phase:
1. State which project(s) are being modified.
2. Explain the responsibility being implemented.
3. Confirm project references and architectural boundaries.
4. Create interfaces/contracts first where appropriate.
5. Implement the functionality in the correct layer.
6. Register dependencies through the appropriate DI extension.
7. Add and validate configuration.
8. Add API-boundary and business validation as appropriate.
9. Add structured logging and correlation propagation.
10. Add centralized exception handling or appropriate exception translation.
11. Add security controls.
12. Add unit tests.
13. Add integration tests where appropriate.
14. Update API/technical documentation and ADRs where needed.
15. Build the solution and ensure it compiles.
16. Run relevant tests and ensure existing tests continue to pass.
17. Review the final changes for dependency violations, security concerns, duplication, and maintainability.
18. Summarize changed files, behavior, tests executed, and any outstanding issues.
Do not weaken architectural boundaries to resolve compilation issues. Fix the dependency design or composition root instead.
25.3 Change Management in Existing Codebases
Before modifying existing code:
- Locate the owning module and project.
- Identify all callers and dependencies affected by the change.
- Preserve existing public contracts unless a breaking change is explicitly approved.
- Prefer small, reviewable changes.
- Avoid unrelated refactoring in a feature task.
- Update tests and documentation with the implementation.
- Do not create a parallel implementation when an existing service or abstraction can be extended safely.
- Explain any necessary breaking change and provide a migration approach.

# 26. Architecture Enforcement
These rules are mandatory throughout development.

If a requested implementation conflicts with this architecture:
1. Identify the conflict.
2. Explain why it violates the architecture.
3. Recommend an architecture-compliant solution.
4. Implement the compliant solution after the required approval.
Do not silently bypass architectural boundaries.

The mandatory business execution flow is:
Controller / API Endpoint
        ↓
Public Module Business Facade
        ↓
Internal Business Services / Domain Logic
        ↓
Calculation / Processing / Data / Integration abstractions
        ↓
Concrete Data and Integration implementations
The Controller must never bypass the Facade to directly access internal business services, calculation/processing components, Data, cache implementations, or external integrations.

The Business layer must remain independently testable and isolated from API, UI, database, and external-provider implementation details.

The architecture must support the first module and future modules equally well. Build reusable foundations where justified, but do not introduce speculative abstractions or domain-specific assumptions into unrelated modules.

Final rule: preserve the separation of responsibilities, dependency inversion, module boundaries, security, testability, and operational reliability in every implementation.