/**
 * Tenant IDs that exist in the database for system/bootstrap reasons but
 * are NOT meaningful "orgs" in a `MultiTenant` deployment and therefore
 * MUST be hidden from every CSP-Admin-facing org/tenant picker and
 * portfolio rollup.
 *
 * - **System tenant** (`...000`) — owns `[GlobalReference]` rows
 *   (NIST control catalog, OSCAL framework metadata, CSP-inherited
 *   components, etc.) per Feature 048 / FR-070. Not a workload boundary.
 * - **Default tenant** (`...001`) — `TenantBootstrapService` creates
 *   this row for `SingleTenant` deployments (and as a backfill target
 *   for legacy single-tenant data migrated to `MultiTenant`). In a
 *   `MultiTenant` CSP deployment it is a vestige; real orgs come from
 *   the onboarding wizard, not from this fallback row. Surfacing it as
 *   an "org" in the `TenantPicker` and `OrgsTable` confuses operators
 *   (it shows up labeled `Ato.Copilot.Default` from the original
 *   bootstrap snapshot — not a meaningful display name).
 *
 * Centralizing the list here keeps both the `TenantPicker` (org-switcher
 * dropdown) and the CSP-Portfolio `OrgsTable` consistent. If a future
 * use case needs to *include* one of these rows (e.g. a system-tenant
 * health view), the consumer should opt out of this filter explicitly,
 * not redefine the constant.
 *
 * Source: `Ato.Copilot.Core.Services.Tenancy.TenantBootstrapService`
 * (`SystemTenantId` / `DefaultTenantId`).
 */
export const SYSTEM_TENANT_ID = '00000000-0000-0000-0000-000000000000';
export const DEFAULT_TENANT_ID = '00000000-0000-0000-0000-000000000001';

export const VESTIGE_TENANT_IDS: ReadonlySet<string> = new Set([
  SYSTEM_TENANT_ID,
  DEFAULT_TENANT_ID,
]);

/**
 * Returns `true` if the given tenant id is a system/default vestige row
 * that should be hidden from CSP-Admin-facing org pickers / rollups.
 * Comparison is case-insensitive (the API may surface either casing).
 */
export function isVestigeTenant(tenantId: string): boolean {
  return VESTIGE_TENANT_IDS.has(tenantId.toLowerCase());
}
