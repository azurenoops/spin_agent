# Issue #1001: State-Aware Narratives and Reference Library

Source: https://github.com/azurenoops/spin_agent/issues/1001
Branch: `feat/1001-narrative-library` (base `c3d74d9b`).

## Specification

Implement the supplied four-view mock within the existing system layout:
Control Narratives, Narrative Library, Import & map, and Review change.
Persistent left navigation and the existing task panel remain available.
Branding clarification (2026-09-21): match the deployed SPIN dashboard's light
theme rather than introducing a dark central workspace. Retain the existing logo,
tenant branding, shell typography, white surfaces, gray borders and indigo actions
across all four views. Keep responsive layout and workflow behavior unchanged.
Bottom navigation uses the requested labels: `02 · Narratives`, `02 · Library`,
`03 · Import & map`, `04 · Review change`. Review links identify an actual
proposal; never fabricate a sample v8 or snapshot identifier.

Uploaded language is an unverified reference claim, never implementation evidence.
Publishing references cannot change implementation or authorization decisions.
Policy and technical freshness are independent. Approved content remains active
until an authorized reviewer accepts a versioned proposal. Missing evidence and
conflicting claims remain visible. Implementation progress reflects implemented
controls, independently of narrative approval.

Import supports XLSX/CSV columns Control ID, Policy Narrative, Technical Narrative;
DOCX and digital PDF text; labeled plain text/Markdown; and direct paste.
Scanned PDFs without extractable text return an explicit OCR-required result;
OCR is not silently simulated. Extraction is a draft: users correct control IDs,
policy/technical classification, and organization/system/capability scope before
acknowledging and publishing. Unknown controls and incomplete mappings block publish.

Remove Document Sources from Settings and browser-local source selection from
generation. Offer an explicit, opt-in migration of legacy URLs in the library;
do not delete local values before acknowledgement or fetch arbitrary URLs.

## Design

Reuse existing .NET services, tenant-filtered EF context, authenticated actor,
versioned narrative governance, and dashboard system layout. Reuse installed
ClosedXML, OpenXML and PdfPig for parsing. Enforce upload size/type and
expanded-content limits; treat extracted material as untrusted data, not model
instructions. Server validation and role checks apply to preview, mapping,
publication, generation and review. No new cloud resource or OCR service is assumed.

Reference revisions and extracted passages persist server-side with tenant and
scope ownership. Generation records immutable reference revision identifiers and
observed context provenance. Relevant changes compare against that provenance:
policy/reference revisions affect policy freshness; component, capability,
boundary and configuration changes affect applicable technical narratives.
Assessment changes matter only when relevant to cited controls/evidence.
Repeated identical observations must not create duplicate proposals.

Verify current service contracts incrementally before adding persistence or
generation fields. Do not replace complete assignment checks with a single-role
snapshot. No bypasses, fake readiness or fabricated evidence. Generation and review
errors must preserve the active approved version.

## Tasks and Verification Gates

- [ ] Remove browser-local generation dependency with red/green tests.
- [ ] Persist scoped, tenant-isolated references and draft imports; enforce RBAC.
- [ ] Implement bounded parsers and editable mapping preview for supported formats.
- [ ] Publish reviewed mappings atomically and retain immutable revisions.
- [ ] Connect library routes, sidebar, import flow and bottom navigation.
- [ ] Ground generation in system state plus published applicable references.
- [ ] Track independent freshness and create deduplicated review proposals.
- [ ] Wire diff, provenance, conflict/gap display and authorized versioned decisions.
- [ ] Remove old Settings entry point and provide explicit migration workflow.
- [ ] Validate desktop/mobile layout and real interactions with synthetic data.
- [ ] Run unit, HTTP integration, isolation/RBAC, parser, browser, build and coverage gates.
- [ ] Offer local manual acceptance; preview external writes before publication.

The mock's sample values are not production defaults. Import must process real
files; a static mock is not completion. Issue #1001 is the feature tracking record.
Approved and linked stories: #1006 (references), #1007 (generation/freshness),
#1008 (review), #1009 (interface/migration).

System reference authors must hold an active MissionOwner, SystemOwner, ISSO or
ISSM assignment. Shared Organization/Capability publications require an active
tenant-level ISSM or Administrator assignment linked to the authenticated person.
Readers need a system assignment or that tenant authority. Proposal acceptance
requires ISSM authority; the author cannot approve their own proposal. All checks
are tenant-scoped and repeated on mutation, with no browser-persona bypass.

## Draft Publication Status (2026-09-21)

This is an incomplete implementation for review, not a merge-ready release.
Implemented slices include scoped reference import/publication, bounded parsers,
manual grounded proposals, versioned review, and the four light-themed SPIN views.

Recorded focused checks: 16 parser tests, 13 library/proposal service tests,
2 authenticated SQLite HTTP tests, 19 dashboard UI tests, and 2 isolated
desktop/mobile browser workflows passed. Dashboard TypeScript checking passed.
Browser fixtures are synthetic; no real model or production data was exercised.

Remaining work and acceptance gates:

- Automatic relevant-change proposal generation is not implemented.
- Technical component scoping and inherited responsibilities need completion;
	approved-proposal freshness after manual edits needs further validation.
- Align administrator review permissions with the specified ISSM-only rule;
	complete review audit/history and approved-baseline handling.
- Support scope changes after extraction and harden concurrent publication and
	proposal creation, parser edge cases, and model-response failure coverage.
- Validate SQL Server schema and tenant/RLS integration; run full regression,
	build and required modified-path coverage gates.
- Synchronize canonical feature planning artifacts and user documentation.
- Complete interactive local manual acceptance. The Vite preview started, but
	login configuration returned HTTP 500; screenshots alone are not acceptance.

Keep the PR in draft and link, rather than close, #1001 and its stories.