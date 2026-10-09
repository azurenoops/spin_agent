# CSP portal product brief — follow this in SPIN

Status: **source of truth** for CSP portal product claims (including RMF
bounds, frozen wording, inheritor monitoring, Fed/Civ vs DoD vs CMMC, and
how we build without a second system).  
Date: 2026-10-09  
Companion IA: [csp-portal-product-brief.html](csp-portal-product-brief.html)  
MO / Org counterpart: [mo-org-portal-product-brief.md](mo-org-portal-product-brief.md)

Feature 079 encodes these claims as requirements. If spec, mock, or UI copy
conflicts with this brief, **update this brief first**, then the spec.

This is **not** a second portal and it does **not** replace Feature 079’s required
UI targets. SPIN already has two workspaces:

| Brief name | SPIN name | Entry |
|---|---|---|
| CSP portal | Provider workspace | `/workspaces/csp` |
| MO / Org portal | Organization / Systems workspace | `/workspaces/organizations/{tenantId}` |

Follow this brief by mapping each intent onto those surfaces, then closing gaps
inside Features [078](../../specs/078-role-aware-workspaces/spec.md) and
[079](../../specs/079-provider-system-workflow-consolidation/spec.md). Do not
start a parallel `CspPortal` app. How to add E2/E6 without a debt spike is
**Build without a second system** below.

The required visual/task targets remain the
[provider workspace mock](provider-workspace-mock/index.html) and
[Systems mock](system-overview-mock/pages.html) until Feature 079 is
explicitly updated. The 2026-10-09 walk below treats the
[clickable IA](csp-portal-product-brief.html) as the product target and
lists live-workspace changes needed to follow it.

---

## How to follow it

1. Open the [clickable IA](csp-portal-product-brief.html) and walk a path
   (publish offering, approve MO, defend parent ATO).
2. Hit the matching **production route** in the table below (not a new page).
3. Score the epic: already true in code, partial, or missing. Use
   [csp-product-validation-2026-09-26.md](csp-product-validation-2026-09-26.md)
   before assuming a screen is end-to-end complete.
4. If the gap is real, add or update a Feature 079 / 078 task. Do not invent a
   new spec-kit feature until this overlay’s conflicts are decided.
5. Verify the downstream Mission Owner document/export path. An offering save
   is not an ATO.

Local check: run the dashboard, sign in as a provider admin, open
`/workspaces/csp`, and complete one path against the IA.

---

## Product intent (from the brief)

The provider workspace productizes **offerings**, keeps the **recorded parent
authorization** defensible, and shows whether each attached Mission Owner’s
**declared use is still inside that authorization’s scope**.

It does not run the Mission Owner package. It does not issue, extend, or
withdraw an ATO. It publishes what missions may inherit and shows
**in-scope / exception / out-of-scope** consumption of the recorded offering
authorization.

Keep these facts separate (already a SPIN rule):

| Fact | Means |
|---|---|
| Offering publication | Draft / published / deprecated / retired |
| ATO validity | Recorded decision is effective, expired, withdrawn, or conditional |
| Package readiness | Provider evidence/package is complete enough to show |
| Monitoring health | Evidence is current — or stale |
| Inheritance adoption | None / pinned release / drifted |
| Scope fit (impact) | In-scope declared use / CRM or boundary exception / out of authorized scope |
| Authorization type | IATT vs ATO — different AO decisions, not a pipeline |

An offering attachment is not accepted inheritance. A recorded external
decision is not package readiness. SPIN does not issue the offering’s
authorization; it records source-backed facts. A subscription in the CSP
tenant is **hosting**, not inheritance. See **Access, hosting, and
inheritance** below.

**Job to be done:** prove that real tenant use still matches the authorized
offering — without reading every Mission Owner system package.

---

## RMF bounds and CSP value

Expert review, 2026-10-09. The CSP portal is **in RMF bounds and useful** if it
helps the CSP maintain *their* authorized offering and prove who inherited
*what*. It is **out of bounds** the moment it issues, withdraws, or “scores”
an ATO as if the portal were the Authorizing Official.

### Frozen wording (use this in UI and specs)

| Say | Mean | Never say |
|---|---|---|
| Recorded authorization | The AO’s decision as recorded (dates, conditions, scope) | We issued / manage / score the ATO |
| Pinned release | CRM + evidence summary + ATO cites at a version | The current ATO; latest; inherited by attachment |
| In scope | Declared use inside published boundary, IL, and CRM | Healthy; authorized; green ATO |
| Exception | Still at IL; extra path, overlay, or skipped shared duty | Minor; ignore; automatically accepted |
| Out of scope | Declared use is not covered by the recorded authorization | Invalidates the ATO; withdraws the ATO |
| Drifted | Adopted release ≠ current publication | Noncompliant; unauthorized |
| Scope fit | Whether declared use is inside the recorded authorization | Impact score; how the MO affects your ATO |
| Offering ConMon | Freshness of **CSP-owned** evidence, recorded decision dates, offering POA&Ms | Monitoring all MO systems; cATO |
| MO system ConMon | That mission’s resources, customer-control evidence, MO package | CSP oversight feed; implied by attachment |
| Offering access | Approved to associate this system with the published offering | Inherited the ATO; allocated a sub |
| Azure allocation | Bound subscription / RG as hosting (often in the CSP tenant) | Offering access; inheritance; monitoring feed |
| Associated | System recorded as running on the offering | Inherited; authorized; “they have a sub” |
| Adopt | Pinned inheritance release accepted | Attach; allocation; capability listed |
| IATT | Interim Authorization to Test: time-bounded test in a named env | ATO-lite; junior ATO; automatically becomes ATO |
| ATO | Authorization to Operate: operational use of mission data | IATT that “graduated”; three envs = three ATOs |
| Dev / Test / Prod | Hosting lanes the CSP allocated | Authorization decisions; IATT; ATO |
| Shared evidence summary | MO-permitted excerpt or reference for a named shared duty | Raw MO subscription telemetry; silent pull |
| FedRAMP authorization | Recorded civilian cloud authorization (P-ATO or agency ATO) as stated | DoD ATO; IL4; CMMC certificate |
| DoD offering authorization | Recorded IL / PA / eMASS decision for that offering | FedRAMP Moderate “counts as” IL5 |
| CMMC cloud eligibility | Recorded DFARS/FedRAMP Moderate or equivalency **as stated in source** | “CMMC Authorized”; we issued CMMC |

**Sell:** which consumers are still inside the authorization we already have.  
**Input:** declared use + published authorized use, both pinned to a version.

Interactive check: [csp-home-impact-mock](csp-home-impact-mock/index.html).

Feature 079 spec delta (E2 + E6, 2026-10-09):
[spec.md](../../specs/079-provider-system-workflow-consolidation/spec.md),
[plan.md](../../specs/079-provider-system-workflow-consolidation/plan.md),
[tasks.md](../../specs/079-provider-system-workflow-consolidation/tasks.md)
SF01–SF06. Implementation is not authorized until failing tests exist.
GitHub issue token `SCOPEFIT_ISSUE` is preview-only.

### What the CSP is on the hook for

Under NIST RMF / DoD RMF (same pattern in FedRAMP), the CSP owns **the offering
as a system**. Mission Owners still run their own RMF and **leverage** the CSP
authorization. Leverage is valid only for in-scope services, at the authorized
impact level, on a specific package/release.

| RMF job | CSP portal should |
|---|---|
| Categorize / bound the offering | Impact level, data types, services in/out of boundary |
| Select / implement | Provider-implemented controls + a CRM (inherited / shared / customer) |
| Assess | CSP evidence, SAP/SAR for the *offering*, not the MO package |
| Authorize | **Record** the AO’s decision (dates, conditions, scope). Do not issue it |
| Monitor | **Offering** ConMon only (see below). Significant change, CSP POA&Ms, notify inheritors. Not MO-sub telemetry |

Attachment is not inheritance. A green dashboard is not an ATO.

### Access, hosting, and inheritance (three yeses)

**Finding, 2026-10-09 mock walk.** “MO under a CSP” collapses three different
approvals. The CSP **Access requests** queue (Mission systems → Subscribers)
is **offering access**, not Azure RBAC and not leveraged authorization.

The Mission Owner does **not** request access from the CSP catalog. They
request it on that system’s **Provider hosting** tab (Environment extra tab;
live `provider-relationships` / hosting). Companion:
[mo-org-portal-product-brief.md](mo-org-portal-product-brief.md).

| Yes | Means | Where MO asks | Where CSP acts | Inherits the offering ATO? |
|---|---|---|---|---|
| Offering access | This system may be a consumer of the published offering | **Provider hosting** → Request, then declare use | Mission systems → Access requests → what-if → approve / refuse | No |
| Azure hosting | Where the system runs (sub / RG in the CSP tenant, or MO tenant) | Environment & hosting; allocation **after** approve | Allocate / bind scope. Not the what-if screen | No |
| Pinned inheritance | Cite CRM + evidence summary + recorded authorization at a release | **Control inheritance summary** → adopt | Publish inheritance releases; show drift | Only after **adopt** |

**Chicken or egg.** Cloud placement often happens first (someone already gave
the MO a subscription in the CSP tenant). Compliance still does not skip
declare-use / what-if / adopt. A sub in the CSP tenant is **environment**,
not inheritance. **Inheritance is never first.**

**Intended order (compliance product)**

1. MO has a system record (standalone is valid).
2. Request offering access (Provider hosting).
3. Declare use (IL, data, extra paths — CSP may score this; not an SSP share).
4. CSP what-if → approve or refuse. Approve is access, not inherit, not an ATO.
5. Allocate / bind the Azure subscription (hosting fact).
6. Associate “this system runs here.”
7. Adopt a pinned inheritance release.
8. Residual SSP / assess / POA&M remain the MO’s package.

If the sub already exists, steps 5–6 **document** placement; steps 2–4 and 7
still happen. Out-of-scope declared use can sit in a CSP sub and remain
**out of authorized scope**.

**Request correction** (per-MO impact) is not a new access request. It returns
to that system’s **Provider hosting** tab to correct **declared use**. Access
stays. Scope fit stays until the MO resubmits and the CSP re-scores. Not
Administration, not email, not the MO SSP.

**Never:** “they have a sub, so they inherited our ATO.” Never: CSP catalog is
where the MO requests access. Never: Access requests queue = inheritance
adopters. Never: approve access = allocate Azure = adopt release.

Interactive check: [csp-home-impact-mock](csp-home-impact-mock/index.html)
(Access requests) and [mo-org-portal-mock](mo-org-portal-mock/index.html)
(Provider hosting).

**Walk this in the mocks (551st ISR Ground Station):**

1. MO Systems → **ISR Ground Station** → Environment → **Provider hosting**.
2. Request → Declare use (**IATT + Dev** for the happy path; switch to Test to see exception) → **Send to CSP**.
3. Open CSP **what-if** from the waiting step (Mission systems → Access requests).
4. CSP estimate → approve or refuse. Approve is access only.
5. Return to MO Provider hosting → **Allocate** (existing CSP-tenant sub or defer) → **Associate**.
6. **Control inheritance summary** → Adopt r2. Residual work remains.

Do not skip to adopt because a sub exists. Mission Planning / Training Cloud stay pre-attached to show correction and drift.

### IATT vs ATO (two authorization paths)

**Finding, 2026-10-09.** Conversation shorthand “ITT” is treated as **IATT**
(Interim Authorization to Test) unless a named contract means otherwise.
IATT and ATO are **two AO decisions**, not one path with a promotion step.

| | IATT | ATO |
|---|---|---|
| AO question | May this system be **tested**, in this env, for this window? | May this system **operate**, with this data, for this mission? |
| Typical env | Dev / isolated test | Prod (Test only if that *is* the operational system) |
| Data | Test / synthetic. Live CUI is usually out of scope | Mission data the system actually processes |
| Time | Short, explicit end date | Authorization period (and conditions) |
| Inheritance | May pin a **test** CRM/release if that is what was recorded | Pins the **operational** inheritance release |
| What it is not | Not ATO-lite. Not production. Not cATO | Not an IATT that graduated |

**Does IATT feed an ATO?** Only as **evidence**, if reused. Test results may
support the ATO SAP/SAR. The IATT **decision** does not become the ATO.
Passing Dev tests does not authorize Prod. A locked-down Dev does not
authorize Test.

**Dev / Test / Prod are hosting, not three authorizations.** CSP onboarding
that hands the MO three subscriptions (Flank Speed–style pattern) is
**allocate / associate**. The MO can hold all three subs and still have
**no IATT and no ATO**.

If IATT is scoped to **Dev** but Dev is locked like Prod, MOs often test in
**Test**. That is declared-use drift, not a preference:

| Declared path | Declared env | Scope-fit claim |
|---|---|---|
| IATT | Dev | In scope for the test decision (env dimension) |
| IATT | Test | **Exception** — common when Dev is unusable; still not an ATO |
| IATT | Prod | **Out of scope** — testing in production |
| ATO | Prod | In scope for operational use (env dimension) |
| ATO | Test / Dev | Exception or out of scope unless that lane *is* the operational system |

A test inheritance release is not the prod release. Adopting the prod CRM
because a Test sub exists is “we have a sub, so we inherited the ATO.”

**Product:** declared use includes **authorization path** (IATT vs ATO) and
**env lane** (Dev / Test / Prod). CSP what-if scores those against the
recorded offering authorization **for that path and lane**. SPIN still does
not issue the IATT or the ATO.

**Never:** three envs = three ATOs. Never: IATT in Test is “closer to ATO.”
Never: IATT automatically becomes ATO when they move to Prod.

### Fed/Civ, DoD, and CMMC (one CSP, several authorizations)

**Source of truth for multi-market CSPs.** Conversation shorthand “CCMC” is
treated as **CMMC** (Cybersecurity Maturity Model Certification) unless a
named office/contract is specified.

A CSP does not get one authorization “for the government.” Three programs
reuse NIST controls but **different AOs, packages, and customers**.

```text
CSP offering (commercial product)
 ├── Fed/Civ: FedRAMP (P-ATO or agency ATO) — NIST SP 800-53 Low/Moderate/High
 ├── DoD: IL-authorized PA/ATO (IL2–IL6, SRG, often eMASS) — often CNSSI 1253
 └── CMMC: not a CSP ATO — DIB contractors must use a qualifying cloud
           if they process CUI there
```

| Who | What they need from the CSP | What they still own |
|---|---|---|
| Civilian agency (Fed/Civ) | FedRAMP-authorized offering + CRM | Agency system ATO |
| DoD mission | IL-authorized offering + DoD CRM/overlays | Mission ATO (eMASS) |
| DIB contractor (CMMC) | Cloud that meets FedRAMP Moderate or **recorded** equivalency | CMMC / NIST 800-171 on **their** environment |

**Fed/Civ.** JAB P-ATO or agency ATO is the **offering** authorization. The
agency still ATOs *its* system. Inheritance is the FedRAMP CRM. Offering
ConMon is FedRAMP ConMon, not every agency tenant’s subscriptions.

**DoD.** Same commercial product is usually a **different** boundary, overlay,
and AO. FedRAMP Moderate **does not equal** IL4/IL5. DoD may *leverage*
FedRAMP as evidence; that does not replace a DoD authorization decision.
Missions inherit only in-scope services at the **authorized IL**.

**CMMC.** Certifies the **contractor’s** CUI environment, not the CSP’s 800-53
package. If the contractor uses a CSP for CUI, DFARS 252.204-7012 requires
FedRAMP Moderate or DoD-defined FedRAMP equivalency (third-party evidence,
not a slide that says “equivalent”). Confirm current DoD policy before
recording equivalency; SPIN must not invent it. The CSP package is
**environment qualification**, not the contractor’s CMMC certificate.

**Product rule:** model **offerings per recorded authorization**, not “the
company is FedRAMP” as one ATO.

- Offering A: FedRAMP Moderate, civilian CRM  
- Offering B: IL5 Azure Gov, DoD CRM  
- Optional recorded fact: DFARS/CMMC cloud **eligibility as stated in source**

MOs adopt **one pinned release**. Scope-fit is against **that** release’s
IL/CRM. Never: “FedRAMP exists, so IL5 declared use is in scope.” Never:
“CMMC Authorized” on CSP home because a FedRAMP date exists.

Equivalency details are **confirm against current DoD policy**, not something
this portal issues.

### How the CSP monitors inheritors (not MO systems)

**Source of truth for this distinction.** Specs and UI must not imply a CSP
sensor on every Mission Owner subscription.

The CSP does **not** monitor all MO systems from this portal. Workload and
customer-control evidence live on **each MO’s subscriptions** and in **that
MO’s package**. Attachment, allocation, or “they use our ATO” does not create
monitoring connectivity, complete visibility, or the right to pull those
subscriptions.

Three jobs stay separate:

| Job | Whose boundary | Where evidence lives | CSP portal |
|---|---|---|---|
| Offering ConMon | CSP offering | CSP-owned evidence, CSP POA&Ms, recorded decision dates | Yes — expiry, withdrawal, provider evidence age, release change |
| Scope fit (E6) | Recorded offering authorization | MO **declaration** + published CRM / IL / boundary | Yes — in scope / exception / out of scope. Not scans |
| MO system ConMon | Mission authorization | That system’s subs, logs, scans, customer-control evidence | **No.** MO workspace only |

How the CSP “watches all MOs” without sitting on their subs:

- Who is attached, on which **pinned release**
- Scope fit from **declared use** vs published authorized use
- Drifted adopters after a new inheritance release
- Shared-duty evidence **the MO chose to share** (summary or controlled reference), with age
- Offering-level signals that actually hit inheritors (withdrawn decision, stale platform AU evidence)

No share → **unavailable**, not “healthy, no findings.”

Customer-implemented duties (and the customer half of shared controls) stay on
the MO side unless they explicitly share a permitted summary. If the CSP
**operates** a landing zone, platform logs may show shared-service events.
That is still **offering/platform ConMon**. It must not be labeled MO
authorization evidence or cATO for every mission.

**Never imply MO-sub telemetry:** a provider allocation, hosting assignment,
or approved subscription MUST NOT be presented as a monitoring feed, scan
source, or complete evidence collection over the mission’s Azure (or other)
subscriptions.

### In bounds and valuable (keep)

These are real CSP pains, not theater. Priority order a CSP ISSM will pay for:

1. **Defensible inheritance package** — CRM + evidence summary + ATO cites, pinned as a release. The thing handed to the MO and the 3PAO. Publishing CRM ≠ publishing inheritance ≠ authorization.
2. **Who is on which release**, and drift after a change. Subscriber *count* is not risk.
3. **Out-of-scope use queued before the annual review** — declared use vs published authorized use. Rename “invalidating the ATO” to **out of authorized scope**.
4. **What-if at onboarding** — “does this declared use fit the published boundary/CRM/IL?” Not “may they have an ATO?”
5. **Offering ConMon + CSP POA&Ms tied to releases** — tell inheritors which release still exposes a weakness.
6. **Three facts, never one light** — validity vs package readiness vs monitoring health.
7. **Significant change → drifted adopters, no silent upgrade.**

Live SPIN is stronger at source → review → publish capabilities for the MO’s
documents (Feature 079). This overlay adds the CSP-selfish job: **protect the
recorded authorization from real tenant use.** Both jobs belong in one
workspace. Do not replace document lineage with a subscriber risk score.

### Scope-fit language (keep the screens, change the claim)

Fits / Exception / Invalidating is **not** a NIST control or a standard RMF
artifact. There is no official score for “this customer invalidates us.”

It **is** a legitimate ISSM question: *is this customer’s declared use still
inside the authorized scope of the offering we recorded?* That is boundary
management + CRM compliance + inheritance eligibility — not a new kind of ATO.

| UI / working label | RMF claim to make | Why |
|---|---|---|
| Fits | **In-scope declared use** | Eligible to inherit the pinned release |
| Exception | **CRM / boundary exception** | Still at IL, but extra path, overlay, or skipped shared duty. Residual risk; needs a human |
| Invalidating | **Out of authorized scope** | Use exceeds IL, data type, or boundary. The recorded decision no longer *covers that use*. The ATO is not automatically void |

**Sell:** “we show which consumers are still inside the authorization we already have.”  
**Do not sell:** “we score how each MO affects your ATO” — AOs will hear that the tool makes authorization decisions.

Non-negotiable: out-of-scope use does **not** auto-withdraw the parent ATO. The
AO withdrew it, it expired, or a significant change triggered reauth. Input is
**declared use + published authorized use**, both pinned to a version/release —
not the MO SSP, scans, or a peek at the mission package.

### Out of bounds (drop or never imply)

- Issuing, extending, or revoking the offering ATO from this portal
- Issuing the Mission Owner ATO
- Treating association, allocation, or a published capability as accepted inheritance
- Treating a subscription in the CSP tenant as accepted inheritance or as the MO ATO
- Treating the CSP Access requests queue as inheritance adoption
- MO requesting offering access from the CSP catalog instead of Provider hosting
- One green “ATO health” tile
- Silent CRM edit to make a bad customer “fit”
- Cloud telemetry as proof of continuous authorization / cATO
- **Implied MO-sub telemetry** — treating attached MO subscriptions as a CSP monitoring feed
- Broad read of MO narratives, SAR, or MO AO memos as “oversight”
- Using MO-package ATO counts as the CSP home (orgs authorized ≠ offering still authorized)
- Treating FedRAMP Moderate as a DoD IL authorization, or a FedRAMP date as a CMMC certificate
- One offering/ATO covering Fed/Civ, DoD, and CMMC customers without separate recorded decisions
- Treating IATT as ATO-lite, or as a decision that automatically becomes an ATO
- Treating Dev / Test / Prod allocations as IATT or ATO decisions
- Treating IATT work in Test or Prod as in-scope because Dev was too locked down

These are not “nice to have later.” They are how a compliance product fails an
interview.

### Verdict

| Question | Answer |
|---|---|
| In RMF bounds? | **Yes**, as offering RMF + leveraged-auth operations. **No**, as an ATO engine |
| Useful to a CSP? | **Yes** — CRM / inheritance / drift / out-of-scope queue is the gap PDFs do not fill |
| Provides value? | **High**, if recording vs issuing, attachment vs inheritance, and impact as *scope fit* not *authorization* stay intact |
| Cut? | Auto-withdraw, MO package body, one green light, impact without a pinned version, implied MO-sub telemetry, FedRAMP-as-DoD-ATO, CMMC badge from a FedRAMP date |

---

## Build without a second system

**Source of truth for engineering.** The live provider workspace is already
large. E2/E6 add product meaning; they must not add a parallel CSP app,
a second impact engine, or a forever-dual Overview.

Debt explodes if we **fork**. It shrinks if every new behavior is a
**projection or one additive record** on paths we already have, and each PR
**names what it reuses and what dual it will not create**.

### Rules

1. **Same workspace, same routes.** `/workspaces/csp` and existing offering
   pages. No `CspPortal`, no second Dashboard app, no duplicate nav chrome.
2. **Projection first.** E2 three facts are reads of recorded decisions,
   offering/package overview, and **offering** monitoring. No composite
   `AtoHealth` table.
3. **One new write model only where the old one cannot tell the truth.**
   E6 is an additive `ScopeFitRecord` on the existing relationship. Do not
   clone offerings, subscriptions, or impact reviews.
4. **Do not overload the wrong ledger.** Provider change-impact
   (`ProviderImpactService`) stays “offering change → missions.” Scope fit
   stays “declared use vs recorded authorization.” Mixing them is new debt.
5. **One write path per fact.** If Overview shows three facts, that is the
   CSP home. Do not also keep MO-package ATO counts on that page
   (`CspDashboardPage` may remain cross-org oversight elsewhere; it is not
   offering ConMon).
6. **Slice freeze.** This build is E2 + E6 only. CRM publish, inheritance
   publish, and what-if wait. Building them now duplicates 079 work.
7. **Retire or bound every dual.** FR-012: no indefinite dual editing. Each
   PR either overlays the live screen or records a named retirement issue.
   Do not leave “old impact” and “new scope fit” as two user-facing answers
   to the same question.
8. **TDD against the lie you are removing.** Fail on a single green ATO
   tile, on MO ATO counts on provider home, on `OutOfScope` mutating a
   decision, and on scope-fit stored as an impact disposition — then green
   the overlay.
9. **Keep paying the old tests.** Existing publication, adoption, isolation,
   and change-impact tests must still pass. If they fail, you coupled engines.

### Known duals (pay down, do not fork)

| Live today | Product truth | This slice |
|---|---|---|
| Provider Overview is a source-review desk | Three facts + scope-fit queue | Overlay `ProviderWorkspacePage`; do not add a second home |
| Unscoped CSP `/` can show MO ATO counts | Not offering posture | Do not use those counts on `/workspaces/csp` |
| Offering `impact` = change fan-out | Scope fit is a different question | New record + views; keep change-impact on Changes |
| Capabilities tab ≈ CRM | Versioned control allocation is later | **Do not** rebuild CRM in E2/E6 |
| Nested ConMon on Changes | Offering ConMon, not MO-sub telemetry | Reuse provider monitoring signals only |
| 079 provider mock vs home/impact mock | 079 mock remains required except this overlay | Do not replace 36 screens |

### PR shape

Each implementation PR (SF01–SF06) lists in the description:

- **Reuse:** types/services/routes touched  
- **Additive:** only `ScopeFitRecord` (E6) or none (E2)  
- **Did not create:** parallel portal, health enum, impact-disposition reuse,
  MO-sub telemetry  
- **Dual closed or bounded:** screen/path and owner if still open  
- **Tests:** new failing-then-green plus the existing impact/publication suite still green  

If a change needs a new service that is not a thin store around
`ScopeFitRecord`, stop and update this brief first.

---

## Implementation waves

**Source of truth for sequencing.** Two tracks run in parallel: **CSP overlay**
(this brief) and **Mission Owner package** (#1038). Do not merge them. Do not
block E2/E6 on CRM, what-if, or MO ConMon. Bugs stay on a fix-current stream.

GitHub: parents #1002, #1023, #1038. E2/E6 child is preview-only
(`SCOPEFIT_ISSUE` under #1023). No issue numbers invented.

### Track C — CSP / provider (this brief)

| Wave | Goal | Issues / tasks | Done when |
|---|---|---|---|
| **C0** | Spec only | Brief + 079 US7/US8, FR-014–FR-024, SF01–SF06 | Failing tests exist; user can review locally. No production code yet |
| **C1** | E2 three facts | SF01–SF03 on `/workspaces/csp` + offering overview. Overlay #1002 / #1025 home, do not replace nav | Validity, readiness, health disagree when they should. No MO ATO counts on CSP home. No composite ATO-health tile |
| **C2** | E6 scope-fit record | SF04–SF06. Distinct from #1028. **Signed off 2026-10-09** (`9c26c2e5`, not pushed) | Each approved relationship is in scope / exception / out of scope / unevaluated. `OutOfScope` does not withdraw the recorded authorization. No MO SSP body. Path/lane fields wait for C6 |
| **C3** | 079 provider mock UI (existing children) | #1025 nav, #1026 catalog, #1027 authoring, #1028 review/publish + **change** impact | Publication still pinned; adopters not silently upgraded. #1028 is offering-change fan-out, not C2 |
| **C4** | Inheritor inventory | #1029, #1030, #1031, #1032 | Org list/detail is inventory + **offering access**, not a monitoring feed (FR-023). Access ≠ allocate ≠ adopt. Not IATT/ATO |
| **C5** | Inheritance / CRM (later) | #957, #1021, #1033–#1035, #1037, #1019 | Versioned CRM + pinned inheritance release. Test release ≠ prod release. Not in C1–C2 |
| **C6** | What-if attach (later) | No issue yet; after C2. Pairs with **M3**. **Mock signed off 2026-10-09** | MO **Provider hosting** originates the request. Declare includes **IATT vs ATO** and **Dev/Test/Prod**. CSP Access requests → estimate → approve/refuse. Approve is access only — not Azure allocate, not adopt, not an issued IATT/ATO. IATT+Test = exception; IATT+Prod = out of scope |
| **C7** | Declared-use correction (later) | No issue yet; after C6 | Per-MO impact **Request correction** lands on MO Provider hosting. Access stays. Scope fit stays until resubmit + re-score. Not Administration, not the MO SSP |
| **C8** | Three yeses on live IA (later) | No issue yet; after C4+C6. Mock-first | Live subscribers / impact copy matches the mock: offering access ≠ Azure hosting ≠ pinned inheritance. A sub in the CSP tenant is not an ATO |

### Track M — Mission Owner / system package (#1038)

| Wave | Goal | Issues | Done when |
|---|---|---|---|
| **M1** | Truthful package / Overview | #1039, #1041, #1042, #1043, #1046 (+ #1055–#1059 as children) | Initial package without fabricating an AO decision; unknown ≠ pass |
| **M2** | MO system ConMon | #1044, #1045, #1058 | Rules scoped to **that system’s** boundary. Not CSP telemetry of all inheritors |
| **M3** | Provider hosting walk (later) | No issue yet; pairs with **C6**. **Mock signed off 2026-10-09** | System definition extra tab: Request → declare use (IATT/ATO + Dev/Test/Prod) → wait on CSP what-if → allocate lane → associate. Opening the tab creates no inheritance. Standalone remains valid |
| **M4** | IATT vs ATO on the system (later) | No issue yet; after M1/M3 | Record IATT and ATO as **separate** facts. IATT evidence may feed the ATO package; the IATT decision does not become the ATO. Dev/Test/Prod stay hosting |

#1023 stays the feature parent for **both** CSP-backed and standalone ATO.
Standalone work continues in M1 even if C2 is unfinished. Attachment still
does not imply inheritance.

Design mocks already walk C6–C8 and M3–M4
([csp-home-impact-mock](csp-home-impact-mock/index.html),
[mo-org-portal-mock](mo-org-portal-mock/index.html)). **Mock-first:** do not
change live IA for those waves until the mock is signed off. C1/C2 live
overlay stays the only authorized production slice.

### Fix-current (not a product wave)

Security, auth, and package bugs (#842, #994, #941, #989, #977, …) and
environment/QA bugs (#587, #635, …) are **not** sequenced behind C1–C8.
Do not close them as superseded by this brief.

### Coordination rules

- One wave in flight per track (C and M can run together).
- C1/C2 PRs follow **Build without a second system**.
- C3 must not encode C2 as impact-review dispositions.
- M2 must not encode CSP offering ConMon as MO-sub telemetry.
- Next GitHub child (`SCOPEFIT_ISSUE`) is C1+C2 only. **C2 is signed off** (2026-10-09). Preview that issue before posting. C6–C8 / M3–M4 issues may be drafted after C6 mock sign-off; do not post unresolved tokens.
- C6 and M3 share **one** access-request record. Origin is MO Provider hosting, not the CSP catalog.
- C7 does not revoke access or auto-flip scope-fit.
- C6 scores IATT vs ATO and Dev/Test/Prod; it does not issue either authorization.
- C8 is copy/IA on live subscribers and impact after the mock is approved.

---

## Screen map (brief → live routes)

Provider nav today ([ProviderNavigation.tsx](../../src/Ato.Copilot.Dashboard/src/features/provider-workspace/ProviderNavigation.tsx)):
**Overview · Offerings · Mission systems · Changes · Administration**.

Offering tabs today ([OfferingSectionNavigation.tsx](../../src/Ato.Copilot.Dashboard/src/features/provider-authorizations/OfferingSectionNavigation.tsx)):
**Overview · Authorizations & sources · Services & scope · Capabilities &
responsibilities · Evidence & findings**.

| Brief screen | Follow on this route | Primary code |
|---|---|---|
| Tenant home | `/workspaces/csp` | [ProviderWorkspacePage.tsx](../../src/Ato.Copilot.Dashboard/src/features/provider-workspace/ProviderWorkspacePage.tsx) |
| Offering catalog | `/workspaces/csp/authorizations` | [OfferingList.tsx](../../src/Ato.Copilot.Dashboard/src/features/provider-authorizations/OfferingList.tsx) |
| Offering overview | `/workspaces/csp/authorizations/offerings/{id}` | [OfferingOverview.tsx](../../src/Ato.Copilot.Dashboard/src/features/provider-authorizations/OfferingOverview.tsx) |
| Shared responsibility (CRM) | Offering tab **Capabilities & responsibilities** | [OfferingCapabilities.tsx](../../src/Ato.Copilot.Dashboard/src/features/provider-authorizations/OfferingCapabilities.tsx) |
| Offering ATO | Offering tab **Authorizations & sources** | [AuthorizationsPage.tsx](../../src/Ato.Copilot.Dashboard/src/features/provider-authorizations/AuthorizationsPage.tsx) |
| Inheritance packages | Published capability releases on the offering | Security Capabilities + package import pipeline |
| MO subscribers | `/workspaces/csp/systems` | `ProviderWorkspacePage` missions view |
| Tenant / per-MO impact | Offering **impact** + `/workspaces/csp/provider-changes` | [ProviderImpactService](../../src/Ato.Copilot.Core/Services/ProviderAuthorizations/ProviderImpactService.cs), [ProviderChangesPage](../../src/Ato.Copilot.Dashboard/src/features/provider-workspace/ProviderChangesPage.tsx) |
| What-if attach | **Missing as a named flow** — closest is mission association / allocation | [MissionAssociationWizard](../../src/Ato.Copilot.Dashboard/src/features/provider-relationships/MissionAssociationWizard.tsx) |
| ConMon | `/workspaces/csp/provider-changes` and offering monitoring | [ProviderMonitoringService](../../src/Ato.Copilot.Core/Services/ProviderAuthorizations/ProviderMonitoringService.cs) |
| CSP POA&Ms | Offering **Evidence & findings** | [ProviderFindingService](../../src/Ato.Copilot.Core/Services/ProviderAuthorizations/ProviderFindingService.cs) |
| Significant changes | `/workspaces/csp/provider-changes` | Provider impact / monitoring |
| Tenant access | `/workspaces/csp/provider-administration` | [ProviderAdministration.tsx](../../src/Ato.Copilot.Dashboard/src/features/provider-authorizations/ProviderAdministration.tsx) |

Mission Owner work stays under `/workspaces/organizations/{tenantId}/systems/...`.
Do not pull SSP/assessment bodies into the provider workspace.

---

## Epic map

MVP in the brief: catalog + parent ATO facts + CRM publication + subscriber
inventory + per-MO impact.

| ID | Brief epic | Follow as | Current reading (code + Sept 26 validation, not a new full audit) |
|---|---|---|---|
| E1 | Offering catalog | Existing offerings | Live. Versioned product identity exists; keep offerings distinct. |
| E2 | Offering ATO as three facts | Recorded decisions vs readiness vs monitoring | Partial. External decisions are recorded; do not treat that as issuing an ATO. Validity / readiness / health still collapse in places. |
| E3 | Shared responsibility | Capability responsibilities | Partial. Publication and customer duties exist; CRM must stay pinned to a release. |
| E4 | Inheritance publication | Canonical capability releases | Partial. Exact-set releases exist; drift vs what a mission adopted must stay visible. |
| E5 | MO access | Offering access from MO Provider hosting; not a second invite product | Partial. Association/allocation exist. Approve access ≠ allocate Azure ≠ adopt. |
| E6 | MO impact on parent ATO | **Primary overlay** | Partial. Impact and mission lists exist; Sept 26 notes impact is still largely a dependency view, not residual-risk scoring. |
| E7 | What-if attach | CSP side of offering access (declared use vs recorded authorization) | Missing as a pre-approval estimate. Not a sub grant. |
| E8 | Significant change orchestration | Provider changes | Partial. Downstream missions can be listed; reauth vs notify-only is not an automatic decision. |
| E9 | CSP POA&M + ConMon | Findings + monitoring | Partial. Provider findings exist; offering ConMon is not cloud telemetry. |
| E10 | Tenant access & audit | Administration + `/audit` | Partial. Provider admin is limited; do not grant MOs CSP.Admin. |
| E11 | Export packages | Mission export using reviewed provider facts | Partial. Export integrity is a known 079/issue #1040 concern. |

**E6 is the overlay that is not already the mock’s headline.** The Sept 26
validation says the provider workspace’s strongest purpose is reusable records
for Mission Owner documentation. This brief adds: **score consumption of the
parent authorization**. Keep both. Do not replace document lineage with a
subscriber count.

---

## Conflicts to decide before coding

| Topic | This brief | Current SPIN | Until decided |
|---|---|---|---|
| Primary nav | Home, catalog, ATO, impact, ConMon, access as peer areas | Overview, Offerings, Mission systems, Changes, Administration | **Change live nav toward the IA**; update Feature 079 mock in the same change |
| “Parent ATO management” | Operate the offering authorization | Record source-backed decisions; SPIN does not issue them | Record, don’t issue — but **show the three facts** on home and offering ATO |
| What-if attach | Gate approval on estimated ATO impact | Association/allocation without a named what-if | **Add** as a named step before assign/associate |
| MO package visibility | Impact record only | Relationship/allocation/release; no general MO SSP read | Keep the restriction |
| Label | CSP portal | Provider workspace (CSP is one kind of provider) | Keep “provider workspace” in product UI |

---

## First follow sequence (no new portal)

1. Walk `/workspaces/csp` → Offerings → one offering’s five tabs → Mission
   systems → Changes → Administration with the IA open.
2. For each MVP epic (E1–E6, E9–E10), write one sentence: *done / partial /
   missing* against the live screen, with a file citation.
3. Treat **E6 impact scoring** and **E2 three-fact ATO display** as the first
   product gaps if the walk confirms them.
4. Only then open Feature 079 `spec.md` / `tasks.md` (or a new feature if 079
   cannot absorb it). Preview GitHub issue writes; do not push without
   approval.

Walk recorded 2026-10-09 against dashboard/provider services in this worktree
(not a signed-in runtime session). See **IA walk — what needs to change**.

---

## IA walk — what needs to change

Evidence: live routes under
[ApplicationRoutes.tsx](../../src/Ato.Copilot.Dashboard/src/ApplicationRoutes.tsx),
[ProviderNavigation.tsx](../../src/Ato.Copilot.Dashboard/src/features/provider-workspace/ProviderNavigation.tsx),
[ProviderWorkspacePage.tsx](../../src/Ato.Copilot.Dashboard/src/features/provider-workspace/ProviderWorkspacePage.tsx),
[AuthorizationsPage.tsx](../../src/Ato.Copilot.Dashboard/src/features/provider-authorizations/AuthorizationsPage.tsx).
This is a source walk, not a clicked authenticated session.

### Keep (do not rip out)

- Two workspaces. Do not build a second CSP app.
- Recorded external decisions. SPIN still does not issue the offering ATO.
- No general read of the Mission Owner SSP from the provider workspace.
- Source-package import → review → capability publication pipeline.
- Distinct states: offering access ≠ Azure allocation ≠ association ≠ capability adoption.

### Change the information architecture

Live primary nav is Overview · Offerings · Mission systems · Changes ·
Administration. The IA needs **Impact** and **ConMon** as first-class work,
and **ATO / inheritance** as offering work, not buried under “add source
material.”

Proposed live nav to match the IA:

1. **Overview** — tenant posture (today’s Overview is a source-review desk)
2. **Offerings** — catalog (already the right place)
3. **Mission systems** — subscribers (rename job, not just label)
4. **Impact** — split out of Changes; reverse the question
5. **Changes / monitoring** — keep as Sustain
6. **Administration** — keep; finish the roster

Offering tabs today: Overview · Authorizations & sources · Services & scope ·
Capabilities & responsibilities · Evidence & findings.

Add or retarget tabs so a user can open **Offering ATO**, **CRM /
inheritance release**, and **subscribers** without a `?task=` query.

### Screen-by-screen

| IA screen | Live today | Change |
|---|---|---|
| Tenant home | `/workspaces/csp` is “maintain services and evidence.” Metrics on an offering are capabilities / associated systems / source docs / open findings. Unscoped CSP `/` is org-count + MO ATO rollup ([CspDashboardPage](../../src/Ato.Copilot.Dashboard/src/features/csp-dashboard/CspDashboardPage.tsx)). | Rebuild Overview as tenant posture: offering publication, **parent ATO dates**, MO count **by impact severity**, open CSP findings, stale evidence. Actions: offerings, tenant impact, ConMon queue. Stop using MO-package ATO counts as the provider home. |
| Offering catalog | `/authorizations` lists name, service model, associated-system count, published-capability status, lifecycle as a footnote. | Show version, impact level, publication state, **highest consumer impact**, **ATO standing of the published version**. Create offering stays. |
| Offering overview | Source → scope → capability publish checklist. Primary action is Review publication. | Product page: boundary, data types, impact level, current vs previous version, pending access. Actions: CRM, offering ATO, subscribers. Keep the publish checklist as a nested “release readiness” block, not the whole page. |
| Shared responsibility | Capabilities tab: implementations + provider/customer split per capability. | Control-family CRM: every in-scope control is inherited / shared / customer, **pinned to a version**, with unassigned gaps. Publishing a CRM version is distinct from publishing a capability. |
| Offering ATO | Authorizations & sources: recorded external decisions + source packages. Copy already says recording ≠ issuing. | Keep recording. Add the **three facts** on one screen: validity (effective/expired/withdrawn/conditional), package readiness, monitoring health. Do not collapse them into “metadata review state.” |
| Inheritance packages | Published capability releases; adopters live on Mission systems. | Named **inheritance release** (CRM + evidence summary + ATO cites). List which MOs adopted which release. Show **drift** when current ≠ adopted. Never silent-upgrade. |
| MO subscribers | Mission systems: allocation, association, adopted release. Action is Assign service scope. | Access lifecycle: requested / approved / revoked. **Access requests originate on MO Provider hosting**, not this catalog. Approve is not allocate Azure and not adopt. Keep allocation as a later step. |
| Tenant impact | Changes + offering `impact` = **provider change → affected missions**. ImpactPanel says it is not a coverage-change proof. | **Reverse the question.** Aggregate how MO use stretches the parent ATO (boundary creep, overlay stacking, CRM exceptions, invalidating use). Dependency fan-out stays as a different screen. |
| Per-MO impact | Relationship detail: scope snapshot, association, adoption, Systems handoff preview. | Declared use vs authorized use, inherited controls relied on, exceptions, severity Fits / Exception / Invalidating. Still no MO SSP body. |
| What-if attach | Missing. Closest is Assign service scope / association wizard. | CSP side of offering access (step 4). Estimate whether **declared use** fits the recorded authorization. Can refuse. Not a sub grant and not inheritance. |
| Continuous monitoring | Nested tab on Changes: expiry, withdrawal, evidence freshness, published-release change. | First-class Sustain view for the **parent** ATO. Keep those signals. Do not pretend this is cloud telemetry. |
| CSP POA&Ms | Evidence & findings: provider findings + evidence library. | Treat as offering-owned POA&M. Show which inheritance releases still expose the weakness. Notify affected MOs when inherited posture changes. |
| Significant changes | Change queue dispositions: AcceptForPublication / RequestChanges / Reject. | Classify **reauth vs delta assessment vs notify-only**. List downstream MOs on the old inheritance release. |
| Tenant access | Administration: signed-in admin only (“not a complete roster”), Entra lookup, org enrollment. | Provider-team roster with grant/revoke. MO offering-access request queue. Keep lookup ≠ grant. |

### Build order if the IA is the target

1. **E2** — three-fact offering ATO on home + offering ATO page (display only; still do not issue).
2. **E6** — `ImpactRecord` (Fits / Exception / Invalidating) on each subscription; tenant roll-up. This is the IA’s reason to exist.
3. **E4** — inheritance release + drift column on subscribers.
4. **E5 + E7** — MO Provider hosting request → CSP what-if → approve, **then** allocate Azure, **then** MO adopt. Do not collapse.
5. **E3** — CRM as versioned control allocation, feeding the inheritance release.
6. Nav/tab relabel so those paths are reachable without `?task=` / nested Changes tabs.

Do not implement until Feature 079 (or a follow-on feature) is updated to
absorb this. Preview GitHub issue writes; do not push without approval.

---

## Home and impact sketches

Interactive UI mock: [csp-home-impact-mock/index.html](csp-home-impact-mock/index.html)
(filters, queue, dialogs, empty/unavailable/restricted). Static sketches also
remain on the [clickable IA](csp-portal-product-brief.html). Synthetic specimen only.

**Tenant home** answers: does the parent ATO still match real tenant use?

- Four metrics: offerings, earliest parent ATO expiry, MOs by Fits / Exception /
  Invalidating, items that need a human.
- Three-fact strip: ATO validity · package readiness · monitoring health.
  Never one green light.
- Queue: invalidating use, stale evidence, drifted inheritance.
- Offerings table includes **worst impact**, not MO-package ATO counts.

**Tenant impact** answers: how does attached MO use stretch this offering’s
parent ATO?

- Filter is offering + version + inheritance release.
- Drivers: data/IL, connections, overlays, CRM exceptions.
- Rows are impact records. Provider-change fan-out stays on Significant changes.
- ISSM strip: invalidating use does not auto-withdraw the ATO.

**MO subscribers / Access requests** is the CSP inbox for **offering access**.
The MO started that request on **Provider hosting**. Approve here is not
allocate Azure and not adopt. `551st ISRW / ISR Ground Station` in the mock
is this queue only — it is not an MO seed system.

**Per-MO impact** is one `ImpactRecord`.

- Side-by-side declared vs authorized use, both pinned to a version/release.
- Why-invalidating checks cite offering fields, not the MO SSP.
- Stores declaration + severity; does not store MO narratives, SAR, or AO memo.
- **Request correction** goes to that system’s **Provider hosting** tab in the MO/Org workspace (declared use only). It does not go to CSP Administration, email, or the MO SSP. Access stays. Scope fit stays until the MO resubmits declared use and the CSP re-scores.
