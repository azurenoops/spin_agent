# Spec 075 — CSP Integration: AWS Commercial, AWS GovCloud, GCP

**Epic:** #84 — Feature 049: CSP Integration (AWS, GCP)
**GitHub Issue:** #84
**Wave:** 9 — Core UX & Integrations
**Status:** Draft
**Branch:** `075-csp-aws-gcp-integration`

---

## Background

ATO Copilot is Azure-native today. The `AzureEnvironmentProfile` entity and `ComplianceAssessmentTool` (`compliance_assess`) are built exclusively around Azure subscriptions, Azure Policy, Microsoft Defender for Cloud, and Azure SDK clients. Mission owners running workloads in AWS Commercial, AWS GovCloud, or Google Cloud Platform (GCP) have no path to onboard those systems into SPIN.

DoD multi-cloud reality: DISA's milCloud2 (AWS GovCloud), AWS GovCloud IL2/IL4/IL5, and GCP Public Sector are all in active use. SPIN must support these environments using the same `RegisteredSystem` → `ComplianceFinding` → POA&M / SSP pipeline that Azure uses.

## Clarifications

- All three CSPs must flow into the same compliance data model — no CSP-specific forks of core entities
- AWS GovCloud (us-gov-east-1, us-gov-west-1) must be treated as a distinct CSP profile from AWS Commercial
- Authentication: AWS uses IAM role + external ID (cross-account assume-role); GCP uses service account JSON credentials
- Scanning: AWS uses Security Hub + Config Rules; GCP uses Security Command Center (SCC)
- No changes to Azure scanning — this is additive CSP support

## User Stories

### US1 — Onboard AWS Account (P1)
As a mission owner running workloads in AWS Commercial or AWS GovCloud, I want to authenticate ATO Copilot to my AWS account (IAM role + external ID) and register it as a `RegisteredSystem` so that I can begin the RMF process for that system.

### US2 — Run AWS Security Hub Compliance Scan (P1)
As an ISSO, I want to run a compliance scan against my registered AWS account that collects findings from AWS Security Hub (mapped to NIST 800-53 controls) so that I get a `ComplianceFinding` list equivalent to what Azure Defender provides.

### US3 — AWS Config Rules Evidence Collection (P1)
As an ISSO, I want AWS Config Rules compliance results automatically mapped to `EvidenceArtifact` records for relevant controls so that I have automated evidence for my SSP.

### US4 — Onboard GCP Project (P2)
As a mission owner with GCP workloads, I want to register a GCP project in SPIN using a service account credential so that my GCP environment participates in the RMF workflow.

### US5 — GCP Security Command Center Scan (P2)
As an ISSO, I want SCC findings from my GCP project mapped to NIST 800-53 controls and surfaced as `ComplianceFinding` records so that my GCP system has the same compliance posture visibility as Azure.

### US6 — Unified Multi-CSP Dashboard (P2)
As an ISSO managing systems across Azure, AWS, and GCP, I want a single compliance posture view that shows control coverage across all CSPs so that I do not need to switch between tools.

## Architecture

### Abstraction Layer

Introduce `ICloudScanProvider` interface (per CSP):

```csharp
public interface ICloudScanProvider
{
    CloudProvider Provider { get; }
    Task<IReadOnlyList<ComplianceFinding>> ScanAsync(CloudScanRequest request, CancellationToken ct);
    Task<ConnectionTestResult> TestConnectionAsync(CancellationToken ct);
}
```

Existing Azure scanner (`IAtoComplianceEngine`) becomes `AzureCloudScanProvider` implementing this interface. New `AwsCloudScanProvider` and `GcpCloudScanProvider` follow the same pattern.

### New Entities

- `CloudEnvironmentProfile` — generalized version of `AzureEnvironmentProfile`, with `CloudProvider` enum (Azure | AwsCommercial | AwsGovCloud | Gcp)
- `AwsCredential` — IAM role ARN, external ID, account ID (stored in Key Vault, referenced by ID)
- `GcpCredential` — project ID, service account key ID (stored in Key Vault)

### Control Mapping

AWS Security Hub findings map to NIST 800-53 controls via AWS's published control mappings (AWS Foundational Security Best Practices). GCP SCC findings map via Google's NIST mapping for GCP Public Sector.

Both mappings stored as seed data in `NistControlMappings` table (already exists for Azure Policy).

## Feature Flags

AWS and GCP scanning are gated behind feature flags (`FeatureFlags.AwsScanning`, `FeatureFlags.GcpScanning`) — disabled by default. Operators enable per-deployment.

---

## Scope

**In scope:**
- `CloudProvider` enum + `CloudEnvironmentProfile` entity (generalized from `AzureEnvironmentProfile`)
- `AwsCredential` + `GcpCredential` credential models (Key Vault backed)
- `ICloudScanProvider` abstraction + `AwsCloudScanProvider` implementation (Security Hub + Config Rules)
- `GcpCloudScanProvider` implementation (Security Command Center)
- NIST 800-53 control mapping seed data for AWS and GCP findings
- `compliance_assess` tool extended to accept `cloud_provider` parameter
- Feature flags for AWS/GCP scanning
- Unit tests for mapping logic + credential handling

**Out of scope:**
- milCloud2 / DISA-specific cloud profiles (future)
- Azure Government changes (existing, not modified)
- eMASS submission differences per CSP (tracked in Feature 071)
- Multi-CSP dashboard UI components (tracked in Dashboard issues)

---

## Data Model (see data-model.md)

---

## Acceptance Criteria

```gherkin
Scenario: Register AWS account
  Given a mission owner with AWS IAM role ARN "arn:aws:iam::123456789:role/SpinReader"
  When they register the account via onboard_system with cloud_provider=AwsCommercial
  Then a RegisteredSystem is created with CloudProvider=AwsCommercial
  And the IAM role ARN is stored in credential store (not in DB directly)

Scenario: AWS Security Hub scan produces ComplianceFinding records
  Given a registered AWS system with valid IAM credentials
  When compliance_assess is called for that system
  Then findings from AWS Security Hub appear as ComplianceFinding records
  And each finding maps to at least one NIST 800-53 control ID

Scenario: Feature flag gates AWS scan
  Given the AwsScanning feature flag is disabled
  When an ISSO attempts to run a scan on an AWS system
  Then the system returns a clear error: "AWS scanning is not enabled for this deployment"
```
