# ─── Stage 1: Build React Frontend ───────────────────────────────
FROM node:20.18.1-alpine AS frontend-build

WORKDIR /app/ClientApp

# Copy package files first for layer caching.
# Must include package-lock.json and .npmrc: a floating `npm install` from
# package.json alone resolved an incompatible ajv/ajv-keywords pair and
# failed CD build-image (2026-09-01) with
# `Cannot find module 'ajv/dist/compile/codegen'`.
# .npmrc sets legacy-peer-deps=true (react-scripts@5 optional peer vs TS 5).
COPY src/Ato.Copilot.Chat/ClientApp/package.json \
     src/Ato.Copilot.Chat/ClientApp/package-lock.json \
     src/Ato.Copilot.Chat/ClientApp/.npmrc \
     ./
RUN npm ci

# #region agent log
RUN node -e "const fs=require('fs');const path=require('path');const pkg=require('ajv/package.json');const dir=path.dirname(require.resolve('ajv/package.json'));const codegen=fs.existsSync(path.join(dir,'dist/compile/codegen.js'))||fs.existsSync(path.join(dir,'dist/compile/codegen/index.js'));console.log(JSON.stringify({sessionId:'3a8241',hypothesisId:'C',location:'Chat/Dockerfile:npm-ci',message:'ajv resolved after npm ci',data:{ajvVersion:pkg.version,codegenExists:codegen,ajvDir:dir},timestamp:Date.now()}));"
# #endregion

# Copy frontend source and build
COPY src/Ato.Copilot.Chat/ClientApp/ ./
ENV CI=true
ENV TSC_COMPILE_ON_ERROR=true
RUN npm run build

# ─── Stage 2: Build .NET Backend ─────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS backend-build

WORKDIR /src

# Copy solution and project files for restore caching
COPY Ato.Copilot.sln ./
COPY src/Ato.Copilot.Chat/Ato.Copilot.Chat.csproj src/Ato.Copilot.Chat/
COPY src/Ato.Copilot.Core/Ato.Copilot.Core.csproj src/Ato.Copilot.Core/
COPY src/Ato.Copilot.Agents/Ato.Copilot.Agents.csproj src/Ato.Copilot.Agents/
COPY src/Ato.Copilot.State/Ato.Copilot.State.csproj src/Ato.Copilot.State/
COPY src/Ato.Copilot.Mcp/Ato.Copilot.Mcp.csproj src/Ato.Copilot.Mcp/

# Restore only the Chat project and its dependencies
RUN dotnet restore src/Ato.Copilot.Chat/Ato.Copilot.Chat.csproj

# Copy all source code and publish
COPY src/ src/
RUN dotnet publish src/Ato.Copilot.Chat/Ato.Copilot.Chat.csproj \
    -c Release \
    -o /app/publish

# ─── Stage 3: Runtime ────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime

# Create non-root user per FR-040
RUN addgroup --system --gid 1001 appgroup && \
    adduser --system --uid 1001 --ingroup appgroup appuser

# Install curl for health checks
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*

WORKDIR /app

# Copy published .NET app
COPY --from=backend-build /app/publish .

# Copy built React app to wwwroot
COPY --from=frontend-build /app/ClientApp/build ./wwwroot

# Create uploads and logs directories with proper permissions
RUN mkdir -p /app/uploads /app/logs && chown -R appuser:appgroup /app/uploads /app/logs

# Set environment
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENV DOTNET_RUNNING_IN_CONTAINER=true

# Expose port
EXPOSE 8080

# Switch to non-root user
USER appuser

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=15s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "Ato.Copilot.Chat.dll"]
