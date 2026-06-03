# Quickstart: RMF Workflow Completeness (Epic #121)

## Prerequisites

- .NET 8 SDK
- Node.js 20+ / pnpm
- Docker (for local Postgres / SQL Server via `docker-compose`)
- Access to the repo at `/tmp/ato-copilot` (or your local clone)

---

## 1. Run the API locally

```bash
cd /tmp/ato-copilot
docker-compose up -d db          # start local database

cd src/Ato.Copilot.Api
dotnet restore
dotnet run
# API available at https://localhost:7000
```

Verify the new endpoint exists:
```bash
curl -sk https://localhost:7000/swagger/index.html | grep authorize
```

---

## 2. Seed an AO user and test system

```bash
cd /tmp/ato-copilot
dotnet run --project tools/Ato.Copilot.Seeder -- \
  --seed-ao-user ao@example.com \
  --seed-system "Test System A"
```

The seeder creates:
- A user with `Compliance.AuthorizingOfficial` claim for "Test System A".
- One `AuthorizationDecision` row with `ExpirationDate = UtcNow + 25 days`
  (to exercise the pending-decisions widget).

---

## 3. Test the REST endpoint

```bash
# Obtain a token for the AO user (dev identity server)
TOKEN=$(curl -s http://localhost:5001/connect/token \
  -d 'grant_type=password&client_id=dev&username=ao@example.com&password=dev123' \
  | jq -r .access_token)

SYSTEM_ID="<paste system ID from seeder output>"

# POST an authorization decision
curl -X POST https://localhost:7000/api/v1/systems/$SYSTEM_ID/authorize \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "decisionType": "ATO",
    "expirationDate": "2027-06-01T00:00:00Z",
    "residualRiskLevel": "Low",
    "residualRiskJustification": "All controls implemented.",
    "riskAcceptances": []
  }'
# Expected: 201 with AuthorizationDecisionDto

# GET pending decisions
curl https://localhost:7000/api/v1/ao/pending-decisions \
  -H "Authorization: Bearer $TOKEN"
# Expected: 200 with paginated list
```

---

## 4. Run the dashboard locally

```bash
cd src/Ato.Copilot.Dashboard
pnpm install
pnpm dev
# Dashboard at http://localhost:5173
```

Navigate to:
- `http://localhost:5173/portfolio` — verify AO pending decisions widget.
- `http://localhost:5173/systems/$SYSTEM_ID/authorize` — verify Authorize page.

---

## 5. Run unit tests

```bash
# Backend
dotnet test tests/Ato.Copilot.Api.Tests/ --filter "Category=Authorization"

# Frontend
cd src/Ato.Copilot.Dashboard
pnpm test -- --testPathPattern="AuthorizePage|AoPendingDecisions|useAuthorize"
```

All tests should pass before opening a PR for #121.
