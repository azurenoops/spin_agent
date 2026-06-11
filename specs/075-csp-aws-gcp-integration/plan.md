# Plan — Feature 075: CSP AWS/GCP Integration

## Overview

Feature 075 extends ATO Copilot with multi-cloud scanning support (AWS Commercial, AWS GovCloud, GCP) using a provider abstraction pattern. The Azure engine is refactored as the first implementation; AWS and GCP follow.

## Implementation Sequence

### Phase 1: Abstraction (1–2 days)
Introduce `ICloudScanProvider` and `CloudScanProviderRegistry`. Refactor `IAtoComplianceEngine` as `AzureCloudScanProvider`. Update `ComplianceAssessmentTool` to route by provider. No functional change — Azure still works exactly as before.

**Done signal:** `compliance_assess` tool works unchanged; new `cloud_provider` param accepted but ignored (Azure default).

### Phase 2: Credentials (1 day)
Add `AwsCredential` + `GcpCredential` entities and EF migration. Implement `ICloudCredentialStore` reading from Azure Key Vault. Unit test credential round-trip.

**Done signal:** Credentials stored/retrieved without actual credential values in DB.

### Phase 3: AWS (3–4 days)
`AwsCloudScanProvider`: Security Hub API calls, Config Rules API calls, NIST control mapping. Feature flag gate. Integration tests with mock AWS responses.

**Done signal:** Mock Security Hub response → ComplianceFinding records with correct control IDs.

### Phase 4: GCP (2–3 days)
`GcpCloudScanProvider`: Security Command Center API, NIST control mapping. Feature flag gate.

**Done signal:** Mock SCC response → ComplianceFinding records.

### Phase 5: Onboarding (1 day)
Update `RegisteredSystem` model additively. Onboarding step 2 shows cloud provider selector. Scan routing uses registered provider.

### Phase 6: Testing + CI (1 day)
Regression tests for Azure. Integration tests for AWS/GCP with mocked cloud SDK responses.

## Risk Mitigation

| Risk | Mitigation |
|---|---|
| AWS SDK version conflicts | Pin `AWSSDK.Core` to same version used by other AWS integrations in solution |
| GCP SDK size bloat | Use `Google.Cloud.SecurityCenter.V1` only — avoid pulling in full GCP SDK |
| Credential leakage | Never store credential values in DB; always Key Vault secret ID reference |
| Azure regression | AzureCloudScanProvider is an exact refactor — existing tests must pass before any new code |
