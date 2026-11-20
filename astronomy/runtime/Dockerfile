# syntax=docker/dockerfile:1
# AST-11 (api#76) - runtime image for the packaged MyVedicStar astronomy engine.
#
# Build context: the folder of .nupkg files produced by "dotnet pack", NOT the repository. No source and no SDK
# enters the final image; the packages are restored once during the build and copied forward.
#
# The probe program is taken from the repository via a named build context ("probesrc"), so this image stays a pure
# consumer of the .nupkg files.
#
# Kernels are deliberately NOT baked in by this file. It proves two things a repository build cannot:
#   1. the packaged engine starts and runs in an image that contains nothing else, and
#   2. with no toolkit and no kernel present it REFUSES with a named dependency instead of degrading quietly.
# A deployment image adds the kernels at build or publish time (see astronomy/PACKAGING.md). Nothing is fetched at
# run time, and the chiseled base has no shell or package manager with which it could be.

ARG DOTNET_VERSION=10.0

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION}-noble AS restore
ARG PACKAGE_VERSION=0.2.0
WORKDIR /probe
COPY . /feed

RUN cat > /probe/nuget.config <<'EOF'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="astronomy-packages" value="/feed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
</configuration>
EOF

COPY --from=probesrc / /probe/

RUN cd /probe \
    && dotnet build --configuration Release \
        -p:ProbeFeed=/feed -p:ProbePackageVersion="${PACKAGE_VERSION}"

# Chiseled runtime: no shell and no package manager.
FROM mcr.microsoft.com/dotnet/runtime:${DOTNET_VERSION}-noble-chiseled
WORKDIR /app
COPY --from=restore /probe/bin/Release/net10.0/ /app/
# /var/spice is where a deployment mounts DE440/DE441, the leap-second table and the planetary constants. It is left
# empty here so the engine's configured mount points are simply absent - which it reports - rather than invented.
USER $APP_UID
ENTRYPOINT ["dotnet", "MyVedicStar.Astronomy.PackageCheck.dll"]
