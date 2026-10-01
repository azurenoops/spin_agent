# ADR: governed System design graph and replaceable rendering

Status: implementation decision for Feature 079, September 30, 2026.

## Context

System definition currently owns six consolidated task tabs and reviewed
canonical records, but no dedicated governed architecture graph. The supplied
System Design HTML is the composition/interaction reference; its illustrative
counts and outcomes are not source data. Design meaning must survive renderer
replacement and appear in approved SSP/OSCAL output.

The September 30 follow-up screenshots confirm the required page composition:
seventh tab, design status and next review gate, baseline-change callout,
six source cards plus Azure, four view pills, canvas beside selected-element
inspector, then design gaps and SSP output readiness. Preserve this hierarchy
inside the existing SPIN shell. The user's architecture chain is authoritative:

```text
Canonical SPIN records -> System Design projection service
-> versioned design graph -> interactive diagram + accessible table
-> approved baseline -> SSP / OSCAL / ConMon
```

## Options

| Option | Benefits | Costs and limits |
|---|---|---|
| React Flow (`@xyflow/react`) with ELK | Established graph interaction, keyboard selection, grouping, pan/zoom and replaceable layout | Additional dependencies/license and vulnerability review; adapter and stable IDs required |
| Custom SVG editor | Minimal dependency surface and direct artifact rendering | Substantial custom interaction, focus, grouping and large-graph maintenance |
| Read-only diagrams with structured editing | Accessible editing, deterministic artifacts, simpler interaction | Does not alone satisfy interactive node/layout requirements |

## Decision

Use React Flow behind an application adapter with deterministic ELK layout,
subject to verified package compatibility, exact pinned versions, lockfile and
license/dependency audit before installation. Do not import transitive D3.
Use the same domain DTOs in a keyboard-accessible structured editor.

The backend owns the governed graph, provenance, gaps, source precedence and
review decisions. Layout is a separate versioned presentation record; moving a
node cannot change boundary inclusion or protection. Static approved SVG output
is generated server-side without requiring a browser or graph-library objects.
Preserve manual positions when new nodes appear; explicit reset/auto-layout is
the only action that rearranges existing manual positions.

## Consequences

New dependencies must not become domain contracts. Approved output cannot depend
on a browser viewport or current unsaved placement. Graph rendering is not
validation; gaps and readiness remain server-owned. Structured editing must
support the same essential decisions without drag-and-drop. Library licensing,
audit results and pinned versions will be recorded with the implementation.

### Dependency selection

The UI dependency metadata review selected exact versions `@xyflow/react`
12.11.6 (MIT; React/React DOM peers >=17, compatible with the existing React 19
Dashboard) and `elkjs` 0.12.0 (dual licensed; use the EPL-2.0 option, not the GPL
alternative). Retain upstream license notices and isolate both behind the
presentation adapter. Do not import D3 or other undeclared transitive packages.
The implementation's lockfile and dependency-audit result remain verification
requirements; this license choice does not assert a vulnerability-free graph.

## Dependency verification (September 30, 2026)

The npm registry reports `@xyflow/react` **12.11.6**, MIT, with React,
React DOM and their type packages `>=17`; this is compatible with the
Dashboard's React 19 declarations. `elkjs` **0.12.0** is dual-licensed
`EPL-2.0 OR GPL-3.0-or-later`; use the EPL-2.0 option and retain its notices.
Both are exact-pinned. Only the presentation adapter imports them; no D3
transitive dependency is imported directly. Automated dependency audit results
are reported after lockfile installation, not inferred from these licenses.

Rendering is bounded to 300 nodes and 600 relationships per visible graph.
Larger graphs explicitly fall back to a 50-record paginated structured editor;
the total and page range remain visible and all records remain reachable.
This is a rendering budget, not a domain-record truncation.

## Backend implementation and verification boundary

`ISystemDesignService` is a scoped, server-authorized service. Its public DTOs
are in `Core/Dtos/SystemDesign/SystemDesignContracts.cs`; the browser adapter is
`Dashboard/src/api/systemDesign.ts`. `/api/dashboard/systems/{systemId}/design`
provides graph reads/draft writes, review, reconciliation, proposal decisions,
revision history/snapshots, retained approval, and separately versioned layout.
Every content write requires the current revision and a rationale. Layout writes
use their own version and never change the design revision.

Three additive stores are installed by `SystemDesignSchemaAdditions`:
`SystemDesignWorkspaces`, `SystemDesignRevisions`, and `SystemDesignLayouts`.
They carry tenant-scoped EF filters and enroll in the existing SQL Server RLS
installer after schema creation. SQLite integration tests exercise persistence
and schema idempotence. Generated SQL Server DDL is checked structurally;
the local SQL Server startup has now installed these additive tables and an
authorized graph read succeeded. Populated cross-tenant RLS verification is not
claimed from startup or SQLite tests.

Profile provenance comes from retained, hash-verified approval audit snapshots,
including structured child rows. Current drafts are not relabeled as historical
approvals. A source fingerprint includes canonical changes and retained
observations. Approvals are immutable revision snapshots; later working drafts
cannot replace them. `GetApprovedAsync` reauthorizes the current tenant/member
and returns a separate freshness flag; exporters must use their existing
freshness gate rather than treating an old approval as a fresh source.

Source authority and explicit boundary assignments remain separate from graph
review. Persisted workspace permissions control editing; an independently
assigned ISSM reviews. There is no seventh `ProfileSectionType`, and generic
profile writes do not own System design. The existing profile-completeness
response additively includes design status, current revision and approved
revision, without changing the five mandatory-profile denominator. Design
completeness, source freshness and contribution summaries remain evaluated by
the dedicated graph contract; the profile card does not invent design readiness.

The service does **not** run Azure discovery or create monitoring connectivity.
It reads retained discovery only within exact attached resource IDs, and reads
the tenant/system-attributed monitoring ledger. These enter as recoverable
proposals, not verified topology or healthy monitoring. Monitoring observations
retain their change description and control reference; they do not automatically
edit resources, controls, responsibilities, evidence or authorization decisions.
Live relationship discovery and automatic impact-to-document propagation are
not implemented by this service.

Server budgets are 1,000 nodes, 3,000 relationships, 200 groups, 2,000 proposal
records and 4 MB per retained graph. Exceeding a source budget is an explicit
failure, not silent truncation. History lists the most recent 200 revisions;
any retained revision can be read directly by revision number. Presentation
fallback is deterministic and preserves manual positions; the Dashboard's ELK
adapter supplies view-specific automatic layout.

Local verification:

```sh
dotnet test tests/Ato.Copilot.Tests.Unit --filter FullyQualifiedName~SystemDesign
dotnet test tests/Ato.Copilot.Tests.Integration --filter FullyQualifiedName~SystemDesignHttpTests
dotnet build Ato.Copilot.sln
```

Manually use the System design tab as an assigned owner: save a partial draft,
reload, reconcile, defer/recover a proposal, inspect gaps, and submit. Switch to
an independently assigned ISSM to request revision or approve a gap-free design.
Derive a new working revision and compare it with the retained approval; changing
layout must not change the design revision. Check the generated SSP/OSCAL
separately before claiming document-output acceptance. Leave the additive tables
and retained snapshots intact if rolling back the application.

`npm audit --json` after installation reports 13 advisories: 6 high, 4 moderate,
3 low, and 0 critical. Affected packages are existing Babel/Vitest/browser-data,
brace-expansion, browserslist, diff, form-data, lodash, picomatch,
postcss-selector-parser and ws dependencies. The report does not identify
`@xyflow/react`, `@xyflow/system`, or `elkjs`; this is not a claim that the
application has no vulnerabilities. Unrelated dependency upgrades are not
included in this feature.
Unmodified MIT and EPL license notices are copied into the Dashboard's
`public/third-party/` assets for distribution with the built application,
alongside upstream corresponding-source references.

Browser navigation needs an early `popstate` registry because the application
uses `BrowserRouter`, not a data router with `useBlocker`. The registry is loaded
before router initialization; the System design editor registers only while
unsaved. It restores the current history entry before displaying a cancellable
dialog and delegates an explicit discard to the original history navigation.
Link/programmatic navigation is intercepted before route unmount; full-document
departure uses `beforeunload`. A real-browser regression verifies Back, cancel,
and explicit discard.

The initial layout record (`version: 0`) may contain server grid placeholders;
the renderer replaces those unpersisted defaults with the deterministic view
layout. After a presentation edit/save, existing positions are preserved.
Canonical `collapsedGroups` contains only server graph group IDs. Computed
Boundary/Network presentation groups use `visibility` keys prefixed
`presentation-group:<view>:`; these are view-only preferences, not domain groups
or boundary assignments. Node visibility remains keyed by node ID.

The add-existing picker accepts only server-staged `Added` proposals with
canonical source references or server-returned `availableNodes`. Records already
projected are not duplicated. A proposed external design element can be authored
without fabricating a canonical source; it remains undetermined and cannot
bypass the required canonical interconnection/agreement for boundary crossings.
When no addition is staged, users create or correct the record in the existing
Inventory & boundary or Ports & interconnections workflow, then reconcile.
The browser never creates an authoritative source identity from typed text.

## Clarified presentation contract

### Automatic relationship presentation

Server-classified `SourceRecord` nodes and known legacy source-only kinds stay
in structured records but are excluded from the architectural canvas. Explicit
recorded Access/Membership/UsesService/Attachment/Containment/HostingAssociation
edges retain source and review information and are labelled as associations,
not network flows. Manual access/data-flow relationships remain subject to flow
validation and are not filtered merely because they share an access label.

Each diagram view has its own renderer context and saved presentation. An
unsaved layout is fitted only after React Flow reports node measurement complete,
so returning from a one-node flow view cannot leave a multi-node context graph
in the old viewport. Saved manual placement remains the authority for display.

Controlled layout refreshes preserve measured node dimensions and explicitly
request handle-internals refresh through React Flow's public hook after layout.
Discarding measured geometry had caused intermittent missing edges despite valid
graph data. Rendering stays bounded by the existing 300-node/600-edge budget;
within that budget records are rendered without viewport-based edge culling.

Automatic source associations use `DesignSource.Type = RecordedRelationship`.
They have distinct Access, Membership, UsesService, Attachment, Containment or
HostingAssociation types and source-version pins. Data-flow presentation uses
the supported explicit flow types; existing authored NetworkConnection and
Dependency values retain their previous full validation requirements rather than
being reclassified as source-backed associations. Unknown relationship types
cannot bypass server validation. Diagram recipe v2 changes new artifact identities
without rewriting previously retained approved images.

The September 30 screenshots retain the existing System definition shell and
seven tabs, followed by the System design workspace, a next-review gate,
approved-baseline change notice, compact source contributions, graph/inspector,
and source-linked gap/readiness records. Illustrative counts and dates are not
application data.

Relationship origin is distinct from relationship purpose and review state.
The renderer labels canonical, user-authored, Azure-observed, imported,
AI-suggested, and undetermined origins using explicit source provenance,
precedence, and authored-draft state. A canonical reference alone does not
establish a verified relationship. Distinct line patterns supplement text; color
does not carry the distinction by itself. No renderer rule assigns PPS, boundary
approval, or provider coverage.

Initial review-package preview uses the saved working design projection; a
separate approved-output link uses `source=approved`. The structured relationship
editor selects canonical PPS and interconnection records and keeps agreement
status source-owned. The shared navigation guard also protects existing profile
forms when leaving for System design, not only departures from the design tab.
