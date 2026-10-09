# MO / Org portal product brief — follow this in SPIN

Status: **source of truth** for Mission Owner / organization portal claims
(including standalone vs CSP-attached, eMASS import, residual package work
against pinned inheritance, RMF function placement, System definition tabs,
and RMF bounds).  
Date: 2026-10-09  
Companion IA: [mo-org-portal-product-brief.html](mo-org-portal-product-brief.html)  
Interactive mock: [mo-org-portal-mock/index.html](mo-org-portal-mock/index.html) (not a 079 required target)  
CSP counterpart: [csp-portal-product-brief.md](csp-portal-product-brief.md)

If spec, mock, or UI copy conflicts with this brief, **update this brief
first**, then the spec. Frozen wording that is shared with the CSP brief
must stay identical.

This is **not** a second application. SPIN already has the organization /
Systems workspace at `/workspaces/organizations/{tenantId}`. Do not start a
parallel `MoPortal` app. Required visual target remains the
[Systems mock](system-overview-mock/pages.html) until Feature 079 is
explicitly updated. Track **M** in the CSP brief’s implementation waves.

| Brief name | SPIN name | Entry |
|---|---|---|
| CSP portal | Provider workspace | `/workspaces/csp` |
| MO / Org portal | Organization / Systems workspace | `/workspaces/organizations/{tenantId}` |

---

## Product intent

The MO/Org portal is where a mission **creates or imports a system** and
drives it to a **documented package** for ATO review. It can run
**standalone** (no CSP) or **attach** to a CSP offering and adopt a **pinned
inheritance release** the CSP portal published.

It does not issue the AO’s decision. Saving a form is not an ATO. Export is
not eMASS receipt. Attachment is not accepted inheritance.

**Job to be done:** make the next documentation task obvious, reuse what the
CSP already published where it is valid, and keep residual work (customer /
shared duties, evidence, package) honest.

---

## Frozen wording

Shared with the [CSP brief](csp-portal-product-brief.md). Do not invent
synonyms.

| Say | Mean | Never say |
|---|---|---|
| Recorded authorization | AO decision as recorded | We issued / the form granted an ATO |
| Pinned release | CRM + evidence summary + ATO cites at a version | The current CSP ATO; inherited by attach |
| In scope | Declared use inside published boundary, IL, and CRM | Healthy; green ATO |
| Exception | Still at IL; extra path, overlay, or skipped shared duty | Ignore; automatically accepted |
| Out of scope | Declared use is not covered by the recorded offering authorization | Invalidates the CSP ATO |
| Drifted | Adopted release ≠ CSP’s current publication | Silently upgraded; noncompliant |
| Scope fit | Declared use vs recorded offering authorization | How we affect the CSP ATO (CSP-side ledger) |
| Residual work | Customer + remaining shared duties after adoption | All 800-53 controls still ours |
| eMASS import | Bring an existing package in as a working copy | eMASS replaced; we are the system of record |
| Package export | Generate artifacts for review/submission prep | Submitted to eMASS; AO decided |
| Offering ConMon | CSP-owned evidence (not this portal’s job) | We monitor the CSP |
| MO system ConMon | This system’s recorded resources and customer evidence | CSP feed over our subs |

**Sell:** the next residual documentation task, with provenance.  
**Do not sell:** one-click ATO, inherited-because-attached, or cATO from telemetry.

---

## Standalone vs CSP-attached

Both modes use the **same** system record. Attachment is optional and
reversible. #1023 remains: an organization can complete ATO with **no** CSP.

| | Standalone | CSP-attached |
|---|---|---|
| Inheritance | None from a cloud offering (other overlays still apply) | Adopt a **pinned** CSP inheritance release |
| Control work | Full selected baseline (plus org overlays) | Residual set: customer + remaining shared. Inherited stay inherited **for that release** |
| Evidence | All local / org | Local customer evidence + **permitted** CSP summaries. No silent pull of CSP private files |
| Declared use | System categorization / boundary | Same, plus declaration the CSP uses for scope-fit |
| CSP change | N/A | Adopted release **drifts** when CSP publishes a new one. Not auto-upgraded |
| Detach | N/A | Residual set recalculates to full; adopted release retained as history, not current inheritance |

Attach sequence (must stay distinct):

1. **Subscribe / access** — CSP approves (their what-if). Not inheritance.  
2. **Associate** — this system is related to that offering/scope. Not inheritance.  
3. **Adopt pinned release** — exact CRM + evidence summary + recorded offering authorization cites. **This** is inheritance.  
4. **Confirm residual duties** — customer/shared work remains open.  
5. **Declare use** — IL, data, connections the CSP may score for scope-fit. Not an SSP body share.

Fed/Civ vs DoD vs CMMC (MO view): pick the **offering that matches the
authorization you need**. A FedRAMP Moderate offering does not cover an IL5
mission. A CMMC contractor still owns 800-171 on **their** environment even
when the cloud is FedRAMP-qualified. See CSP brief **Fed/Civ, DoD, and CMMC**.

---

## eMASS import

eMASS remains the DoD **system of record** where required. This portal is the
**working layer**.

- Import existing package metadata: system identity, categorization, controls,
  implementation statements, POA&Ms, artifacts, authorization status — as
  **source-backed** records with provenance.  
- Do not fabricate missing authorization metadata.  
- Re-import / refresh picks up eMASS changes; conflicts stay explicit.  
- Export / stage updates for send-back is **package preparation**, not
  submission receipt, not the AO decision.  
- An imported “Authorized” status is a **recorded external fact**, not
  something this portal issued.

Standalone import and later CSP attach are both valid. Import does not create
inheritance. Attach after import still requires adopt + residual confirmation.

---

## Residual package work

After a pinned release is adopted, the MO package is **not** empty. Residual
work is the product.

| Residual | Meaning |
|---|---|
| Customer-implemented controls | Still implement, evidence, assess |
| Shared duties | Confirm the customer half; link permitted CSP evidence |
| Inherited | Cite the adopted release; do not re-collect CSP evidence |
| Overlays / extras | Org, STIG, connection-specific work still local |
| Drift | CSP published r3; you are on r2 — review, then adopt or stay with rationale |
| Out of scope (CSP view) | Your declared use exceeded the offering. Correct declaration, detach, or wait for a significant change **on the CSP side**. Do not silently edit their CRM |

Package pipeline stays four distinct facts (Feature 079 / constitution):

1. Preparation / readiness  
2. Export  
3. eMASS submission / receipt  
4. AO decision  

A ready form is not 2–4.

---

## RMF bounds (MO portal)

| RMF job | This portal |
|---|---|
| Categorize | System FIPS 199 / CNSSI 1253, data types, boundary |
| Select | Baseline + overlays; subtract **adopted** inherited set only after adopt |
| Implement | Residual narratives and evidence; inherited cite pinned release |
| Assess | SAP/SAR for **this** system; do not treat CSP SAR as yours |
| Authorize | Record AO decision if entered; do not issue it from save |
| Monitor | **This system’s** recorded resources. Not CSP offering ConMon. Not implied that CSP sees your subs |

CSP ISSM may see declared use and scope-fit **on the CSP portal**. They must
not read this system’s SSP/SAR/POA&M body from that portal.

The thin clickable IA names the primary path (create / attach / residual /
package). **Narratives and the other RMF functions stay on existing Systems
screens.** They do not move to the CSP portal. They are not a seventh app.

### RMF functions — brief slot vs live route

Prefix: `/workspaces/organizations/{tenantId}/…`

| RMF job | Brief slot | Live Systems route | Standalone | CSP-attached |
|---|---|---|---|---|
| Categorize | Create / import, then system definition | `profile/MissionAndPurpose`, `UsersAndAccess`, `EnvironmentAndDeployment`, `DataTypes`, `boundaries`, `profile/PortsProtocolsAndServices` | Full local boundary | Same local boundary **plus** declared use the CSP may score |
| Select | Residual after adopt (or full baseline if standalone) | `baseline`, `security-capabilities` | Full selected baseline | Subtract **adopted pinned release** only after adopt. Association does not subtract |
| Implement | Residual duties + evidence | `inheritance/subscriptions`, **`narratives`**, `evidence`, `legal` | All narratives and evidence are ours | **Inherited:** cite the pinned release, do not rewrite CSP statements. **Residual:** customer/shared narratives and local evidence |
| Assess | Still this portal (not its own IA tile) | `assessments?tab=plan`, `assessments`, `remediation` | SAP/SAR for this system | Assess the **residual** set. CSP SAR is not yours |
| Authorize | Package four facts | `documents`, `documents/preview`, `documents?tab=exports`, `emass/status`, `authorize` | Record AO / external decision | Same. Adopting a CSP release is not an MO ATO |
| Monitor | System ConMon | `conmon`, `conmon/rules`, `conmon/changes`, `conmon/impacts` | This system’s recorded resources | Still this system only. Not CSP offering ConMon |

Supporting work that is not a seventh RMF phase, but stays here: `poam`,
`deviations`, `roles`, `history`.

### Narratives

- **Route:** `/systems/:id/narratives` (nav: Controls & evidence → Narratives).
- **Standalone:** every implementation statement is authored here.
- **Attached:** inherited controls **cite the adopted release**; residual
  controls still get MO narratives. Do not pull the CSP SSP body.
- **eMASS import:** source review today is identity-only. If imported
  narratives land later, they stay source-backed working copies — not
  “we replaced eMASS.”

### System definition tabs

RMF **Categorize** (and the SSP system description). Overlay of live
`SYSTEM_SCREEN_GROUPS` → System definition. Saving a draft is **not** an
approved SSP baseline and **not** an ATO.

Left nav is the group. Pages are tabs. **Provider hosting** is an extra tab
only on Environment (live `provider-relationships`), not a seventh left-nav
item. Leveraged-authorization records are not a definition tab; they are
source-backed hosting/inheritance context.

| Tab | Live route | RMF / SSP job | On this screen | Standalone | CSP-attached | Never |
|---|---|---|---|---|---|---|
| Mission | `profile/MissionAndPurpose` | SSP system description | Name, purpose, who depends, disruption impact | Same | Same. Does not attach a CSP | “Mission saved = ATO” |
| Users | `profile/UsersAndAccess` | SSP users and access | Populations and access *needs* | Same | Same | Granting Entra/CAC; ISSM role assignment (that is Team & permissions) |
| Environment & hosting | `profile/EnvironmentAndDeployment` | SSP environment | Where it runs; org-managed vs allocated offering | Hosting deferred / org-managed is valid | Association records *where* it runs | Association = inheritance or control subtraction |
| Provider hosting (extra) | `provider-relationships` / `…/hosting` | Access + associate | Request, declare use, CSP what-if status. **CSP declared-use corrections land here** | Hidden until they ask to attach | Extra tab on Environment only | Inheritance; SSP body share to CSP; correction ≠ revoke / ATO |
| Data | `profile/DataTypes` | Information types; privacy *screening inputs* | CUI / PII candidates that feed categorization | Same | Declared data types may be scored by CSP for scope-fit | Approving FIPS 199 / CNSSI 1253 or a PTA/PIA |
| Components & system scope | `boundaries` | Authorization boundary / inclusion | In vs out of this system; operators and rationale. Save scope draft ≠ approved baseline | Full local scope | CSP control plane stays out unless it is truly this system | Pulling the whole cloud into the MO boundary because of attach |
| Ports & interconnections | `profile/PortsProtocolsAndServices` | PPS + interconnection register | Permitted flows and external systems | Same | Hub peering is still not an ISA | Port row = authorization to connect |
| System design | `profile/SystemDesign` | SSP illustrated architecture; OSCAL network-architecture + data-flow | Six views: Context, Authorization boundary, Logical, Data flows, Network, Azure deployment. Build from recorded tabs. Save draft / submit for review | Same in-scope design | Do not treat CSP attach as a complete architecture | Canvas = authorized design; completeness % = ATO |

Next after definition: **Select** (baseline / capabilities) and **Implement** (responsibilities, narratives). ConMon later binds only to **recorded in-scope** resources.

### Controls & evidence tabs

RMF **Select** then **Implement**. Live group order. Extra tab **Control inheritance summary** only on Applied capabilities / Responsibilities (live `security-capabilities` and `inheritance*`), not on Narratives or Evidence.

| Tab | Live route | RMF / SSP job | On this screen | Standalone | CSP-attached | Never |
|---|---|---|---|---|---|---|
| Categorization & baseline | `baseline` | Select control set from impact | C/I/A, overlay, selected control count. Data types *feed* this | Full selected baseline | Baseline stays; residual work is not “delete inherited from the catalog” | Selecting baseline = implemented; save = ATO |
| Applied capabilities | `security-capabilities` | What this system uses | Org vs provider capabilities and placement | Org capabilities only | Provider listed as associated until **adopt** | Provider capability = inherited |
| Responsibilities | `inheritance/subscriptions` | Residual CRM | Customer / shared / inherited. Confirm customer half | All customer | Inherited cite pinned release; shared still open | Inherited = unimplemented; attach = CRM |
| Control inheritance summary (extra) | `inheritance` | Adopt pinned release | Adopted vs CSP current; drift; duty counts | Hidden / empty until attach | Adopt or stay with rationale | Silent upgrade; association = inheritance |
| Narratives | `narratives` | Implement | Residual statements; inherited cites | Author all | Do not rewrite CSP SSP body | Pulling CSP SAR/SSP |
| Evidence | `evidence` | Supporting artifacts | All / System / Provider shared | Local only | Permitted summaries; unavailable ≠ healthy | Silent CSP private files; MO-sub telemetry |
| Policies | `legal` | Policy references | Named org/provider policy records | Org policies | Shared summaries allowed | Policy reference = ATO |

Residual work after adopt is the product. Assess residual only.

### Assessment & risk tabs

RMF **Assess**, plus supporting risk records (not a seventh phase). Extra tab **Assessment environment** only on SAP/SAR (live `assessments*`), matching `SystemTaskNavigation`.

| Tab | Live route | RMF / SSP job | On this screen | Standalone | CSP-attached | Never |
|---|---|---|---|---|---|---|
| Assessment plan | `assessments?tab=plan` | SAP for this system | Title, lead, residual scope, schedule | Full selected baseline | Residual set; inherited cite pinned release, not re-tested as unimplemented | SAP save = executed assessment; CSP SAR is yours |
| Assessments & results | `assessments` | Collect / SAR | Determinations for residual controls | This system’s SAR | Do not copy CSP SAR | Collection = AO decision |
| Assessment environment (extra) | `assessments/environment` | Reuse recorded environment | No separate subscription picker; independent entitlement/scope/access/permission checks | Same | Provider relationship not required | Attachment = monitoring feed |
| Findings & remediation | `remediation` | Weaknesses → tasks | Finding queue; POA&M not auto-created | Same | Residual findings only | Finding = POA&M; finding = ATO |
| POA&M | `poam` | Remediation commitments | Milestones, overdue, verify | Same | Same | Overdue = withdrawn ATO; export = eMASS receipt |
| Exceptions | `deviations` | Waiver / risk acceptance / false positive | Separate records; submitted ≠ approved | Same | Same | Submission = AO decision |

Assess residual only. Package four facts stay under ATO Readiness.

---

## Personas

| Persona | Primary job | Must not |
|---|---|---|
| Mission Owner | Create/import system, declare use, residual authoring | Treat attach as inheritance or ATO |
| ISSM | Review residual package, readiness, drift | Collapse export/submission/AO |
| ISSO | Customer evidence, local ConMon, POA&Ms | Pull CSP private attachments |
| SCA | Assess residual set | Assess inherited controls as if unimplemented without cause |
| AO | Record authorization decision | Be told the portal decided |
| System owner | Boundary, inventory, interconnections | Skip residual CRM |

CSP operators are **counterparts**, not users of this portal.

---

## Epics (MO)

| ID | Epic | MVP vs later |
|---|---|---|
| M-E1 | Create system | MVP (exists; keep honest) |
| M-E2 | eMASS import as working copy | MVP path; provenance required |
| M-E3 | Standalone residual = full baseline | MVP |
| M-E4 | Adopt pinned CSP release | MVP with #1023 / #957 |
| M-E5 | Residual CRM / duties / evidence | MVP |
| M-E6 | Drift when CSP publishes | With C5 on CSP side |
| M-E7 | Package export vs submission vs AO | Track M1 (#1038–#1043) |
| M-E8 | MO system ConMon | Track M2 (#1044/#1045) — this system only |

---

## Data model (MO-owned)

```text
Organization
  └── System (new or eMASS-imported)
        ├── Categorization / boundary / inventory
        ├── Optional: Subscription → AdoptedRelease (pinned)
        ├── Residual implementations + evidence
        ├── DeclaredUse (what CSP may score)
        ├── Package readiness / export / exchange observations
        └── Recorded AO decision (optional, external)
```

Do not copy the CSP SSP body. Do not store CSP private evidence without an
explicit share. Adopted release citations must survive CSP “newest” publication.

---

## Information architecture (clickable in HTML)

Primary paths:

1. **Standalone to package** — Create or eMASS import → residual RMF → export.  
   Done when readiness is honest and export is not called submission.  
2. **Attach and inherit** — Request access → (CSP what-if) → associate → **adopt
   pinned release** → residual duties.  
   Done when inherited controls cite that release and customer work remains open.  
3. **CSP published a new release** — Drift banner → review → adopt r3 or stay
   on r2 with rationale. Never silent upgrade.  
4. **ConMon this system** — Rules on **recorded** resources. Unavailable
   telemetry ≠ no findings.

Screens: org portfolio, new system, eMASS import, system overview, attach /
adopt, residual duties, evidence, package, ConMon, roles.

### Overview tabs

Live `SystemReadinessOverview` (after #1052 / workspace refinements). Left nav
item remains **Readiness**. In-page tabs:

| Tab | Live | On this screen | Never |
|---|---|---|---|
| Package preparation | `?overview=readiness` (default) | RMF journey (browse ≠ record phase). Check again. Grouped package work. Sidebar: documents + **four package milestones** (prep / export / eMASS / AO) kept separate | Finding counts = phase complete; check = approve / inherit / eMASS / ATO; CSP three-fact validity tile |
| Monitoring & follow-up | `?overview=monitoring` | This system’s coverage, findings, POA&M overdue. Enabled ≠ healthy ≠ cATO ≠ authorization | Unavailable = no findings; attachment = monitoring feed; CSP offering ConMon |

Confirm recorded phase is an explicit audited action. Browsing Prepare…Monitor does not change the recorded phase. Drift for adopted releases is a banner; inheritance work is Control inheritance summary, not this overview.

---

## Live Systems routes (2026-10-09)

Walked **this worktree’s route table and page copy**, not a signed-in session.
Vite may serve the Dashboard shell; org URLs load the SPA. Authenticated
screens were not clicked.

Prefix: `/workspaces/organizations/{tenantId}/…`  
Paths below are workspace-local after that prefix.

Required 079 UI remains the [Systems mock](system-overview-mock/pages.html).
This map is overlay intent on that surface. Do not fork `MoPortal`.

| IA screen | Live route | What it is today | Fit |
|---|---|---|---|
| Org portfolio | `/` → PortfolioRiskProfile **and** `/systems` → PortfolioDashboard | Two homes. Risk profile: coverage %, compliance average, ATO counts. Systems list: **+ Add System**, Compliance and ATO columns | **Gap.** Not one portfolio of standalone / attached / imported, next residual task, or drift |
| Create system | `/systems/new` redirects to `/systems` and opens SystemSetupJourney | Identity, team, source, hosting (including deferred), monitoring later. Copy: ready for preparation, not authorization or eMASS submission | **Close.** Standalone by default |
| eMASS import | Setup **Bring existing documentation** + SystemSourcePanel / SystemSourceReview. Bulk: org onboarding Step3EmassImport. Later: `/systems/:id/emass/status` | Per-system: one XLSX/PDF ≤ 4 MiB; **identity fields only**; upload does not change facts; apply does not approve SSP. Status page is **round-trip vs export**, not intake | **Partial.** Working-copy identity, not a full eMASS package import. Provenance language is already honest |
| System overview | `/systems/:id` → SystemReadinessOverview | “A clear path to your ATO package” + role next-actions. Copy: readiness ≠ AO decision or eMASS acceptance. Tabs: Readiness / Monitoring | **Partial.** Next-task pattern exists. No adopted-release, residual-open, or CSP-drift banner |
| Request offering access | `/systems/:id/provider-relationships` (+ `/setup`, Environment hosting) | Opening the task creates no allocations. Associate hosting, then ISSM/ISSO confirm subscriptions separately. CSP workspace cannot run this | **Close** for associate. CSP what-if stays on the CSP portal |
| Adopt pinned release | Same wizard (capability + `releaseId`) **and** `/inheritance` **and** `/inheritance/subscriptions` | Adopt is **per capability**, not one offering CRM + evidence summary + recorded-authorization cite. Inheritance table can still set designations / org defaults | **Gap.** Sequence attach → adopt pinned release → residual is split and not pinned as one inheritance product |
| Residual duties | `/inheritance/subscriptions` (+ narratives, baseline) | Customer/shared confirmation after allocation. Provider snapshot can go stale (`PendingReview`) | **Close** as residual workbench. Not labeled residual-after-pinned-release |
| Evidence | `/systems/:id/evidence` | Views: All / System / **Provider shared** | **Close.** Local + permitted CSP summaries |
| Package | ATO Readiness: `documents`, `documents/preview`, `documents?tab=exports`, `emass/status`, `authorize` | Prep / preview / export / eMASS recon / recorded decisions are already separate routes | **Close** on four facts. Conflict: **Issue Authorization** still exists beside **Record external decision** |
| System ConMon | `/systems/:id/conmon` (`/rules`, `/changes`, `/impacts`) | Know which **system resources** are monitored | **Close.** This system only. Watch Healthy vs Unavailable copy (same trap as CSP FR-023) |

### Already aligned

- Overlay of the org workspace, not a second app.
- Create/setup does not claim an ATO.
- Association copy already says it is not subscription/inheritance.
- eMASS source review already refuses to overwrite authorization decisions.
- Package facts are split across screens, not one green tile.
- Evidence already has a provider-shared view.
- ConMon is scoped to this system’s recorded resources.

### Track M gaps (do not start a new portal)

1. **One portfolio language** — `/` vs `/systems` both score “compliance / ATO.” This brief wants mode + next residual task + drift.
2. **eMASS import as working copy** — today’s source path is identity-only; `emass/status` is recon after export.
3. **One adopt-pinned-release step** — today: hosting wizard + per-capability adoption + inheritance designations + responsibility confirm.
4. **Overview drift / residual** — missing on SystemReadinessOverview.
5. **Authorize copy** — keep record-external; treat **Issue Authorization** as out of bounds unless AO-in-product is an explicit exception.

---

## Out of bounds

- Issuing the MO or CSP ATO  
- Inheritance from attach, allocation, or “we use Flank Speed”  
- Silent upgrade to newest CSP release  
- Treating FedRAMP Moderate as IL5 coverage  
- CMMC certificate because the cloud is FedRAMP  
- CSP portal reading this SSP  
- MO portal running CSP offering ConMon  
- eMASS import as proof we replaced eMASS  
- cATO from a green scan  

---

## Build without a second system

Overlay `/workspaces/organizations/{tenantId}` and existing system screens.
Reuse adoption, inheritance, package-readiness, and ConMon services. Do not
fork `MoPortal`. Do not clone CSP offerings into the org database.

Track **M** waves (CSP brief): M1 package/Overview (#1038 family); M2 this
system’s ConMon. C2 scope-fit is **CSP-side**; this portal only supplies
**declared use** and residual work.

---

## How to follow it

1. Open [mo-org-portal-product-brief.html](mo-org-portal-product-brief.html) and the [interactive mock](mo-org-portal-mock/index.html).  
2. Walk standalone vs attach vs eMASS vs residual vs drift.  
3. Use **Live Systems routes** and **RMF functions** as the current map
   (`/workspaces/organizations/{id}/…`). Narratives stay at `/systems/:id/narratives`.  
4. Gaps go on Track M / #1023 / #957 — not a new portal feature unless 079
   cannot absorb them.

Required Systems mock remains in force until 079 says otherwise.
