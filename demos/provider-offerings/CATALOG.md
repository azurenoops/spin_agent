# Provider offering catalog

SYNTHETIC DEMONSTRATION ONLY. All services, measurements, decisions, identities and evidence in this package are fictional. Not Microsoft provider data, a DoD approval, an IL5 authorization, a customer entitlement, or operational evidence.

## Azure IL5 · Shared services — 1.2

Directory: `azure-il5-shared-services/release-1.2/`

Parser input: `azure-il5-shared-services/release-1.2/review-source.zip` with exactly six JSON documents; readable originals in `sources/`.

Expected native candidates: **69** total; 8 Component, 8 Capability, 16 ControlMapping, 32 Responsibility, 1 AuthorizationReference, 1 AuthorizationDecisionClaim, 1 BoundaryClaim, 1 AssessmentFinding, 1 PoamItem.

## Microsoft 365 · Collaboration — 1.0

Directory: `microsoft-365-collaboration/release-1.0/`

Parser input: `microsoft-365-collaboration/release-1.0/review-source.zip` with exactly six JSON documents; readable originals in `sources/`.

Expected native candidates: **46** total; 5 Component, 5 Capability, 11 ControlMapping, 20 Responsibility, 1 AuthorizationReference, 1 AuthorizationDecisionClaim, 1 BoundaryClaim, 1 AssessmentFinding, 1 PoamItem.

## Azure IL5 · Shared services — 1.3 proposed

Directory: `azure-il5-shared-services/release-1.3-proposed/`

Parser input: `azure-il5-shared-services/release-1.3-proposed/review-source.zip` with exactly six JSON documents; readable originals in `sources/`.

Expected native candidates: **69** total; 8 Component, 8 Capability, 16 ControlMapping, 32 Responsibility, 1 AuthorizationReference, 1 AuthorizationDecisionClaim, 1 BoundaryClaim, 1 AssessmentFinding, 1 PoamItem.

## Files in each baseline

- `service-guide.{md,docx,pdf}` — scope, exclusions, component coverage and onboarding.
- `synthetic-reference-record.{md,docx,pdf}` — fictional issuer, type, dates and conditions.
- `customer-responsibility-matrix.{md,docx,pdf,csv}` — per-control provider/shared/customer duties.
- `customer-assessment-summary.{md,docx,pdf}` — demo-approved summary, moderate finding and corrective plan.
- `PRIVATE-assessment-working-paper.{md,docx,pdf}` — one logical internal attachment in three formats; seven-row ledger.
- `evidence-index.{md,docx,pdf,csv}` — constructed evidence descriptions, citations and distribution handling.
- `release-notes.{md,docx,pdf}` — baseline status and release scenario.
- `customer-bundle.zip` — the six customer documents and two CSVs; excludes PRIVATE working paper and internal manifests.
- `review-source.zip` — canonical deterministic analyzer input containing exactly six JSON documents.
- `sources/*.json` — readable originals for all six import documents.

## Six-source import layout

1. `01-service-scope.json`: `components`, `boundaryClaims`.
2. `02-capability-catalog.json`: `capabilities` with supported `componentIds`, `controlIds`, and `responsibility`.
3. `03-customer-responsibilities.json`: `responsibilities` with supported `capabilityId` dependencies and explicit duties.
4. `04-synthetic-reference-record.json`: `authorizationReferences`, `authorizationDecisionClaims`.
5. `05-assessment-summary.json`: `assessmentFindings`.
6. `06-corrective-plan.json`: `poamItems`.

All six are recognized native structured documents, not unknown JSON metadata
masquerading as analyzed text. Titles, descriptions and typed claim qualifications
identify the synthetic nature. There are no PDF, DOCX, CSV, README, manifest,
encrypted entry, or private working-paper documents in the import ZIP.

## Walkthrough for the real application operator

1. Create the Azure shared-services offering with its own provider-managed boundary;
   create Microsoft 365 as an independent SaaS offering. Never reuse Azure resource
   allocations as SaaS entitlement.
   Manifest `hostingScopes` and `environments` provide explicitly fictional
   document-backed identities. Azure uses `AzureUSGovernment`; collaboration uses
   a `Service` relationship in `Microsoft365DoD`. Source document 01 and the service
   guide cite the exact identities. They are not permission to contact any cloud.
2. Upload each baseline `review-source.zip` (exactly six JSON source documents).
   Verify the native candidate counts above, source citations, dependencies,
   and finding-to-corrective-plan relationship before accepting any declaration.
3. Native `components` create service components. `capabilities` reference their
   component IDs and list `controlIds`; each emits one capability, one mapping
   per control and one Shared responsibility. `responsibilities` adds three
   separately worded Provider/Shared/Customer duties per capability.
4. `authorizationReferences` uses only `id`, `name`, `reference`, `issuer`,
   `issuedAt`, `expiresAt`, `description`. The synthetic reference must not be
   represented as a real ATO. Typed claims use `id`, `name`, `claim` with the
   corresponding lower-camel-case claim section. The analyzer binds source fields.
5. Review the synthetic distribution-decision and boundary claims, the Open
   Moderate assessment finding, and Planned POA&M item. None changes authority
   or finding workflow state merely by being parsed. Required closure evidence
   has not been submitted.
6. Attach the customer documents as approved-demo distribution materials.
   Attach ONE preferred format of the private working paper and set actual
   application visibility to PRIVATE. Do not upload internal manifests as sources.
7. Publish/review baseline versions via the application's normal controls to
   establish the requested available 1.2 and 1.0 scenario. File generation alone
   does not establish application release status. Labels 1.2, 1.3 and 1.0 are
   source-document editions only; the canonical publication revision may be an
   integer. Never invent a semantic-version API or equate edition labels with
   persisted revision numbers.
8. Upload Azure's full 1.3 proposed inventory as a separate review revision.
   Compare stable IDs: only audit and backup descriptions change. Keep 1.2
   available; do not publish or auto-adopt 1.3 merely to demonstrate a proposal.
9. Manually open the PDFs and DOCX files. Inspect source citations in the real
   review UI and confirm customer downloads exclude the private attachment.

## Audit and limitations

`internal-manifest.json` records document SHA-256 digests, source locators, control
coverage, expected native counts and companion-document locations. It intentionally
does not hash itself. Stable source IDs are fictional and are not live entity IDs.
Candidate keys are generated by the analyzer; do not substitute source IDs for
runtime candidate keys in API calls.

The generator and validator are local standard-library utilities. Candidate counts
are derived from the inspected C# native parser, not a claim that the real API was
run. Prose/PDF extraction can generate additional proposals; these companions are
not part of the deterministic six-file review ZIP. None of these artifacts is an
OSCAL conformance claim. No document asserts a real connector, license, authorization
decision, provider assessment result, or government endorsement.
