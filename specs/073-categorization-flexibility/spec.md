# Spec 073 — Categorization Flexibility & Service-Branch Overlays

**Epic:** #62 — Feature 053: Categorization Flexibility
**GitHub Issue:** #62
**Wave:** 9 — Core UX & Integrations
**Status:** Draft
**Branch:** `073-categorization-flexibility`

---

## Background

ATO Copilot supports the FIPS 199 + NIST SP 800-60 categorization model: each registered system
carries one `SecurityCategorization` with per-information-type Confidentiality / Integrity /
Availability (CIA) impact values, and an aggregate `ImpactLevel` that drives NIST baseline
selection (see spec 018-sap-generation). This covers the minimum RMF/FISMA requirement.

DoD operational reality is substantially more complex. The Army RMF Knowledge Service (Army RMF
KS), SECNAV/DON RMF, Air Force RMF Process Guide, USMC, USSF, and DoDI 8510.01 / 8500.01 each
publish **service-specific categorization overlays** that:

- **Add dimensions** beyond CIA (Privacy, Safety, Mission Assurance Category, Confidentiality
  Level, Operational Tempo)
- **Override SP 800-60 provisional impact levels** for service-specific information types
- **Change the high-water-mark aggregation logic** (MAC-driven rules, threshold conditions)
- **Produce SPIN-compatible authorization package output** tailored to each service's requirements

Today, an ISSO must hand-edit the categorization to match their service overlay, the edits are
invisible to the AO, and the methodology cannot be derived from the SSP package. This feature
delivers **first-class service-branch categorization overlays** that compose on top of the FIPS
199 base, producing categorization output that is both **correct** and **fully traceable** for
any DoD service or command.

### Relationship to Existing Features

| Feature | Relationship |
|---------|-------------|
| 018-sap-generation | Consumes `ImpactLevel` for baseline selection — overlay-driven IL values feed directly in |
| 024-narrative-governance | Overlay publish/approve workflow uses governance chain |
| 035-deviation-management | Overlay upgrade under an active ATO triggers `SignificantChange` event |
| 048-tenant-isolation | CSP-published global overlays are consumed read-only per tenant-isolation rules |
| 022-ssp-full-oscal / 037-ssp-document-export | SSP §10 rationale auto-generated from overlay trace |

---

## Verified Source Facts

The following facts were verified against actual source code before authoring this spec.

**`SecurityCategorization`** (`TenantScoped`, `RmfModels.cs` ~line 248):
- `Id: string` (GUID, `[Key][MaxLength(36)]`) PK
- `TenantId: Guid`
- `RegisteredSystemId: string` FK → `RegisteredSystem`
- `IsNationalSecuritySystem: bool`
- `Justification: string?` `[MaxLength(4000)]`
- `CategorizedBy: string` `[Required][MaxLength(200)]`
- `CategorizedAt: DateTime`
- `ModifiedAt: DateTime?`
- Navigation: `RegisteredSystem`, `ICollection<InformationType>`
- Computed (NotMapped): `ConfidentialityImpact`, `IntegrityImpact`, `AvailabilityImpact`,
  `OverallCategorization`, `DoDImpactLevel`, `NistBaseline`, `FormalNotation`

**`InformationType`** (`GlobalReference`, `RmfModels.cs` ~line 348):
- `Id: string` PK, `SecurityCategorizationId: string` FK
- `Sp80060Id: string`, `Name: string`, `Category: string?`
- `ConfidentialityImpact: ImpactValue`, `IntegrityImpact: ImpactValue`, `AvailabilityImpact: ImpactValue`
- `UsesProvisionalImpactLevels: bool`, `AdjustmentJustification: string?`
- Navigation: `SecurityCategorization`

**`OverlayDocument`** (`OverlayDocument.cs`): existing entity for **control-level** overlays
(CNSSI-1253, DoD-8140, SECNAVINST). This feature introduces a **separate** entity
`CategorizationOverlay` for **categorization-level** overlays. Do not confuse the two.

**`CategorizeSystemTool`** (`CategorizationTools.cs`, tool name `compliance_categorize_system`):
- Accepts `system_id`, `information_types[]`, `is_national_security_system`, `justification`
- Calls `ICategorizationService`
- Returns computed `ImpactLevel`, `NistBaseline`, `FormalNotation`

**`ImpactLevel` record** (`ImpactLevelModels.cs`): DoD IL record type (IL2–IL6, FedRAMP),
separate from the `ImpactValue` enum (`Low=0, Moderate=1, High=2`) used on C/I/A values.

---

## Functional Requirements

### FR-001 — Overlay Catalog (US1)
The system exposes a curated catalog of service-branch overlays (built-in) that an ISSO can
apply to a system's categorization. Built-in overlays include:
`army-rmf-v3`, `navy-secnav-v2`, `af-rmf-v4`, `usmc-rmf-v1`, `ussf-rmf-v1`,
`dodi-8510-mac-cl`, `dod-privacy-overlay`, `classified-information-overlay`,
`cross-domain-solution-overlay`, `space-platform-overlay`.

### FR-002 — SP 800-60 Override Per Information Type (US3)
A `CategorizationOverlay` declares per-information-type overrides that replace the SP 800-60
provisional impact values. Overrides must only **raise** impacts, never lower them below the
SP 800-60 floor (`OVERLAY_RULE_CANNOT_LOWER_FIPS199`).

### FR-003 — Custom Dimensions (US6)
An overlay may declare additional impact dimensions beyond CIA (e.g., `Privacy`, `Safety`,
`MAC`, `CL`). Custom dimensions are rated `Low | Moderate | High` or a custom enumeration
declared by the overlay. Custom dimension answers are stored in
`SecurityCategorization.CustomDimensions` (JSON column); standard CIA columns are unchanged.

### FR-004 — Custom Aggregation Rule (US7)
An overlay may override the default FIPS 199 high-water-mark aggregation rule with a
declarative rule (threshold conditions, tabular lookups, `max()` expressions). Rules are
declarative — no scripting. Rules may only **raise** `ImpactLevel`; rules that would lower
the FIPS 199 floor fail validation (`OVERLAY_RULE_CANNOT_LOWER_FIPS199`).

### FR-005 — Overlay Stacking (US8)
A single system may apply up to 5 overlays simultaneously. Conflicts:
- Dimension value conflicts: highest value wins
- Aggregation rule conflicts: highest-priority overlay's rule wins; suppressed rules recorded

### FR-006 — Org-Authored Custom Overlays (US2)
An `OrgAdministrator` can author custom overlays in a structured editor (Decision Questions,
Dimension Overrides, Aggregation Rule). Custom overlays follow a `Draft → InReview → Approved`
governance flow (Feature 024) before appearing in the system-level picker.

### FR-007 — Overlay Versioning (US9)
Overlays are version-pinned per system. When a catalog overlay is updated (e.g., army-rmf v3.3
supersedes v3.2), systems pinned to v3.2 are unaffected until the ISSO explicitly upgrades.
An upgrade notice appears on the system page; upgrading requires re-attestation.

### FR-008 — Categorization Audit Trail (US10)
Every change to a system's categorization (CIA values, overlay application, overlay upgrade,
custom dimension answers) creates a `CategorizationAuditEntry` row. The AO and SCA can drill
from `ImpactLevel` to the full methodology trace.

### FR-009 — AO Notification on Impact Level Change (US4)
When `ImpactLevel` changes (overlay applied, upgraded, or removed), and the system is in
`RmfPhase.Authorize` or `Monitor`, a `SignificantChange` event is raised (Feature 035). The
assigned AO receives notification.

### FR-010 — Overlay Inheritance (US5)
Overlays published at the parent org level are automatically inherited by all child systems
in that org unless explicitly waived. CSP-Admin global overlays are inherited by all tenants
(read-only, per Feature 048 rules).

### FR-011 — FIPS 199 Base Locked
Overlays cannot remove FIPS 199 C/I/A questions. The editor returns
`400 OVERLAY_FIPS199_BASE_LOCKED` if an overlay attempts to suppress a base question.

### FR-012 — DoD Gate
DoD-specific built-in overlays (`dodi-8510-mac-cl`, `classified-information-overlay`, etc.)
are gated behind `Categorization:Overlays:DodEnabled` tenant config. Non-DoD deployments see
these overlays listed but marked `DoD-only`; applying them requires the flag set to `true`.

### FR-013 — Audit Trace Read for AO/SCA
Any user with at least `Analyst` role can view the full categorization trace. Mutation actions
(apply overlay, upgrade, author overlay) require `ISSO` or `ISSM` or `OrgAdministrator`.

### FR-014 — SSP Export Integration
When the SSP §10 Categorization section exports, it auto-generates the categorization rationale
from the overlay trace (overlay name, version, key overrides) per Feature 022 / 037.

### FR-015 — Re-Attestation on Overlay Upgrade
Overlay upgrades that change `ImpactLevel` require re-attestation through the Feature 024
governance workflow. Upgrades that do not change `ImpactLevel` may be applied with a single
ISSO approval and a `ChangedBy` + `Justification` audit entry.

---

## Non-Functional Requirements

| ID | Requirement |
|----|-------------|
| NFR-001 | `GET /api/categorization-overlays` p95 < 200 ms under 100 RPS |
| NFR-002 | Overlay resolution (apply + compute ImpactLevel) < 50 ms per system |
| NFR-003 | Audit entries append-only; no row may be deleted or updated |
| NFR-004 | `CategorizationOverlay` JSON columns (SP800-60 overrides, aggregation rule, custom dimensions) validated at write time — malformed JSON returns 400 |
| NFR-005 | All overlay mutations write to the existing `AuditLogEntry` table (Feature 011 pattern) in addition to `CategorizationAuditEntry` |
| NFR-006 | Built-in overlay data files ship under `data/overlays/` and are loaded at startup; no runtime HTTP calls to external sources |

---

## User Stories

### US1 — Apply a Built-in Service Overlay (P1)
**As an ISSO**, I want to apply organization-specific categorization overlays so that SPIN aligns
with how my service or command defines system risk.

**Acceptance Criteria:**
1. Overlay picker lists catalog overlays grouped by `Service | Mission | Privacy | Classification | Other`
2. Picking "Army RMF Knowledge Service" injects Army-specific MAC and Mission Impact questions
3. After save, `SecurityCategorization.OverlayIds` contains the pinned version ID (`army-rmf:v3.2`)
4. FIPS 199 base C/I/A values are preserved; never replaced
5. DoD-only overlays are gated by tenant config flag

### US2 — Author a Custom Organization Overlay (P1)
**As an OrgAdministrator**, I want to create a custom overlay for my command so that every
system in the org can use it alongside built-in overlays.

**Acceptance Criteria:**
1. Editor offers: Metadata, Decision Questions, Dimension Overrides, Aggregation Rule sections
2. Publish flow: `Draft → InReview → Approved` (Feature 024)
3. Removing FIPS 199 base questions returns `400 OVERLAY_FIPS199_BASE_LOCKED`
4. Editing an Approved overlay creates a new Draft version; pinned systems unaffected

### US3 — Override SP 800-60 Provisional Impacts (P1)
**As an ISSO**, I want the overlay to raise provisional SP 800-60 impact values for specific
information types so the categorization reflects my service's policy.

**Acceptance Criteria:**
1. Wizard shows SP 800-60 default (gray) alongside overlay-modified value (blue) with citation
2. Override references SP 800-60 info type ID (e.g., `C.3.5.1`)
3. Override may only raise impacts; lowering returns `400 OVERLAY_RULE_CANNOT_LOWER_FIPS199`

### US4 — AO Notification on Impact Level Change (P1)
**As an AO**, I want to be notified when a system's `ImpactLevel` changes under an approved
ATO so I can evaluate the impact on the authorization decision.

**Acceptance Criteria:**
1. `SignificantChange` event raised when IL changes during `Authorize` or `Monitor` phase
2. AO receives notification with old IL, new IL, and overlay that caused the change
3. Change is visible in the Feature 005 Compliance Watch dashboard

### US5 — Overlay Inheritance (P2)
**As an OrgAdministrator**, I want overlays published at my org level to be automatically
available to all systems in the org without requiring each ISSO to manually select them.

**Acceptance Criteria:**
1. Org-level overlays appear in the system picker tagged `Org-Default`
2. ISSO can waive an inherited overlay per system with ISSM approval and justification
3. CSP-Admin global overlays inherited by all tenants read-only

### US6 — Custom Dimensions Beyond CIA (P2)
**As an ISSO**, I want the overlay to add dimensions like Privacy or Safety so I can capture
the full impact picture required by my service's risk methodology.

**Acceptance Criteria:**
1. Custom dimension questions appear in the wizard alongside C/I/A
2. Custom dimension answers stored in `SecurityCategorization.CustomDimensions` (JSON)
3. Removing an overlay marks custom dimension answers `Effective=false` — not deleted

### US7 — Custom Aggregation Rule (P2)
**As an ISSO**, I want the overlay to declare how `ImpactLevel` is computed from all
dimensions so that MAC-driven or threshold-driven services get the right IL automatically.

**Acceptance Criteria:**
1. Default rule (`FipsHighWaterMark`) used when no overlay rule is present
2. Overlay rule replaces default for that system; previous IL recorded in audit
3. Rule referencing undeclared dimension returns `400 OVERLAY_RULE_REFERENCES_UNKNOWN_DIMENSION`
4. Rule conflict on stack: highest-priority overlay's rule wins; suppressed rule recorded

### US8 — Stack Multiple Overlays (P2)
**As an ISSO**, I want to apply multiple overlays simultaneously (e.g., AF RMF + DoD Privacy)
so my system's categorization satisfies multiple policy frameworks at once.

**Acceptance Criteria:**
1. Up to 5 overlays stackable; > 5 triggers soft warning (non-blocking)
2. Methodology panel shows each overlay's contribution per dimension
3. Value conflicts resolved by `max()`; aggregation rule conflicts by priority order

### US9 — Overlay Versioning & Re-Categorization on Upgrade (P2)
**As an ISSO**, I want to be notified when my service updates their overlay so I can explicitly
review and apply the upgrade rather than have it silently change my system.

**Acceptance Criteria:**
1. Systems pinned to old version unaffected when new catalog version publishes
2. Upgrade notice with `Preview changes` diff (added questions, modified overrides, predicted IL)
3. Applying upgrade requires re-attestation (Feature 024 governance)
4. Deprecated/retired overlays show red warning; saves blocked until replaced or waived

### US10 — Audit Trace and Read-Only Drilldown (P1)
**As an AO**, I want to drill from `ImpactLevel` to every overlay that contributed so I can
make an authorization decision with full methodology visibility.

**Acceptance Criteria:**
1. Clicking `ImpactLevel` opens a panel showing: aggregation rule, source overlay/version, each
   dimension's resolution chain
2. SSP §10 auto-generates rationale text from trace — no hand-authored prose required for bare min
3. Even if an overlay is retired from catalog, historical trace renders overlay name + version +
   `RetiredFromCatalog` marker
4. Read-only roles see full trace; mutation actions hidden

---

## Out of Scope

- Replacing FIPS 199 entirely — overlays augment, never remove C/I/A base
- Lowering the FIPS 199 high-water-mark floor via overlay rules
- Scriptable / Turing-complete aggregation rules (declarative only)
- AI-assisted overlay authoring from policy PDFs (future enhancement)
- Per-control overlays (covered by 018-sap-generation, 044-org-control-inheritance)
- Civilian overlays (FedRAMP, FISMA-only, NIST CSF profiles) — DoD-first; plumbing is generic
- Cross-tenant overlay editing (tenants consume CSP global overlays read-only)
- Overlay-driven control-enhancement selection (that remains in 018-sap-generation)

---

## Definition of Done

- [ ] `CategorizationOverlay` entity + migration applied and passing `dotnet build`
- [ ] `CategorizationAuditEntry` entity + migration applied
- [ ] `SecurityCategorization` extended with `OverlayIds`, `CustomDimensions`, `LastModifiedBy`
- [ ] `GET /api/categorization-overlays` returns catalog with correct DoD gate
- [ ] `POST /api/systems/{id}/categorization/overlays` applies overlay and computes IL
- [ ] `DELETE /api/systems/{id}/categorization/overlays/{overlayId}` removes overlay
- [ ] `GET /api/systems/{id}/categorization/audit` returns ordered audit trail
- [ ] MCP tools `compliance_apply_categorization_overlay` and `compliance_get_categorization_audit` implemented
- [ ] AO notification raised when IL changes during Authorize/Monitor phase
- [ ] SSP §10 export includes overlay trace rationale
- [ ] All unit + integration tests pass (`dotnet test`)
- [ ] Spec checklist in `checklists/requirements.md` verified complete
