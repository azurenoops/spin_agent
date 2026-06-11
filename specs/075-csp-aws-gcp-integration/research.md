# Research — Feature 075: CSP AWS/GCP Integration

## Options Considered

### Option A: Per-CSP Scan Service (Chosen)
Separate `AwsCloudScanProvider` + `GcpCloudScanProvider` behind `ICloudScanProvider` abstraction. Registered in DI by `CloudProvider` enum. `ComplianceAssessmentTool` routes to the correct provider.

**Pros:** Clean separation, independently testable, no Azure regression risk, new CSPs can be added without touching existing code.
**Cons:** Requires refactoring `IAtoComplianceEngine` as a first step.

### Option B: Extend IAtoComplianceEngine with CSP flags
Add AWS/GCP code paths directly into `IAtoComplianceEngine`.

**Rejected:** Violates SRP, increases Azure regression risk, creates an unmaintainable monolith.

### Option C: Separate compliance agent per CSP
Separate `AwsComplianceAgent`, `GcpComplianceAgent` classes with no shared abstraction.

**Rejected:** Duplicates tool registration, dispatch logic, no shared finding model pipeline.

## AWS Scanning Decision

AWS Security Hub is the primary signal source. It aggregates findings from AWS Config, GuardDuty, Inspector, and Macie into a single API surface. The ASFF (Amazon Security Finding Format) includes a `Compliance.SecurityControlId` field that maps directly to AWS Foundational Security Best Practices control IDs (e.g., `EC2.1`, `IAM.3`). AWS publishes a mapping from these to NIST 800-53 controls.

AWS Config Rules are used as a secondary/evidence source (config rule → EvidenceArtifact) — their compliance states confirm resource-level configuration.

## GCP Scanning Decision

GCP Security Command Center (SCC) Standard tier is available in all GCP projects. SCC findings include a `category` field (e.g., `FIREWALL_RULE_LOGGING_DISABLED`) and Google publishes a NIST 800-53 mapping for GCP Public Sector. SCC is the closest AWS-equivalent for GCP.

## Authentication Decision

- AWS: IAM role assumption via STS `AssumeRole` with external ID. This is the AWS-recommended cross-account access pattern. No long-lived access keys stored.
- GCP: Service account with workload identity. Key stored as JSON in Azure Key Vault, referenced by Key Vault secret ID in DB.
- Azure (existing): Service principal + managed identity. No change.

## Control Mapping Approach

Both AWS and GCP publish their own NIST mappings. These will be loaded from JSON seed files at startup (not stored in DB as rows) for performance and ease of updates. The `NistControlMappings` static helper class will be extended.
