# Local review and synthetic acceptance

## Local application and design review

Application implementation has started. The
[checkpoint](tasks.md#implementation-checkpoint-and-release-gates)
lists implementation and verification evidence. The production-host HTTP fixture
now passes the source-to-retained-export flow; live external integrations and
user acceptance are still separate gates.

The session's Dashboard preview is running at `http://127.0.0.1:5197`.
It uses the existing configured local API; that API has not been redeployed by
this session. A successful frontend view does not prove the new backend
package-purpose/provenance code is loaded. For backend local setup use the
repository's documented HTTP mode with an isolated database and approved
synthetic identities, not shared production data:

```bash
cd src/Ato.Copilot.Mcp
dotnet run -- --http
```

Confirm port/configuration against the [README](../../README.md#run-locally-http-mode)
and point `VITE_API_PROXY_TARGET` to that instance before starting a separate
Dashboard preview. Do not start a second server on an occupied port or reset
existing volumes.

Current provider URLs: `/workspaces/csp`, `/workspaces/csp/authorizations`,
`/workspaces/csp/systems`, `/workspaces/csp/provider-changes`, and
`/workspaces/csp/provider-administration`. Customer work remains in the existing
authorized organization workspace. On Documents, explicitly choose Initial
ATO submission to use the new purpose; legacy behavior is not silently relaxed.

Document previews open at
`/workspaces/organizations/{tenant}/systems/{system}/documents/preview`.
With the rebuilt API, inspect actual generated content, provider-source gaps
and content hash, then switch to OSCAL source. The current working-data warning
must remain visible for unapproved inputs. Retain the returned preview, inspect
its approved source pins, then explicitly confirm it before exporting exact
OSCAL bytes. Existing subscriptions may require explicit re-adoption
because migration never guesses a current release from historical records.

For archives/change bundles, select the purpose and choose a completed baseline
package and recorded decision from the server-supplied options. A change bundle
also needs a retained reviewed SSP preview. The selected IDs/hashes are pinned,
not copied from current form text or silently inferred. Retry after failure
requires fresh source selection and validation.

Provider source monitoring is at
`/workspaces/csp/provider-changes?tab=monitoring`; Systems monitoring has
`conmon`, `conmon/rules`, `conmon/changes`, `conmon/impacts`, and `conmon/reports`.
The written plan remains at `conmon/plan`. Rule evaluation and reassessment
recommendations are not authorization decisions.

### Regression commands

```bash
dotnet test tests/Ato.Copilot.Tests.Integration/Ato.Copilot.Tests.Integration.csproj \
  --filter 'FullyQualifiedName~ProviderMissionWorkflowHttpTests|FullyQualifiedName~DocumentPreviewHttpTests'
```

The real local HTTP fixture verifies exact retained output and access denials.
SQL Server testcontainers currently have an environment-blocked verification
run (execution timeouts and unresponsive Docker CLI). Restore Docker through the
environment owner, then rerun `dotnet test Ato.Copilot.sln`; do not mask failures
or reset shared volumes. The non-SQL integration filter used during this blocker
is reported separately and does not replace the full-suite gate.

Open locally:

- [Provider gallery](../../docs/design/provider-workspace-mock/gallery.html)
- [Provider interactive mock](../../docs/design/provider-workspace-mock/index.html)
- [Systems gallery](../../docs/design/system-overview-mock/gallery.html)
- [Systems interactive mock](../../docs/design/system-overview-mock/pages.html)
- [Screen/route matrix](contracts/screen-route-migration.md)
- [Proposed GitHub writes](github-issue-preview.md)

In the provider mock, follow `sources -> import -> analysis -> source-review ->
release -> allocation -> system-hosting -> system-capabilities -> system-duties
-> system-evidence -> system-documents -> system-package`. Confirm that
publication, association, and adoption are distinct actions, customer duties
remain open, and document output is explicitly synthetic. Inspect empty,
unavailable and restricted variants with the toolbar.

In the Systems mock, review all 30 pages and both overview scenarios.
At widths 1440px and 390px inspect navigation, layout, task actions and dialogs.
These observations validate design only, not backend behavior or cloud/eMASS.

To build documentation without changing tracked `site/` output:

```bash
python3 -m mkdocs build --site-dir /tmp/spin-feature079-docs-review
```

Use a new output directory if that location is already used by another process.
The build emits static HTML; no application or external service is started.

## Deterministic fixture contract (T003)

Create through existing supported test-host setup and services, not production
seed data or privileged ad-hoc SQL:

| Fixture | Definition |
|---|---|
| Provider | `DEMO Provider`, synthetic CSP admin identity; no customer membership inferred |
| Organizations | `DEMO Alpha` and `DEMO Beta`, explicit memberships and scoped roles |
| Systems | `DEMO Harbor` and `DEMO Beacon`; share a synthetic Azure subscription but disjoint resource scopes |
| Users | Provider admin/reviewer; Alpha Mission Owner; Alpha ISSM/ISSO; SCA; AO; Beta-only reader |
| Offering | `DEMO Azure Shared Services`, Azure scope with one reviewed allocation per system |
| Source decision | `DEMO Shared Services Decision`, `DEMO Review Authority`, source date `2025-04-17`, explicitly synthetic |
| Package | Decision/implementation/responsibility matrix/permitted summary, plus unreadable/encrypted/excluded/partial entry cases |
| Capability | Centralized audit collection, published version 1 then successor 2 with changed retention duty |
| Evidence | One shareable summary, one private attachment, one required customer configuration artifact |
| System content | Distinctive approved mission/user/data/ports values; different draft edits to prove baseline isolation |
| Clock | Fixed export/evaluation clock distinct from source decision date |

Reuse existing Harbor/synthetic package fixtures where their content meets these
assertions; do not substitute their sample authorization as a real provider fact.
The fixture must leave an inventory of generated IDs for each route and be
repeatable with cleanup scoped only to its owned test database/storage.

## Manual application walkthrough (user acceptance not recorded)

Use an isolated local stack following [existing setup](../../README.md) and
[CSP manual acceptance](../../docs/dev/csp-manual-acceptance.md). Do not reset
shared volumes, reuse historical live identity approvals, or enable cloud calls.
The existing guide uses `http://localhost:5173`; verify the running stack first.
Workspace-relative routes below must use the UI's actual selected workspace URL.
For CSP, existing entry is `/workspaces/csp/onboarding/csp`.

| Step / role | Route | Expected result |
|---|---|---|
| 1 / Provider | `/authorizations/import` | Real durable receipt; one result for retry of the same idempotency key |
| 2 / Provider reviewer | `/authorizations/offerings/{id}/packages/{packageId}` | Private candidates with per-file coverage gaps; no automatic publication |
| 3 / Provider reviewer | Existing package/capability review entry | Exact revision/hash approval publishes version 1; stale approval fails visibly |
| 4 / Provider | Offering hosting allocation view | Allocation persists, but mission association/adoption still absent |
| 5 / Alpha Mission Owner | `/systems/{harbor}/profile/EnvironmentAndDeployment/hosting` | Explicit association succeeds only for eligible scope; Beta or out-of-scope system denied |
| 6 / Alpha Mission Owner | `/systems/{harbor}/security-capabilities` | Apply exact published release; refresh preserves pinned version and open duties |
| 7 / Alpha ISSM/ISSO | `/systems/{harbor}/inheritance/subscriptions` | Authorized responsibility review/confirmation; MO alone cannot gain its authority |
| 8 / Alpha ISSO | `/systems/{harbor}/evidence` | Permitted summary accessible; private attachment denied; customer evidence actionable |
| 9 / Alpha ISSM/ISSO | Proposed `/systems/{harbor}/documents/preview` | Exact approved source values/releases/duties visible; draft edits excluded |
| 10 / Alpha ISSM/ISSO | `/systems/{harbor}/documents` | Initial package generated without AO decision; inspect actual files and source manifest |
| 11 / Alpha ISSO | `/systems/{harbor}/emass/status` | Export alone shows no receipt/accepted import; file conflict resolution remains explicit |
| 12 / Provider + Alpha reviewer | Offering impact and proposed system ConMon impact routes | Version 2 produces scoped review; version 1 documents/history remain readable |
| 13 / AO | `/systems/{harbor}/authorize` | Only explicitly authorized decision workflow can record authority; prior steps did not do so |

Reload after every mutation. Use separate browser profiles for different actors.
For same-actor concurrency, open two tabs, submit a stale edit, and verify
conflict handling without losing saved data. Revoke membership, restrict
evidence, fail collection and interrupt export using deterministic test controls;
verify visible errors and no false success.

## Manual exchange acceptance (local user review pending)

With a rebuilt API and an assigned ISSM/ISSO in a synthetic system, open the
eMASS page. Generate a package in Documents first if there is no retained export.

1. Verify an export alone leaves **Manual exchange history** empty.
2. Select that exact package. Record a receiving workflow, synthetic receipt
   reference and actual event time with **Receipt recorded**.
3. Reload and verify package ID/hash/version, actor and both timestamps.
4. Append **Import rejected** or **Partial import** using an external result
   reference. Verify this does not change the system's authorization decision.
5. Correct an observation with a reason; verify both original and correction
   remain visible. Do not overwrite or delete the original.
6. Use two tabs at the same history version. Submit in the first, then the
   second: the second must show a conflict and retain its entered fields until
   the user reloads/reviews. Do not silently retry a changed request.
7. As a Mission Owner or AO, verify history is readable without the outcome
   form. A user without system access, or from another tenant, must not see it.

For workbook reconciliation, review a retained field difference and select
**Accept returned value**. Confirm **Record resolution** remains disabled until
**Resolution rationale** contains text. Record a reason, reload, and verify
the conflict response and `EmassConflict.Resolve` audit retain it with the
authenticated actor and original values. **Keep SPIN** and **Defer** also accept
reasons; defer then resolve to verify both decisions remain in audit history.
Bulk acceptance requires its own reason and stops on a failed item. Change a
local field after comparison and verify acceptance rejects the stale difference
instead of overwriting it. These actions must leave manual exchange history
and authorization decisions unchanged.

Targeted automated commands (no live connector):

```bash
dotnet test tests/Ato.Copilot.Tests.Unit --filter 'FullyQualifiedName~EmassExchange'
dotnet test tests/Ato.Copilot.Tests.Integration --filter 'FullyQualifiedName~EmassExchangeEndpointsTests'
cd src/Ato.Copilot.Dashboard
npm test -- src/__tests__/components/EmassExchangeHistory.test.tsx
npx tsc --noEmit
```

## Evidence record per increment

Record build SHA, fixture IDs, role/workspace, exact route, actions, expected and
observed outputs, screenshots, generated-file hashes/field comparisons, test
commands/results, and limitations. Mark **implemented**, **automated checks
passed**, and **user accepted** separately. Keep live integration NOT RUN until
the external test was explicitly authorized and observed.
