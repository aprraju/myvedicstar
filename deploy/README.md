# MyVedicStar: release to Azure Container Apps

**Terraform owns the infrastructure**: the Container App, its identity, the registry, Key Vault,
PostgreSQL, network and monitoring. **This repository owns the release**. GitHub Actions builds the image and
updates only the existing Container App's image, application settings, Key Vault references and health
probes. The same image deploys without being rebuilt, and nothing environment-specific is baked into it.

```
deploy/
  docker/Dockerfile            Image build (context = repository root; see /.dockerignore)
  environments/prod.json       Application-owned, non-secret settings for prod
  scripts/build-image.ps1      Build the image in ACR (az acr build, no local Docker)
  scripts/deploy-app.ps1       Release a tag to the existing Container App and wait until it is healthy
  scripts/set-secrets.ps1      Store secrets in Key Vault (interactive; never runs in CI)
.github/workflows/
  authorize-api.yml            PR: build, test, checks.  main: build image, then deploy to prod
  authorize-api-deploy.yml     Deploy a given tag to prod (also used manually for rollback)
```

## Configuration

| Source | Holds | Owner |
|---|---|---|
| `appsettings.json` (in the image) | Defaults that are safe in every environment | app code |
| Container App environment variables set by Terraform | Infrastructure endpoints (App Insights, Key Vault URI, ...) | Terraform |
| `deploy/environments/prod.json` → Container App environment variables | Application settings (auth audience, Graph tenant, feature flags, ...) | this repo |
| Key Vault → Container App secret reference | Secrets (`graph-client-secret`) | a person, via `set-secrets.ps1` or the portal |

`deploy-app.ps1` **merges** its settings into the container. A Terraform variable is kept unless
`prod.json` defines one with the same name. The Container App reads secrets through its managed identity
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

The GitHub identity is the `myvedicstar` app registration (client ID `6a0e280e-…`, service principal object
ID `8e41e8a4-01d7-4b1c-8bd2-436e0ec19453`). It currently has **no federated credential and no role
assignments**, so the workflows cannot sign in until these exist. Prefer adding them to Terraform; the
equivalent CLI commands are:

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
  --role Reader --scope $DATA_RG/Microsoft.KeyVault/vaults/kv-myvedicstar-prod-xw4t                 # secret names only
```

**3. The Graph client secret.** Create `graph-client-secret` in `kv-myvedicstar-prod-xw4t`: the client
secret of the Entra External ID app registration that has Microsoft Graph `User.Read.All`. Use the portal,
or run `./deploy/scripts/set-secrets.ps1` from a network the vault allows (its firewall denies public access by
default). You need `Key Vault Secrets Officer` on the vault.

**4. Stop Terraform from reverting releases.** The release changes parts of the Container App that
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

## Releasing

- **Automatic**: push to `main`. The workflow tests, builds `myvedicstar-authorize-api:<commit SHA>` in ACR, then
  deploys it to prod.
- **Manual / rollback**: run *Authorize API - deploy* with an earlier commit SHA. The revision mode is single:
  traffic moves to the new revision only after its probes pass, and the deploy fails, leaving the previous
  revision serving, if the new one does not become ready. It then checks `GET /health/ready` through the
  public URL.
- **Local**, with the same scripts after `az login`:
  ```powershell
  $tag = ./deploy/scripts/build-image.ps1 -RegistryName crmyvedicstarprodxw4t
  ./deploy/scripts/deploy-app.ps1 -ResourceGroup rg-myvedicstar-prod-app-eus2 -ContainerAppName ca-myvedicstar-api-prod `
      -RegistryName crmyvedicstarprodxw4t -ImageTag $tag
  ```
- **Secret rotation**: update the secret in Key Vault, then redeploy the current tag so the container reloads it.

## Open items

- **Graph client ID**: `prod.json` assumes the API app registration (`1a3384a0-…`, the token audience) is
  also the one with Graph `User.Read.All`. Change `Integrations__MicrosoftGraph__ClientId` if a separate
  registration is used.
- **Terraform variables the app does not read yet**: `Authentication__Authority`/`Authentication__Audience`
  (the app reads `Authentication__EntraExternalId__*`, set by `prod.json`), `Database__*` (passwordless PostgreSQL
  via managed identity), `KeyVault__Uri`, `Storage__BlobEndpoint` and `Security__OriginVerify*`.
  User persistence stays off (`FeatureFlags__UserPersistence=false`) until the app supports managed-identity
  database authentication; the Terraform migration job `caj-myvedicstar-migrate-prod` is then the place to run
  migrations.
- **Capacity**: Terraform sets 0.25 vCPU / 0.5 GiB, `maxReplicas: 1` and scale-to-zero. Raise `minReplicas` to at
  least 1 (2 for zone resilience) to avoid cold starts and single-instance outages.
