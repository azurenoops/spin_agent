# Local provider package ingestion acceptance

This workflow extends Features 048 and 078. It must be manually accepted before
being declared complete. Local implementation/testing does not authorize a
Docker deployment, live provider changes, GitHub updates or pushes.

## Approved local AI connection - September 24, 2026

The retained Flank Speed receipt `b75da9a4-3b95-41ff-b87b-26668a412a77`
is incomplete, not a successful full-document import. Its four PDFs and twelve
worksheet entries report that text was extracted but semantic analysis is
unavailable. The structured JSON supplies the 154 candidates; the other two
processed entries are containers. Eight excluded entries are workbook metadata.
The running Docker service has no configured AI endpoint or Entra credentials.
UI routing tests and successful text extraction do not establish semantic coverage.

The user approved using the existing project
`https://ato-copilot-ai.services.ai.azure.com/api/projects/proj-ato-copilot-ai`.
Read-only Azure inspection confirmed the existing `gpt-5.4-mini-1` deployment,
OpenAI endpoint `https://ato-copilot-ai.openai.azure.com/`, and Entra-only
authentication (`disableLocalAuth=true`). A synthetic connectivity request using
the host's signed-in identity returned HTTP 200. The Docker identity and actual
analyzer request compatibility have not yet been verified.

Approved Azure changes: create the single-tenant daemon app/service principal
`ato-copilot-local-docker-ai` in tenant
`f465eb03-88ee-4389-980b-48e355eb8fda`, with no redirect URLs or Graph permissions.
Assign Cognitive Services OpenAI User only on `ato-copilot-ai`, resource group
`rg-ato-copilot`, subscription `9524e27e-9c67-4792-b5f2-5d6e3edbdf42`.
Create one credential named `docker-local-development`, expiring
`2026-10-25T02:17:15Z`; store it only in the ignored, owner-readable local `.env`.
Do not print the credential, enable API keys, grant subscription-wide permissions,
or create/replace model deployments.

Before retrying the retained receipt, verify the deployed model's exact request
compatibility and the container's Entra authentication. Preserve source identities,
human reviews, exclusions and publication gates. Never convert budget, model or
validation failures into successful coverage. A configuration-only restart is not
proof of recovery: measure the retry's per-source outcomes. Manual user acceptance
remains required.

The approved identity was created: client ID
`10e7bbea-799a-40dc-b81f-adeb1fa64c00`, service-principal object ID
`f16f4171-064d-45ba-9c5a-6c57d237f976`, scoped role-assignment ID
`c48e315a-416a-49b1-92b8-2d71d07d1b01`. Its credential is not recorded here.
Existing unrelated authority configuration is preserved; the AI client uses
its explicit Azure Public cloud setting.

Compatibility checks must exercise the real Azure OpenAI SDK serialization, not
only a mocked IChatClient. The approved deployment rejected `tool_choice: none`
without a tools array (HTTP 400); a synthetic JSON request without that parameter
succeeded with temperature zero and `max_completion_tokens`. Add transport-level
regressions for absent tools/tool choice and the bounded completion-token budget,
plus a Docker credential-forwarding contract test, before changing code/config.
Keep model-output validation and prohibition on tool execution unchanged.

The real transport regression confirmed that empty tools already omit
`tool_choice` correctly: no analyzer option change is needed. The actual defect
is Azure.AI.OpenAI 2.1's compatibility rewrite from `max_completion_tokens` to
`max_tokens`. Use that SDK's supported
`SetNewMaxCompletionTokensPropertyEnabled` extension in a thin chat-client
adapter, enabled only by `AzureAi:UseMaxCompletionTokens`. Leave the default
disabled for existing deployments. Forward the opt-in through Docker and enable
it locally for the approved model. This avoids a dependency upgrade, model-name
heuristics, custom HTTP payload rewriting, or removing the output-token limit.

Verified local deployment, September 25 UTC:

- The approved service principal completed an authenticated synthetic request
  against `gpt-5.4-mini-1` (HTTP 200).
- The real SDK transport test first failed on the legacy token parameter.
  After the opt-in adapter and credential forwarding, 89 focused transport,
  registration, semantic, resume and deployment tests passed. The adapter has
  100% measured line coverage. Streaming/synchronous/asynchronous calls preserve
  tool options and caller-owned options. Existing deployments remain opted out.
- A broader analyzer run exposed a missing existing Harbor ZIP test fixture.
  That run is not claimed green; the missing fixture was not replaced or hidden.
- Rebuilt/recreated only MCP. Dashboard, Chat, SQL and Redis container IDs
  remained unchanged. No volumes were reset.
- Retried retained receipt `b75da9a4-3b95-41ff-b87b-26668a412a77` once. Revision 4
  remains `NeedsAttention`: 3 processed containers/structured sources, 16
  incomplete document sources and 8 metadata exclusions. All 154 original
  candidate payloads, including review state/revision, compare unchanged by
  `candidateId`. The receipt remains unassociated and unpublished.
- The failure is no longer absent AI configuration. Nine document sources
  reported invalid/incomplete model output, one hit its call/overall timeout,
  and six were not reached before the total time budget expired.
- A separate, one-call, read-only diagnostic against the synthetic SSP captured
  the exact failure: ten page segments (22,708 input characters) produced an
  unterminated 27,637-character response and a non-normal completion. The SDK
  connection is working, but this batch cannot be accepted. The temporary live
  diagnostic test was removed; no live-cloud dependency remains in unit tests.

Full-document ingestion is **not resolved or accepted**. The next correction
must bound batches by output needs and define whether a long import continues
through bounded background passes or requires explicit manual retries. Do not
raise limits blindly, accept partial JSON, weaken citation validation, or label
the existing receipt complete merely because AI connectivity now works.

Approved follow-up: continue long imports automatically in smaller, bounded
background passes, showing durable progress. Split multi-segment batches after
an output-limit completion or a per-call timeout; never accept the partial
response. Retain the learned batch size, successful segment acknowledgements,
and cumulative call charges in the server-owned checkpoint. A total pass timeout
may enqueue only its unfinished entries for another pass, within the original
call ceiling. Invalid citations/schema, single-segment failures, exhausted
budgets and no-progress passes must stop explicitly, not loop.

Use the existing Received/Processing queue and lease fencing, not an in-memory
timer or new database table. Add optional progress to the authorized receipt
response (completed/total segments, calls/limit, automatic-continuation flag).
Old checkpoints and clients remain valid. Preserve source bytes, human
candidate edits/reviews, exclusions, explicit association, and publication gates.
The progress denominator excludes explicitly excluded sources: exclusions must
never count as analyzed segments. When child analysis completes, refresh the
container's aggregate coverage reason without rewriting its retained bytes or
human decisions. Restart tests must verify that permanent validation errors are
not included in the automatic retry selection.

The first deployed continuation retry reached revision 7 using 27 of 64 calls.
Two bounded passes were observed in the authorized browser, but all 16 narrative
sources remained incomplete and no new candidates were accepted. All 154
original candidates, source hashes and exclusions remained unchanged. A separate
one-call synthetic SSP diagnostic then returned valid JSON with `stop` completion,
but all 32 proposals used the unsupported literal key `required citations`, and
the root omitted `familyCoverage`. The prompt itself describes "and required
citations" in its property list and initially specifies only two root fields
before adding a third later. Correct the contradictory contract wording and give
literal JSON property names/examples; do not normalize unexpected fields or
weaken the validator. This diagnostic does not establish the failure cause of
every other source.

After the prompt correction, the same bounded SSP diagnostic produced the
correct three-field root and canonical citation properties. Validation then
rejected citations: one proposal referenced an unknown long segment hash, others
used the hash of the wrong page, and one quote reconstructed non-contiguous table
text. Use short batch-local segment aliases on the model wire and resolve them
only through the server-owned supplied batch. Persist the original stable source
keys, reject unknown aliases and keep exact quote/field validation unchanged.
Explicitly forbid reconstructed table rows and joined quote fragments.

The corrected aliased SSP probe returned a normal `stop` response containing
36 proposals with no validator exceptions; the one-call diagnostic deliberately
stopped before later pages. Temporary live tests were removed from the unit
suite. Focused offline verification passed 165 backend tests; 32 focused
Dashboard tests, `tsc --noEmit` and the Dashboard production build passed.
The broader package run reported 345 passed and three failures: unimplemented
profile-schema fields, a missing candidate profile projection, and the missing
Harbor ZIP fixture. A separate Dashboard upload test failed because its
Node WebCrypto stub rejected a JSDOM FileReader buffer. These runs are not
represented as fully green.

Modified continuation/receipt paths measured 84-96% line coverage in the first
coverage run; the receipt UI measured 94.33%. Docker builds used the existing
NuGet archive context and the previously verified Microsoft npm mirror. The
default-registry cached Dashboard install lacked `tsc`; no dependency versions,
lockfile, application limits or credentials were changed to bypass that failure.
Only MCP and Dashboard were replaced. SQL, Redis, Chat and their volumes were
preserved. The authenticated Chromium check observed saved progress and automatic
continuation on the existing offering-scoped receipt.

Final deployed verification (September 25): the corrected-contract retry reached
revision 9, with 286/428 included source segments analyzed, 45/64 cumulative
model calls, 13 processed entries, six incomplete entries and eight exclusions.
It added 100 `NeedsReview` proposals (254 total). The receipt is still
`NeedsAttention`, unassociated and unpublished; no limits were reset. The
remaining sources are the SSP, assessment and operations PDFs, and workbook
worksheets 4, 6 and 8. Their exact latest validator sub-reasons have not yet been
captured; do not assert they all share the diagnosed SSP prompt defect.

The strict before/after comparison intentionally failed because seven older
projected claims changed 39 derived relationship resolutions from `Resolved` to
`Ambiguous` after duplicate source identifiers were introduced by new proposals.
Inspection traced this to existing checkpoint relationship recomputation and
read-only legacy claim recovery. All other original candidate fields, including
review decisions/revisions and asserted source facts, compared unchanged; source
hashes, lengths and exclusions also remained unchanged. Do not hide these
ambiguities or describe the complete candidate responses as identical. They
require explicit reconciliation/review.

Authenticated Chromium reload retained the final segment/call counts, showed
`Needs Attention`, and removed the automatic-continuation message. MCP and
Dashboard are healthy at the local host ports 3002 and 5173; SQL, Redis and Chat
container identities stayed unchanged. End-to-end ingestion and user acceptance
remain open. No commit, push, GitHub write, association or publication was made.

Follow-up diagnosis (September 25): six isolated, one-call replays of the actual
pending batches reproduced validation failures without modifying the receipt.
The responses invented/combined field text, returned a string instead of the
authorization-reference object, labeled Responsibility proposals as
`Inventory/NoDeclarations`, misclassified POA&M as authorization references, and
put long duty descriptions into the 50-character responsibility field. The
prompt first restricted kinds to the original five, then separately permitted
four more, without defining their classification or family mapping.

Correct that contradictory internal contract and provide explicit examples for
claims and responsibility roles. Let the model cite a supplied segment key
without copying its text: the server supplies that segment's exact retained
quote. Continue accepting and strictly checking explicit quotes for compatible
responses. Neither mode may accept unknown keys or fields absent from the cited
text. This removes the unnecessary model quote-reconstruction step, not the
grounding check. Fix alias-aware duplicate-citation detection as part of the
same validation path. Preserve cumulative charges, human reviews, exclusions,
stable persisted source keys, and all association/publication gates. Verify
bounded actual pending batches before another full receipt retry; no completion
is claimed by these diagnostic calls.

The first corrected diagnostic passed the operations PDF and two worksheet
batches, but three sources still failed: the PDF table text had interleaved
columns that the model paraphrased, claim qualifications lacked bindings, and
POA&M candidates omitted their required typed payload. Use the SDK's strict JSON
schema response format to enforce kind-specific shapes, not prompt wording
alone. Require claims for claim kinds and bounded role fields. When a model
omits field bindings, derive them server-side using the existing exact-support
predicate; explicitly supplied bindings remain strictly validated. This is
mechanical provenance assembly, not inference or automatic acceptance. Keep
whole-batch rejection, exact field/quote support and the original budgets.

The strict-schema replay passed the POA&M worksheet batch with six typed
proposals. Across the bounded diagnostics, the operations PDF and worksheets
4, 6 and 8 now have accepted batches; this does not prove every pending segment
has completed. The SSP still reconstructs interleaved role/duty columns, and
the assessment PDF still reconstructs two milestone descriptions. Their
unsupported values remain rejected. No retained receipt retry, deployment,
budget reset or human-decision change was performed during this follow-up.
The temporary cloud diagnostic test has been removed. PDF reading order and
immutable citation provenance remain the next unresolved design/correction.
The extractor currently joins `GetWords()` results with spaces for each page;
the observed table cells consequently interrupt one another's text. Replacing
that retained text under an existing segment key would invalidate old citations
and is not an acceptable repair. A layout-aware correction must preserve the
original evidence representation and its references.

Offline validation after this follow-up: 66 focused semantic tests passed.
The package-wide run passed 331 tests and failed the same three previously
recorded cases (profile schema fields, profile projection, missing Harbor ZIP).
The old missing-binding test now explicitly supplies an incomplete binding
array, so it continues testing atomic rejection rather than the intentionally
supported omission/derivation path. Modified analyzer files exceeded 90% line
coverage; the new response-schema builder reached 100%. No Dashboard code was
changed. No full-suite success or manual acceptance is claimed.

Approved continuation (September 25): correct PDF reading order and recover
selected unfinished entries without changing their retained page strings.
Use the installed PDF layout library to group words into spatial text blocks.
New extraction records its layout version. For a legacy unfinished page, retain
the original segment and append a versioned layout view with its own locator
and stable key. Persist the old-to-new analysis-view mapping in the existing
server-owned entry checkpoint. Only active views participate in semantic/family
coverage and progress; superseded views remain available to every old citation.
Already analyzed pages, excluded entries and unselected entries are untouched.
Replacement pages and added text are charged against existing extraction
budgets; model charges remain cumulative. Validate mappings on restart and
never count an unprocessed new view as analyzed. No new table is needed for
this recovery metadata.
Layout work is bounded to 5,000 words and 512 blocks per page before the
reading-order graph; all grouped words/blocks must be conserved. Exceeding a
limit is an explicit source failure, not partial successful text.

The first conservation regression was traced to PdfPig emitting standalone
zero-height space tokens between real words on simple pages. Docstrum correctly
omits those separator tokens and reconstructs spacing between words. Normalize
whitespace-only tokens before grouping and conservation checks; do not remove
any text-bearing word or accept a partial layout. A portable single-line fixture
covers this behavior. The separate combined-checkpoint isolation fixture also
needs the family-coverage maps from every combined original; the production
checkpoint validator correctly rejected its incomplete test setup.

After these corrections, all 355 package tests passed (none skipped). Measured
line coverage for the new layout implementation is 81.31%; the response-schema
builder is 100%. The three previously recorded profile/fixture regressions are
included in this green run.

Isolated replay of the retained synthetic PDFs used one model call per attempt,
with unchanged production input/output/time bounds and no receipt writes. The
assessment accepted twelve pending pages and 29 proposals. The SSP first
rejected an unsupported field atomically; a second isolated attempt accepted ten
pages and three proposals. This demonstrates the new views can be analyzed, not
that arbitrary model responses are reliable or that the full import is complete.
The temporary cloud harness was removed before deployment.

Docker verification (September 25, 17:31 UTC): rebuilt the MCP image, recreated
only that service, and verified healthy MCP/Dashboard responses. SQL Server has
the additive profile, family-coverage and claim columns. No volumes or data were
reset. A single retained-receipt retry advanced revision 9 to 11:

| Measure | Before | After |
|---|---:|---:|
| Analyzed active source segments | 286/428 | 383/428 |
| Cumulative model calls | 45/64 | 58/64 |
| Complete / incomplete / excluded entries | 13 / 6 / 8 | 16 / 3 / 8 |
| Unreviewed proposals | 254 | 439 |

All 428 original segments and every old citation are unchanged. The checkpoint
appended 94 layout views (522 retained representations, still 428 active source
segments); cumulative PDF-page charges increased to 202. All original artifact
identities/hashes, exclusions and review states were preserved. Five derived
relationship resolutions changed from Resolved to Ambiguous as additional
proposals exposed duplicate targets; no asserted source facts changed. All 185
new proposals require review. The receipt remains unassociated and unpublished.

The fresh authenticated browser displayed the new progress with successful
package API responses and no page errors. This is not full ingestion acceptance:
28 SSP pages, four Controls worksheet rows and thirteen Roles worksheet rows
remain incomplete after model responses failed strict validation. Six calls
remain under the existing ceiling. Do not reset the budget or repeatedly retry
without investigating the rejected batches. Human review/duplicate ambiguity is
separate from these 45 genuinely unfinished source segments.

Approved follow-up: allow one corrective response for a completely received but
invalid semantic batch, within the existing cumulative 64-call ceiling and
unchanged time/output limits. Send bounded validator feedback identifying the
candidate/field where available, never the entire rejected response. Resubmit
the same retained source segments and strict schema. Do not retain any rejected
proposal, bypass validation, infer source support, or reduce the original
coverage obligation. A second invalid response leaves the source incomplete.
Malformed streaming protocol, tool output, transport errors and cancelled work
are not corrective-response candidates. The normal budget checks apply before
the extra call; no receipt budget is reset or increased.

Bounded correction verification (September 25, 17:52 UTC): all 362 package tests
passed. Seven new cases cover correction success, repeated invalid output,
cumulative limits across restart and bounded/untrusted diagnostic labels.
Existing rejection cases still reject atomically; only their expected call
count changed from one to two for complete invalid responses. Tool, protocol
and provider failures are verified to remain single-attempt failures.
Changed semantic paths measured over 92% line coverage.

The corrected MCP image was rebuilt/deployed. A single guarded retry advanced
revision 11 to 13, finishing the SSP and Controls worksheet: **415/428 segments,
64/64 cumulative calls, 18 complete / 1 incomplete / 8 excluded entries, 486
unreviewed proposals**. Runtime logs show the Roles worksheet received one
corrective response but failed strict validation again; its thirteen segments
remain incomplete. The total call ceiling is now exhausted and was not raised
or reset. This is not full import completion.

The latest retry preserved all 439 existing candidate payloads/review states,
artifact hashes and exclusions; all 47 new proposals require review. Across both
deployments, all 428 original segments and all original citations remain
identical. The receipt is still unassociated and unpublished. Further model
execution requires an explicit additional allowance and investigation of the
remaining rejection; another blind retry cannot bypass the exhausted ceiling.

Relationship decision: the user chose to keep ambiguous links for manual
review, rather than introduce a new target-selection workflow. The audit found
different JSON/workbook payloads and provenance for the same twelve component
identifiers; existing tests explicitly require this case to remain ambiguous.
No relationship was forcibly resolved or source proposals silently merged.

### Source exclusion notices (September 25)

The live source manifest returns identical `reason` and `exclusionReason` values
for intentionally excluded workbook metadata, including `docProps/app.xml` and
`xl/theme/theme1.xml`. Before this correction, the Dashboard rendered both values
as separate amber warnings, duplicating one explanation and making expected
exclusions look like analysis failures.

Render an excluded entry's explanation once as a neutral "Excluded from
analysis" notice. If only the general reason is available, retain it in that
notice. Preserve any distinct processing failure as a separate warning, along
with semantic-family warnings, source downloads, exclusion status and manifest
accounting. Do not infer successful analysis or change the receipt, review,
publication or model-call budgets. The Roles worksheet failure remains a real
incomplete-analysis warning.

Manual acceptance: refresh the retained package in Authorizations. Verify
`app.xml` and `theme1.xml` each show one neutral notice and remain Excluded and
downloadable. Verify the Roles worksheet still shows its analysis warning.

Verification: five new regression cases failed before the component change.
Afterward, 44 focused entry, recovery, candidate-review and progress tests passed;
the entry component reached 100% line coverage and 90.62% branch coverage.
Dashboard type-check and production build passed. The broader package-import
suite returned 115 passing and nine failing tests, including the previously
recorded Node WebCrypto/JSDOM upload-buffer failure; no full-suite success is
claimed.

The default-registry cached Docker dependency layer again lacked `tsc`; the
previously approved Microsoft npm feed built successfully without deleting
shared caches. Only the Dashboard was recreated, with a rollback image retained.
All five services remained healthy and both IPv4 and IPv6 localhost returned
HTTP 200. An authenticated Chromium visit verified one white-background notice
per highlighted metadata entry, enabled downloads and the preserved Roles
warning, with no page errors or failed package API calls.

The pre/post-deployment receipt and all 27 entry DTOs were identical. The receipt
was already revision 14 with an offering association before this UI deployment;
that association was preserved. Analysis remained at 415/428 segments and 64/64
model calls. No retry, exclusion, review, approval or publication request was
sent. User manual acceptance remains pending.

### Local Docker hostname reachability

On September 25, the Docker site responded over IPv4 but `localhost:5173`
timed out over IPv6. Inspection found Docker's wildcard listener and a separate,
paused `electron-vite` process from the Jarvis repository (PID 95622) bound to
`[::1]:5173`. The hostname request reached that conflicting listener, not the
healthy dashboard container. The user explicitly approved stopping only that
Jarvis development-server process. No Docker restart, data reset or package
retry is part of this repair.

After the approved termination, Docker was the only listener on port 5173.
IPv4, IPv6 and default `localhost` requests all returned HTTP 200. A fresh
authenticated browser loaded the user's exact offering/package URL with
successful package API requests, current 415/428 progress and no page errors.
All five existing Docker containers remained healthy without being recreated.

For local acceptance, verify both `curl -4` and `curl -6` against the advertised
`localhost` URL, plus an authenticated browser visit using that same hostname.
A successful `127.0.0.1` check alone does not prove `localhost` reaches Docker.
Do not run unrelated development servers on the Docker dashboard's port.

## Authorization-led follow-up: implementation in progress

The superseding workflow is defined in
[the provider authorization contract](../../specs/078-role-aware-workspaces/contracts/provider-authorizations.md).
Authorizations will own offering, external decision, boundary, package versions,
findings and source review. Security Capabilities remains the reusable catalog
and release pipeline. The older package-only URLs and results below describe the
currently verified implementation, not completion of the new workflow.

The local inputs under
[`output/pdf/csp-ato-test-package/`](../../output/pdf/csp-ato-test-package/README.md)
now provide a production-style, explicitly synthetic Flank Speed IL5 / Microsoft
365 DoD tenant package. Its contract includes twelve logical components, twenty-four
capabilities, forty representative control narratives, assessment/operational
annexes, six open findings/remediation plans and an unsigned decision draft.
Neither the sample boundary nor Microsoft inheritance is verified authority.
The original nine-page Harbor PDF is preserved in the unit-test fixtures for
the existing analyzer regression. "Clean" means structurally readable sources,
not complete analysis or an absence of findings. Expected-result files remain
test targets, not measured application outcomes. This artifact-only change
does not alter application processing, approval or publication behavior.

The replacement contains 108 PDF pages, a structured import projection and a
registers workbook. Eight artifact checks and two focused analyzer tests passed
(Flank Speed JSON plus preserved Harbor PDF); this is not a full-suite result or
live import acceptance. Start manual intake with the clean ZIP linked in the
package README. The needs-attention variant adds encrypted, malformed and
unsupported entries to exercise explicit per-source outcomes.

New acceptance gates, still pending application implementation/verification:

1. Create two distinct offerings and boundaries from **Authorizations → Import
   existing authorization package**. Upload with stable receipt recovery and
   retain separate package versions.
2. Confirm onboarding can continue after receipt without reviewing inventory.
   Unconfirmed decision claims and unpublished inventory must remain so.
3. Review source citations, boundary exclusions, proposed coverage and remaining
   duties. Findings and POA&M must not appear as capabilities; submitted evidence
   must leave findings open pending explicit review.
4. Approve an exact eligible inventory/dependency/impact set, then publish
   separately. Modify a bound revision to verify stale approval rejection.
   Prior releases and unrelated drafts must remain unchanged.
5. Associate a mission system with an assigned offering/environment/resource
   scope. Confirm that this alone does not grant authorization or inheritance.
   Check published-version suggestions and protected-source access separately.
6. Confirm covered-workload status is restricted to that mission system's
   **Authorizing Official**, with authority/evidence and exact recorded scope.
   Ordinary system and provider administrators must be rejected.
7. Revise/withdraw/supersede decision or scope data and inspect affected inventory
   and mission reviews. Existing historical decisions, releases and approved
   narratives must not be silently rewritten.
8. Exercise denied access, partial extraction, lost-response retry, refresh,
   retained approval and restart recovery at desktop and narrow widths.

No new authorization-led runtime success is claimed by this checklist. Manual
user acceptance remains open.

### Scope-review API regression - September 24, 2026

The live Flank Speed receipt reproduced HTTP 422 for
`candidates?type=BoundaryClaim`. The service validates only the five original
candidate kinds, while its analyzer and Dashboard use four additional claim
kinds. Unfiltered reads contain two boundary candidates, but the response DTO
and processing projection omit their typed claim fields.

Correct the filter against the existing candidate-kind enum and persist/project
the existing typed claim in candidate payloads. Existing receipts must recover
the claim only from their own retained analysis checkpoint, matching candidate
identity, kind and ordered citations; never infer scope from a name or replace
human edits. Keep strict invalid-filter rejection, provider access checks,
review state, processing exceptions and publication gates unchanged. No new
tables, migration, re-upload or reanalysis is required for scope suggestions.

The receipt's sixteen exceptions separately report unavailable complete semantic
analysis for four PDFs and twelve workbook sheets. Eight workbook metadata parts
are explicitly excluded. The structured JSON produced 154 candidates. Fixing
scope review must not mark those other sources analyzed or claim complete
package coverage. Verify the service, HTTP contract and saved receipt before
manual acceptance.

Live verification also exposed that excluded and included boundary claims were
offered with identical selection controls. Show each claim's stated relationship.
Only a cited, non-rejected, explicitly `Included` claim with a scope may prefill
the included boundary. Keep exclusions and undetermined claims visible for
source review, explain their disabled selection, and retain manual entry.

Verification for this correction:

- Red tests reproduced the four unsupported kind filters, missing typed payload,
  saved-receipt projection, and selectable excluded/undetermined scope.
- 71 focused service/worker/review tests and 8 package-lifecycle HTTP tests passed.
  Compatibility recovery has 100% measured line coverage in the focused run.
- 15 focused Dashboard scope/import-handoff/association tests passed.
  `FileFirstImport.tsx` has 100% line coverage (73.33% branch coverage).
  Dashboard `tsc --noEmit` and production build passed.
- A broader adjacent intake selection still fails four tests in
  `OfferingIntake.test.tsx` (receipt/reload and uncertain-upload handling).
  That component was not changed here; these failures remain unresolved.
  This is not a full-suite green claim.
- Rebuilt/replaced MCP and Dashboard only. Both containers are healthy; SQL,
  Redis and Chat retain their original container IDs and startup times. No
  database reset, source re-upload, association, boundary save or publication
  was performed during browser verification.
- The original Flank Speed receipt returns HTTP 200 and two cited typed boundary
  claims. Included scope can prefill the editable boundary; excluded scope is
  visibly labeled and disabled. Verified the 1,065-character included scope
  and source subject in the boundary form without saving.
- Receipt revision 2, 154 candidates, 3 processed / 16 unsupported / 8 excluded
  entries and unpublished state are unchanged. Local Docker has
  `ATO_AZUREAI__ENABLED=false`; complete PDF/workbook semantic analysis remains
  unavailable. Those warnings were not bypassed.

The saved receipt URL can be reloaded without another upload. Select the
**Included** source statement, choose the intended existing offering (or create
one), and review the populated boundary and citations before saving. Exclusions
must remain exclusions. User manual acceptance is still pending.

### Profile-2 extraction verification

The isolated analyzer run passed 214 tests. A subsequent six-test fixture run
passed the new Azure authorization fixture tests together with the original
Azure and Harbor checks; these are overlapping selections, not 220 distinct
tests or a full-suite regression result.

[Azure authorization example](../examples/package-imports/azure-authorization-example.json)
is an additive structured fixture that completes all five analysis families
without an AI client. Its 23 source-supported proposals comprise four
components, four capabilities, four control mappings, four responsibilities,
one explicitly fictional decision, two boundary claims, two findings and two
POA&M items. All 12 typed relationships resolve; checkpoint serialization and
replay preserve identities, claims and citations. Existing Azure and Harbor
fixtures are unchanged. Analyzer completion confirms neither external authority
nor eligibility to publish.

The user confirmed that reviewed offering capabilities may be published without
a current recorded external authorization. The normal exact-set approval and
impact gates still apply. Keep the fixture's fictional decision unconfirmed/
ineffective as appropriate, show authorization as not established, and verify
that covered-workload confirmation is blocked rather than changing the source
to manufacture authority. Publication of reviewed service functions is separate
from an authorization assertion.

Use this structured fixture for the eventual no-model browser acceptance flow.
Harbor narrative sources still exercise conservative labeled extraction and
honest incomplete-analysis handling; arbitrary prose, unsupported layouts,
OCR-dependent or encrypted content do not become complete merely because their
text or archive entry was accounted for. No live model call or Azure mutation
was used for these tests.

## Safety and prerequisites

Use a disposable local database and file-storage root with an authorized ordinary
CSP administrator. Do not use a customer workspace, impersonation session, real
ATO package or the shared reference library for automated acceptance.
Do not reset existing databases or Docker volumes.

### Explicit clean-slate reset approval (2026-09-24)

The user separately authorized an irreversible reset of this local app's test
data, including Chat. The approved scope is exactly these five verified
`ato-copilot` Compose volumes: `ato-copilot_sql-data`,
`ato-copilot_redis-data`, `ato-copilot_ato-data`, `ato-copilot_ato-logs` and
`ato-copilot_chat-logs`. Stop the app containers before deleting those volumes,
then start the current images without rebuilding or seeding test scenarios.
Other Docker projects, images, source files and the mounted Azure credentials
are outside that approval and must remain untouched.

Fresh startup can recreate schema, built-in reference/configuration records,
health-check activity and logs. Their presence does not mean prior user data
survived. Previous provider, organization, system and package examples must not
be assumed to exist after the reset. All manual acceptance scenarios start
unexecuted; resetting the data is not acceptance of any CSP feature.

Reset verification: all five volumes were recreated at
`2026-09-24T15:54:59Z` and all five containers became healthy. Dashboard, MCP and
Chat returned HTTP 200. CSP/provider business tables and registered systems had
zero rows; Chat tables and `/data` were empty and Redis reported zero keys.
Fresh reference data consisted of 1,196 NIST controls, four overlay documents,
one migration flag and the `Ato.Copilot.System` tenant with the all-zero ID.
The nine development identities remain configuration, not seeded memberships.
Start the [CSP manual acceptance guide](csp-manual-acceptance.md) from `/login`;
no scenario data was created during reset verification.

### Local Docker upload persistence

Local Docker startup was separately approved for current-worktree review on
2026-09-24; this does not establish acceptance of the unfinished workflow.
The MCP service mounts the named `ato-data` volume at `/data`, preserving local
package sources and evidence across container replacement. Keep the existing
SQL Server and Redis volumes; do not remove volumes when rebuilding the app.
Before first adding this mount to another environment, preserve any existing
files in the container's `/data` directory rather than hiding them with an empty
volume. That directory was checked and empty in this local deployment.

The current Dashboard and MCP images built using the repository's documented
NuGet archive context and npm mirror. Both containers became healthy; the new
Dashboard JavaScript and MCP health endpoint returned HTTP 200. A temporary
file survived removal of its writer container and was read through the same
volume by a second container, then deleted. SQL Server and Redis were not
recreated. Browser verification reached sign-in; authenticated workflow
acceptance remains pending.

The existing local setup/build instructions in [contributing.md](contributing.md)
apply. The Dashboard Vite server proxies `/api` to `localhost:3002` by default;
`VITE_API_PROXY_TARGET` can target a separately started local MCP process.
The local provider UI route is:

`http://127.0.0.1:5174/workspaces/csp/security-capabilities/imports`

The onboarding upload remains at `http://127.0.0.1:5174/onboarding/csp`.
These URLs require a running build of the new source and proper authorization;
an existing Docker image does not automatically contain local edits.

### SQLite package and publication prerequisites

SQLite startup runs the historical EF migrations followed by additive schema
modules; it does not use SQL Server's model-based table discovery. The historical
SQLite baseline omits `CspInheritedComponents` and `CspInheritedCapabilities`.
Without these tables an upload can return a durable `202`, but analysis fails
while checking published duplicates before saving its checkpoint.

`CspInheritedCatalogSchemaAdditions` creates both missing catalog tables and their
indexes before `WorkspaceOperationsSchemaAdditions` runs. The new catalog module
does not update or remove existing catalog rows. Existing startup modules supply
the rest of the package-to-release path:

- The private package, entry, candidate, approval and audit ledgers.
- Canonical working revisions, contributors, duties, publication previews,
  releases and release impacts.
- Capability subscriptions and provider responsibility source events.

The migrated-baseline startup regression checks these tables in both deployment
modes and repeats startup against the same database, preserving the existing
package, published contributor and unreviewed capability. This is distinct from
an `EnsureCreated` test database.

When testing this correction, build the current source into isolated artifacts
and start that binary against the same disposable SQLite database and evidence
root. Do not delete the database, switch providers or suppress a DDL error. A
running process does not automatically load source edits. After successful schema
initialization, explicitly retry the retained failed package; a second upload is
not required for this missing-table failure.

### Start a separate local demo

The earlier verified demo used ports 5174 and 3004. A later implementation-stage
check found neither listener active; do not assume these URLs are currently
running. To create your own, run the following from the repository root with
those ports free. Keep the
same `demo` directory for restart/recovery testing; use a different empty
directory for a new onboarding scenario. These commands do not reset data or
change Docker. The development-only identity is synthetic, not an auth bypass.

```bash
demo="$PWD/TestResults/package-demo"
mkdir -p "$demo"
dotnet build src/Ato.Copilot.Mcp/Ato.Copilot.Mcp.csproj \
  --artifacts-path "$demo/build"

env \
  ASPNETCORE_ENVIRONMENT=Development \
  ATO_Server__Urls=http://127.0.0.1:3004 \
  ATO_Server__Port=3004 \
  ATO_Database__Provider=SQLite \
  "ATO_ConnectionStrings__DefaultConnection=Data Source=$demo/database.sqlite" \
  "ATO_Evidence__LocalStoragePath=$demo/evidence" \
  ATO_Deployment__Mode=MultiTenant \
  ATO_AzureAi__Enabled=false \
  ATO_AzureAi__Endpoint= \
  ATO_AzureAi__FoundryProjectEndpoint= \
  ATO_CacAuth__SimulationMode=true \
  ATO_CacAuth__SimulatedIdentity__UserPrincipalName=package-admin@example.invalid \
  'ATO_CacAuth__SimulatedIdentity__DisplayName=Synthetic package administrator' \
  ATO_CacAuth__SimulatedIdentities__0__IdentityId=package-admin \
  'ATO_CacAuth__SimulatedIdentities__0__DisplayName=Synthetic package administrator' \
  ATO_CacAuth__SimulatedIdentities__0__Oid=aaaaaaaa-1000-4000-8000-000000000001 \
  ATO_CacAuth__SimulatedIdentities__0__Tid=aaaaaaaa-1000-4000-8000-000000000002 \
  ATO_CacAuth__SimulatedIdentities__0__TenantId=aaaaaaaa-1000-4000-8000-000000000002 \
  ATO_CacAuth__SimulatedIdentities__0__Roles__0=CSP.Admin \
  ATO_CacAuth__SimulatedIdentities__0__Persona=CspAdmin \
  dotnet "$demo/build/bin/Ato.Copilot.Mcp/debug/Ato.Copilot.Mcp.dll" --http
```

In a second terminal:

```bash
cd src/Ato.Copilot.Dashboard
VITE_API_PROXY_TARGET=http://127.0.0.1:3004 \
  npm run dev -- --host 127.0.0.1 --port 5174 --strictPort
```

Open `http://127.0.0.1:5174/login` and choose **Synthetic package administrator**.
This model-disabled demo validates deterministic structured extraction; it does
not claim semantic analysis of arbitrary narrative documents. Such inputs must
retain explicit coverage exceptions. Simulation is unavailable outside the
Development environment.

### Recover an obsolete development sign-in

Open `http://localhost:5174/login` and choose **Synthetic package administrator**
if using the already-running localhost demo. Use one hostname consistently:
`localhost` and `127.0.0.1` have separate cookies, but ports on the same hostname
share cookies.

A previously selected development identity can disappear from configuration.
Its stale `ato-simulation` cookie must not block public login configuration or
the gated identity selector. Both authentication and tenant middleware now
recognize exactly `GET /api/auth/login-config` and `POST /api/auth/simulate` as
pre-session requests. The simulation endpoint retains its environment,
configuration, identity-validation and audit gates; no protected route becomes
anonymous. A protected request with a stale cookie still returns 401.

The Dashboard preserves that specific error instead of trying MSAL renewal,
then returns to login with an explanation and the existing identity selector.
It never picks a replacement identity automatically. Clearing cookies is not
required.

The real-browser regression used a synthetic stale cookie on `localhost:5174`:
bootstrap 200, protected imports 401, explicit identity selection 204, then the
previously published package remained visible. Targeted checks passed: 51
backend CAC/tenant tests, 19 login/package HTTP tests, and 333 Dashboard tests.
Dashboard type-checking and production build passed; build warnings and the
full-suite qualifications below remain. The local API was rebuilt against the
same database and evidence directory; Docker was not changed.

Manual acceptance:

- [ ] Reload the previously failing page; login configuration loads.
- [ ] Select the configured synthetic administrator explicitly.
- [ ] Open Security Capabilities -> Review imports and confirm retained records.
- [ ] Refresh and confirm the chosen session and saved package outcomes remain.

## Synthetic inputs

Only the files in [package-import examples](../examples/package-imports/) are
intended for this checklist. They are synthetic and make no authorization claims.

### Azure example package

[azure-example-package.json](../examples/package-imports/azure-example-package.json)
uses the existing structured inventory contract
and can be uploaded on its own. It contains four example components and four
capabilities, four proposed control mappings, and four proposed shared
responsibilities:

| Example component | Example capability | Proposed control |
| --- | --- | --- |
| Microsoft Entra ID | Identity and access management | IA-2 |
| Azure Monitor | Audit review and monitoring, with Entra ID as a second contributor | AU-6 |
| Azure Key Vault | Cryptographic key management | SC-12 |
| Azure Firewall | Network boundary protection | SC-7 |

Service descriptions are grounded in Microsoft Learn:
[Microsoft Entra](https://learn.microsoft.com/en-us/entra/fundamentals/what-is-entra),
[Azure Monitor](https://learn.microsoft.com/en-us/azure/azure-monitor/fundamentals/overview),
[Azure Key Vault](https://learn.microsoft.com/en-us/azure/key-vault/general/overview),
and [Azure Firewall](https://learn.microsoft.com/en-us/azure/firewall/overview).
These links describe service functionality, not an assessed implementation or
an official Microsoft control-allocation matrix. All mappings and responsibility
allocations in this fixture are illustrative proposals requiring review.
Each record's source description retains the example disclaimer.

This is **not an official Microsoft ATO package or verified authorization**.
It contains no authorization letter, certification, authorization dates, real
tenant identifiers or resource evidence. The local import must remain
unpublished with every generated record awaiting review. Existing example
packages and published releases must not be replaced.

The retained local Azure example is package
`42490ad7-d4b9-48a6-b8b2-c8803abb3548`: 1/1 source processed, four components,
four capabilities, four mappings and four responsibility proposals, all
NeedsReview/Unpublished. Real UI re-upload returned the same 202 receipt without
changing candidates or existing package decisions. The 166 analyzer tests passed,
including the two failing-first Azure fixture tests. No application code,
schema, identity permissions, approval or publication was changed for this
example-content follow-up.

Create an archive from that directory, writing outside the repository:

```bash
cd docs/examples/package-imports
zip /tmp/spin-synthetic-package.zip \
  synthetic-inventory.json synthetic-oscal.json \
  no-candidates.json unsupported.synthetic
```

For nested-archive acceptance, include that ZIP as an entry in another ZIP.
The unsupported fixture must appear with an explicit reason. The empty structured
fixture must not silently disappear. Candidate generation and enrichment depend
on supported source semantics and the configured analysis service; an unavailable
model or OCR service must produce an actionable exception, not invented records
or a completed-analysis badge.

PDF retry treats retained page text and embedded attachments separately. A PDF
marked extracted can still require semantic work on its own page segments or
unfinished attachment enumeration, even when its manifest already has children.
Completed page extraction must not run again; completed semantic segments and
budget charges remain in the checkpoint. Explicitly excluding a PDF also excludes
its descendants from retry.

For the minimal review-to-release path, first upload `synthetic-inventory.json`
alone. It explicitly declares one component, one capability, their contributor
relationship, and a proposed `AU-2` Provider responsibility. The production
worker/analyzer HTTP integration test uses this shape. `synthetic-oscal.json`
instead exercises OSCAL component/control/responsibility extraction; it does
not by itself declare a publishable capability. The mixed archive deliberately
adds incomplete-analysis and overlapping-record decisions and is not a
one-click success fixture.

`synthetic-authorization-reference.json` is a separate source-reference review
fixture. Its stated authority and dates are synthetic, not a real authorization.
After the proposed reference is reviewed, the offering source panel must show a
reviewed reference with citations, never a verified authorization or a changed
system ATO.

## Manual checklist

### Onboarding

- [ ] Without uploading, advance through setup; optional upload remains optional.
- [ ] Upload the synthetic package. Receipt appears only after originals and
  manifest are durable. Do not see an extracted-record inventory/review form.
- [ ] Continue onboarding while processing. Completion activates only the
  profile; all generated records remain unpublished.
- [ ] Refresh/close/reopen the browser after receipt. Recover the same package.
- [ ] Interrupt the local MCP process during analysis, restart against the same
  disposable database/storage root and verify unfinished work resumes.
- [ ] Retry an uncertain upload with the same key and content: same package,
  no duplicate candidates. Different content with the same key is a conflict.

### Portal review

- [ ] Navigate through Security Capabilities -> Review imports. Review progress,
  complete source-entry coverage and actionable exceptions.
- [ ] Inspect component and capability pages, including pagination and empty
  states. Open citations and protected original sources.
- [ ] Verify a high-confidence result is still unpublished and unapproved.
- [ ] Edit source-supported fields/mappings/responsibilities; reject an unsuitable
  candidate with a reason; resolve a duplicate without overwriting published
  content. Resolve contributor dependencies explicitly.
- [ ] Unsupported/unreadable/failed content blocks affected approval. Exclude
  deliberately with a reason, keeping coverage visibly incomplete.
- [ ] Review selected components and capabilities. Generate impact preview,
  explicitly approve that exact selection, then publish as a separate action.
- [ ] Edit after approval and confirm old publication fails with a stale conflict.
- [ ] Refresh after approval and after publication; persisted outcomes remain.
- [ ] Repeat publication using the same key: same release, no extra records.
- [ ] Keep an unrelated Draft and an existing published record in the disposable
  database; neither changes during import or publication of the selected set.

### Isolation and layout

- [ ] Ordinary subscriber and impersonated requests cannot read package metadata,
  unpublished candidates or protected bytes, or mutate/retry/publish them.
- [ ] Post-onboarding import follows exactly the same review gate.
- [ ] Offering source panel links imported packages; a recorded authorization
  reference is not shown as a verified decision or mission-system ATO.
- [ ] Reference-only review accepts a 2,000-character source title without
  inventory classification and rejects 2,001 characters; ordinary inventory
  names remain limited to 256 characters with a valid component type.
- [ ] At 390px and desktop widths, there is no horizontal page overflow.
- [ ] Complete file selection, review navigation, edit, preview and confirmation
  using the keyboard; labels, focus and inline errors remain usable.

## Storage, upgrade and recovery

See the [package contract](../../specs/078-role-aware-workspaces/contracts/package-imports.md)
for additive schema, lease/retry and compatibility rules. Back up both the
database and file-storage root together. Reverting application binaries must not
delete package ledgers or retained originals. No backfill may publish old drafts.
The additive generated-analysis checkpoint supports unfinished-only retries and
is separate from human-reviewed candidate content. Retained package rows from a
pre-checkpoint implementation need explicit resubmission with a new upload key
when no safe checkpoint exists; keep their original sources. Do not treat this
compatibility error as successful recovery or reset completed review work.

## Verification record

The implementation is locally runnable. This checklist is not evidence of user
acceptance, and full-suite regression remains qualified below. Manual sign-off
stays separate.

Local verification results (23 September 2026; user acceptance remains open):

| Check | Observed result |
|---|---|
| Dashboard unit suite | 1,441 tests passed, including recovery, reference UI and optional wizard navigation |
| Dashboard type checking and production build | Passed |
| Package browser flow, 1440px and 390px | 8 verified, including refresh/lost-response recovery and reference-source handoff |
| Existing organization, provider publication and theme browser checks | 17 passed |
| Final isolated .NET solution build | Passed, 123 warnings and 0 errors |
| Full .NET unit run | Aborted after 1,054 passes and 14 `BadImageFormatException` failures |
| Full .NET integration run | 1,356 passed, 13 failed, 55 skipped |
| Subsequent isolated full unit snapshot | 6,712 passed, 0 failed, 0 skipped; no test-host crash |
| Final full unit suite after the SQLite startup fix | 6,716 passed, 0 failed, 0 skipped |
| Final package/onboarding HTTP and SQL Server schema checks | 41 passed, 0 failed, 0 skipped |
| Subsequent isolated full integration snapshot | 1,388 passed, 21 failed, 22 skipped |
| Failed integration classes rerun separately from the same binaries | All 59 tests passed across seven classes; no skips or code changes |
| Final targeted analyzer suite | 164 passed, 0 failed, 0 skipped; 90.22% line / 81.51% branch coverage |
| Subsequent reference-only validation correction | 46 service/recovery unit tests and 45 HTTP/schema tests passed; 0 failures/skips |
| Reference-only editor compatibility | 9 failing-first regressions, then 103 package UI tests passed; Dashboard typecheck/build passed |
| Final source-stable full unit suite, including PDF retry correction | 6,724 passed, 1 failed, 0 skipped; classifier timing assertion took 1.064s against a 1s limit |
| Classifier test class rerun alone using the same binaries | 2 passed, 0 failed; threshold and code unchanged |
| Final source-stable focused verification with coverage | 238 unit/schema tests and 45 HTTP/schema tests passed; no failures/skips |
| New backend service/endpoint/worker/schema line coverage | 83.54% in the unit report and 85.07% in the HTTP report, each across 1,246 lines; separate, not merged reports |

The final production Dashboard preview passed all 25 package/existing-flow
browser checks (21 workflow/theme checks and four organization-dialog checks).
Package/onboarding UI coverage before the additional wizard-navigation check was 97.98% lines and
91.78% branches across 90 focused tests.

The integration failures include tenant/impersonation fixture failures and two
SQL Server command timeouts. Concurrent builds and a coverage instrumentation
file lock were observed; the unit-host crash is not a passed regression result.
Subsequent .NET verification used separate build artifacts to avoid shared
`bin`/`obj` writes. Browser fixture tests do not establish real backend
authorization or durable storage; those require the separate HTTP/storage tests
and the manual checks above.

The subsequent isolated full snapshot compiled successfully and completed
without the earlier unit-host crash. Its integration failures comprise eight
SQL Server timeout/closed-connection failures, two scan-worker timing/cleanup
failures, and the eleven previously observed tenant/impersonation failures.
This is not a clean regression result. The snapshot predates the SQLite
bootstrap correction; the final full unit and targeted HTTP runs cover the
corrected source, but do not replace a clean full integration run.
All 21 failing cases subsequently passed when their seven test classes ran
separately (59 tests total). This establishes non-reproduction in isolation,
not a resolved whole-suite failure or a verified diagnosis of its cause.
The analyzer's final tests cover PDF attachment accounting and optional semantic
analysis with mocked clients only. OCR, encrypted content, additional PDF stream
filters, live model behavior and process-level hostile-PDF isolation remain
outside the verified coverage.

A subsequent reference-only correction accepts source-derived titles up to
2,000 characters and nullable inventory classification for reference edits.
Inventory constraints remain unchanged. The focused backend counts above cover
that correction; the earlier full-suite counts predate it. The local MCP was
rebuilt into separate artifacts (54 warnings, 0 errors), preserving the same
database and original package. Real-browser checks confirmed the existing
publication and 1440px/390px layouts survived that runtime update.

The matching editor follow-up was also verified through the real browser/API:
a synthetic source containing an exact 2,000-character reference title was
uploaded, reviewed with `componentType: null`, and recovered as Reviewed after
refresh. It remained unpublished, publication stayed disabled, and its page
had no horizontal overflow at 390px. This retained demo is package
`22c3bd2d-914c-4080-94fc-d5571bc8e933`. Reference titles over the limit and
unchanged inventory constraints are covered by the focused UI/API tests.

The final PDF retry correction preserves selection of a Processed PDF whose
checkpoint still has unfinished page semantics or attachment enumeration.
Backend verification included six targeted selector cases, then 35 passing
service/unit tests and seven passing lifecycle HTTP tests. These selector tests
use a mocked analyzer and do not independently establish native PDF parsing.
The source-stable full solution build passed with 123 warnings and no errors.
Its full unit run failed the existing one-second evidence-classifier performance
assertion at approximately 1.064 seconds. Both tests in that class passed when
run alone from the same binaries. This is not a clean full-unit result or a
verified explanation of the timing difference; no threshold was relaxed.
The final runtime now includes that PDF correction. It was started against the
same retained database/storage after the source-stable focused tests completed.
Its real-browser check again recovered the existing canonical publication with
repeat publication disabled and no page overflow at 1440px or 390px.

The standalone SQLite host also logs existing query-translation errors from
the escalation worker and SSP/authorization-package export retention services.
Those separate background workflows were not repaired here. The new import
worker, review and canonical publication completed despite those errors; this
does not establish overall application health.

The source Dashboard on port 5174 now proxies to the isolated local MCP on
`127.0.0.1:3004`; socket inspection confirmed loopback-only binding. Configure
`ATO_Server__Urls` explicitly: `ASPNETCORE_URLS` alone is insufficient because
the MCP host adds its application-specific `Server:Urls` address.
The production-bundle fixture-test preview is on port 5175. The existing Docker
site on port 5173 has **not** been updated by this package-ingestion task. A new
frontend by itself does not update the API it proxies.

Real-browser verification persisted a synthetic package receipt and completed
onboarding without reviewing records. It exposed a fresh SQLite bootstrap
blocker: the migration baseline lacked the existing CSP catalog tables.
The additive startup correction was subsequently applied to the **same**
disposable database, retaining its provider profile, package ID and original
file. A portal retry then produced three source-supported candidates.

The real UI subsequently persisted component/capability review, generated an
exact two-record preview, and saved approval. Reload restored that approval
while the package remained unpublished. Separate publication produced one
component and one canonical capability release; another reload restored those
same outcomes with repeat publication disabled. The supporting responsibility
remained private and was not silently published.

Standalone Chromium checks against the real local API also verified the saved
outcome and no horizontal page overflow at 1440px and 390px. The checked release
is `24b723fd-cc6f-4859-9079-b144dac3b48e` in package
`33db3f22-c60d-45a6-9c12-ed4508f6fb36`. Re-uploading the identical synthetic file
in this demo intentionally recovers the existing package; use a new disposable
demo directory to manually repeat initial onboarding.
