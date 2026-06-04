# Quickstart: OSCAL SSP Export + CI Schema Validation (Epic #122)

## Prerequisites

- .NET 8 SDK
- Node.js 20+ / npm
- Docker (for local API with database)
- Access to the repo at `/tmp/ato-copilot`

---

## 1. Run the API locally

```bash
cd /tmp/ato-copilot
docker-compose up -d db

cd src/Ato.Copilot.Api
dotnet restore
dotnet run
# API at https://localhost:7000
```

---

## 2. Seed a complete test system

```bash
dotnet run --project tools/Ato.Copilot.Seeder -- \
  --seed-oscal-system "OSCAL Test System" \
  --with-categorization Moderate \
  --with-baseline "NIST SP 800-53 Rev 5 Moderate" \
  --with-narratives 5
```

Note the `systemId` from seeder output.

---

## 3. Test the export endpoint

```bash
SYSTEM_ID="<paste systemId>"
TOKEN="<bearer token from dev identity server>"

# Default (envelope response)
curl -s https://localhost:7000/api/v1/systems/$SYSTEM_ID/exports/oscal-ssp \
  -H "Authorization: Bearer $TOKEN" | jq .validationStatus

# Raw OSCAL (no envelope)
curl -s https://localhost:7000/api/v1/systems/$SYSTEM_ID/exports/oscal-ssp \
  -H "Authorization: Bearer $TOKEN" \
  -H "Accept: application/oscal+json" | jq '."system-security-plan".uuid'
```

---

## 4. Run the CI validation script locally

```bash
cd /tmp/ato-copilot/ci-tools
npm install

node validate-oscal-exports.mjs \
  --base-url https://localhost:7000 \
  --system-id $SYSTEM_ID \
  --schema-dir ../src/Ato.Copilot.Agents/Compliance/Resources/oscal-schemas \
  --token $TOKEN

# Expected output:
# ✓ SSP export: valid
# ✓ Assessment Plan export: valid
# ✓ Assessment Results export: valid
# ✓ POA&M export: valid
# All OSCAL exports passed schema validation.
```

To test a failure, temporarily remove the `metadata.title` field from
`OscalSspExportService.cs` (do not commit), then re-run the script.
Expected output: `✗ SSP export: FAILED — #/system-security-plan/metadata: required`.

---

## 5. Run unit tests

```bash
# Backend — all export validation tests
dotnet test tests/Ato.Copilot.Agents.Tests/ --filter "FullyQualifiedName~OscalSspExport"

# Backend — integration round-trip test
dotnet test tests/Ato.Copilot.Api.Tests/ --filter "Category=OscalIntegration"
```

---

## 6. Run CI locally (act)

If you have `act` installed (https://github.com/nektos/act):

```bash
cd /tmp/ato-copilot
act pull_request --job oscal-validation
```

---

## Environment Variables for CI Script

| Variable | Description | Example |
|----------|-------------|---------|
| `OSCAL_API_BASE_URL` | API base URL | `https://localhost:7000` |
| `OSCAL_SYSTEM_ID` | Test system UUID | `f9e8d7c6-...` |
| `OSCAL_API_TOKEN` | Bearer token | `eyJ...` |
| `OSCAL_SCHEMA_DIR` | Path to schema directory | `./oscal-schemas` |

The CI step injects these from GitHub Actions secrets and the seeded system ID.
