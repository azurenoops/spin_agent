# Current-state evidence and architecture

## Implementation follow-up

The source audit below is the **historical baseline**, not a claim that the
subsequent implementation is missing. See the
[current checkpoint](tasks.md#implementation-checkpoint-and-release-gates) for
commands, counts and limitations.

| Workflow | Current evidence |
|---|---|
| Provider source -> mission -> output | Real production-host fixture passes through source worker, explicit publication, allocation, MO association/adoption, separate ISSM confirmation, approved summary sharing and schema-valid retained export/download. |
| Source truth/history | Explicit current-adoption selection and revision fencing preserve old snapshots. Reviewed provider facts and all six approved profile snapshots feed actual document builders. No source dates/types/authority are manufactured. |
| Mock-defined UI | Provider task navigation, all 30 Systems destinations, source/detail/review tasks, scoped monitoring, history, external decisions and SAP draft fields are wired to services. Desktop/mobile tests and shared native dialogs supplement domain tests; user visual acceptance is not claimed. |
| Evidence | Explicit named-system summary grants, version hashes, revocation and export/download permission rechecks are implemented. Original private artifacts remain denied. |
| Package purposes | Legacy, InitialSubmission, AuthorizedBaselineArchive and retained SSP ChangeSubmission are distinct. Source selectors load system-authorized baseline/decision/preview hashes. Archives/delta bundles do not masquerade as regenerated assessments or AO decisions. |
| Monitoring | Provider source rules and system scope rules use distinct ownership, shared typed evaluation, existing scheduling, retained evaluation/history and explicit mission disposition. Collection failure is not zero alerts. |
| eMASS | Manual exchange history and conflict-resolution rationale are persisted separately. No live receipt/import is asserted. |
| Service models | Manual Service versus Azure scope discrimination supports SaaS without fictitious Azure IDs; old Microsoft references remain readable beside generic upstream references. |
| Verification limit | Source builds and targeted/whole local suites have passed as recorded. A later all-integration rerun hit SQL Server timeouts and an unresponsive Docker CLI; SQL Server release verification remains blocked, not suppressed. |

## Baseline and confidence

Source baseline: `13204325f21d2ff7e0028e0065a52bb795cb5bee`, originally
`feature/1002-workspace-delivery-1037`. The session started at `bd06f9d9` and
locally fast-forwarded 75 commits with user approval. The earlier main-only
trace lacked offerings/releases/hosting that this baseline contains; those
older absence findings are not applicable.

The configured `azurenoops/ato-copilot` remote resolves to
`azurenoops/spin_agent` on GitHub. Issue bodies and hierarchy were read there.
No application tests, live Azure/Entra/eMASS integration, generated-artifact
comparison, or deployed failure reproduction was performed in this planning
pass. Checked-in tests were inspected, not executed. Therefore no workflow below
is classified as runtime **Implemented and verified**.

The sibling design directory grew after the initial inventory. The actual copy
contained **198 files**, excluding `.DS_Store`, and all destination SHA-256
comparisons matched at copy time. It includes a new onboarding companion;
retaining that reference does not expand this feature's approved screen scope.

## UI -> service -> authorization -> persistence -> processing -> output

| Workflow/classification | Sequential evidence | Output/test limits and disposition |
|---|---|---|
| Offerings/decisions: **Implemented but not verified** | [AuthorizationsPage](../../src/Ato.Copilot.Dashboard/src/features/provider-authorizations/AuthorizationsPage.tsx) -> [ProviderAuthorizationEndpoints](../../src/Ato.Copilot.Mcp/Endpoints/Csp/ProviderAuthorizationEndpoints.cs) -> [ProviderAuthorizationService](../../src/Ato.Copilot.Core/Services/ProviderAuthorizations/ProviderAuthorizationService.cs) and [decision operations](../../src/Ato.Copilot.Core/Services/ProviderAuthorizations/ProviderAuthorizationService.Decisions.cs) -> [provider models](../../src/Ato.Copilot.Core/Models/ProviderAuthorizations/ProviderAuthorizationModels.cs) | Offering/boundary/decision/citation records exist. Provider store authorizes CSP-admin/non-impersonated operations. Reuse, but complete export linkage. [Offering endpoint tests](../../tests/Ato.Copilot.Tests.Unit/ProviderAuthorizations/OfferingOverviewEndpointTests.cs) use a test host, not the complete production pipeline. |
| Source processing/publication: **Implemented but not verified** | [CspPackageImportEndpoints](../../src/Ato.Copilot.Mcp/Endpoints/Csp/CspPackageImportEndpoints.cs) -> [CspPackageService](../../src/Ato.Copilot.Core/Services/PackageImports/CspPackageService.cs) -> retained receipt and [association](../../src/Ato.Copilot.Core/Services/PackageImports/CspPackageService.Association.cs) -> [worker](../../src/Ato.Copilot.Mcp/Services/CspPackageWorker.cs)/[processor](../../src/Ato.Copilot.Core/Services/PackageImports/CspPackageProcessor.cs) -> [review](../../src/Ato.Copilot.Core/Services/PackageImports/CspPackageService.Review.cs) -> [transactional publication](../../src/Ato.Copilot.Core/Services/PackageImports/CspPackageService.Publication.cs) | Explicit review/current approved candidates and retained context precede immutable release. Preserve recovery, exclusions and idempotency. [Worker tests](../../tests/Ato.Copilot.Tests.Unit/PackageImports/CspPackageWorkerTests.cs) do not prove the entire host-to-document chain. |
| Scope/allocations: **Implemented but not verified** | Hosting UI -> [ProviderHostingEndpoints](../../src/Ato.Copilot.Mcp/Endpoints/Csp/ProviderHostingEndpoints.cs) -> [ProviderHostingService](../../src/Ato.Copilot.Core/Services/ProviderAuthorizations/ProviderHostingService.cs) -> immutable predecessor-linked scope revisions and system assignments | Azure-shaped scope already exists; no generic service model assumed. [Hosting tests](../../tests/Ato.Copilot.Tests.Unit/ProviderAuthorizations/ProviderHostingServiceTests.cs) cover service behavior; export integration not established. |
| Association/adoption: **Implemented but not verified** | Mission wizard -> [ProviderMissionEndpoints](../../src/Ato.Copilot.Mcp/Endpoints/Csp/ProviderMissionEndpoints.cs) -> [ProviderMissionService](../../src/Ato.Copilot.Core/Services/ProviderAuthorizations/ProviderMissionService.cs)/[Applicability](../../src/Ato.Copilot.Core/Services/ProviderAuthorizations/ProviderMissionService.Applicability.cs) -> pinned release/context/assignment adoption snapshot -> canonical subscription | [Mission tests](../../tests/Ato.Copilot.Tests.Unit/ProviderAuthorizations/ProviderMissionServiceTests.cs) inspect explicit adoption and separate responsibility review. Preserve role distinctions and historical pinning. |
| Responsibility handoff: **Implemented but not verified** | Review UI -> [CapabilitySubscriptionEndpoints](../../src/Ato.Copilot.Mcp/Endpoints/CapabilitySubscriptionEndpoints.cs) -> [CapabilityResponsibilityService](../../src/Ato.Copilot.Core/Services/CapabilityResponsibilityService.cs) -> [reconciliation](../../src/Ato.Copilot.Core/Services/CapabilityResponsibilityService.Reconciliation.cs) -> inheritance projection -> [fan-out worker](../../src/Ato.Copilot.Core/Services/CspResponsibilityFanoutWorker.cs) -> [impact dispatcher](../../src/Ato.Copilot.Core/Services/CapabilityResponsibilityImpactDispatcher.cs) -> narrative proposal | Revision checks, scoped ISSM/ISSO write authority and durable delivery are foundations to keep. [Integration tests](../../tests/Ato.Copilot.Tests.Integration/CapabilityResponsibilityTests.cs) are evidence of coverage, not this session's passing execution. |
| Provider findings and mission evidence: **Partially implemented** | [ProviderFindingEndpoints](../../src/Ato.Copilot.Mcp/Endpoints/Csp/ProviderFindingEndpoints.cs) -> [ProviderFindingService](../../src/Ato.Copilot.Core/Services/ProviderAuthorizations/ProviderFindingService.cs) -> provider-owned retained evidence/finding records; mission [EvidenceArtifactModels](../../src/Ato.Copilot.Core/Models/Compliance/EvidenceArtifactModels.cs) separately target system/control/capability | Source-inspected provider findings branch in AuthorizationsPage falls through to generic text rather than a dedicated findings view. Mission evidence lacks the adoption/release lineage required by this feature. Backend reuse and explicit distribution are preferable to another evidence subsystem. |
| Association representations: **Distinct models with possible duplicate writes; obsolescence not proven** | Organization placement (`SystemCapabilityLink`), provider subscription (`CapabilitySubscription`), and hosting/adoption snapshots serve different identity/version roles | Trace their callers/side effects before removing anything. Consolidate redundant entry or writes only where replacement proves equivalent. Coexistence alone is not a bug. |
| Document/export: **Partially implemented** | [Documents](../../src/Ato.Copilot.Dashboard/src/pages/Documents.tsx) -> [DashboardExportsEndpoints](../../src/Ato.Copilot.Mcp/Endpoints/Dashboard/DashboardExportsEndpoints.cs) -> export job -> [SspExportBackgroundService](../../src/Ato.Copilot.Agents/Compliance/Services/SspExportBackgroundService.cs) -> [SspService](../../src/Ato.Copilot.Agents/Compliance/Services/SspService.cs)/[OscalSspExportService](../../src/Ato.Copilot.Agents/Compliance/Services/OscalSspExportService.cs) -> file/history/download | Rendered preview and reviewed provider linkage are not established. OSCAL leveraged authorization construction uses provider names/current date rather than reviewed source metadata. Scoped export-operation enforcement needs production-host tests; no exploit or deployed failure is claimed. [Export endpoint tests](../../tests/Ato.Copilot.Tests.Integration/SspExportEndpointTests.cs) manually map handlers. |
| Profile -> export: **Partially implemented** | [SystemProfileService](../../src/Ato.Copilot.Agents/Compliance/Services/SystemProfileService.cs) saves/reviews profile content; SspService reads registered-system/SSP/related records | An approved-profile-to-output bridge is not proven by those paths. #1041 must test distinctive scalar and child values before adding any new projection. |
| Purpose/readiness: **Partially implemented; duplicated calculations** | [PackageValidationService](../../src/Ato.Copilot.Agents/Compliance/Services/PackageValidationService.cs) -> [AuthorizationPackageService](../../src/Ato.Copilot.Agents/Compliance/Services/AuthorizationPackageService.cs); separately [EmassExportReadinessService](../../src/Ato.Copilot.Agents/Compliance/Services/EmassExportReadinessService.cs) and Documents summaries | Initial package purpose, required section presence and unavailable-check semantics need correction. Reproduce #1039-#1043 using actual output/fault injection; source findings are not runtime reproductions. |
| eMASS: **Implemented but not verified** | [EmassStatus](../../src/Ato.Copilot.Dashboard/src/pages/EmassStatus.tsx) -> [EmassWorkflowEndpoints](../../src/Ato.Copilot.Mcp/Endpoints/EmassWorkflowEndpoints.cs) -> diff/conflict persistence and explicit resolution | [Endpoint tests](../../tests/Ato.Copilot.Tests.Integration/Compliance/EmassWorkflowEndpointsTests.cs) do not exercise the full production host happy path. Receipt/import acceptance remains distinct from internal export. |
| Provider impact: **Partially implemented** | [ProviderImpactEndpoints](../../src/Ato.Copilot.Mcp/Endpoints/Csp/ProviderImpactEndpoints.cs) -> [ProviderImpactService](../../src/Ato.Copilot.Core/Services/ProviderAuthorizations/ProviderImpactService.cs), [material](../../src/Ato.Copilot.Core/Services/ProviderAuthorizations/ProviderImpactService.Material.cs), [details](../../src/Ato.Copilot.Core/Services/ProviderAuthorizations/ProviderImpactService.Details.cs) -> retained previews/reviews/targets | Dependency views and exact context hashes exist, but semantic control/duty/document deltas are not established. Extend existing reviewed impact instead of replacing it. |
| ConMon: **Partially implemented; existing writes found during implementation recheck** | [client](../../src/Ato.Copilot.Dashboard/src/api/conmon.ts) calls plan/report/significant-change/reauthorization writes; [DashboardAuthorizationEndpoints](../../src/Ato.Copilot.Mcp/Endpoints/Dashboard/DashboardAuthorizationEndpoints.cs) registers all four via `MapAuthorizationRoutes`, called by [DashboardEndpoints](../../src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs). [DashboardConMonEndpoints](../../src/Ato.Copilot.Mcp/Endpoints/Dashboard/DashboardConMonEndpoints.cs) separately exposes reads. | The prior missing-route finding was wrong: it stopped at one mapper. Reuse the actual handlers; test production registration/permissions rather than add duplicate routes. Subscription totals still do not establish resource attribution or collection health. |

Implementation recheck, 2026-09-26: the complete sequential ConMon write handlers
were read in `DashboardAuthorizationEndpoints.cs` (plan, report, change and
reauthorization check). They invoke existing `IConMonService` operations.
`CheckReauthorizationAsync(..., true)` can regress the RMF phase to Assess;
this is not an AO authorization decision and must remain an explicitly
authorized action. This correction supersedes the planning missing-route claim.

## Authoritative flow and missing joins

```text
Source receipt/hash -> private candidates/exceptions -> reviewed source revision
  -> immutable decision/boundary/scope + published capability/context
  -> provider allocation -> mission association -> pinned adoption
  -> subscription -> responsibility confirmation -> inheritance/narrative proposal
  -> [complete permitted evidence and approved document projection]
  -> preview snapshot -> purpose-specific validation -> immutable export
  -> separately recorded eMASS outcomes -> separately authorized AO decision
```

Provider changes/cloud observations must join through retained scope/adoption to
implementation/duty/evidence/document impact. Provider review, mission
disposition and authorization decisions remain independent.

## Current route evidence

[ApplicationRoutes](../../src/Ato.Copilot.Dashboard/src/ApplicationRoutes.tsx)
registers the system and provider destinations;
[SystemLayout](../../src/Ato.Copilot.Dashboard/src/components/layout/SystemLayout.tsx)
lists the current 24 primary system navigation destinations. Application routes
are mounted through workspace navigation; retain its scope wrapper.

- Provider: `/authorizations/*`; offering sections include overview, boundary,
  inherited-coverage, packages and impact. Findings is advertised but not given
  a dedicated renderer in the inspected branch.
- System root: `/systems/:id`; hosting is reachable through
  `provider-relationships`, its `setup` child, and
  `profile/EnvironmentAndDeployment/hosting`.
- System destinations: `boundaries`, `legal`, `documents`, `conmon`,
  `emass/status`, `narratives/*`, `deviations`, `assessments`,
  `assessments/environment`, `remediation`, `evidence`,
  `security-capabilities/*`, `security-capabilities/inventory`, `poam`,
  `inheritance`, `inheritance/subscriptions`, `baseline`,
  `profile/:sectionType`, `authorize`, `roles`.
- Compatibility aliases: `components/*`, `capability-coverage/*`,
  `control-inheritance`, `categorization`, `capabilities/*`, `mission-purpose`,
  `users-access`, `environment`, `data-types`, `ports-protocols`,
  `leveraged-auth`, `legal-regulatory`.

F8 (Azure assessment destination) is present at this baseline and must not be
reopened as a missing-route defect. Live navigation is still an acceptance test.

## Existing specifications: reuse, do not rewrite history

- [078](../078-role-aware-workspaces/spec.md) and its provider/package/system
  contracts own authenticated workspaces and the newer domain foundations.
- [050](../050-csp-capability-lifecycle/spec.md) records legacy vetting/reparenting;
  do not treat historical overrides as a reason to bypass current publication.
- [037](../037-ssp-document-export/spec.md) and
  [041](../041-emass-package/spec.md) own document/package machinery; 079 adds
  reviewed lineage and purpose-specific semantics.
- [071](../071-emass-workflow-sync/spec.md) owns file-based reconciliation;
  historical claims of guaranteed eMASS acceptance are not current proof.
- [072](../072-anti-double-entry/spec.md) owns origin/divergence presentation.
- [075](../075-csp-aws-gcp-integration/spec.md) describes broader cloud connectors;
  079 does not revive that scope or reopen closed #84.

## Remaining evidence gates

The spec is implementation-ready at a phase/contract level, not proof of working
production behavior. Before each behavioral PR: reproduce its source finding,
read current logs for reported failures, validate applicable permissions,
complete the per-unit disposition record and run failing tests. Before deletion:
trace imports, other clients/MCP, jobs and retained-history readers. Before
real-provider configuration: obtain actual catalog, boundary, decisions,
responsibility matrix, sharing rules and receiving eMASS contract.
