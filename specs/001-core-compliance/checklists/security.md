# Security Checklist: Core Compliance Capabilities

**Purpose**: Validate completeness, clarity, and consistency of security-related requirements
**Created**: 2026-02-21
**Feature**: [spec.md](../spec.md)

## Authentication & Authorization

- [x] CHK001 Is the `DefaultAzureCredential` auth chain order explicitly documented with precedence rules for each environment (dev, CI, production)? [Clarity, Spec §R-004]
- [x] CHK002 Are the minimum Azure RBAC roles required per scan type (resource/policy/Defender) specified individually, not just globally? [Completeness, Gap]
- [x] CHK003 Is the behavior defined when `DefaultAzureCredential` fails to obtain a token (retry, error message, graceful degradation)? [Coverage, Edge Case]
- [x] CHK004 Are service principal vs. managed identity vs. user-delegated auth scenarios all addressed for Azure Government? [Completeness, Spec §R-004]
- [x] CHK005 Is the `ComplianceAuthorizationMiddleware` role-to-permission mapping exhaustively documented for every MCP tool? [Completeness, Gap]
- [x] CHK006 Are requirements defined for how user role is determined at runtime (header, token claim, configuration)? [Clarity, Gap]

## Role-Based Access Control

- [x] CHK007 Is the permission matrix (ComplianceOfficer/PlatformEngineer/Auditor × tool) fully specified for all 12 MCP tools? [Completeness, Spec §FR-006]
- [x] CHK008 Are RBAC deny scenarios defined with specific error codes and user-facing messages per tool? [Clarity, Spec §US7]
- [x] CHK009 Is the behavior specified when an unknown or missing role is encountered? [Coverage, Edge Case]
- [x] CHK010 Are requirements defined for role escalation or delegation (e.g., PlatformEngineer requesting ComplianceOfficer approval)? [Coverage, Gap]

## Credential & Secret Protection

- [x] CHK011 Are subscription IDs, tenant IDs, and resource IDs classified as sensitive or non-sensitive in log output? [Clarity, Gap]
- [x] CHK012 Is there a requirement to sanitize Azure API responses before persisting findings (strip tokens, SAS URLs, connection strings)? [Completeness, Gap]
- [x] CHK013 Are requirements specified for how `Database:ConnectionString` secrets are managed (environment variables, Azure Key Vault, dotnet user-secrets)? [Completeness, Gap]
- [x] CHK014 Is the `Encrypt=True` requirement for SQL connections documented for both SQLite (dev) and SQL Server (prod) contexts? [Clarity, Spec §R-006]

## Data Protection

- [x] CHK015 Are data-at-rest encryption requirements defined for SQLite in dev and SQL Server in production? [Completeness, Gap]
- [x] CHK016 Is the SHA-256 content hash on evidence (FR-017) sufficient, or should signing/chain-of-custody requirements be specified? [Clarity, Spec §Data Model §4]
- [x] CHK017 Are retention and purging requirements defined for assessment data, findings, evidence, and audit logs? [Completeness, Gap]
- [x] CHK018 Are requirements defined for data residency (US-only for Azure Government workloads)? [Compliance, Gap]

## Azure Government Specifics

- [x] CHK019 Is the dual-cloud endpoint mapping (commercial vs. government) specified for all Azure services used (Resource Graph, Policy Insights, Defender, SQL, Identity)? [Completeness, Spec §R-004]
- [x] CHK020 Are requirements defined for what happens when the system is misconfigured for the wrong cloud (e.g., government endpoints with commercial credentials)? [Coverage, Edge Case]
- [x] CHK021 Is the NIST catalog GitHub fetch requirement compatible with air-gapped Azure Government environments where GitHub may be unreachable? [Consistency, Spec §FR-017]
- [x] CHK022 Are FedRAMP/FISMA boundary requirements defined for the Security Posture Intelligence Navigator itself as a system processing compliance data? [Compliance, Gap]

## Remediation Safety

- [x] CHK023 Are rollback requirements defined for failed remediations beyond "guidance"? (Automated rollback? Manual steps? State restoration?) [Clarity, Spec §US3]
- [x] CHK024 Is the scope of "high-risk warning" for AC/IA/SC families precisely defined — just a message or a blocking approval gate? [Ambiguity, Spec §US3]
- [x] CHK025 Are requirements defined for preventing concurrent remediations on the same subscription/resource? [Coverage, Gap]
- [x] CHK026 Is there a requirement to validate that the authenticated user has ARM write permissions before attempting remediation? [Completeness, Gap]

## Notes

- Check items off as completed: `[x]`
- Items tagged `[Gap]` indicate missing requirements that should be added to spec.md
- Items tagged with `[Spec §...]` reference existing spec sections to evaluate
- Address gaps before implementation to avoid security rework
