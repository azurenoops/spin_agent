1|# Quickstart: OSCAL Full Compliance Suite (076)
2|
3|## Prerequisites
4|
5|- SPIN Agent dev environment running
6|- Docker installed (for oscal-cli validation sidecar)
7|- Azure AI Foundry credentials configured (for AI decomposition)
8|
9|## OSCAL CLI Validation (Local)
10|
11|```bash
12|# Pull the oscal-cli Docker image
13|docker pull ghcr.io/metaschema-framework/oscal-cli:latest
14|
15|# Validate an exported SSP
16|docker run --rm -v $(pwd)/output:/data oscal-cli validate /data/ssp.json
17|
18|# Resolve a FedRAMP profile
19|docker run --rm -v $(pwd):/data oscal-cli resolve-profile /data/fedramp-high-profile.json /data/resolved.json
20|```
21|
22|## Test OSCAL Export Endpoints
23|
24|```bash
25|BASE="https://localhost:5001"
26|TOKEN="<jwt>"
27|
28|# Export SSP
29|curl -H "Authorization: Bearer ***   "$BASE/api/systems/{id}/oscal/export/ssp" -o ssp.json
30|
31|# Export POA&M
32|curl -H "Authorization: Bearer ***   "$BASE/api/systems/{id}/oscal/export/poam" -o poam.json
33|
34|# Export SAR
35|curl -H "Authorization: Bearer ***   "$BASE/api/systems/{id}/oscal/export/sar" -o sar.json
36|
37|# Validate exported document
38|curl -X POST -H "Authorization: Bearer ***   -H "Content-Type: application/json"   -d @ssp.json   "$BASE/api/systems/{id}/oscal/validate"
39|
40|# Import SSP (preview mode)
41|curl -X POST -H "Authorization: Bearer ***   -F "file=@xacta-ssp.json"   "$BASE/api/systems/{id}/oscal/import/ssp?mode=preview"
42|
43|# AI decompose a control
44|curl -X POST -H "Authorization: Bearer ***   "$BASE/api/systems/{id}/controls/ac-1/oscal/decompose"
45|```
46|
47|## Running OSCAL Unit Tests
48|
49|```bash
50|# All OSCAL tests
51|dotnet test --filter "FullyQualifiedName~Oscal" --logger "console;verbosity=normal"
52|
53|# Specific service
54|dotnet test --filter "FullyQualifiedName~OscalSarExportServiceTests"
55|dotnet test --filter "FullyQualifiedName~EmassBridgeServiceTests"
56|```
57|
58|## Key OSCAL Gotchas
59|
60|1. **kebab-case JSON** — All OSCAL properties use kebab-case. `.NET 8` handles this with `JsonNamingPolicy.KebabCaseLower`.
61|2. **UUID must change on every edit** — Use `IOscalUuidRegistry.GetOrCreateUuid(systemId, documentType)` which regenerates on each call.
62|3. **`markup-multiline` is NOT CommonMark** — NIST Markdown subset. Do not use raw HTML or GFM extensions.
63|4. **FedRAMP props require namespace** — `{ "name": "implementation-status", "ns": "https://fedramp.gov/ns/oscal", "value": "implemented" }`
64|5. **control-id is always lowercase** — `ac-1`, not `AC-1`. eMASS wants uppercase — transform at bridge layer only.
65|6. **`by-components[].component-uuid` must exist in `system-implementation.components[]`** — OSCAL Metaschema constraint. Validate component inventory exists before export.
66|