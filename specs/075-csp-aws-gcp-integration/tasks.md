# Tasks — Feature 075: CSP AWS/GCP Integration

## Phase 1 — Abstraction Layer (Foundation)
- T001: Define `CloudProvider` enum (Azure | AwsCommercial | AwsGovCloud | Gcp)
- T002: Define `ICloudScanProvider` interface with `ScanAsync` + `TestConnectionAsync`
- T003: Refactor `IAtoComplianceEngine` as `AzureCloudScanProvider` implementing `ICloudScanProvider`
- T004: Create `CloudScanProviderRegistry` — DI-registered, returns provider by `CloudProvider` enum
- T005: Update `ComplianceAssessmentTool` (`compliance_assess`) to accept optional `cloud_provider` param

## Phase 2 — Credential Models
- T006: Create `AwsCredential` entity (IAM role ARN, external ID, account ID, region, Key Vault secret ref)
- T007: Create `GcpCredential` entity (project ID, service account key ID, Key Vault secret ref)
- T008: Create `ICloudCredentialStore` interface + `KeyVaultCloudCredentialStore` implementation
- T009: EF Core migration: `CloudCredentials` table (polymorphic — `CloudProvider` discriminator)

## Phase 3 — AWS Cloud Scan Provider
- T010: Create `AwsCloudScanProvider` class in `Ato.Copilot.Agents/Compliance/Scanners/Aws/`
- T011: Implement Security Hub findings fetch via `AWSSDK.SecurityHub` NuGet
- T012: Implement Config Rules compliance results fetch via `AWSSDK.ConfigService`
- T013: Create `AwsNistControlMapper` — maps Security Hub controls to NIST 800-53 IDs
- T014: Seed AWS NIST control mappings (AWS Foundational Security Best Practices)
- T015: Connection test: verify AssumeRole succeeds before running scan
- T016: Unit tests for `AwsCloudScanProvider` and `AwsNistControlMapper`

## Phase 4 — GCP Cloud Scan Provider
- T017: Create `GcpCloudScanProvider` class in `Ato.Copilot.Agents/Compliance/Scanners/Gcp/`
- T018: Implement SCC findings fetch via `Google.Cloud.SecurityCenter.V1` NuGet
- T019: Create `GcpNistControlMapper` — maps SCC categories to NIST 800-53 IDs
- T020: Seed GCP NIST control mappings (GCP NIST 800-53 mapping)
- T021: Unit tests for `GcpCloudScanProvider` and `GcpNistControlMapper`

## Phase 5 — Onboarding Integration
- T022: Extend `RegisteredSystem.AzureProfile` → `CloudEnvironmentProfile` (generalized, migration-safe)
- T023: Update onboarding wizard (Step 2) to show cloud provider selector
- T024: Route onboarding scan to correct `ICloudScanProvider` based on registered system's provider

## Phase 6 — Feature Flags
- T025: Add `FeatureFlags.AwsScanning` + `FeatureFlags.GcpScanning` constants
- T026: Gate `AwsCloudScanProvider` + `GcpCloudScanProvider` registration behind feature flags
- T027: Return clear error message when scan attempted on disabled CSP

## Phase 7 — Integration Tests + CI
- T028: Integration tests: AWS credential validation, mock Security Hub response → ComplianceFinding
- T029: Integration tests: GCP credential validation, mock SCC response → ComplianceFinding
- T030: Verify Azure scanning unaffected (regression suite)
