# Provider authorization workspace approval mock

Design only. All providers, decisions, dates, resources, findings, customers and telemetry are synthetic. No live Azure access, authorization decisions or API mutations occur.

Open `index.html` directly in a browser. This extends the source-package and onboarding mocks with a dedicated provider authorization workspace.

## Latest local design verification

The authorization-led handoff updates were checked with standalone Chromium at
1440px and 390px: catalog source summary to Authorizations, import dialog,
package-review deep link, deferred onboarding, disabled unreviewed approval/
publication, Escape dismissal and horizontal viewport overflow checks passed.
This verifies only the interactive design artifacts, not production API
behavior, persistence or accessibility conformance. The competing catalog
reference editor was removed; decision management belongs in Authorizations.

## Superseding implementation direction

The September 23 22:54 user request authorizes local implementation and testing
of an authorization-led offering workflow, not deployment or external writes.
This direction supersedes ownership assumptions in the earlier source-package
mock. Begin in **Authorizations -> Import existing authorization package**.
Select/create an offering, record boundary context and upload original sources.
Onboarding can initiate optional receipt; detailed review belongs here.

Authorizations owns external decisions, boundaries/Azure scope, package versions,
inherited Microsoft references, source and authorization-impact review, findings,
POA&M, evidence and deadlines. Security Capabilities owns the shared reusable
catalog, authoring, contributors, release versions, duties and adoption. Both
use the same records and existing publication pipeline.

The mock's customer-boundary illustration is only one possible relationship.
An actual mission system can have a separate boundary, an externally evidenced
covered-workload relationship, or an undetermined relationship requiring review.
Subscription/resource assignment alone never establishes coverage. Covered
scope requires authority, evidence and the mission system's Authorizing Official
using existing system authorization permissions, not a simple toggle.
Newly discovered resources remain outside recorded coverage pending review.

Provider findings and POA&M are directly offering-owned. Submitted evidence
remains pending review; importing a source-stated status never closes a finding.

Support multiple offerings and external decisions. Extracted metadata remains
unconfirmed; explicit confirmation records the external decision rather than
issuing one. Superseded/withdrawn records retain history. Package or catalog/scope
changes trigger impact review; publication does not amend authorization and
authorization changes do not auto-release working capabilities.

## Illustrated relationship

This example shows a provider offering with a defined boundary, inherited
Microsoft references and customer systems consuming services from separate
boundaries. It is not a rule that every customer relationship has that shape:
the implementation supports evidenced AO-reviewed covered scope and undetermined
relationships as described above. Catalog publication, package review,
authorization decisions and technical monitoring have separate states.

## Review journey

1. Overview: inspect the recorded provider decision, conditions, current source package and working package.
2. Boundary: inspect included provider services, excluded customer workloads and a proposed resource addition.
3. Inherited coverage: inspect Microsoft/provider/customer responsibilities and source references.
4. Packages: simulate receiving a revised package, view coverage exceptions and open the existing component/capability review mock.
5. Findings: inspect an open remediation item and attach sample evidence; evidence enters review without closing the finding.
6. Customer impact: preview affected subscriptions, acknowledge review, and simulate publishing a previously approved capability revision. No customer ATO or approved narrative changes.

The artifact itself does not define API contracts, Azure connector permissions
or persistence. Those require documented implementation planning under the
superseding request. Reuse current application services; sample data does not
establish that the model exists. No live Azure connector is needed for the demo.

## Manual preview

Open `index.html`, exercise all six tabs and the scenario selector, then inspect mobile layout. Existing navigation outside this mock is visual context only. Remove this directory to undo these mock artifacts.

## Verification results

Browser checks passed for all six sections at desktop and 390px mobile width; no page-wide horizontal overflow or browser script errors remained. Verified source dialogs, boundary-review creation without scope expansion, package partial-analysis recovery, evidence submission without finding closure, acknowledgement-gated simulated publication, missing-decision state and unavailable-telemetry state. The initial customer-impact mobile overflow was corrected and the checks rerun successfully.

Screenshots: `01-overview.png`, `02-boundary.png`, `03-coverage.png`, `04-packages.png`, `05-findings.png`, `06-impact.png`, `07-mobile-overview.png`.

These are prototype checks, not application acceptance tests. No production code, Azure resource, or authorization record was changed.
