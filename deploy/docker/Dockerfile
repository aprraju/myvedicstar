# syntax=docker/dockerfile:1
# Runtime image for the combined MyVedicDay and MyVedicStar APIs. The build context is the `dotnet publish` output, not the
# repository: deploy/scripts/build-image.ps1 publishes on the build machine (restoring MyVedicStar.* packages from
# GitHub Packages with GITHUB_TOKEN) and then builds this image in ACR. No source, SDK or package credential ever
# enters the image.
#
# The image holds no environment-specific configuration or secrets; Azure Container Apps supplies them at runtime
# (environment variables and Key Vault secret references).

ARG DOTNET_VERSION=10.0

# Chiseled: no shell or package manager, runs as a non-root user. "extra" adds ICU and tzdata,
# which time-zone-aware Panchang calculations need.
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION}-noble-chiseled-extra
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_gcServer=1
EXPOSE 8080

COPY . .
USER $APP_UID
ENTRYPOINT ["dotnet", "MyVedicStar.API.dll"]
