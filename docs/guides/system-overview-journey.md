# System Overview: RMF journey and package work

The overview answers where the system is in RMF, which documentation work needs
attention, who has an explicit assignment, and what has actually been recorded
for documents, package handoff and monitoring.

The design reference is the supplied `ato-overview-full-page.html` outside this
worktree. Its example counts, phase and preview actions are not production data.
The existing application shell and system navigation remain unchanged.

## Local manual acceptance

Use the updated MCP and Dashboard with your existing authorized local
configuration and an isolated/sanitized test system. Do not change shared demo
records merely to populate the overview. Run both touched services: a newly
built Dashboard against an older MCP cannot provide the new work projection.

### Live preview on port 4196

The `agents/mission-tab-form-cleanup-ssp` worktree contains both the Dashboard
and matching API changes. On October 5, 2026, its existing Vite preview on
port **4196** was connected to an API rebuilt from that worktree, using image
`ato-copilot-mcp:mission-overview-1312bde8-20261005`.
Only the MCP container was replaced. Its environment, ports, network, user,
restart policy and data mounts were checked for equality; the SQL Server,
Redis, Chat and deployed Dashboard container IDs remained unchanged.

Open `http://localhost:4196` using normal authorized sign-in and select the
system's workspace. Refresh the Overview. If a retained readiness run has no
individual findings, choose **Check again** rather than interpreting the missing
detail as zero findings. Live API logs confirmed successful readiness and
grouped-work reads for both a retained run and a refreshed run. Requests without
the required identity or workspace access remain denied; no authorization
checks were disabled for this preview.

This verifies the matching live API deployment, not downstream document export,
AI output quality, monitoring connectivity or eMASS acceptance.

For synthetic browser acceptance, no retained-data or model writes are needed:

```bash
cd src/Ato.Copilot.Dashboard
npm run dev -- --host 127.0.0.1 --port 5198 --strictPort
```

In another terminal in the same directory:

```bash
PLAYWRIGHT_BASE_URL=http://127.0.0.1:5198 npx playwright test \
  e2e/tests/system-overview-journey.spec.ts \
  e2e/tests/system-readiness-actions-079.spec.ts \
  e2e/tests/system-role-next-actions-079.spec.ts --reporter=list
```

These intercept requests with synthetic records while exercising the actual SPA.
They do not prove live model availability, a healthy Azure collector or eMASS
acceptance. For manual business-record checks, open the selected system's root
Overview route through normal authorized sign-in.

### 1. Recorded phase versus browsing

1. Verify **A clear path to your ATO package**, followed by all seven phases:
   Prepare, Categorize, Select, Implement, Assess, Authorize and Monitor.
2. A default/legacy phase value with no explicit confirmation metadata must say
   **Current RMF phase: Not confirmed**. An explicitly confirmed phase shows
   its audit source, actor and local date/time. No earlier phase is marked complete.
3. Click Assess or use Left/Right/Home/End on phase buttons. Only **Viewing phase**
   and its educational/contextual work links change. Inspect the network/audit:
   browsing must not write phase records or readiness runs.
4. An authorized manager can choose **Confirm recorded phase**, enter a rationale
   and explicitly confirm. Existing forward/backward gate rules apply; the
   overview never forces a transition. A denied or rejected operation retains the
   rationale and does not announce successful confirmation.
5. A read-only account has no phase confirmation action. AI explanation is always
   labelled a proposal, not the recorded phase.

### 2. Readiness and grouped work

1. The page reads saved **Initial submission** readiness. It does not automatically
   validate or treat role suggestions as assigned work.
2. Verify blocking requirements, warnings and total **individual findings** against
   the retained result. Aggregate check counts and work-group counts are different.
3. Older runs without retained individual findings explicitly say the detail is
   unavailable. They require **Check again**; they must not claim zero findings
   or reinterpret their aggregate checks as a finding count.
4. Choose **Check again**. Verify the successful check time is readable in local
   time and source freshness is Current, Stale or Unavailable as reported.
5. Reject/disconnect a refresh. The previous successful result and its timestamp
   remain visible, with an explicit failure message. The old result is not a
   successful new check. A persisted latest failure also retains access to the
   previous successful run.
6. Expand work groups. Each group lists its actual underlying findings,
   severities, controls, document contribution, current verified assignment and
   existing workflow action. Expand **Technical identifiers** for retained IDs.
7. Compare all group totals with raw finding totals. Use group pagination and
   **Next findings** for groups larger than twenty. **View all findings** opens
   details on the current page, not thousands of unbounded cards.
8. A system-design priority appears only when an evaluated finding identifies
   review as a final-SSP prerequisite. It is labelled **Rule-based priority**,
   separately from AI suggestions. No owner or deadline is inferred.
9. Choose **Assigned to me**. Only explicit current Person assignments qualify.
   A role alone is not an assignment. Revoked/replaced/ambiguous assignments
   produce **Owner not provided** and are excluded from personal work.
10. If personal work is empty, system-wide gaps may still remain. Choose
    **View all system work**. Browse a phase and expand a group, follow its source
    workflow and return; ownership, section, browsed phase and expanded groups
    are encoded in the return URL.

### 3. Documents and milestones

1. Compare **Your ATO package** with canonical document records. A readiness
   finding shows **Gaps** independently of whether a document/section is Draft,
   UnderReview, Approved or missing. Individual recorded statuses are inspectable.
2. SSP sections, boundary/inventory, SAP, SAR, evidence, POA&M and monitoring
   records come from the existing workspace. Absence is **Not recorded**, not
   fabricated non-applicability or approval.
3. **Preview working SSP** uses the existing preview route. Working preview is
   not an approved export. Other outputs remain in their owning workflow; the
   overview does not invent a preview integration.
4. Preparation, export, human-recorded eMASS observations and recorded decisions
   are separate. Pending/failed package jobs are not completed exports. An export
   is not a submission. A recorded decision may be inactive, expired or a denial.
5. Following the page's read-only links must not change provider source,
   reviewed baselines, accepted responsibility or approval history.

### 4. Source-supported AI help

CI correction (October 7, PR 1064): the working-explanation React setter must
be named for local working state, not the document editor's `setContent` API.
The grounding guard remains unchanged; this is not an exemption for inserting
ungrounded claims. The following correction/refresh checks remain the local
manual acceptance path, not evidence of accepted document changes.

1. Open **Explain next action with AI** or **Explain this group with AI**.
   The explanation endpoint uses retained findings and a read-only source context;
   it does not expose mutation tools or write drafts, phases or approvals.
2. For a control-specific group, explicitly select a recorded provider scope.
   Published duties/splits are displayed directly as source facts, separately
   from AI interpretation and system acceptance. No scope uses available system
   records/evidence without assuming provider responsibility.
3. Inspect source versions, explanation-context hash and unresolved questions.
   This hash identifies explanation inputs, not necessarily the readiness run
   hash; the original run/source hash remains in the retained finding source.
4. Correct **Your working explanation** and refresh. The new suggestion is shown
   separately; user wording is not silently replaced. Explicitly choosing the
   refreshed suggestion is a working-view action, not an accepted record save.
5. Use existing narrative/requirement and responsibility links to map text or
   prepare actual source-supported draft responses. Their existing provenance,
   authority, edit preservation and independent review remain authoritative.
   **Map existing text with AI** and **Prepare draft responses with AI** request
   read-only suggestions against the authoritative catalog and saved narrative.
   Missing catalog/statement context is an error or question, not an invented
   requirement ID. Suggestions remain editable; their application belongs in
   the existing reviewed response workflow, not an overview auto-save.
6. Missing model/source access, source changes, oversized context and failed
   generation are explicit errors. No synthetic success text, evidence,
   implementation fact, test result or authorization metadata is supplied.

### 5. Monitoring and follow-up

1. Choose **Monitoring & follow-up**. The RMF journey remains visible.
2. Inspect actual coverage state, collector errors, resource scope, owners,
   rule versions, cadence, baseline references and evaluation dates. Enabled rules
   do not establish connectivity or health. Unsupported canonical scope is shown.
3. Inspect attributed changes, impact dispositions, affected evidence/document
   records, reviewer and rationale. Proposed updates remain separate from the
   reviewed baseline.
4. Follow the existing coverage/rules/change/impact/evidence/POA&M workflows.
   Overview reads must not trigger a scan, apply a proposal or change an
   authorization decision.
5. Empty or unavailable results do not establish complete visibility, cATO
   readiness or an unchanged system.

### 6. Keyboard, layout and permission checks

Use both themes at desktop and 390px. Phase/section/ownership navigation supports
keyboard access; disclosure controls expose individual findings. Confirm no
horizontal overflow, status meaning without color, preserved failed-save inputs,
and modal cancellation/focus handling. Try a denied system and a second tenant:
neither page/API route may reveal the first tenant's records.

## Verification scope and remaining limits

The implementation retains findings additively in existing readiness-check JSON;
there is no new table or replacement readiness/review store. Group projections
are server-paged, and existing authorized lifecycle/document/source contracts
remain authoritative.

Automated tests cover synthetic source/run/owner/phase/AI contracts, large
finding sets, permission/tenant/purpose fences, retained prior runs and downstream
package/design preservation. Browser tests exercise real SPA layout/interaction
with synthetic APIs.

Live AI quality, SQL Server concurrent phase writes, operational Azure collection,
retained-data final PDF/DOCX/eMASS receiving acceptance and actual submission are
not established by those fixtures. User manual acceptance and complete
modified-path coverage/build-warning gates must be recorded separately rather
than inferred from page rendering.
