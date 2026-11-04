# Docket AI connector (docket-mcp --http): MCP, REST + OpenAPI and OAuth sign-in for remote AI assistants.
# Built and published by .github/workflows/release.yml as ghcr.io/quartifex/docket-connector.
#   docker build -t docket-connector .
# The SDK runs natively on the build machine and cross-compiles for the target (arm64 under QEMU is unreliable).
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src
COPY Directory.Build.props ./
COPY src/Docket.Core/Docket.Core.csproj src/Docket.Core/
COPY src/Docket.Mcp/Docket.Mcp.csproj src/Docket.Mcp/
RUN dotnet restore src/Docket.Mcp/Docket.Mcp.csproj -a $TARGETARCH
COPY src/Docket.Core/ src/Docket.Core/
COPY src/Docket.Cli/Shared/ src/Docket.Cli/Shared/
COPY src/Docket.Mcp/ src/Docket.Mcp/
COPY src/Docket.App/Assets/Docket.ico src/Docket.App/Assets/
ARG VERSION=0.0.0
RUN dotnet publish src/Docket.Mcp/Docket.Mcp.csproj -c Release -a $TARGETARCH --self-contained false -o /out --no-restore -p:Version=$VERSION -p:DebugType=none

FROM mcr.microsoft.com/dotnet/aspnet:10.0
LABEL org.opencontainers.image.source="https://github.com/quartifex/docket" \
      org.opencontainers.image.description="Docket AI connector: MCP + OpenAPI for Vikunja tasks" \
      org.opencontainers.image.licenses="AGPL-3.0-only"
WORKDIR /app
COPY --from=build /out ./
RUN mkdir -p /data && chown "$APP_UID" /data
USER $APP_UID
# PATH so "docker compose exec connector docket-mcp setup" works.
ENV DOCKET_DATA_DIR=/data \
    DOCKET_MCP_URLS=http://0.0.0.0:8080 \
    DOTNET_gcServer=0 \
    PATH="/app:${PATH}"
VOLUME /data
EXPOSE 8080
ENTRYPOINT ["/app/docket-mcp"]
CMD ["--http"]
