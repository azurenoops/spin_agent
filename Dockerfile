# ──────────────────────────────────────────────────────────────
#  Security Posture Intelligence Navigator — Multi-stage Docker Build
# ──────────────────────────────────────────────────────────────

# Empty unless an approved package-only build context overrides this stage.
FROM scratch AS nuget-packages

# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy solution and project files
COPY Ato.Copilot.sln ./
COPY src/Ato.Copilot.Core/Ato.Copilot.Core.csproj src/Ato.Copilot.Core/
COPY src/Ato.Copilot.State/Ato.Copilot.State.csproj src/Ato.Copilot.State/
COPY src/Ato.Copilot.Agents/Ato.Copilot.Agents.csproj src/Ato.Copilot.Agents/
COPY src/Ato.Copilot.Mcp/Ato.Copilot.Mcp.csproj src/Ato.Copilot.Mcp/

# Restore
ARG NUGET_SOURCE=https://api.nuget.org/v3/index.json
RUN --mount=type=bind,from=nuget-packages,target=/nuget-feed \
    dotnet restore src/Ato.Copilot.Mcp/Ato.Copilot.Mcp.csproj --source "$NUGET_SOURCE"

# Copy source
COPY src/ src/

# Build & Publish
RUN dotnet publish src/Ato.Copilot.Mcp/Ato.Copilot.Mcp.csproj \
    --no-restore \
    -c Release \
    -o /app/publish

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Install curl for HEALTHCHECK — Azure CLI removed (fix #662: 300MB bloat + attack surface)
# For local dev credential passthrough, mount ~/.azure via docker-compose volume instead.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl ca-certificates \
    && rm -rf /var/lib/apt/lists/*

# Create non-root user
RUN groupadd -r atocopilot && useradd -r -g atocopilot -m atocopilot

# Create data directories and Azure CLI creds mount point
RUN mkdir -p /data /app/logs /home/atocopilot/.azure \
    && chown -R atocopilot:atocopilot /data /app/logs /home/atocopilot/.azure

# Copy published app
COPY --from=build /app/publish .

# Switch to non-root user
USER atocopilot

EXPOSE 3001

# Health check — polls the /health endpoint (rate-limit exempt, unauthenticated)
# Fix #660: enables Docker-native health status for standalone and Swarm deployments
HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
  CMD curl -f http://localhost:3001/health || exit 1

ENTRYPOINT ["dotnet", "Ato.Copilot.Mcp.dll", "--http"]
