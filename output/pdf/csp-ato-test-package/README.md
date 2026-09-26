# Flank Speed IL5 - production-style synthetic ATO package

**SYNTHETIC TEST DATA - NOT A VALID ATO. No real Navy, Microsoft, DISA or
authorizing-official approval is represented. No operational CUI is included.**

This package models a **Flank Speed-style Microsoft 365 DoD collaboration tenant**,
not an Azure IaaS provider, GCC High tenant, or copy of the Navy's real boundary.
The name supplies scenario context only. Invented configuration, assessment,
personnel, timing and remediation details are explicitly test assumptions.

## Scope and acceptance contract

The replacement expands the former four-component Harbor example into:

- An SSP with boundary/data flows, categorization assumptions, roles, twelve
  logical service/operational components, twenty-four reusable capabilities,
  and forty representative NIST SP 800-53 Rev. 5 control narratives.
- An assessment plan/report with examine/interview/test procedures, six open
  fictional findings, associated remediation plans and explicit evidence gaps.
- Operational annexes for access/change management, incident response,
  continuity, monitoring and evidence handling.
- An **unsigned, ineffective decision draft**, not an issued authorization.
- A structured import projection, a cross-referenced workbook, clean and
  needs-attention ZIPs, SHA-256 manifest and exact expected-result ledger.

Forty controls are a **representative subset**, not the complete DoD IL5,
FedRAMP High, CNSSI 1253, or organization-tailored baseline. A SaaS provider's
public compliance description is not evidence that this fictional tenant
inherits a verified authorization. Tenant categorization, service availability,
licensing, control applicability and inheritance all require real review.

Separate responsibilities throughout:

1. **Microsoft service layer**: candidate inherited infrastructure/service
   protections; current authorized boundary and customer-responsibility
   evidence are not supplied.
2. **Tenant operator**: tenant policy, identities, privileged administration,
   sharing, logging, change control, incident coordination and monitoring.
3. **Customer/command**: mission authorization, data-owner decisions, endpoints,
   networks, personnel eligibility and retained responsibilities.

Endpoints, customer networks, third-party SaaS/backup, arbitrary Azure resources,
unapproved applications, and support-channel data handling are not automatically
inside this boundary. No classified processing is claimed. Retention/version
history is not described as a universal backup or restoration guarantee.

## Upload and review

Use [flankspeed-il5-ato-clean.zip](flankspeed-il5-ato-clean.zip) first. "Clean" means structurally readable
source files, not zero findings, complete semantic analysis, or an authorized
system. The archive contains four PDFs, the structured JSON and the workbook.
Do not upload the generator, expected-results ledger or manifest as source
documents.

| Source artifact | Contents | Pages |
|---|---|---:|
| [System Security Plan](01-flankspeed-il5-ssp.pdf) | Boundary, data flows, roles, components, capabilities and control narratives | 68 |
| [Assessment and remediation](02-flankspeed-il5-assessment.pdf) | Assessment plan/report, six findings and six open POA&Ms with future closure requirements | 17 |
| [Operational annexes](03-flankspeed-il5-operations.pdf) | Seven procedures, monitoring activities, evidence register and four fictional samples | 20 |
| [Unsigned decision draft](04-flankspeed-il5-unsigned-decision.pdf) | Proposed scope, conditions, exclusions and decision-review checklist | 3 |
| [Structured import projection](05-flankspeed-il5-components.json) | Stable inventory identities and separate decision, boundary, finding and POA&M claims | - |
| [Registers workbook](06-flankspeed-il5-registers.xlsx) | Cross-referenced inventory, controls, findings, milestones, evidence and operations | - |

Total PDF length: **108 pages**. Use [expected-results.json](expected-results.json)
for exact fixture IDs and PDF page citations, and
[package-manifest.json](package-manifest.json) for file hashes and archive entries.

1. Sign in as the appropriate CSP administrator.
2. In **Authorizations**, open the existing offering and choose **Upload
   package**. Keep the same offering; do not create a duplicate just for import.
3. Select or explicitly prepare a tenant-configuration boundary. Do not invent
   an Azure subscription/resource ID for this Microsoft 365 tenant.
4. Select the clean ZIP, enter a package name and upload. Optional onboarding
   upload may also use this archive; onboarding remains receipt/progress only.
5. Continue review through the persisted receipt. Reconcile repeated PDF,
   workbook and JSON identities using the stable `FS-CMP-*` and `FS-CAP-*` IDs.
6. Review citations, proposed mappings, responsibilities, duplicates and
   dependencies. Approve exact revisions and publish separately only when the
   application's actual gates permit it. Do not change the draft decision into
   an effective ATO merely to satisfy a guard.

The [needs-attention ZIP](flankspeed-il5-ato-needs-attention.zip) repeats the same six source files and adds an actually
encrypted PDF, malformed JSON and an unsupported `.synthetic` attachment.
The public fixture password is `fixture-only-password`; it is not a credential.
All nine archive entries require an explicit outcome. Test this variant as a
separate intake; duplicate handling is expected when the clean sources already
exist. Do not reset an existing provider just to run it.

## What realistic does and does not mean

The source documents use production-style organization, traceable IDs,
responsibility allocation, evidence registers, milestones, assessment criteria,
configuration assumptions and operational handoffs. They do **not** include:

- A real signed decision, provider authorization letter, current authorization
  registry verification, assessment attestation or Microsoft customer-only
  authorization package.
- Actual tenant exports, scan results, personnel records, logs or approvals.
- A complete control baseline or every enhancement/organization-defined
  parameter; independent evidence that any control is operating effectively.

The structured JSON is an application import fixture, **not OSCAL**. Neither
the workbook nor the PDFs are official eMASS/FedRAMP submission templates.
Current extraction can be partial, and typed-claim review/publication gaps in
the application are not fixed by these documents. Expected results are target
semantics and structural checks, never fabricated application test results.

## Public grounding

Read on 2026-09-24; these establish public context, not this tenant's authority:

- [Microsoft Office 365 GCC High and DoD](https://learn.microsoft.com/en-us/office365/servicedescriptions/office-365-platform-service-description/office-365-us-government/gcc-high-and-dod):
  DoD versus GCC High, IL5 context and the support-boundary caveat.
- [Microsoft DoD IL5 overview](https://learn.microsoft.com/en-us/compliance/regulatory/offering-dod-il5):
  service-specific applicability and continuing customer responsibilities.
- [NIST SP 800-37 Rev. 2](https://csrc.nist.gov/pubs/sp/800/37/r2/final):
  RMF, system/common-control authorization and monitoring.
- [NIST SP 800-53A Rev. 5](https://csrc.nist.gov/pubs/sp/800/53/a/r5/final):
  assessment methodology; the page also notes its 5.2.0 update.

The public Navy Flank Speed page was unavailable to the retrieval tool; no
Navy-specific configuration or current ATO status was verified or reproduced.

## Reproduction and verification

`source-content.json` is the human-readable scenario source. `build_package.py`
generates the deliverables; `test_package.py` verifies their structure,
cross-references, markings, archive coverage and provenance.
The generator requires Python with `reportlab`, `pypdf[crypto]`, `openpyxl` and
`pymupdf`; the crypto extra supports the deliberately encrypted appendix. It
makes no network or application calls. Run these commands from this directory
using the environment where those dependencies are installed:

```bash
python build_package.py
python -m unittest -v test_package.py
```

Optional visual review (choose a destination outside the upload directory):

```bash
python render_previews.py /path/to/preview-output
```

Regenerating replaces only the named Flank Speed deliverables and their
manifest/expected results. It does not upload, seed, approve or publish records.
For lost-response/retry tests, reuse the same archive bytes rather than rebuilding
the package; regenerating encrypted content can change its hash.
The original Harbor PDF is preserved under the unit-test fixtures for its
existing regression; it is not part of either new upload archive.

Git treats PDFs as binary artifacts to preserve their byte offsets and manifest
hashes. Do not apply text whitespace or line-ending cleanup to generated PDFs.

Verified locally:

- **8/8 artifact checks passed:** entity counts/cross-references, evidence states,
  exact PDF page citations, synthetic markings and text bounds, component-section
  pagination, structured claim separation, SHA-256/archive integrity, real
  exception fixtures and workbook safety.
- **2/2 focused .NET analyzer tests passed:** the new Flank Speed JSON projection
  and the unchanged original Harbor PDF regression. No full application suite
  was run for this artifact-only replacement.
- All 108 PDF pages were rendered and their contact sheets visually reviewed,
  with detailed component/control/finding/POA&M spot checks during generation.

No live upload, review, approval or publication was performed. Manual application
acceptance remains open; these checks do not establish PDF/workbook semantic
extraction completeness, authorization validity or production readiness.
