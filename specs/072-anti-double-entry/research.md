# Research — 072: Anti-Double-Entry (SPIN/eMASS Sync Status)

Decision records for architectural choices in Feature 072 (Issue #60).

---

## R1: Linking `EmassFieldSnapshot` to `RegisteredSystem` — String PK Boundary

**Question:** Should `EmassFieldSnapshot.SystemId` be a FK with navigation to `RegisteredSystem`?

**Answer: No navigation property — string FK only.**

### Evidence

`RegisteredSystem.Id` is a string PK (GUID as string, `[MaxLength(36)]`) per
`src/Ato.Copilot.Core/Models/Compliance/RmfModels.cs`. The codebase convention for string PK
foreign keys avoids navigation properties on the referencing side in many places to prevent
circular dependency chains and migration complications.

`EmassFieldSnapshot` lives in `Ato.Copilot.Core.Models.Onboarding` (same namespace as
`EmassImportSession`), while `RegisteredSystem` lives in `Ato.Copilot.Core.Models.Compliance`.
Adding a navigation property would create a cross-namespace dependency from `Onboarding` →
`Compliance` models, which goes against the dependency direction of the codebase.

The sync status service resolves the system by querying `AtoCopilotContext.RegisteredSystems`
directly (EF + RLS), not through a navigation property.

**Decision: R1 — `EmassFieldSnapshot.SystemId: string [MaxLength(36)]`. No navigation
to `RegisteredSystem`. FK constraint is NOT added at the DB level (soft FK via application logic).**

---

## R2: Divergence Check Timing — Synchronous vs Background Job

**Question:** Should divergence detection run synchronously at entity-save time, or as a
background/scheduled job?

**Answer: Synchronous at save time.**

### Rationale

1. **Immediate feedback loop**: The sync status badge must reflect reality immediately after
   an ISSO edits a field. A background job delay of seconds to minutes would show stale
   "In Sync" after the ISSO just edited the field — confusing and defeats the purpose.
2. **Low overhead**: The divergence check is an indexed lookup of at most 6 rows
   (`EmassFieldSnapshot` cardinality per system). At < 5 ms query time, the total overhead
   is negligible compared to a typical HTTP handler (50–200 ms).
3. **No background infrastructure needed**: Adding a background queue or hosted service for
   a 6-row lookup is overengineering. The existing `Channel`-based background infrastructure
   (Feature 037) is reserved for heavy operations (package generation, OSCAL export).
4. **Atomic with the save**: If the save fails, the divergence check never runs — no
   orphaned divergence state.

**Guard against divergence-check exceptions**: If `CheckDivergenceAsync` throws (e.g., DB
transient error), the exception is caught, logged as warning, and swallowed. The main entity
save is not rolled back due to a failed divergence check.

**Decision: R2 — Synchronous divergence check at entity save. Swallow exceptions with warning log.**

---

## R3: Field Value Normalization for Divergence Comparison

**Question:** How should SPIN enum values (e.g., `ImpactLevel.Moderate`) be compared against
raw eMASS string values (e.g., `"Moderate"`, `"MODERATE"`, `"moderate"`)  for divergence detection?

**Answer: Canonical string normalization with `ToUpperInvariant()` on both sides.**

### Rationale

eMASS XLSX exports are not standardized — casing and whitespace vary between eMASS versions
and agency configurations. SPIN stores enum values that serialize to Pascal-case strings.

The normalization algorithm:
1. `emassValue.Trim().ToUpperInvariant()`
2. `spinValue.ToString().ToUpperInvariant()` (where `spinValue` is the current enum or string value)
3. Compare equality

Edge cases:
- eMASS value `"LOW"`, SPIN value `ImpactLevel.Low.ToString() = "Low"` → both → `"LOW"` → equal (In Sync)
- eMASS value `"MODERATE"`, SPIN value `"Moderate"` → both → `"MODERATE"` → equal (In Sync)
- eMASS value `"  High "` → trimmed → `"HIGH"`, SPIN `"High"` → `"HIGH"` → equal (In Sync)
- eMASS value `"CNSSI 1253 Moderate"`, SPIN value `"Moderate"` → NOT equal → Diverged
  (correctly flagged — annotated value vs simple value)

The `EmassTrackedField` constants define the canonical SPIN property name used to look up
the current value via reflection or a switch on `FieldName`.

**Decision: R3 — Trim + ToUpperInvariant() comparison. Log a structured note when comparison
yields unexpected mismatch for debugging during initial rollout.**

---

## R4: New Entity vs Extending Existing Models

**Question:** Should divergence state be stored as new columns on `RegisteredSystem` and
`SecurityCategorization` (e.g., `EmassSourcedAt`, `EmassValue`, `IsDiverged`), or as a
separate `EmassFieldSnapshot` table?

**Answer: Separate `EmassFieldSnapshot` table.**

### Rationale

1. **Extensibility**: The list of tracked fields may grow (e.g., `InterconnectionCount`,
   `AuthorizationBoundaryDefined`). Adding per-field columns to core entities would bloat
   their schemas with eMASS-specific audit metadata.
2. **Normalized design**: One row per `(SystemId, FieldName)` is cleaner than 12+ new nullable
   columns spread across `RegisteredSystem`, `SecurityCategorization`, and `ControlBaseline`.
3. **Separation of concerns**: eMASS import lineage is an onboarding concern; core entity
   models should not carry import metadata beyond the minimum (e.g., `DitprId` is a true
   system attribute, not just import metadata).
4. **Audit trail**: The `EmassFieldSnapshot` table can store multiple snapshots over time if
   needed (currently one per field, but the design allows history if re-import frequency
   warrants it in a future iteration).

**Decision: R4 — New `EmassFieldSnapshot` table. Only `DitprId` is added directly to
`RegisteredSystem` because it is a genuine system attribute (DoD system identifier), not
merely import metadata.**

---

## R5: Banner Dismiss Storage — localStorage vs Server-Side Preference

**Question:** Should the "dismiss this banner" action write to `localStorage` (client-only)
or to a server-side `UserPreference` record?

**Answer: Primary implementation uses `localStorage` for per-session UX; `POST /dismiss`
endpoint exists for server-side persistence (optional, out of scope for initial release).**

### Rationale

1. **Performance**: A server round-trip to dismiss an informational UI banner is excessive.
   `localStorage` is instant and survives page reloads (same browser).
2. **Scope**: The banner is informational — it does not affect security controls or compliance
   data. Losing the dismiss preference on a different browser/device is a minor UX annoyance,
   not a correctness issue.
3. **Existing precedent**: The dashboard already uses `localStorage` for UI preferences
   (e.g., sidebar state, role switcher).
4. **Progressive enhancement**: The `POST /api/systems/{id}/emass-sync-status/dismiss`
   endpoint is specced (T013) to enable server-side persistence in a future iteration
   (cross-device consistency). The initial implementation may store in `localStorage` only.

**Decision: R5 — `localStorage` for dismiss UX. `POST /dismiss` endpoint specced but
server-side storage is a Phase 2 enhancement. Badge divergence state is NEVER affected by dismiss.**

---

## R6: Sync Status Badge Visibility — Hidden vs Disabled for SCA/AO

**Question:** Should the sync badge be hidden (`return null`) or rendered in a disabled/read-only
state for SCA and AO roles?

**Answer: Hidden.**

### Rationale

1. **UX clarity**: SCA and AO do not own the eMASS import workflow. Showing them a badge they
   cannot act on creates confusion ("Why can't I click this? What does it mean?").
2. **Role context**: SCA (Security Control Assessor) is a read-only assessor role; AO
   (Authorizing Official) approves authorization decisions but does not manage data entry.
   Neither role initiates or resolves eMASS sync divergences.
3. **Consistency**: The existing pattern for write-gated controls in the dashboard is to
   hide them for read-only roles (e.g., Subscribe button in Epic #225 is hidden for SCA/AO,
   not grayed out).

The `GET /api/systems/{id}/emass-sync-status` endpoint returns 403 for SCA/AO — this is the
backend enforcement. The frontend `EmassSyncBadge` component checks `settings.role` client-side
as an additional UX guard (defense in depth).

**Decision: R6 — Badge hidden for SCA/AO roles (both client and server enforcement).**

---

## R7: Pre-Population Strategy — Overwrite vs Non-Destructive Merge

**Question:** When an eMASS import is committed and a SPIN field already has a value, should
the import overwrite it?

**Answer: Non-destructive merge — do NOT overwrite existing SPIN values.**

### Rationale

1. **Data integrity**: An ISSO may have manually entered system data before importing from eMASS,
   or may have intentionally edited the field after a prior import. Silently overwriting their
   work would be a destructive action.
2. **Feature 071 ownership**: Explicit re-sync / overwrite workflows are the domain of
   Feature 071 (Issue #59), which handles round-trip sync with user confirmation. Feature 072
   is specifically about data entry minimization — filling gaps, not replacing existing data.
3. **Still useful**: Even when a field is not overwritten, the `EmassFieldSnapshot` is still
   created with the eMASS value. This allows divergence detection to work correctly: if the
   SPIN value differs from the eMASS value, the badge shows Yellow/Diverged — which is
   the right behavior.
4. **Toast visibility**: The "N fields pre-populated" toast counts only fields that were empty
   in SPIN before the import. Fields that were already set and skipped are not counted.

**Decision: R7 — Non-destructive merge. Always create/update `EmassFieldSnapshot`. Only
pre-populate SPIN field if it is null/empty. Never overwrite a user-entered value.**
