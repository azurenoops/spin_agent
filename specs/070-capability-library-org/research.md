# Research — 070: Capability Library (Org Scope)

Decision records for architectural choices in Epic #225.

---

## R1: Separate Endpoint vs Reusing `/api/csp/inherited-components`

**Question:** Can the org capability catalog reuse `GET /api/csp/inherited-components`?

**Answer: No. A separate endpoint is required.**

### Evidence

The existing `CspInheritedComponentEndpoints.cs` endpoint:
1. Exposes `importedBy`, `sourceFileName`, `sourceArtifactReference`, `rowVersion`, and
   `capabilityNeedsReviewCount` — all internal admin-only fields not appropriate for org users.
2. Returns **component-level** pagination; to see capabilities, callers must fan-out per
   component (N+1 pattern used by `CapabilityLibrary.tsx`).
3. Returns **Draft and Archived components** to CSP Admins (FR-104 — non-admins see Published
   only, but the filter is role-based at the endpoint, not structural).
4. Has a different OpenAPI tag (`"CSP Inherited Components"`) and is intended for the admin
   management surface, not the org catalog browse.

The new `GET /api/capability-library` endpoint:
- Is **capability-centric** (flattened, no component fan-out required)
- Hard-filters `Status = Published` on the component and `Status = Mapped` on the capability,
  regardless of caller role (no admin override)
- Exposes only the safe projection: `{ id, capabilityName, componentName, componentType,
  mappedNistControlIds, mappingConfidence?, description? }`
- Lives in a separate `CapabilityLibraryEndpoints.cs` file with its own tag

**Decision: R1 — Implement separate endpoint. Do not modify `CspInheritedComponentEndpoints.cs`.**

---

## R2: System-Scoped Subscriptions

**Question:** Should subscriptions be org-scoped (one sub per org) or system-scoped (one sub per system)?

**Answer: System-scoped.**

### Rationale

An org may run multiple systems at different FedRAMP impact levels. Azure Active Directory
(Identity/Platform type) may be subscribed to a High-impact system's SSP but not to a Low-impact
system's SSP because the control coverage requirements differ. Forcing org-scoped subscriptions
would either:
(a) Block selective inheritance across systems, or
(b) Require a complex per-system override table that essentially re-implements system scope

System-scoped subscriptions are consistent with how `ControlInheritance` and
`OrgInheritanceDefault` work — both have system-level granularity.

The unique constraint is `(TenantId, SystemId, CspInheritedCapabilityId)` for Active rows,
implemented in application code (not a DB unique constraint, to allow re-subscribe after cancel).

**Decision: R2 — System-scoped subscriptions. One `CapabilitySubscription` row per (tenant, system, capability).**

---

## R3: Idempotent Subscribe (200 vs 409)

**Question:** When a capability is already subscribed, should a second POST return 409 CONFLICT or 200 OK?

**Answer: 200 OK (idempotent).**

### Rationale

1. **UI retry safety**: React components may fire duplicate POST requests on double-click or
   StrictMode remount. A 409 would surface a confusing error to the user.
2. **CSP publish pipeline**: When a CSP Admin re-publishes a component, any dependent subscription
   reconciliation logic may re-call subscribe. Idempotency avoids errors in that pipeline.
3. **REST semantics**: PUT is canonically idempotent; POST is not required to be, but for
   subscription creation the duplicate-safe contract is more user-friendly and simpler to
   test. The behavior is documented in the HTTP API contract.
4. **Prior art in codebase**: `CspInheritedComponentService.PublishAsync` uses the same pattern —
   re-publish returns the existing row rather than throwing.

**Decision: R3 — Re-subscribe returns 200 with the existing Active subscription row. No 409.**

---

## R4: ISSO/ISSM Gate Rationale

**Question:** Why restrict subscribe/unsubscribe to ISSO/ISSM? Why not allow ISSM + SCA + MissionOwner?

**Answer: ISSO/ISSM only, enforced at API level.**

### Rationale

1. **Control inheritance is a compliance commitment.** Subscribing a capability to a system
   means declaring to auditors that those NIST controls are inherited from the CSP. This is
   a formal ATO-impacting action and must be gated to the security officers responsible
   for the SSP (ISSO/ISSM per DoD/RMF roles).
2. **SCA (Security Control Assessor) is read-only by design.** SCAs assess but do not
   authorize changes to control inheritance. Allowing SCAs to subscribe would blur this
   separation of duties.
3. **AO (Authorizing Official) may approve, but the ISSO/ISSM executes.** The AO role in
   ATO Copilot is already scoped to authorization decisions, not SSP authoring.
4. **Consistent with existing write gates.** `CspInheritedComponentEndpoints.cs` comment
   block explicitly lists the write-gate pattern: writes require `CSP.Admin`; this epic
   applies the same pattern with `ISSO/ISSM` as the write-authorized roles.

**Decision: R4 — Subscribe/unsubscribe requires role `ISSO` OR `ISSM`. SCA/AO get 403 on mutation.**

---

## R5: Pagination Strategy

**Question:** Server-side pagination with EF `.Skip().Take()` or client-side with full result set?

**Answer: Server-side pagination.**

### Rationale

1. The CSP capability catalog could contain hundreds to thousands of capabilities as more
   CSP profiles are onboarded. Client-side pagination of a 5,000-row result set via a
   single API call is not acceptable for 200 ms P95 targets.
2. Server-side pagination with `page`/`pageSize` query params aligns with every other
   paginated endpoint in the codebase (`CspInheritedComponentsPage`, `CapabilityHistoryPage`).
3. Default `pageSize=20` with max `pageSize=100` matches the existing pattern.
4. The compound index `(Status, CspInheritedComponentId)` on capabilities (already implied
   by the FK) supports efficient filtered queries.

**Decision: R5 — Server-side pagination. `?page=1&pageSize=20`. Max `pageSize=100`.**

---

## R6: Subscription Triggering Inheritance Chain

**Question:** Should subscribe/unsubscribe synchronously complete the Epic #223 SSP inheritance chain
before returning HTTP 200/204, or fire-and-forget?

**Answer: Fire-and-forget (async).**

### Rationale

1. **Epic #223 chain scope**: The SSP inheritance propagation may touch multiple control
   baseline rows, control implementation stubs, and narrative generation triggers. This
   is not a sub-100 ms operation.
2. **HTTP timeout risk**: Synchronous propagation risks HTTP 500 or timeout on slow chains,
   making the subscribe action appear unreliable.
3. **Existing pattern**: The existing `RemapAsync` in `CspInheritedComponentService` already
   uses fire-and-forget for history-row writing side effects. The notification / propagation
   pattern in the codebase is consistently async.
4. **Eventual consistency is acceptable**: The SSP is a living document. A short propagation
   delay (seconds to minutes) between subscription and SSP control inheritance is acceptable
   per the RMF lifecycle model.
5. **Rollback isolation**: If the SSP propagation fails after the subscription row is committed,
   the subscription row is the source of truth and a retry can re-trigger propagation.

If the Epic #223 service does not yet exist at the time of implementation, the call site should
log a structured warning: `"CapabilitySubscription created but Epic223 chain not yet wired"`.

**Decision: R6 — Fire-and-forget. Subscribe/unsubscribe returns immediately after DB commit.
SSP propagation runs asynchronously.**
