# CSP portal product validation

Date: September 26, 2026  
Scope: Flank Speed, cArmy, Cloud One, and the current SPIN provider implementation  
Status: Research and source-code audit; proposed direction, not implemented changes

## Decision

Keep the provider workspace. Its strongest purpose is to maintain the reusable service, authorization, control implementation, responsibility, and evidence records that Mission Owners need to document their systems and maintain those documents as services change.

The current implementation has a substantial foundation, but this audit does **not** establish that it delivers an end-to-end, eMASS-ready provider-to-mission workflow. Important gaps include Azure-only scope types, Microsoft-specific upstream authorization references, a disconnected authorization export path, an incomplete findings route, and limited explanation of actual control/document impacts.

The product test for every provider feature should be:

> Which Mission Owner system record, submission artifact, or ongoing monitoring decision does this feature produce or maintain—and can we trace that output to reviewed source material?

“Published capability” is an intermediate result. Success is a Mission Owner applying the correct release, understanding remaining duties, and producing accurate, traceable submission material.

## What the three providers establish

| Provider | Publicly documented facts | SPIN design implication |
|---|---|---|
| Navy Flank Speed | The Navy's June 2023 announcement explicitly describes Azure IaaS hosting for Mission Owner applications at IL5. Flank Speed also encompasses Microsoft 365 collaboration. The announcement's future PaaS/IL6 plans do not establish their current availability. [Navy announcement](https://www.navy.mil/Press-Office/News-Stories/display-news/Article/3432370/department-of-the-navy-opens-flank-speed-azure-for-business/) | Model individual offerings. An Azure hosting offering and a Microsoft 365 service must retain their own service scope, evidence, and applicable responsibility statements. A Microsoft 365 source package cannot automatically establish an Azure workload's coverage. |
| Army cArmy | ECMA describes a secure multicloud ecosystem, centrally operated common services, access/border security services, and a formal onboarding process. Its detailed service catalog and technical portal require CAC access. Its FAQ lists AWS, Azure, GCP, and OCI; the common-services section specifically discusses AWS/Azure. [ECMA](https://www.army.mil/ecma) | Model the selected environment and shared services, with explicit eligibility and responsibility records. Do not assume all listed clouds have identical services or authorization scope. Reference ECMA onboarding rather than rebuilding its provisioning process. |
| Air Force Cloud One | The DAF catalog describes multicloud hosting, security guardrails, WAF, access points, CSSP SIEM, and authorized shared services. [DAF catalog](https://software.af.mil/) A 2023 BES brief distinguishes brokered, Mission System Owner-managed space from boundary-protected development environments. [BES brief, PDF pages 19–20](https://www.airforcebes.af.mil/Portals/23/documents/2023/2023%20VID%20Master%20Slides_PUBLIC%20FINAL.pdf?ver=aNbNJRaYngRQHBCZi8WXvg%3D%3D) | Record both the service model and management arrangement. Buying through a cloud program does not establish which security services protect a workload. Capture provider, mission, and negotiated responsibilities per offering and environment. Confirm current options during provider onboarding. |

These sources validate the **product pattern**, not any particular customer's inheritance entitlement. Actual decisions, control matrices, current service catalogs, evidence-sharing terms, and customer agreements are needed before publishing authoritative offerings.

“Provider workspace” is a clearer umbrella label than assuming every participant is the commercial cloud company. SPIN should distinguish the underlying cloud supplier, enterprise service operator, common control provider, cyber defense provider, and mission system owner. Multiple roles may be performed by one organization, but that must be recorded rather than inferred.

The shared-responsibility model depends on the service being consumed and the agreement. The DoD Cloud Security Playbook specifically emphasizes understanding the SLA, incident response, data protection, and IAM responsibilities. [Cloud Security Playbook, Volume 1, Play 2](https://dodcio.defense.gov/Portals/0/Documents/Library/CloudSecurityPlaybookVol1.pdf)

## What a provider should supply and manage

This is a proposed product contract, not a claim that all three programs currently distribute these items in the same format.

| Provider-managed record | What Mission Owners use it for | Contribution to submission and sustainment |
|---|---|---|
| Offering and service description | Identify the exact service, cloud, environment, service model, management arrangement, permitted use, and contacts | SSP system description, hosting architecture, service dependencies |
| External authorization and upstream references | Review source-stated authority, decision, dates, conditions, boundary, and source system/package identifiers | Leveraged authorization references and supporting evidence; preserve distinctions among platform ATOs, provisional authorizations, and other records |
| Boundary and hosting/service scope | Establish which services/resources are included, excluded, and allocated to a mission | System boundary, component inventory, deployment description, interconnection documentation |
| Versioned security capabilities | Select reusable implementations supported by actual services, components, controls, and evidence | Inherited/shared control narratives and implementation details |
| Responsibility matrix | Understand provider, mission, and shared duties, including required customer configuration and evidence | Customer implementation tasks, control narratives, assessment scope, operational procedures |
| Evidence and assessment references | Obtain permission-appropriate evidence and trace its origin, freshness, and applicable release | SSP attachments, assessment planning/results references, evidence index |
| Provider findings and remediation | Understand relevant service weaknesses, compensating measures, remediation status, and customer action | Linked mission risk review and POA&M entries when applicable; avoid blindly copying every provider finding |
| Customer service associations | Identify which system consumes which offering, allocation, and capability release | Reproducible inheritance eligibility and service dependency records |
| Changes and monitoring obligations | Receive scoped service changes, expiration/withdrawal events, evidence updates, and required responses | Updated documents, monitoring reports, impact reviews, reassessment recommendations |

A security capability should state **what protection is provided, by which implementation, for which scope, with what evidence, and what the customer must still do**. A product name such as “Azure Key Vault” alone is a component/service reference; it is not enough to establish a reusable control implementation.

Do not require a complete ATO ZIP as the only entry point. Permit authorized sources such as a responsibility matrix, control implementation export, service guide, decision letter, assessment reference, and evidence attachment. Preserve provenance and incomplete-analysis status. Classify extracted material into appropriate record types instead of turning every paragraph into a capability.

## Intended provider-to-mission flow

1. **Define the offering.** Record the actual service and management arrangement. Identify its operator and upstream providers.
2. **Add authorized source material.** Import package contents or individual records. Show what was analyzed, unreadable, excluded, or unresolved.
3. **Review proposed records.** Confirm decisions, boundary statements, components, capabilities, control duties, citations, and duplicates. Keep imported proposals private until reviewed.
4. **Publish a reviewed service release.** Bind capability content and responsibilities to exact source and offering versions. Make publication status distinct from authorization standing.
5. **Associate the mission system.** The provider records the available allocation or service entitlement; the Mission Owner associates their system with that exact scope. For SaaS, use the appropriate service relationship rather than requiring an Azure subscription.
6. **Apply capabilities explicitly.** The Mission Owner selects applicable published releases and reviews remaining responsibilities. Association alone does not complete these steps.
7. **Build the system package.** Combine provider-derived implementation material with mission-specific narratives, configurations, evidence, assessment results, and risk decisions. Show source/version links in previews.
8. **Prepare and reconcile eMASS delivery.** Validate the selected receiving workflow and supported formats, retain exported versions, and record actual transfer/import outcomes. Export creation, receipt, import acceptance, inheritance approval, and authorization are separate states.
9. **Monitor and maintain.** Route relevant provider and cloud changes to the affected system, controls, evidence, documents, and accountable reviewers. Preserve prior releases and submitted packages.

eMASS supports control inheritance relationships, artifacts, assessments, and POA&M workflows. That makes provider reuse a strong fit, but it does not establish that an arbitrary ZIP or OSCAL file will be accepted by a particular eMASS instance. The actual integration contract and customer workflow must be verified. [DISA eMASS fact sheet](https://www.disa.mil/~/media/files/disa/fact-sheets/emass.pdf)

## Current implementation: retain and repair

The observations below come from reading the current working tree. They are source-level findings, not evidence that the deployed application or external integrations passed runtime tests. Other uncommitted work was already present and was not changed by this audit.

| Finding | Verified implementation | Recommendation |
|---|---|---|
| Useful offering and authorization foundation | `ProviderAuthorizationService.cs` and `.Decisions.cs` retain offering identity, immutable boundaries, source-stated decision metadata, review state, citations, and lifecycle changes. | Reuse these services. Distinguish decision category and issuer from upstream provider identity; migrate existing data deliberately. |
| Azure-only scope limits the intended providers | `ProviderAuthorizationService.cs:143` accepts only `AzureCloud` and `AzureUSGovernment`. `ProviderAuthorizationStore.cs:80` requires Azure tenant/subscription/resource paths. `ProviderMissionService.Applicability.cs:14` follows Azure allocations. | Introduce explicit, validated scope types incrementally. Retain Azure behavior; add other cloud and SaaS models without pretending unsupported connectors work. |
| Upstream references are Microsoft-specific | `ProviderAuthorizationService.Decisions.cs:178` validates `ProviderDecision` or `InheritedMicrosoftReference`. | Separate relationship kind, source decision type, and provider identity. AWS and other references must not be stored as Microsoft references. |
| Reviewed publication already has meaningful safeguards | `CspPackageService.Review.cs` handles candidate review, exclusions, and retry. `.Publication.cs` requires exact approval, active provider onboarding, and current approved candidates, then publishes canonical components/capability releases transactionally. | Preserve and simplify the UI around this pipeline. Do not create another publication engine or auto-publish extraction results. |
| Explicit mission association/adoption is valuable | `ProviderHostingService.cs` records scoped assignments. `ProviderMissionService.cs` associates systems. `.Applicability.cs` checks retained context and creates an adoption snapshot; it explicitly separates responsibilities from association. | Keep the separate domain actions, present them as one understandable guided flow, and support service consumption beyond Azure hosting. |
| Mission evidence projection needs a distribution design | `.Applicability.cs:83` presents provider-private citations as unavailable in the mission workspace. | Preserve privacy while providing an explicit shareable evidence package, approved summaries, or access/request workflow. A reference that cannot be inspected or lawfully included is not automatically submission-ready evidence. This finding concerns the reviewed projection, not a claim that every evidence route is absent. |
| Findings backend exists, but the displayed offering route is incomplete | `ProviderFindingService.cs` implements findings, POA&M records, retained evidence, and reviewed closure. `AuthorizationsPage.tsx:21` advertises Findings and evidence, while the rendering branches at lines 128–146 have no dedicated findings page. | Connect a task-focused UI to the existing backend and verify permissions and mission-relevant dissemination. Avoid a second remediation subsystem. |
| Change impact is currently a dependency view | `ProviderImpactService.Details.cs` explicitly describes retained targets and states that semantic control-coverage deltas are not recorded. | Extend it to explain changed implementation/duty/evidence, affected system documents, required review, owner, and disposition. Do not relabel a dependency count as an assessment or reauthorization decision. |
| Export does not use the retained provider authorization in the reviewed path | `OscalSspExportService.cs:530–546` constructs leveraged authorizations from provider names, adds “FedRAMP Authorization,” and sets the authorization date to the current date. | Fix the authoritative data path first. Use reviewed source metadata and stable references; missing authority/dates remain unresolved. This overlaps existing issue #1040 rather than needing a duplicate issue. |

Source locations are under `src/Ato.Copilot.Core/Services/ProviderAuthorizations/`, `src/Ato.Copilot.Core/Services/PackageImports/`, `src/Ato.Copilot.Dashboard/src/features/provider-authorizations/`, and `src/Ato.Copilot.Agents/Compliance/Services/` respectively.

## Proposed portal structure

Use a short provider-level navigation: **Overview · Offerings · Mission systems · Changes · Administration**. Keep global help/history reachable without turning each data entity into a primary task.

Within an offering:

| Page | User's question | Main action |
|---|---|---|
| Overview | What can customers use, and what needs my attention? | Continue the highest-priority setup/review task |
| Authorizations & sources | What documented authority and source material support this service? | Add source material / review extracted details |
| Services & scope | What is included, where is it provided, and who operates it? | Review service scope |
| Capabilities & responsibilities | What can customers reuse, and what must they implement? | Review and publish a release |
| Evidence & findings | What evidence supports it, and what weaknesses need action? | Review evidence / manage remediation |
| Mission systems | Which systems consume the service, scope, and releases? | Review an association |
| Changes & monitoring | What changed, who is affected, and who must respond? | Review impact and customer actions |

These should be views over the existing canonical records and workflows. Repurpose the current pages; remove duplicate state, dead routes, and duplicate editing surfaces as replacements are verified. Put record IDs, hashes, revision fences, and technical diagnostics in details rather than normal task instructions.

The provider overview should prioritize pending reviews, expiring or changed source records, customer actions awaiting response, and evidence needing refresh. Counts need useful destinations and should distinguish unknown/unavailable from zero. Mission system details remain limited to the provider's authorized relationship; provider status must not grant general customer-system access.

## ConMon and cATO contribution

Treat both provider-side service changes and mission-side cloud changes as inputs. Examples include a shared logging service's retention changing, an authorization being withdrawn, evidence expiring, a resource moving outside its recorded scope, or a required diagnostic setting being disabled.

The proposed output chain is:

**Observed change → scoped service/system relationship → affected implementation or duty → affected evidence/document → review task → recorded decision and follow-up.**

For example, a provider logging change could require updating a published implementation statement, notifying systems using that release, and reviewing their audit-control narratives and monitoring plans. This is an illustrative scenario, not a verified Flank Speed service change.

Each rule needs an executable condition, source, cadence, scope, baseline, severity, owner, and response. Evidence should retain observed time, collection time, evaluation version, and collection health. Missing telemetry cannot mean “no changes.” Provider notification, Mission Owner acknowledgment, completed impact review, and AO disposition are distinct states.

Cloud monitoring contributes to continuous authorization readiness, but it does not alone establish cATO. Official guidance includes continuous monitoring, active cyber defense, and DevSecOps practices and assessment. SPIN should surface recommendations and retain authorized decisions rather than automatically declaring cATO or requiring reauthorization after every change. [DoD cATO memo](https://media.defense.gov/2022/Feb/03/2002932852/-1/-1/0/CONTINUOUS-AUTHORIZATION-TO-OPERATE.PDF) [Evaluation criteria](https://dodcio.defense.gov/Portals/0/Documents/Library/cATO-EvaluationCriteria.pdf)

## Delivery priorities and acceptance evidence

1. **Prove one complete Azure mission workflow.** A synthetic provider package produces reviewed releases; a mission associates and applies them; its document previews/export use the same source-backed decision, scope, implementation, and responsibilities. Start with export integrity and evidence handoff before expanding dashboards.
2. **Close incomplete tasks and consolidate UI.** Wire the findings route, simplify review/publication, expose actionable evidence availability, and eliminate duplicate editing paths. Preserve existing authorization and tenancy checks.
3. **Generalize the model at genuine boundaries.** Separate cloud, service model, operational responsibility, authorization category, and service scope. Add non-Azure/manual service records first where useful; implement live integrations only with verified contracts and tests.
4. **Complete the change-to-document loop.** Reuse impact and monitoring services, connect controls/documents, and record customer review and disposition. Track provider release changes independently from live cloud telemetry.

Acceptance scenarios to put into the implementation plan:

- A synthetic Azure hosting offering and a synthetic M365 offering under one provider cannot acquire each other's boundary or inherited implementation accidentally.
- A provider package containing a decision, implementation statements, a responsibility matrix, and an unreadable attachment produces distinct proposals and an honest coverage exception. Nothing publishes without explicit review.
- An eligible mission system can associate a scoped allocation and apply a specific capability release; shared/customer duties remain visible and actionable.
- Systems in the same subscription but outside the allocation or authorized tenant cannot inherit eligibility or see private evidence accidentally.
- Brokered/unmanaged and managed offerings can express different duties; procurement alone creates no control coverage.
- The reviewed provider decision's actual title, issuer, date, and source survive document preview/export without fabricated values. Missing facts remain flagged.
- Published evidence respects sharing permissions, while permitted evidence or references remain traceable in the mission's submission package.
- A provider release or authorization change identifies actual dependent missions and creates review work without silently rewriting past exports or mission acceptance.
- A scoped cloud change reaches the relevant mission and document review; unavailable telemetry is distinguishable from a healthy evaluation with no changes.
- Retries, concurrent edits, stale approvals, revoked access, and failed integrations preserve history and do not duplicate records or falsely report success.
- The user can complete each revised task locally; record test results and manual-review steps before closing the related issue.

Measure traceability and usefulness: adopted implementations with accessible supporting evidence, unresolved customer duties, source freshness, time to prepare a reviewed package, export validation/reconciliation failures, and time to disposition relevant changes. Do not substitute a generic compliance score for these outcomes.

## Boundaries of this investigation

Public government sources and the current repository support the conclusions above. Current provider ATO packages, CAC-only catalogs, customer responsibility matrices, and live eMASS integrations were not available for verification. Public descriptions do not establish current authorization dates, eligible services, specific inherited controls, or permission to redistribute provider documents.

Before configuring a real provider, obtain its current service catalog, decision and boundary references, control/responsibility matrix, approved evidence-sharing rules, customer agreement, monitoring/change process, and receiving eMASS workflow. Confirm these with the provider security/service owner. Never turn the public research or synthetic demo data into actual authorization records.

This audit changed documentation only. No application changes, automated application tests, cloud calls, external issue writes, or pushes were performed as part of this report.
