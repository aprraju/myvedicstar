# MyVedicStar API

`src/MyVedicStar.API/MyVedicStar.API.csproj` replaces the authorization API and shared host as the production entry point. It owns authorization and GeoNames controllers. `MyVedicDay.Api` remains a separate referenced project with its own controllers and standalone entry point. Business, calculation, data, caching and provider libraries retain their separate responsibilities.

Existing routes, request parameters, response bodies and access requirements are preserved, including `POST /api/v1/auth/userinfo`, Panchang and Muhurta endpoints, and anonymous GeoNames routes under `/api/v1/locations`. Authentication, protection, serialization and middleware are registered once. One `/openapi/v1.json` document and `/swagger` UI describe every endpoint.

Use the SDK selected by `global.json`. Private packages require `GITHUB_TOKEN` with package read access; see `nuget.config`.

```powershell
dotnet run --project src/MyVedicStar.API --launch-profile shared
dotnet run --project src/MyVedicDay.Api --launch-profile http
dotnet test MyVedicDay.sln --configuration Release
dotnet test MyVedicStar.slnx --configuration Release
dotnet test tests/MyVedicStar.Host.Tests.Integration --configuration Release
./tests/deployment/DeploymentContract.Tests.ps1
```

The `shared` development profile uses port 5289. `MyVedicStar.slnx` contains the consolidated API and authorization tests; `MyVedicDay.sln` retains the standalone Day API and its tests. `MyVedicStar.Deployment.slnx` contains the release projects. Existing test project names are retained. Set `MYVEDICSTAR_TEST_POSTGRES` to a disposable PostgreSQL server connection string to run database tests. On Windows, set `Logging__EventLog__LogLevel__Default` to `None` if the account cannot write Event Log.

Published defaults are namespaced under `apis/day` and `apis/star` to avoid file collisions. Day environment overrides retain the existing combined-host configuration precedence; environment variables and command-line options override the defaults. Development settings are excluded from publishing.

GeoNames is configured in `src/MyVedicStar.API/appsettings.json`, with `Enabled: true` and username `aprraju`. These GeoNames defaults override Day's compatibility defaults in the combined API. Set `GeoNames__Enabled` and `GeoNames__Username` to override them at deployment; the account must have GeoNames web service access enabled.

GeoNames controller source lives in `MyVedicStar.API`. The GeoNames project links that source to keep standalone Day intact. When the combined API starts, it excludes the compatibility controller from discovery so each GeoNames route is registered once.

The GitHub deployment workflow validates both domain suites, API integration tests, deployment preservation and the published application before releasing `myvedicstar-api:<commit SHA>`. `deploy/scripts/build-image.ps1` publishes the new project; the Docker entry point is `MyVedicStar.API.dll`.

The existing production Container App, image repository, ports, health routes, secrets and configuration keys are retained. Deployment preserves existing networking, resources, scaling and unrelated settings. See [deployment instructions](deploy/README.md) and [cache configuration](docs/hybrid-cache.md).

Run `./tests/deployment/PublishedHost.Smoke.Tests.ps1 -PublishDirectory <publish-output>` to validate the published payload and routes. This source change does not deploy to Azure or change DNS. Existing consumer hostnames must continue to resolve to the deployed application.

Domain documentation remains under [docs/day](docs/day/README.md).
