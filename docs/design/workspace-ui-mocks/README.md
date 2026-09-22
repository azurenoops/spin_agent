# SPIN workspace UI mock references

These are the original interactive HTML design prototypes, copied without modification. They embed their branding assets and have no external HTTP dependencies in their source. Open a file directly in a browser to inspect and interact with it. Read its HTML when implementation details or copy need inspection.

## Files

- [CSP catalog, authoring and publication](spin-csp-mocks.html)
- [CSP Organizations, organization detail, provisioning and support](spin-csp-organizations.html)
- [Organization library, capability detail and guided setup](spin-capability-mocks.html)

## Issue-to-screen index

The UI identifiers below match the pending issue package, not GitHub issue numbers. Each issue body also includes the repository path and its exact screen references.

| Draft | File and screen |
|---|---|
| UI-01 | [spin-csp-mocks.html](spin-csp-mocks.html) — CSP workspace strip and navigation; [spin-csp-organizations.html](spin-csp-organizations.html) — CSP Organizations navigation; [spin-capability-mocks.html](spin-capability-mocks.html) — organization workspace navigation |
| UI-02 | [spin-csp-mocks.html](spin-csp-mocks.html) — Provider catalog; switch capability/component views and expand the Azure offering |
| UI-03 | [spin-csp-mocks.html](spin-csp-mocks.html) — Capability authoring: Implementation, Coverage & duties, Subscribers; Add capability |
| UI-04 | [spin-csp-mocks.html](spin-csp-mocks.html) — Review & publish: diff, customer impact, publication gate and notification preview |
| UI-05 | [spin-csp-organizations.html](spin-csp-organizations.html) — Organizations landing page: search, filters, summary cards and row actions |
| UI-06 | [spin-csp-organizations.html](spin-csp-organizations.html) — View organization: Overview & systems, Provider subscriptions and Provider activity |
| UI-07 | [spin-csp-organizations.html](spin-csp-organizations.html) — Add organization: provisioning form |
| UI-08 | [spin-csp-organizations.html](spin-csp-organizations.html) — Enter support: reason, reference and acknowledgement |
| UI-09 | [spin-capability-mocks.html](spin-capability-mocks.html) — Security capabilities: By capability / By component and workspace/source variations |
| UI-10 | [spin-capability-mocks.html](spin-capability-mocks.html) — Capability detail: contributors, control coverage, responsibility and evidence/narratives |
| UI-11 | [spin-capability-mocks.html](spin-capability-mocks.html) — Guided setup: capability, components and final review |

## How Copilot should use these mocks

1. Open the referenced HTML and exercise the specified screen, tabs, forms and view toggles before changing the application.
2. Reuse the application’s existing SPIN assets and theme tokens. Match the mock’s hierarchy, layout and interactions while implementing through existing application components and services.
3. The issue’s vetted contracts and acceptance criteria take precedence over simulated prototype semantics. Do not copy fake metrics, hardcoded demo names, fictional revision numbers or simulated successful writes into production.
4. Keep lifecycle, onboarding, mapping review, publication, system responsibility and AO authorization distinct. Catalog availability is not blanket inheritance; local-only organization workflows remain valid.
5. Contact email is not membership; provider detail is not implicit support access. Confirmations and review actions require actual server permissions and persisted revisions.
6. These files do not establish that any backend feature exists. Do not embed them into the production app as a substitute for implementation. Test the real screens and provide a local user preview.

## Publication prerequisite

These assets and their index must be committed and published to a repository ref accessible to the implementing Copilot task before the issue batch is posted/assigned. Local-only file paths are insufficient. Include these exact files with the issue publication approval; verify the remote files exist and include their accessible repository links in the publication handoff. Issue bodies link to the immutable published revision of these files.

## Original-file integrity

- `spin-csp-mocks.html`: SHA-256 `e6be6065a127e3d14319f7a00cae8164d099b45b84450a74cec643fb9a824276`
- `spin-csp-organizations.html`: SHA-256 `9efc11fd7026b2e07477faa1ea9af994a00605090e0c8e13e7095d404b205b3f`
- `spin-capability-mocks.html`: SHA-256 `6e4ee7b358d7758d1e3e0bde9c20054c5e0f389ef95db9bd29aa3ac08c7ad8b8`
