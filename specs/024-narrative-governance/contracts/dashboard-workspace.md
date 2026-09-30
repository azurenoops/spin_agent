# Control Narrative Workspace Contract

## Purpose

The system narrative workspace is an aggregate read and navigation surface over
the existing narrative stores. It does not create a parallel statement, proposal,
evidence, responsibility, or history store.

## Inclusion rules

- **Needs attention**: a control is included when either Policy or Technical
  content is missing, the current control version is not Approved, a current
  proposal is actionable or stale, or the retained proposal records conflicts or
  missing evidence.
- **All controls**: every persisted `ControlImplementation` in the selected
  tenant/system scope.
- **Approved statements**: a control is included only when its authoritative
  approved version snapshot exists and contains nonblank Policy and Technical
  content. The control's implementation status is reported separately and does
  not affect this inclusion rule.

Counts and rows MUST be calculated by the same server projection before paging.
A failed projection is an error, not a zero-count response.

`GET /api/dashboard/systems/{systemId}/narrative-workspace` accepts `view`,
`search`, `family`, `status`, `page`, and `pageSize`. Search, family, and
implementation-status filters are applied before all three counts and before the
selected view is paged. The response includes the validated `systemId`, all three
view counts, proposed-update count, rows, and server-derived author/reviewer/evidence
permissions.

## Statement state

Policy and Technical state are projected independently from:

1. whether the current content is empty,
2. whether the authoritative approved snapshot contains that statement,
3. the current control-level governance lifecycle,
4. current/stale proposals for that narrative type.

The projection MUST keep completeness, approval, source freshness, implementation
status, assessment results, and authorization decisions as separate fields.

## Detail

The selected control detail includes current and approved Policy/Technical
content, version/provenance metadata, current proposals with readable before/after
content, proposal cause and source context, conflicts and evidence gaps, validation
links, responsibility dependencies, immutable version/review history, and
server-derived action permissions/reasons.

`GET /api/dashboard/systems/{systemId}/narrative-workspace/{controlId}` includes
the validated system/control identity, separate statement projections, retained
approved snapshot, proposals, validation evidence, responsibility records,
immutable narrative versions and reviews, and permission-denial reasons.

All reads and mutations validate effective tenant, system, control, narrative
type, proposal identity, expected proposal revision, and expected narrative
version. Foreign scope is returned as not found.

## Existing workflows

- Direct Policy/Technical editing and authorized create/bulk operations remain in
  the existing editor.
- Proposal generation/review remains in the existing `NarrativeProposal` workflow.
- Reference import/publication remains in the existing narrative library.
- Evidence links remain in `ControlValidationLink`.
- Responsibility allocation remains in the responsibility workflow.

## Verified downstream consumers and gap

- DOCX/PDF template generation reads `ControlImplementation.PolicyNarrative` and
  `TechnicalNarrative`.
- OSCAL SSP export emits both current statement fields.
- eMASS export combines both current statement fields into its single
  implementation statement.
- `NarrativeVersion` and `ApprovedVersionId` retain approved snapshots, but the
  inspected document/export consumers do not consistently restore the approved
  snapshot before rendering. The workspace MUST disclose this incomplete
  approved-snapshot connection and MUST NOT claim SSP, eMASS, package, ATO, or
  control-implementation readiness merely because a narrative is saved.
