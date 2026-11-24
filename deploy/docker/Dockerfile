# syntax=docker/dockerfile:1
# Runtime image for the combined MyVedicDay and MyVedicStar APIs. The build context is the `dotnet publish` output, not the
# repository: deploy/scripts/build-image.ps1 publishes on the build machine (restoring MyVedicStar.* packages from
# GitHub Packages with GITHUB_TOKEN) and then builds this image in ACR. No source, SDK or package credential ever
# enters the image.
#
# The image holds no environment-specific configuration or secrets; Azure Container Apps supplies them at runtime
# (environment variables and Key Vault secret references).

ARG DOTNET_VERSION=10.0

# --- SPICE toolkit and kernels (api#173) ----------------------------------------------------------------
# Fetched at BUILD time from a PINNED version (N0067), checksum-verified, then baked in. Nothing is downloaded at
# run time, and no "latest" appears anywhere: the version, the URLs and the hashes are all in provision-spice.sh.
#
# This stage exists on the image that may select `spice`. It must NOT be added to astronomy/runtime/Dockerfile:
# that image is the toolkit-free consumer proof, and giving it a toolkit would make the D1 refusal test pass while
# proving nothing.
#
# It needs a shell and curl; the runtime stage below is chiseled and has neither. The script arrives in the ORDINARY
# build context, under spice-scripts/ (api#193): it was a named build context until 'az acr build' rejected
# --build-context outright, because named contexts are a docker buildx feature the CLI does not forward. The
# directory keeps it out of the publish output proper, so this file still reads as a consumer of that output.
FROM ubuntu:24.04 AS spice-fetch
RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl ca-certificates ncompress \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /provision
COPY spice-scripts/provision-spice.sh /provision/provision-spice.sh
# Fails loudly on a checksum mismatch or a missing libcspice.so; never falls back to whatever the network returned.
RUN bash /provision/provision-spice.sh /out

# Chiseled: no shell or package manager, runs as a non-root user. "extra" adds ICU and tzdata,
# which time-zone-aware Panchang calculations need.
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION}-noble-chiseled-extra
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_gcServer=1
EXPOSE 8080

COPY . .

# /var/spice matches SpiceConfiguration.Default kernel paths, so the engine finds them without configuration. The
# library lands where the loader looks; LD_LIBRARY_PATH is set explicitly rather than relying on ldconfig, which a
# chiseled image does not have.
COPY --from=spice-fetch /out/spice/libcspice.so /usr/lib/libcspice.so
COPY --from=spice-fetch /out/spice/de440.bsp /var/spice/de440.bsp
COPY --from=spice-fetch /out/spice/naif0012.tls /var/spice/naif0012.tls
COPY --from=spice-fetch /out/spice/pck00011.tpc /var/spice/pck00011.tpc
ENV LD_LIBRARY_PATH=/usr/lib

USER $APP_UID
ENTRYPOINT ["dotnet", "MyVedicStar.API.dll"]
