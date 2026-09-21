# CSP and Organization Workspaces (Approved Design)

**Status:** Design approved by the user on 2026-09-21 for issue
[#1002](https://github.com/azurenoops/spin_agent/issues/1002).
Implementation was subsequently authorized. Release verification and publishing
approval remain pending. This page does not describe a shipped feature.

## Purpose

The dashboard already resolves provider and organization variants of portfolio,
systems, components, capabilities and control pages. The proposed change makes
that distinction a consistent authenticated workspace, rather than deriving
navigation from a cached CSP endpoint probe or support impersonation.

The hosting CSP remains a single provider per deployment. Customer organizations
are isolation tenants; organizational subgroups and Entra directories are
different concepts and must not supply the active-organization label.

## Intended experiences

| Workspace | Responsibilities |
|---|---|
| CSP | Provider capabilities, shared-control definitions, provider evidence and narratives, customer organizations and authorized cross-organization oversight |
| Organization | Authorized systems, mission context, boundaries, subscribed capabilities, customer/shared responsibilities, narratives, evidence, assessments, remediation and monitoring |

Organization access does not confer administration or approval. Mission Owners
receive authorized mission/system responsibilities, not organization
administration, SCA review authority or AO approval authority. Multi-role users
retain their valid scoped permissions without turning a global persona into a
permission grant.

## Context is explicit and independent per tab

The user confirmed that separate tabs and copied deep links must retain
independent authorized contexts.

The proposed route families are:

- `/workspaces/csp/...`
- `/workspaces/organizations/{tenantId}/...`
- `/workspaces/organizations/{tenantId}/systems/{systemId}/...`

These identifiers select context; they never authorize it. Each request must
validate the authenticated identity, membership, owning system and operation.
No authentication tokens belong in a route.

The workspace header shows the workspace, active organization, selected system
and effective roles. Loading, denied, missing and revoked contexts are explicit.
The previous organization's data must not remain visible under a new label.

Switching in one tab must not mutate ordinary scope in another tab. A remembered
organization may suggest a landing page only after revalidation. It cannot
override an authorized explicit deep link.

## Support is not ordinary workspace selection

CSP support impersonation remains a separate audited operation, visibly marked
with actor, target and expiration. An ordinary workspace switch neither starts
impersonation nor silently uses an impersonation cookie created in another tab.
Ending/expiring support must remove support-derived access.

The exact request/session migration is gated on the membership and scope
contracts; this document does not claim the current cookie-based behavior meets
the independent-tab requirement.

## Provider changes and customer approval

A provider mapping is not proof of inheritance or implementation. The customer
experience must show persisted Inherited, Shared, Customer or Undesignated
responsibility and its source. Missing baseline/allocation and pending review
are distinct states.

Provider changes should lead to affected-control review work. Approved customer
narratives remain unchanged until an authorized reviewer accepts a versioned
replacement. Authorization decisions are outside automatic propagation.

The Narrative Library from #1001 supplies provider/capability,
organization and system references. Those inputs remain references, not
implementation evidence. Library integration is a dependency, not a renamed
existing component-document list or a placeholder navigation link.

## Delivery dependencies

| Concern | Tracked work |
|---|---|
| Public login bootstrap and fresh CSP lifecycle | #943, #941, #944 |
| Authorized ordinary organization membership | #942 |
| Correct organization identity and stale-context handling | #950 |
| Server-authoritative Mission Profile permissions | #968, implemented by merged PR #1005; feature-branch synchronization pending |
| Responsibility-aware system inheritance | #957 |
| State-aware narrative review and scoped Narrative Library | #1001 |

Issue states and detailed merge gates belong to the feature planning artifacts.
An open dependency is not assumed to be implemented, and this feature cannot
be declared complete while required scope/permission/review integrations remain
missing.

## Validation and rollout

Planned verification includes unit tests, real local HTTP-pipeline integration
tests with tenant-resolution bypass disabled, browser persona journeys,
two-tab/history/deep-link cases and explicit negative authorization tests.
Synthetic UI mocks do not establish backend isolation or real persistence.

Legacy routes will resolve and redirect through authorized context. Previously
stored browser persona/settings values do not grant permissions. Rollout must
be coordinated between server and dashboard, with no production authorization
bypass and no automatic approval of customer artifacts.

Design and implementation approval are recorded. Dependency contracts, testing,
manual acceptance and publishing approval remain gates. Implementation progress
must not be confused with a shipped feature.
