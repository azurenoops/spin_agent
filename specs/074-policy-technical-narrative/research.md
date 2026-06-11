# Research — 074: Policy + Technical Narrative Split & Evidence Classification

Decision records for architectural choices in Feature 052 (#64).

---

## R1: Additive Columns vs. New `ControlNarrative` Table

**Question:** Should the two new narrative halves be stored as columns on
`ControlImplementation` (additive) or as rows in a new `ControlNarrative` table
(using a `NarrativeKind` discriminator row-per-half pattern)?

**Answer: Additive columns on `ControlImplementation`.**

### Evidence

The issue body for #64 states: "Add `PolicyNarrative` + `TechnicalNarrative` as ADDITIONAL
nullable columns (migration-safe additive change). The existing `Narrative` field becomes the
'combined/legacy' field."

A separate `ControlNarrative` table would:
1. Require a JOIN on every SSP render, adding query complexity.
2. Break the existing unique constraint on `(RegisteredSystemId, ControlId)`.
3. Force every consumer of `ControlImplementation` to be updated.
4. Create an unbounded row count if future halves are ever added (though they are out of scope).

Two nullable nullable columns are:
- **Zero-downtime** — deploy the code, then run the migration; existing rows read `NULL`.
- **Rollback-safe** — `Down()` drops two columns with no data loss to the original `Narrative`.
- **Query-transparent** — all existing `SELECT * FROM ControlImplementations` queries continue
  to work; consumers that don't need the new columns see `NULL` without modification.

**Decision: R1 — Additive nullable columns. No new table.**

---

## R2: Legacy `Narrative` Disposition

**Question:** Should the existing `Narrative` field be renamed to `TechnicalNarrative`
(breaking change) or kept as-is (additive)?

**Answer: Keep `Narrative` as-is. Do NOT rename.**

### Rationale

Renaming `Narrative` to any other field name is a breaking change for:
1. All existing EF Core queries that reference `.Narrative` by name.
2. All REST API clients that consume `narrative` from the JSON response.
3. All MCP tools that pass `narrative` to existing endpoints.
4. All SSP export code that references `ci.Narrative`.

The migration strategy instead:
- Back-fills `TechnicalNarrative = Narrative` for all rows where `Narrative IS NOT NULL`.
- Sets `MigratedFromLegacy = true` on those rows.
- Leaves `Narrative` unchanged — consumers that haven't upgraded see the same value.
- New API response shape exposes `legacyNarrative` (maps from `Narrative`) for backward compat.

**Decision: R2 — `Narrative` field preserved as "combined/legacy". No rename.**

---

## R3: `EvidenceNarrativeType` Default for New Uploads

**Question:** Should new `EvidenceArtifact` uploads default to `Unclassified` (3) or
`Technical` (1) since most auto-collected evidence is technical in nature?

**Answer: `Unclassified` (3).**

### Rationale

Defaulting to `Technical` would silently mislabel manually-uploaded policy documents until
the user explicitly re-tags them. `Unclassified` is honest — it signals "needs classification"
and allows the auto-tagging classifier to assign the correct value on its first run.

The trade-off is that the `UnclassifiedEvidence` list in the GET response may be non-empty
after initial upload, but that is preferable to silent mislabeling.

**Decision: R3 — New uploads default to `Unclassified`. Back-fill existing rows to `Combined`
(not `Unclassified`) because the intent of legacy evidence is "supports this control" without
a specific half, which matches the semantic of `Combined`.**

---

## R4: Back-Fill of Legacy Narrative to Policy or Technical?

**Question:** Issue #64 US5 says legacy `ControlImplementation` rows should be migrated to
`NarrativeKind=Technical` (the historical default). But should the migration silently back-fill
`TechnicalNarrative = Narrative`, or create a "Legacy" kind that we sunset?

**Answer: Back-fill to `TechnicalNarrative` with `MigratedFromLegacy = true` flag.**

### Rationale

A third `Legacy` kind:
- Requires UI to handle a third state that is never authored and only exists to be migrated.
- Creates dead code paths in export rendering.
- Confuses auditors who see a `Legacy` section in the SSP.

Back-filling to `TechnicalNarrative` (with the `MigratedFromLegacy` audit flag):
- Ensures the SSP export immediately reflects content in the Technical slot.
- Policy slot renders `[Not Authored]` — correct, because no org has authored it yet.
- The `MigratedFromLegacy` flag lets the UI optionally show a banner: "This technical
  narrative was migrated from a legacy combined narrative. Review and split as needed."

**Decision: R4 — Back-fill to `TechnicalNarrative`; no `Legacy` kind. Issue #64 US5 AC5
confirms this approach.**

---

## R5: Role Gate — Hard 403 vs. Draft Suggestion for PlatformEngineer on Policy

**Question:** Should a `PlatformEngineer` receive a hard 403 when writing `PolicyNarrative`,
or be allowed to submit a "suggested edit" that an ISSM can accept?

**Answer: Hard 403.**

### Rationale

Issue #64 asks: "8. **Engineer write to Policy** — Hard 403 (current proposal), or allow
Engineers to draft Policy as a 'suggested edit' the ISSM can accept?" The issue lists this
as a `/clarify` question with the current proposal being hard 403.

For the spec, hard 403 is chosen because:
1. Simpler to implement and reason about for auditors.
2. A "suggested edit" workflow requires a `PolicyNarrativeDraft` table, approval flow, and
   notification system — all out of scope for Feature 052.
3. Engineers can still author the Technical half directly, which is the common case.

**Decision: R5 — Hard 403 for PlatformEngineer on any request where `policyNarrative` is
non-null. Engineers who need to propose Policy content should do so out-of-band (e.g., Teams
message to ISSM).**

---

## R6: OSCAL Statement IDs — `_smt.a/_smt.b` vs. `_smt.policy/_smt.technical`

**Question:** Issue #64 asks: "OSCAL statement IDs — `_smt.a / _smt.b` (NIST OSCAL
convention) or custom suffixes `_policy / _technical` (clearer but non-standard)?"

**Answer: `_smt.policy` and `_smt.technical`.**

### Rationale

- `_smt.a` and `_smt.b` are generic ordinal suffixes from NIST's OSCAL schema examples;
  they are not semantically meaningful to downstream tooling.
- `_smt.policy` and `_smt.technical` are self-documenting — an eMASS import script or
  FedRAMP reviewer can identify which half is which without a lookup table.
- OSCAL's schema does not restrict `statement-id` to `_smt.a/b`; any string is valid.
- Downstream eMASS concatenation (`Policy + "\n\n" + Technical`) relies on being able to
  identify the two statement entries by their IDs.

**Decision: R6 — Use `{controlId}_smt.policy` and `{controlId}_smt.technical`.**
