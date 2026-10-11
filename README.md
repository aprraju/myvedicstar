# MyVedicStar APIs

This repository contains MyVedicDay and MyVedicStar Authorize. Each API has its own Visual Studio solution, standalone host and tests. Production publishes `MyVedicStar.Host`, which loads both API controller assemblies into one ASP.NET Core host and one container image.

## Develop independently

| API | Solution | Standalone project | HTTP port |
| --- | --- | --- | --- |
| MyVedicDay | `MyVedicDay.sln` | `src/MyVedicDay.Api/MyVedicDay.Api.csproj` | 5275 |
| MyVedicStar | `MyVedicStar.slnx` | `src/MyVedicStar.Authorize.Api/MyVedicStar.Authorize.Api.csproj` | 5014 |
| Shared deployment | `MyVedicStar.Deployment.slnx` | `src/MyVedicStar.Host/MyVedicStar.Host.csproj` | 5289 |

Use the .NET SDK selected by `global.json`. Set `GITHUB_TOKEN` to a token with private package read access before restoring. Never commit the token.

```powershell
dotnet run --project src/MyVedicDay.Api --launch-profile http
dotnet run --project src/MyVedicStar.Authorize.Api --launch-profile http
# Run the combined development host separately:
dotnet run --project src/MyVedicStar.Host --launch-profile shared
```

The standalone solutions have no references to the other API's business modules. The shared deployment solution composes both APIs. Existing controller routes and successful response contracts remain in their original projects, including `POST /api/v1/auth/userinfo` and the Day `/api/v1/...` routes. The combined host exposes one `/openapi/v1.json` document and `/swagger` UI describing both APIs.

Both APIs use the same configured token audience and scope (`access_as_user`), the normalized `mvs_user_id` rate-limit identity, and the common Problem Details metadata policy in `src/SharedApi`. In the combined host, protection, authentication, authorization, JSON serialization and middleware are registered once. Star domain exceptions have an adapter so its dependency failures use the same safe error format as Day.

## Validate independently

```powershell
dotnet test MyVedicDay.sln --configuration Release
dotnet test MyVedicStar.slnx --configuration Release
dotnet test tests/MyVedicStar.Host.Tests.Integration --configuration Release
./tests/deployment/DeploymentContract.Tests.ps1
```

For PostgreSQL tests, set `MYVEDICSTAR_TEST_POSTGRES` to an admin connection string for a disposable test server. The fixture creates and drops a unique test database. Those eight tests skip when no connection string is provided; Star CI supplies a PostgreSQL service so they execute there. Day CI supplies Redis. On Windows accounts without Event Log write permission, use `$env:Logging__EventLog__LogLevel__Default = 'None'` for test runs.

## Shared deployment

- `.github/workflows/myvedicday-ci.yml` validates Day independently.
- `.github/workflows/authorize-api.yml` validates Star independently.
- `.github/workflows/shared-deploy.yml` validates both solutions, runs shared-host and deployment regression tests, verifies combined publishing, then builds and deploys one image on `main`.

Changes to either API on `main` trigger the combined pipeline. Both independent CI workflows are reusable and also run on relevant pull requests, non-main branch pushes, or manual invocation. Failing tests or combined validation prevent deployment; existing advisory security scans remain advisory.

`deploy/scripts/build-image.ps1` publishes `src/MyVedicStar.Host` and sends only its publish output to ACR. The image repository is `myvedicstar-api`, tagged with the source commit. The runtime entry point is `MyVedicStar.Host.dll`. API settings are namespaced under `apis/day` and `apis/star` to avoid publish collisions. Development settings are excluded from the image.

The production target is the existing VedicStar Container App, `ca-myvedicstar-api-prod`, in `rg-myvedicstar-prod-app-eus2`. Set the GitHub `prod` environment variable `CONTAINER_APP_NAME` to that name. Registry, Azure identity and resource group variables are described in [deployment documentation](deploy/README.md). The release preserves existing CPU, memory, ingress, networking, scale rules and unrelated settings; readiness includes Day's cache and Star's database when persistence is enabled. Release automation requires Single revision mode, which the existing target uses.

Run `./tests/deployment/PublishedHost.Smoke.Tests.ps1 -PublishDirectory <publish-output>` to verify the actual published host locally. This checks the image payload, readiness, both route sets and anonymous access rejection. It does not replace a Linux container smoke test.

Before retiring the old Day Container App, route its existing consumer domain to the VedicStar ingress and configure the corresponding certificate/custom domain. Consumers using the old app's generated hostname need a forwarding arrangement or a domain migration. Moving code alone cannot preserve an old hostname. No live Azure deployment or DNS change is performed by this source migration.

The original Day domain documentation is retained under [docs/day](docs/day/README.md).