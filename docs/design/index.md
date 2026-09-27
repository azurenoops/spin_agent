# Design and validation

## Required UI targets

Feature 079 is a coordinated refactor with selective UI replacement. **The UI
must follow the provider and Systems mocks**, not merely borrow their styling.
Reuse working APIs/domain services underneath them; replace conflicting screens.
Any intentional deviation needs explicit user approval.

| Suite | Interactive design | Gallery | Interpretation |
|---|---|---|---|
| Provider workspace and mission handoff | [Open provider](provider-workspace-mock/index.html) | [Provider gallery](provider-workspace-mock/gallery.html) | 36 screens, including eight Systems companions |
| Systems workspace | [Open Systems](system-overview-mock/pages.html) | [Systems gallery](system-overview-mock/gallery.html) | 30 pages plus [overview scenarios](system-overview-mock/index.html) |

All mock records, permissions, dates, counts, cloud behavior and interactions are
synthetic. A working prototype is not proof of authentication, persistence,
extraction, publication, evidence access, or eMASS acceptance in production.
Production must use authorized records and real services, not mock simulation.

### Provider walkthrough

Follow source upload, analysis and review, then release publication. Continue
through service allocation, Mission Owner association, release adoption,
customer responsibilities, evidence, document preview and package readiness.
Use the toolbar for empty, unavailable and restricted variants. Publication,
association and adoption remain distinct even in the simulation.

### Systems walkthrough

Use the page navigation to inspect all 30 pages; compare preparation and
monitoring in the overview concept. Primary actions open simulated dialogs.
Review at desktop and mobile widths; editable forms do not persist data.
The source README files retain authors' prototype verification details in the
repository. MkDocs maps README files to index pages, which conflicts with the
preserved standalone prototype entry points; this index therefore links
directly to the runnable HTML rather than claiming those README pages are
rendered site guides.

## Research and implementation plan

- [Provider product validation](csp-product-validation-2026-09-26.md)
- [System product-goal audit](system-product-goal-audit-2026-09-26.md)
- [Feature 079 specification](../../specs/079-provider-system-workflow-consolidation/spec.md)
- [Implementation sequence](../../specs/079-provider-system-workflow-consolidation/plan.md)
- [Every screen and route](../../specs/079-provider-system-workflow-consolidation/contracts/screen-route-migration.md)
- [Local review instructions](../../specs/079-provider-system-workflow-consolidation/quickstart.md)
- [Issue-write preview](../../specs/079-provider-system-workflow-consolidation/github-issue-preview.md)
- [Provider-to-document architecture decision](../architecture/adr-004-provider-mission-lineage.md)

Research records what was inspected at its stated baseline, not proof that a
deployed system passed the workflow. Feature 079 targets `13204325`, not the
older `bd06f9d9` main-only snapshot.

## Supporting and historical designs

The complete source design directory is retained, including earlier
[workspace roles](workspace-roles-permissions.md),
[source-package flow](provider-source-package-flow.md),
[provider authorization mocks](provider-authorization-mocks/index.html),
and [onboarding companion](onboarding-mock/index.html).
Earlier layouts do not supersede the two required target suites above.
The onboarding companion appeared during the copy; its additional
organization/system flows are reference material pending separate scope review.

The copied package was verified against 198 source files (excluding `.DS_Store`)
at this delivery's copy checkpoint. No source checkout changes were removed.
