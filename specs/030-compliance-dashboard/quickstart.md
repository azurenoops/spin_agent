# Quickstart: Visual Compliance Dashboard & Risk Solutions Library

**Feature**: 030-compliance-dashboard

---

## Prerequisites

- .NET 9.0 SDK
- Node.js 20+ and npm 10+
- SQL Server (local or Docker)
- PowerShell 7+

## Backend Setup

The dashboard API endpoints are added to the existing MCP server project.

### 1. Apply EF Core Migration

```bash
cd src/Ato.Copilot.Mcp
dotnet ef migrations add AddDashboardEntities
dotnet ef database update
```

This adds the following tables:
- `SecurityCapabilities`
- `CapabilityControlMappings`
- `SystemComponents`
- `ComponentCapabilityLinks`
- `ComplianceTrendSnapshots`
- `DashboardActivities`

And modifies `ControlImplementations` with two new columns:
- `SecurityCapabilityId` (nullable FK)
- `IsManuallyCustomized` (bool, default false)

### 2. Build and Run MCP Server

```bash
cd src/Ato.Copilot.Mcp
dotnet build
dotnet run
```

The server starts on `https://localhost:3001` with dashboard endpoints at `/api/dashboard/*`.

### 3. Verify API

```bash
# Portfolio summary
curl https://localhost:3001/api/dashboard/portfolio

# System detail (replace {id} with a real system GUID)
curl https://localhost:3001/api/dashboard/systems/{id}

# Capabilities library
curl https://localhost:3001/api/dashboard/capabilities
```

## Frontend Setup

### 1. Initialize Dashboard SPA

```bash
cd src/Ato.Copilot.Dashboard
npm install
```

### 2. Configure Environment

Create `.env.local`:

```env
VITE_API_BASE_URL=https://localhost:3001/api/dashboard
VITE_POLL_INTERVAL_MS=15000
```

### 3. Start Development Server

```bash
npm run dev
```

The dashboard opens at `http://localhost:5173`.

### 4. Build for Production

```bash
npm run build
```

Output goes to `dist/` for deployment.

## Running Tests

### Backend

```bash
# Unit tests
dotnet test tests/Ato.Copilot.Tests.Unit --filter "FullyQualifiedName~Dashboard|FullyQualifiedName~Capability|FullyQualifiedName~Component|FullyQualifiedName~TrendSnapshot"

# Integration tests
dotnet test tests/Ato.Copilot.Tests.Integration --filter "FullyQualifiedName~Dashboard"

# All tests
dotnet test Ato.Copilot.sln
```

### Frontend

```bash
cd src/Ato.Copilot.Dashboard
npm test            # Run Vitest
npm run test:cov    # With coverage
```

## Key Conventions

- **API DTOs**: All request/response types live in `src/Ato.Copilot.Mcp/Dtos/Dashboard/`
- **Entity models**: New models in `src/Ato.Copilot.Core/Models/`
- **Services**: Business logic in `src/Ato.Copilot.Mcp/Services/` (DashboardService, CapabilityService, ComponentService)
- **Background service**: `ComplianceTrendSnapshotService` runs as `IHostedService` — captures daily snapshots at midnight UTC
- **Frontend API client**: Typed Axios wrappers in `src/Ato.Copilot.Dashboard/src/api/`
- **Polling**: `usePolling` hook at 15-second intervals for dashboard refresh
