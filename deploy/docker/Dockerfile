# syntax=docker/dockerfile:1
# Builds the MyVedicStar Authorize API image. Build context is the repository root:
#   az acr build --registry <acr> --image myvedicstar-authorize-api:<tag> --file deploy/docker/Dockerfile .
# The image holds no environment-specific configuration or secrets; those are supplied at runtime by
# Azure Container Apps (environment variables and Key Vault secret references).

ARG DOTNET_VERSION=10.0

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
ARG BUILD_CONFIGURATION=Release
# Source commit, appended to the informational version reported in telemetry (for example 1.0.0+1a2b3c4d).
ARG SOURCE_REVISION=
WORKDIR /src

# Restore first so the package layer is cached until a project file changes.
COPY src/MyVedicStar.Shared/MyVedicStar.Shared.csproj src/MyVedicStar.Shared/
COPY src/MyVedicStar.Authorize.Data/MyVedicStar.Authorize.Data.csproj src/MyVedicStar.Authorize.Data/
COPY src/MyVedicStar.Authorize.Integration/MyVedicStar.Authorize.Integration.csproj src/MyVedicStar.Authorize.Integration/
COPY src/MyVedicStar.Authorize.Business/MyVedicStar.Authorize.Business.csproj src/MyVedicStar.Authorize.Business/
COPY src/MyVedicStar.Authorize.Api/MyVedicStar.Authorize.Api.csproj src/MyVedicStar.Authorize.Api/
RUN dotnet restore src/MyVedicStar.Authorize.Api/MyVedicStar.Authorize.Api.csproj

COPY src/ src/
RUN dotnet publish src/MyVedicStar.Authorize.Api/MyVedicStar.Authorize.Api.csproj \
    --configuration "${BUILD_CONFIGURATION}" \
    --no-restore \
    --output /app/publish \
    -p:UseAppHost=false \
    -p:SourceRevisionId="${SOURCE_REVISION}"

# Chiseled: no shell or package manager, runs as a non-root user. "extra" adds ICU and tzdata,
# which time-zone-aware Panchang calculations need.
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION}-noble-chiseled-extra AS final
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_gcServer=1
EXPOSE 8080

COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "MyVedicStar.Authorize.Api.dll"]
