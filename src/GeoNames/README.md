# GeoNames location API

The `GeoNames` .NET 10 library is registered by `MyVedicDayApiModule` in both the standalone API and the combined `MyVedicStar.Host`. It exposes anonymous, `public-read` rate-limited routes under `/api/v1/locations`: `search`, `postal`, `reverse`, and `timezone`.

Enable it on the server with `GeoNames__Enabled=true` and `GeoNames__Username=<dedicated-account>`. Enable web services on that GeoNames account. Missing, invalid, or demo usernames fail startup when enabled. Default configuration is disabled; lookup requests then return a generic 503. No username belongs in frontend configuration.

Optional settings: `GeoNames__TimeoutSeconds` (2â€“30, default 10), `GeoNames__SearchCacheHours` (1â€“48, default 24), `GeoNames__CoordinateCacheDays` (1â€“30, default 7). The HTTPS adapter bounds its cache and concurrency, disables redirects and HTTP request logging, preserves coordinates, and propagates quota cooldowns as 429 with Retry-After.

Expose the four routes through the existing gateway, then enable the frontend's `locationSearch.enabled`. The existing `/locations/resolve` workflow remains available. No deployment or live provider request is performed by the tests.

Run `dotnet test tests/GeoNames.Tests/GeoNames.Tests.csproj` for mocked provider tests, and `dotnet test tests/MyVedicDay.Api.Tests.Integration/MyVedicDay.Api.Tests.Integration.csproj --filter FullyQualifiedName~GeoNamesApiTests` for HTTP contract and validation tests.