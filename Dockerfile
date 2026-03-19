# ──────────────────────────────────────────────────────────────
#  ATO Copilot — Multi-stage Docker Build
# ──────────────────────────────────────────────────────────────

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
RUN dotnet restore src/Ato.Copilot.Mcp/Ato.Copilot.Mcp.csproj

# Copy source
COPY src/ src/

# Build & Publish
RUN dotnet publish src/Ato.Copilot.Mcp/Ato.Copilot.Mcp.csproj \
    -c Release \
    -o /app/publish

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Install curl for health checks and Azure CLI for credential passthrough from host
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl ca-certificates gnupg lsb-release \
    && curl -sL https://aka.ms/InstallAzureCLIDeb | bash \
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

ENTRYPOINT ["dotnet", "Ato.Copilot.Mcp.dll", "--http"]
