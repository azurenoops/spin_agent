# Organization-wide Add Security Capability dialog

Proposed UI only. Generated with the built-in image-generation tool; no production behavior or API changes are implemented by this mock.

[View the dialog mock](01-organization-add-dialog.png)

The illustration includes an unavailable system row to explain selection permissions. In implementation, only list systems the user is authorized to discover/read; a no-manage state must not expose otherwise inaccessible system names. The pictured access dropdown is informational, not a role editor.

## Flow

1. Choose **Create in organization** or **Inherit from CSP**.
2. Create a capability with supporting components, or create a component with optional related capabilities. Provider selection preserves published source identity and ownership. A provider component delivered through a capability directs the user to that parent offering; it does not invent a standalone component subscription.
3. Save to the organization library for later use, or explicitly select authorized systems for application. Library availability is distinct from system applicability. New systems are not included automatically.
4. Review exact writes before adding. Local records remain organization-owned; provider records remain provider-managed. Configure component boundary assignments and confirm responsibilities separately for each system.

## Code alignment and implementation limits

Inspected `OrganizationLibrary` and `SetupWizardState` in `src/Ato.Copilot.Dashboard/src/features/workspace-operations/WorkspaceOperationsPage.tsx`. The existing flow has local/provider source selection, inline local capability creation, and a target system. Its current `canManage` requires a selected system and that system's permission. This proposed organization-library-only save and multiple-system application require explicit contract and authorization work; they are not verified existing functionality.

Organization-wide means reusable within the organization, not automatically inherited by every system. Do not infer create authority from membership or an Administrator label. Resolve organization authoring and each system's application permissions on the server. Offer component type/subtype values from supported metadata, not hard-coded mock values. Keep source-qualified IDs, source revision, duplicate detection and resumable write outcomes. Saving must never confirm inherited responsibilities, approve narratives or grant an ATO.

All sample offerings, system names and revisions are illustrative. The board combines source selection states and a system-use review summary; the final confirmation must still show exact writes after validation. Library-only save must work without a CSP choice for organization-owned records. Inherited library availability must use an explicit organization-level reference/entitlement contract, not a fabricated system subscription.

## Generation

Built-in image generation; previous system mock board 02 used only as a SPIN branding reference. Prompt: create a high-fidelity three-panel organization modal board showing local capability/component creation, CSP offering selection with read-only source identity, and library-only versus explicitly selected-system use with an exact-write review summary. Preserve pale lavender context, white panels, navy typography and purple actions. No selected-system shell, automatic all-system inheritance or ATO grant.
