# SPIN administration mock set

Date: 2026-09-30. Design proposals with illustrative records; not implementation or live application verification. Current checkout is `078-onboarding-consolidation` with unrelated onboarding work in progress. No product files were changed for this design task.

## Coverage

+- [01-organization-profile-subscriptions](assets/admin-pages/01-organization-profile-subscriptions.png)
- [02-membership-setup](assets/admin-pages/02-membership-setup.png)
- [03-imports-templates](assets/admin-pages/03-imports-templates.png)
- [04-knowledge-audit](assets/admin-pages/04-knowledge-audit.png)
- [05-provider-administration](assets/admin-pages/05-provider-administration.png)
- [06-intake-platform](assets/admin-pages/06-intake-platform.png)


1. Organization settings — profile, classification, resource links and administration entry points.
2. Azure subscriptions — registration and source distinction, independent of system attachment and access checks.
3. Organization access — people, memberships and administrator assignments kept separate from system RMF roles.
4. Organization setup — setup progress and resume; consolidated draft/resume behavior is proposed and must align with current onboarding contracts.
5. Imported documents — import entry points, source review and mapping status.
6. Templates and narrative seeds — template selection and source upload, without implying approval.
7. Knowledge base management — references and overlays, with permission-dependent actions.
8. Audit log — read-only event review scoped to authorized context.
9. Provider administration — provider setup and links to organizations, offerings and optional allocations.
10. Provider organizations — organization catalog and independent enrollment stages.
11. External allocation intake — proposed FAST/other-source validation and reconciliation; no verified FAST connector implied.
12. Tenant migration — restricted platform operation, separate from ordinary organization administration.

This set covers administration landing pages and key detail states, not every system workflow, wizard step, or permission variation. Narrative authoring and provider offering authoring retain their dedicated workspaces. Personal notification/theme preferences are not organization administration.

## Source grounding

Existing route inventory inspected in `src/Ato.Copilot.Dashboard/src/ApplicationRoutes.tsx`. Representative source inspected for organization access, imports, migration, knowledge management, and current organization onboarding consolidation contract. Existing routes do not prove every illustrated action is implemented. Exact fields, endpoints, permissions and state transitions must be checked before implementation.

Use the real SPIN logo and keep Security Capabilities as one navigation item. Generated illustrations are not authority for new permissions, endpoints, states or branding. Navigation is permission-filtered; do not expose platform or provider operations simply because someone is an organization administrator. Selecting a contact does not grant membership. Creating a membership does not grant a system RMF role. A subscription allocation is optional for consuming provider scopes; subscription attachment remains independently available to authorized mission owners.

All screens need keyboard operation, accessible labels, loading, empty, denied, stale and retry states, preserved drafts and explicit mutation confirmation where warranted. Source import does not approve content; collection health and authorization are separate.

Generated with the built-in image-generation tool. Board prompts are retained in the companion prompt file.

## Implementation clarifications

- Tenant Migration is specifically the existing irreversible SingleTenant-to-MultiTenant deployment conversion. The illustrative migration layout must not be implemented as a general cross-tenant record-transfer tool. Preserve the default-tenant behavior, exact preview, required confirmations and existing authorization.
- Imported Documents must retain OSCAL SSP, template and narrative-seed categories in addition to eMASS and SSP PDF; abbreviated mock tabs do not remove those capabilities.
- Membership identity fields must retain verified directory tenant/object identity semantics. Display names alone cannot establish identity. Any new review step must precede the existing explicit membership grant.
- Pending enrollment intent and current actor access can differ; show whose enrollment is pending rather than imply the current administrator lacks access.
- Reuse actual navigation and branding. Generated sidebars and example names are not requested route changes. All permission and action claims require implementation verification.
- The generated template board shows actions on an unconfigured SAR slot: implementation must disable View/Download/Set default until an actual template exists. Do not infer report-generation prerequisites from illustrative card descriptions.
- Ignore generated claims such as Full organization access and invented role names. Display actual effective permissions and supported role labels only.
- Provider Organizations must retain its existing Add organization action even where the illustration omits it. Membership enrollment concerns the intended customer identity; wording about automatically selecting provider staff is not authoritative.
