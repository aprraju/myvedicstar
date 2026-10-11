> `MyVedicStar.API` owns authorization and GeoNames endpoints and hosts the unchanged, referenced `MyVedicDay.Api`. Both APIs are released together as `myvedicstar-api:<commit SHA>`, with `MyVedicStar.API.dll` as the container entry point.
> Set `CONTAINER_APP_NAME=ca-myvedicstar-api-prod` in the GitHub `prod` environment.
> `/health/ready` covers the cache and, when enabled, the authorization database. The release requires Single revision mode.
> To roll back a combined release, run `deploy-app.ps1 -ImageTag <previous-combined-tag>` with the same configured registry and target.
> Older standalone images use the previous image repository and are not combined releases.

# MyVedicStar: release to Azure Container Apps

**Terraform owns the infrastructure**: the Container App, its identity, the registry, Key Vault,
PostgreSQL, network and monitoring. **This repository owns the release**. GitHub Actions builds the image and
updates only the existing Container App's image, application settings, Key Vault references and health
probes. The same image deploys without being rebuilt, and nothing environment-specific is baked into it.

```
deploy/
  docker/Dockerfile            Runtime image; its build context is the `dotnet publish` output
  environments/prod.json       Application-owned, non-secret settings for prod
  scripts/build-image.ps1      dotnet publish, then build the image in ACR (az acr build, no local Docker)
  scripts/deploy-app.ps1       Release a tag to the existing Container App and wait until it is healthy
  scripts/set-secrets.ps1      Store secrets in Key Vault (interactive; never runs in CI)
.github/workflows/
  authorize-api.yml            Independent Star code scan, build and tests (including PostgreSQL)
  myvedicday-ci.yml            Independent Day code scan, build and tests (including PostgreSQL cache validation)
  shared-deploy.yml            Validate both APIs and the combined host; build one combined image and deploy
                               it to the existing VedicStar Container App on main
.github/scripts/
  code-scan.ps1                Advisory checks run by the code-scan job (never fails)
```

## Configuration

| Source | Holds | Owner |
|---|---|---|
| `appsettings.json` (in the image) | Defaults that are safe in every environment | app code |
| Container App environment variables set by Terraform | Infrastructure endpoints (App Insights, Key Vault URI, ...) | Terraform |
| `deploy/environments/prod.json` → Container App environment variables | Application settings (auth audience, feature flags, ...) | this repo |
| Key Vault → Container App secret reference | Secrets (none at present; listed under `secrets` in `prod.json`) | a person, via `set-secrets.ps1` or the portal |

`deploy-app.ps1` **merges** its settings into the container. A Terraform variable is kept unless
`prod.json` defines one with the same name. When an application setting is retired, list it under
`removedSettings` so the next release deletes it from the container. Probe timings, rollout timeout and smoke-test
retries are also in `prod.json`. Probe paths are read from `HealthChecks` in `appsettings.json`, so the app
and its probes always agree. The Container App reads secrets through its managed identity
`id-myvedicstar-api-prod`, which already has `Key Vault Secrets User`. No secret value passes through
GitHub, the scripts or source control.

### GitHub repository variables

| Variable | Used for |
|---|---|
| `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID` | OIDC sign-in (`azure/login`) |
| `REGISTRY_NAME` | Image build and pull |
| `RESOURCE_GROUP_NAME`, `CONTAINER_APP_NAME` | The Container App to update |
| `REGISTRY_RESOURCE_GROUP` | Not used yet (the scripts look the registry up by name) |

Jobs that touch Azure run in the GitHub environment **`prod`**. Add required reviewers there to gate releases.

## One-time Azure setup

Configure the GitHub deployment identity's federated credential and scoped Azure roles before enabling releases.
The following commands document the intended existing production resources; verify them against your subscription
and use the `prod` GitHub environment. This source migration does not change role assignments.

```bash
SUB=e77bb964-ac4f-44f5-89eb-ba00a5d889d1
SP=8e41e8a4-01d7-4b1c-8bd2-436e0ec19453
APP_RG=/subscriptions/$SUB/resourceGroups/rg-myvedicstar-prod-app-eus2/providers
DATA_RG=/subscriptions/$SUB/resourceGroups/rg-myvedicstar-prod-data-eus2/providers

# 1. Trust GitHub Actions jobs that run in the "prod" environment of aprraju/myvedicstar.
#    This repository's OIDC tokens use immutable owner/repo IDs in the subject (owner@id/repo@id), so the
#    subject must match that form exactly. The exact value appears in AADSTS700213 errors if it ever changes.
az ad app federated-credential create --id 6a0e280e-1397-42c4-a1e0-cda080f3d3cb --parameters '{
  "name": "github-myvedicstar-prod",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:aprraju@867910/myvedicstar@1396964516:environment:prod",
  "audiences": ["api://AzureADTokenExchange"]
}'

# 2. Least-privilege roles for the pipeline
az role assignment create --assignee-object-id $SP --assignee-principal-type ServicePrincipal \
  --role Contributor --scope $APP_RG/Microsoft.ContainerRegistry/registries/crmyvedicstarprodxw4t     # az acr build
az role assignment create --assignee-object-id $SP --assignee-principal-type ServicePrincipal \
  --role Contributor --scope $APP_RG/Microsoft.App/containerApps/ca-myvedicstar-api-prod             # release
az role assignment create --assignee-object-id $SP --assignee-principal-type ServicePrincipal \
  --role "Managed Identity Operator" --scope $APP_RG/Microsoft.ManagedIdentity/userAssignedIdentities/id-myvedicstar-api-prod
az role assignment create --assignee-object-id $SP --assignee-principal-type ServicePrincipal \
  --role "Container Apps Operator" --scope $APP_RG/Microsoft.App/managedEnvironments/cae-myvedicstar-prod-eus2  # join/action: every app update re-links its environment
az role assignment create --assignee-object-id $SP --assignee-principal-type ServicePrincipal \
  --role Reader --scope $DATA_RG/Microsoft.KeyVault/vaults/kv-myvedicstar-prod-xw4t                 # secret names only
```

**3. Package access for this repository.** `MyVedicStar.ApiProtection` is published from
`aprraju/myvedistar-common`. In GitHub, open the package → **Package settings** → **Manage Actions access** →
add `aprraju/myvedicstar` with **Read**. Without this, `dotnet restore` in the workflow fails with 403 even
though it has `packages: read`.

**4. Name and email claims in the access token.** `POST /api/v1/auth/userinfo` returns `id`, `name` and
`email` straight from the validated access token (`oid`, `name`, `email`; see `Authorize:UserIdentityClaims`).
Nothing is looked up in Microsoft Graph. In the Entra External ID tenant, open the API app registration
(`1a3384a0-…`) → **Token configuration** → **Add optional claim** → **Access** and add `email` (and `name`
if the tokens do not already carry it). Without them the API still works but returns `null` for the
missing values.

**5. Stop Terraform from reverting releases.** The release changes parts of the Container App that
Terraform also declares. Without this, the next `terraform apply` resets the image to the placeholder and
drops the application settings:

```hcl
resource "azurerm_container_app" "api" {
  # ...
  lifecycle {
    ignore_changes = [
      secret,                                   # Key Vault references managed by deploy-app.ps1
      template[0].revision_suffix,
      template[0].container[0].image,
      template[0].container[0].env,
      template[0].container[0].startup_probe,
      template[0].container[0].liveness_probe,
      template[0].container[0].readiness_probe,
    ]
  }
}
```

If Terraform should keep owning some environment variables, ignoring `env` means changes to them must be
applied by a release (add them to `prod.json`) rather than by Terraform.

### Application settings (`appsettings.json`)

Values that are the same everywhere live in `appsettings.json`. Any of them can be overridden per environment
with an environment variable (`Section__Key`) in `prod.json`.

| Section | Purpose |
|---|---|
| `ApiDocumentation` | OpenAPI route, Swagger UI path (`/swagger`), titles and descriptions. Published in **every** environment, anonymously; the API operations still require a bearer token. `HttpsServerUrlsOnly` (on everywhere except Development) makes the OpenAPI `servers` URL `https://` so Swagger UI calls the API over HTTPS behind the TLS-terminating ingress. |
| `HealthChecks` | Liveness (`/health/live`) and readiness (`/health/ready`) paths, also used by the container probes. |
| `ApiProtection` | Settings for the shared [MyVedicStar.ApiProtection](https://github.com/aprraju/myvedistar-common) package. **Rate limits:** `/userinfo` uses `authenticated-standard` (120 per user per minute) and every request counts toward `Global` (200 per IP per minute); 429 responses are Problem Details. **Request size:** 64 KB POST/PUT/PATCH bodies, 413 when exceeded. **Correlation:** `X-Correlation-ID` on every response and in every log entry. **`TrustedProxies`:** the only sources whose `X-Forwarded-*` headers count (prod: the Container Apps subnet). **`HealthCheckPaths`:** never rate limited; must include both `HealthChecks` paths, or startup fails. **`SsrfProtection`:** applied to outbound HTTP clients (HTTPS only; private and metadata addresses blocked); none are registered at present. Limits are counted per replica, and every value needs a restart. |
| `TransportSecurity` | In-app HTTP-to-HTTPS redirect. Off in prod, where the Container Apps ingress already redirects. |
| `Authentication` | Read by `AddMyVedicStarAuthentication` (MyVedicStar.ApiProtection). Multi-scheme bearer authentication: each token is routed by its `iss` claim to the Entra External ID, Google, Apple or Facebook scheme. Only **Entra External ID** is enabled, because the UI signs everyone in through Entra (social providers are federated inside it). `TenantDomain` must be `{TenantId}.ciamlogin.com`, the host in the token issuer, even though users sign in at `myvedicstarexternal.ciamlogin.com`; startup fails otherwise. `ClientId` is the API app registration (the token audience). `ValidAlgorithms`, `ClockSkew` and the metadata timings harden the Entra scheme. `RequireAuthenticatedByDefault: true` makes the scope policy the default and fallback. `LogTokenRejectionDetails` (on in Development only) logs why a token was rejected. |
| `Authorization` | Delegated scope the `myvedicstar-api-scope` policy requires (`access_as_user`). Every controller requires that policy, and startup fails if a controller allows anonymous access. |
| `Authorize`, `Database`, `FeatureFlags`, `Observability` | Token claims for the user's ID, name and email; PostgreSQL; feature switches; telemetry service name. |

Fixed security behaviour is code, not configuration:
- JWT signature, issuer, audience and lifetime validation.
- Secure-by-default authorization: any endpoint without `AllowAnonymous` requires a token.
- Security headers, no `Server` header, and `no-store` on profile responses.

## Pipeline

| Stage | Job | Runs on | Blocks the release? |
|---|---|---|---|
| 1. Code scan | `code-scan` | push, PR; manual runs unless `run-code-scan` is off | **No.** Findings are annotations and a job summary. It checks compiler and .NET analyzer diagnostics, code style (`dotnet format`), vulnerable packages (High/Critical shown as errors) and the deployment scripts (PSScriptAnalyzer). |
| 2. Restore | `test` | every run | Yes. Packages come from nuget.org, and `MyVedicStar.*` from GitHub Packages (see `nuget.config`). The package cache is keyed on the project files. |
| 3. Tests | `test` | every run | Yes. Results (TRX) and coverage (Cobertura) are uploaded as the `test-results` artifact and summarised in the run. |
| 4. Build image | `build-image` | push to `main`, manual | Yes |
| 5. Deploy | `deploy-prod` | after the image build | Yes |

Run a scan locally with PowerShell 7: `./.github/scripts/code-scan.ps1 -Check Analyzers|Format|Packages|Scripts`.
Vulnerable packages no longer fail the build. Watch the code-scan summary, and act on any High or Critical finding.

## Releasing

- **Automatic**: push to `main`. The shared deployment workflow tests, builds `myvedicstar-api:<commit SHA>` in ACR, then
  deploys it to prod.
- **Manual / rollback**: run `deploy/scripts/deploy-app.ps1` with `-ImageTag <previous-commit-SHA>` and the existing registry and Container App arguments. The revision mode is single:
  traffic moves to the new revision only after its probes pass, and the deploy fails, leaving the previous
  revision serving, if the new one does not become ready. It then checks `GET /health/ready` through the
  public URL.
- **Local**, with the same scripts after `az login` and with `$env:GITHUB_TOKEN` set to a PAT that has
  `read:packages` (any `dotnet restore` needs it, because `MyVedicStar.*` comes from GitHub Packages):
  ```powershell
  $tag = ./deploy/scripts/build-image.ps1 -RegistryName crmyvedicstarprodxw4t
  ./deploy/scripts/deploy-app.ps1 -ResourceGroup rg-myvedicstar-prod-app-eus2 -ContainerAppName ca-myvedicstar-api-prod `
      -RegistryName crmyvedicstarprodxw4t -ImageTag $tag
  ```
- **Secret rotation**: update the secret in Key Vault, then redeploy the current tag so the container reloads it.
- **API documentation**: `https://<app FQDN>/swagger`, with the OpenAPI document at `/openapi/v1.json`.
- **Version**: the image reports `<Version from the .csproj>+<commit SHA>` in telemetry (`service.version`).

## Open items

- **Retired Graph secret**: the Container App may still define the `graph-client-secret` Key Vault reference
  and the vault still holds the secret. Neither is used any more; remove them when convenient
  (`az containerapp secret remove ... --secret-names graph-client-secret`).
- **Terraform variables the app does not read yet**: `Authentication__Authority`/`Authentication__Audience`
  (the app reads `Authentication__EntraExternalId__*`, set by `prod.json`), `KeyVault__Uri`, `Storage__BlobEndpoint` and `Security__OriginVerify*`.
  User persistence is enabled (`FeatureFlags__UserPersistence=true`). Before deploying, provide the existing
  PostgreSQL `Database__ConnectionString` through a secret and apply EF migrations using the migration job
  `caj-myvedicstar-migrate-prod`. The API does not acquire managed-identity database tokens automatically.
- **Capacity**: Terraform sets 0.25 vCPU / 0.5 GiB, `maxReplicas: 1` and scale-to-zero. Raise `minReplicas` to at
  least 1 (2 for zone resilience) to avoid cold starts and single-instance outages.

HybridCache PostgreSQL L2 remains disabled in prod.json until the reviewed [cache migration](sql/hybrid-cache-migration.sql) is applied to the existing database. Configure the existing Database__ConnectionString secret and follow the [database-first rollout](../docs/hybrid-cache.md#database-first-rollout). No Redis service or additional Azure database is required.
