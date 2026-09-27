# Provider offerings demonstration refresh

**Status**: Deployed and automatically verified locally, September 26, 2026.
Manual demo rehearsal/user acceptance remains separate.  
**Purpose**: A polished synthetic demonstration, not real provider authorization
or evidence of live Azure/Microsoft 365/eMASS integration.

## Target experience

Follow the required [provider mocks](../design/provider-workspace-mock/README.md):

- **Azure IL5 · Shared services**: eight reusable capabilities with explicit
  provider/shared/customer responsibilities and source-backed publication.
- **Microsoft 365 · Collaboration**: a distinct SaaS offering, boundary,
  responsibility matrix, evidence and manual service relationship.
- Reviewed baseline sources and permitted customer summaries; a clearly
  separate proposed change/release and a moderate provider finding for the demo.
- Mission systems consume an explicit eligible allocation and selected published
  release. Neither association nor adoption completes customer responsibilities.

Documents and records must be realistic in content but clearly marked
**SYNTHETIC DEMONSTRATION ONLY**. Do not use real government authorization
signatures, decisions, identities, customer agreements or cloud entitlements.
Use fictional contacts and `example.invalid` addresses.

## Verified initial local state

The running application at `http://localhost:5173` was still the older
`review-1039-20260926-1204` dashboard image. Its MCP counterpart exposed port
3002. The newer worktree preview was at port 5197; therefore a stale deployed
image explains part, but not necessarily all, of the reported UI differences.

Initial API inventory:

- One draft offering: `Azure IL5`.
- One retained package: `flankspeed-il5-ato-clean.zip`, `NeedsAttention`.
- One organization: `SPIN Demo Organization`.
- One system: `SPIN Demo System`.

The deployment belongs to the local `ato-copilot` Compose project. No active
agent session was found for the old deployment worktree during this inspection.

## Preservation and cleanup

A SQL Server `COPY_ONLY` backup was created and verified with `RESTORE
VERIFYONLY ... WITH CHECKSUM`. Provider `/data` files were backed up separately.
Both are stored in the current agent session's restricted local backup directory,
not committed to the repository.

Backup hashes:

- Database: `17f30d6ea2543a3dd6029c57516bde9b914097f49743f303f3d97ef9cfb2b0c9`
- Provider files: `cb98b812191b9c6e2bc06b68403542b798aff32149f2698e623d7444640a361d`

Preserve login identities, membership/role assignments, configuration, and
unrelated records. Do not run the historical blanket SQL seed/wipe scripts:
they predate the current reviewed publication and tenant-isolation contracts.
Cleanup is limited to positively identified superseded synthetic demo records,
after dependency checks. Preserve historical/referenced material or archive it
instead of deleting it merely to make a screen look complete.

Only the MCP and Dashboard images are to be replaced. SQL, Redis, Chat, data
volumes and credential configuration must not be reset. Preserve the prior
image IDs and runtime settings for rollback. Source/backup files containing
credentials remain local and owner-readable.

The first updated API boot exposed a real SQL Server query-translation failure:
provider and mission rule scheduling compared a column with
`DateTime.UtcNow.Ticks` inside LINQ. Capture the clock value before constructing
each query so SQL receives a numeric parameter. Do not disable monitoring or
treat its failures as successful health.

Live source loading then exposed a separate recovery defect: `review-state`
revalidated a saved approval preview without its retained impact-review IDs,
causing `AUTHORIZATION_WORKFLOW_REQUIRED` even after explicit impact acceptance.
Recovery must pass the same stored IDs used by approve/publish. This preserves
the publication gate rather than bypassing it; a real HTTP regression reads the
saved preview before approval.

The live Microsoft 365 publication trace also showed approximately 40 seconds
per exact impact-option GET. The reader enumerated every canonical provider
record and expanded its dependency graph before testing one already-linked source
candidate. An exact candidate lookup must use its provider/offering/package
ownership predicate directly; canonical fallback still enforces graph linkage.
Query-count regression coverage prevents unrelated catalog size from driving
this source-candidate read.

The starter allocation exposed a lifecycle defect: adding a customer allocation
incremented the offering's service revision and invalidated every accepted
provider review. The actual Mission Owner API returned
`OFFERING_CONTEXT_STALE` and `PROVIDER_REVIEW_REQUIRED` for all eight otherwise
published capabilities. Allocation revision/audit must remain independent when
the service definition is unchanged. Existing demo contexts invalidated by that
bug are renewed through explicit canonical review/publication, not raw database
edits or weakened eligibility checks.

The obsolete intake could not be archived because retained canonical context
references it. Its source bytes and history are therefore required. Cleanup uses
explicit review supersession with the published replacement, removing obsolete
active review work without deleting referenced provenance or claiming it was
analyzed/reviewed successfully.

## Data loading and verification

Use the existing Development simulation sign-in and real domain APIs. Upload
supported structured sources, verify actual analysis results, review exact
candidate revisions, record the explicitly synthetic decision, review impact,
approve and publish through the canonical pipeline. Do not seed `Published`,
`Reviewed`, ATO, membership or completion flags with raw SQL.

Human-readable PDF/DOCX/CSV materials accompany the structured sources.
Baseline ingestion must not depend on unapproved live AI calls. A source that
was not analyzed must remain explicitly unresolved, not counted as reviewed.

Before declaring demo-ready:

1. Verify both offerings, their actual published releases and source status.
2. Verify capability count, component links, duties, evidence and finding state.
3. Demonstrate a Mission Owner's eligible service association and adoption.
4. Confirm customer work remains open and source metadata reaches document output.
5. Compare the deployed pages with the supplied mocks at desktop/mobile widths.
6. Report exact changed data, retained records, backup/rollback instructions, and
   all remaining limitations. User acceptance is distinct from automated checks.

## Deployed result

Open the actual Docker dashboard at **http://localhost:5173**. The API is on
port **3002**. The worktree preview on port 5197 is not the deployed demo.

| Offering | Source edition | Current canonical revision | Published capabilities | Retained evidence files | Open finding / planned POA&M |
|---|---|---|---|---|---|
| Azure IL5 · Shared services | 1.2 | 2 | 8 | 15 | 1 / 1 |
| Microsoft 365 · Collaboration | 1.0 | 2 | 5 | 15 | 1 / 1 |

Both baselines were analyzed, explicitly reviewed, approved and published through
normal APIs. Same-content revision 2 renews the recorded context after filling
the source-documented service owner/contact metadata; revision 1 remains in
history. Azure's document edition 1.3 is supplied as a **proposal only** and has
not been imported/published as an active replacement.

The [catalog](../../demos/provider-offerings/CATALOG.md) links the material layout:
service guides, synthetic reference records, responsibility matrices, assessment
summaries, evidence indexes, release notes, private working papers and customer
bundles. Customer bundles exclude private working papers. Actual application
evidence remains provider-private until an explicit named-system summary-sharing
approval; possession of a generated customer bundle is not an application grant.

The old `flankspeed-il5-ato-clean.zip` receipt was superseded at revision 19 by
the new Azure baseline. Its 485 pending reviews and 86 proposed capabilities no
longer appear as active work. Original bytes, candidate flags, historical
references and audit data were preserved. Azure therefore displays **32 retained
source documents**, including old review sources, rather than pretending only
the six new JSON documents ever existed. Microsoft 365 displays six.

Existing provider identity **Flankspeed**, the organization, existing mission
system, identities and memberships were preserved. No mission was deleted,
no role was granted, and no ATO or responsibility-completion flag was seeded.

### Runtime and rollback

- MCP image: `ato-copilot-mcp:provider-demo-20260926-final`.
- Dashboard image: `ato-copilot-dashboard:provider-demo-20260926-1910`.
- Both app containers verified healthy.
- SQL (`3c0f302ae7a3`), Redis (`03bc09cf7026`) and Chat (`dd7515d617bc`)
  retained their original container identities and data volumes.
- Verified backups and the restricted `rollback.compose.json` remain under the
  originating session's `files/demo-backup-20260926` directory. That manifest
  contains private runtime settings: do not publish or commit it.
- For an application rollback, inspect that saved manifest, then use the existing
  `ato-copilot` Compose project with `up -d --no-deps --wait` for its two app
  services only. Do not use `down -v` or `--remove-orphans`. An image rollback
  does not undo newly published data; database restoration is a separate,
  explicitly approved recovery operation.

### Verification evidence

- Final focused run: **116 unit tests passed**, including actual analysis of all
  three source ZIPs, allocation lifecycle and impact publication regressions.
- Final production-host HTTP workflow: **1 passed**, covering source publication,
  mission association/adoption, responsibility handling and retained export.
- Loader owner verified **34 offline tests** on Python 3.9.6, including strict
  parsing of .NET seven-digit fractional timestamps and resumable journal state.
- Dashboard Docker build runs TypeScript compilation and Vite build.
- Provider caption follow-up: **30 UI tests and TypeScript passed**.
- Headless Chromium checked the deployed provider overview, both offering
  overviews, organization portfolio and mission readiness at **1440 and 390 px**:
  ten loaded layouts, no document-level horizontal overflow. Compared captured
  provider/system layouts with the supplied design screenshots; retained real
  workspace/role chrome and actual data rather than mock-only controls/counts.
- After publishing revision 2, created the M365 starter allocation through the
  normal CSP API. The actual Mission Owner API still reports **all 13 releases
  Applicable with no stale-context/provider-review reason codes**. This verifies
  the allocation fix against the live SQL Server deployment.
- Both starter allocations return `canAssociate: true` and no relationship ID.
  Association, authorization-relationship review, adoption and responsibility
  confirmation remain deliberate user actions.

This is local demonstration verification, not production certification or a
claim that the earlier full SQL Server integration release gate passed.

## Manual rehearsal

1. Use Development sign-in as **Dev CSP Admin**. Open **Provider → Offerings**.
   Inspect Azure and Microsoft 365 independently: sources, service scope,
   capabilities/responsibilities, evidence and the open Moderate finding.
   Explain that these are synthetic demonstration records, not real ATOs.
2. Download/open the customer PDFs/DOCX files from the catalog directories.
   Inspect the exact structured source citations in the application.
3. Use **Dev ISSM** in **SPIN Demo Organization**. The organization portfolio has
   **Create mission system**. Create the mission through the normal wizard;
   complete its actual profile/boundary rather than copying provider authority.
4. For the already-prepared starter, use **SPIN Demo System**. It has one eligible
   Azure allocation and one distinct manual-service M365 allocation. For a newly
   created mission, use the provider's **Services & scope** workflow to allocate
   the appropriate documented scope to that new system.
5. Use **Dev Mission Owner**, open the mission's **Environment & hosting** work,
   and explicitly associate the selected allocation. Review the authorization
   relationship without asserting a real government authorization.
6. In **Applied capabilities**, select the current published release and review
   its source/context before adoption. Use the ISSM/ISSO responsibility workflow
   separately; provider publication and MO adoption do not complete customer work.
7. Share only an approved evidence summary for that named eligible mission.
   Verify the mission's visible provider-evidence table; private originals remain
   inaccessible.
8. Continue **Preparing for ATO → Readiness checklist → Generate & export a
   package** with **InitialSubmission**. Resolve real validation gaps. Preview
   and export reviewed source-backed records; do not turn an incomplete starter
   into an approved package by changing status flags.

The starter intentionally has no completed mission association/adoption/ATO, so
the presenter can demonstrate those actions. The existing Dev ISSM can create
systems; Dev Mission Owner can associate/adopt but is not granted system-creation
or ISSM/ISSO responsibility-confirmation authority.

Known boundaries: no live Azure/M365 provisioning or entitlement check, no real
eMASS transfer, and no real government approval. Provider sessions still emitted
an existing notification-endpoint 403 during browser checks; this refresh did not
weaken that endpoint's authorization or hide its errors.
