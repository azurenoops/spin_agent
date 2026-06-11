# Requirements Checklist — Feature 075: CSP AWS/GCP Integration

## Functional Requirements (FR)

- [ ] FR-001: `CloudProvider` enum (Azure | AwsCommercial | AwsGovCloud | Gcp) defined
- [ ] FR-002: `ICloudScanProvider` abstraction with `ScanAsync` + `TestConnectionAsync`
- [ ] FR-003: `AzureCloudScanProvider` wraps existing `IAtoComplianceEngine` (no Azure regression)
- [ ] FR-004: `AwsCloudScanProvider` fetches Security Hub findings and maps to NIST 800-53
- [ ] FR-005: `GcpCloudScanProvider` fetches SCC findings and maps to NIST 800-53
- [ ] FR-006: `AwsCredential` + `GcpCredential` entities with Key Vault backing (no creds in DB)
- [ ] FR-007: `compliance_assess` MCP tool accepts `cloud_provider` parameter
- [ ] FR-008: Feature flags `AwsScanning` + `GcpScanning` gate provider registration
- [ ] FR-009: Clear error when scan attempted on disabled CSP feature
- [ ] FR-010: Credential connection test endpoint

## Security Requirements (SEC)

- [ ] SEC-001: AWS/GCP credentials NEVER stored in DB — Key Vault secret ID only
- [ ] SEC-002: Credential endpoints require ISSM role
- [ ] SEC-003: AWS AssumeRole uses external ID (prevents confused deputy attack)
- [ ] SEC-004: GCP service account key JSON written to Key Vault then discarded from request

## Non-Functional Requirements (NFR)

- [ ] NFR-001: Azure scanning performance unaffected by refactoring (p95 <30s preserved)
- [ ] NFR-002: AWS Security Hub scan completes within 60s for accounts with ≤1000 findings
- [ ] NFR-003: GCP SCC scan completes within 60s for projects with ≤1000 findings
- [ ] NFR-004: Credential store operations complete in <500ms

## Test Coverage (TC)

- [ ] TC-001: `AwsNistControlMapper` — maps all AWS FSBP controls to at least one NIST control
- [ ] TC-002: `GcpNistControlMapper` — maps all SCC categories to at least one NIST control
- [ ] TC-003: `AzureCloudScanProvider` (refactored) passes all existing Azure scan tests
- [ ] TC-004: AWS mock response → ComplianceFinding records correct
- [ ] TC-005: GCP mock response → ComplianceFinding records correct
- [ ] TC-006: Feature flag disabled → clear error, no scan attempted

## Definition of Done Gate

All checkboxes above must be checked before PR merge.
