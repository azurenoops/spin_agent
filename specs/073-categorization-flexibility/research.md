# Research — 071: Categorization Flexibility & Service-Branch Overlays

Decision records for architectural choices in Issue #62 / Feature 053.

---

## R1: Separate `CategorizationOverlay` Entity vs. Reusing `OverlayDocument`

**Question:** Can this feature reuse the existing `OverlayDocument` entity (`OverlayDocument.cs`)?

**Answer: No. A separate entity is required.**

### Evidence

The existing `OverlayDocument` entity:
1. Is designed for **control-level** overlays (CNSSI-1253, DoD-8140, SECNAVINST) — each row
   maps to a single NIST control ID (`ControlId: string [Required, MaxLength(32)]`).
2. Contains only `Content: string` (markdown/plain text) — no structured JSON for SP 800-60
   overrides, custom dimensions, or aggregation rules.
3. Has no `ServiceBranch`, `Scope`, `Status`, `VersionId`, or `WizardQuestions` concepts.
4. Is a `GlobalReference` entity, not suitable for tenant-specific org-authored overlays.

The new `CategorizationOverlay` entity:
- Is **categorization-level** (modifies C/I/A base, adds dimensions, declares aggregation rules)
- Carries structured JSON columns (`Sp80060Overrides`, `CustomDimensions`, `AggregationRule`,
  `WizardQuestions`) validated at write time
- Supports `TenantId = null` for built-in catalog entries and `TenantId = <guid>` for
  org-authored entries (nullable FK pattern)
- Has a lifecycle state machine (`Draft → InReview → Approved → Retired`)

**Decision: R1 — Implement separate `CategorizationOverlay` entity. Do not modify `OverlayDocument.cs`.**

---

## R2: FK-less Overlay Pinning (`OverlayIds` JSON Array) vs. Join Table

**Question:** Should `SecurityCategorization → CategorizationOverlay` be a join table or a
JSON array of version IDs?

**Answer: JSON array of version IDs stored in `SecurityCategorization.OverlayIds`.**

### Rationale

**Join table approach** (`SystemCategorizationOverlay` with FK `CategorizationId + OverlayId`):
- Pro: relational integrity, easy EF Include
- Con: overlay stack ordering requires an additional `Priority` column in the join table;
  removing an overlay version from the catalog would cascade-delete historical pinning data,
  breaking the audit trail

**JSON `OverlayIds` approach** (array of `VersionId` strings, e.g. `["army-rmf:v3.2", "dod-privacy:v2.1"]`):
- Pro: version-pinned by string key; survives catalog retirement (the `VersionId` string
  is preserved in the audit snapshot even if the `CategorizationOverlay` row is retired)
- Pro: array order encodes application order / priority naturally
- Pro: consistent with `SecurityCategorization.CustomDimensions` being a JSON column
- Con: no DB-level referential integrity; application layer validates at write time
- Con: requires a lightweight join to resolve overlay metadata at read time

The audit trail requirement (overlay name preserved even after catalog retirement) is the
decisive factor. The application-layer validation cost is acceptable.

**Decision: R2 — JSON array of `VersionId` strings in `SecurityCategorization.OverlayIds`.**

---

## R3: Declarative Aggregation Rules vs. Expression Evaluation

**Question:** Should overlay aggregation rules be expressed as scripted expressions (C#/JS
lambda), SQL-like expressions, or a structured declarative JSON schema?

**Answer: Structured declarative JSON schema only. No scripting.**

### Rationale

Scripted rules (JS/C# eval):
- Flexible, but: not auditable without a full sandbox, cannot be safely stored in a DB column
  and re-evaluated at every IL computation, risk of injection/DoS, difficult to render as
  human-readable explanation in the AO drilldown panel

Declarative JSON rules:
- Auditable: every condition, threshold, and lookup table is explicit and serializable
- Renderable: the drilldown panel can show "if MAC = I → High; else max(C,I,A)" as prose
- Safe: rules can be validated at write time against a declared dimension set
- Sufficient: DoD overlay rules in practice are `max()` extensions, MAC-based thresholds,
  and tabular lookups — none require Turing-complete computation

Supported declarative rule types (v1 scope):
- `FipsHighWaterMark` — default, `max(C, I, A)`
- `MaxWithCustomDimensions` — `max(C, I, A, dim1, dim2, …)`
- `ThresholdCondition` — `if dim = value then ImpactLevel; else fallback_rule`
- `TabularLookup` — `{ (MAC=I, CL=Secret) → High; (MAC=II, CL=Sensitive) → Moderate; … }`

**Decision: R3 — Declarative JSON rule schema. No scripting. Rule types limited to v1 set.**

---

## R4: Overlay Inheritance Mechanism

**Question:** How should org-level overlay inheritance propagate to child systems?

**Answer: Flag-based inheritance with per-system waiver capability.**

### Rationale

**Auto-apply on new systems**: Simpler, but org admins may not want all overlays auto-applied
to every new system (a Privacy overlay on a system that handles no PII is noise).

**Catalog visibility only**: Overlays show in the picker but are not pre-selected. Requires
ISSO action per system. Easy to miss required overlays.

**Flag-based inheritance (chosen):**
- `CategorizationOverlay.InheritedByDefault = bool` (org-authored overlays)
- Systems created after the flag is set inherit the overlay in their `OverlayIds` automatically
- ISSO may waive per system via `OverlayWaived` audit entry (requires ISSM approval)
- Existing systems are not retroactively modified — inheritance only applies on new systems
  or when the ISSO explicitly clicks "Apply inherited overlays"

This matches how `OrgInheritanceDefault` works in Feature 043/044.

**Decision: R4 — Flag-based inheritance with per-system waiver. Existing systems not retroactively modified.**

---

## R5: Built-in Overlay Data Storage

**Question:** Should built-in overlay JSON files be embedded resources in the assembly or
loose files in the repo under `data/overlays/builtin/`?

**Answer: Loose JSON files under `data/overlays/builtin/` loaded by a startup seed service.**

### Rationale

Embedded resources:
- Pro: deployed with the binary; no file system dependency
- Con: updating overlays requires a recompile + redeploy; ARM/Bicep deployments would
  need a container rebuild for an overlay data update

Loose JSON files:
- Pro: can be updated independently of code (quarterly overlay refresh per issue body §Configuration)
- Pro: consistent with other data seed patterns in the repo
- Pro: easier review process — a PR that updates `army-rmf-v4.json` is clearly scoped
- Con: file path must be configurable for container deployments

The issue body explicitly calls for `data/overlays/` as the data file location. The seed service
uses `IWebHostEnvironment.ContentRootPath` to locate files, falling back to embedded resources
if the directory is absent (container robustness).

**Decision: R5 — Loose JSON files in `data/overlays/builtin/`. Seed service with embedded fallback.**

---

## R6: `ImpactLevel` Column Change — Computed vs. Stored

**Question:** Should the overlay-adjusted `ImpactLevel` be stored in the `SecurityCategorization`
table or always computed on the fly from the overlay stack?

**Answer: Always computed at read time from the overlay stack; no stored column for the adjusted value.**

### Rationale

Storing the adjusted IL:
- Pro: simple queries (`WHERE ImpactLevel = 'High'`)
- Con: stale data risk when overlay definitions change; requires background update job
  on every overlay catalog update to re-stamp all affected systems

Computed at read time:
- Pro: always current; no stale data risk
- Pro: overlay stack resolution is deterministic and fast (< 50 ms per NFR-002)
- Con: filtering by IL in a large system list requires in-memory evaluation or a
  denormalized computed column updated by the service layer

Compromise (chosen): the `SecurityCategorization` entity carries a `NotMapped`
`OverlayAdjustedImpactLevel` property set by the service layer on load. Dashboard queries
that need IL filtering receive a projected DTO with the computed value already resolved.
A periodic background job recomputes and caches the adjusted IL in an ephemeral Redis/memory
cache (not persisted to DB) for high-volume list endpoints.

**Decision: R6 — Computed at service layer; NotMapped property on entity; cache for list endpoints.**
