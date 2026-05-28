/**
 * Feature 051 — Wire types for the dashboard login experience.
 *
 * Mirrors the HTTP contract in
 * `specs/051-login/contracts/http-api.md` and the TypeScript contract in
 * `specs/051-login/contracts/frontend-types.md`. ALL new login UI imports
 * from this module — no inline type definitions.
 */

// ─── § 1 wire types (mirror of http-api.md) ─────────────────────────────

export type AuthMethodId = 'Cac' | 'Entra' | 'Simulation';
export type AzureCloud = 'AzurePublic' | 'AzureUSGovernment';
export type TenantStatus = 'Active' | 'Suspended' | 'Disabled';

export interface BrandingDescriptor {
  deploymentName: string;
  logoUrl: string | null;
  supportEmail: string | null;
}

export interface AuthMethodDescriptor {
  id: AuthMethodId;
  displayName: string;
}

export interface SimulatedIdentityDescriptor {
  id: string;
  displayName: string;
  persona: string;
  tenantId: string;
  roles: string[];
}

export interface SimulationPanelDescriptor {
  identities: SimulatedIdentityDescriptor[];
}

export interface MsalDescriptor {
  clientId: string;
  authority: string;
  redirectUri: string;
  postLogoutRedirectUri: string;
}

export interface LoginConfig {
  branding: BrandingDescriptor;
  defaultMethod: AuthMethodId;
  enabledMethods: AuthMethodDescriptor[];
  cloud: AzureCloud;
  idleTimeoutMinutes: number;
  rememberTenantCookieDays: number;
  /** Null outside Development. */
  simulation: SimulationPanelDescriptor | null;
  msal: MsalDescriptor;
}

export interface TenantSummary {
  id: string;
  displayName: string;
  status: TenantStatus;
}

export interface PimRoleAssignment {
  name: string;
  /** ISO-8601 */
  expiresAt: string;
}

export interface ImpersonationState {
  impersonatedTenant: TenantSummary;
  startedAt: string;
  expiresAt: string;
}

export interface MeResponse {
  oid: string;
  displayName: string;
  persona: string;
  homeTenant: TenantSummary;
  effectiveTenant: TenantSummary;
  isImpersonating: boolean;
  impersonation: ImpersonationState | null;
  pimRoles: PimRoleAssignment[];
  isCspAdmin: boolean;
  isSocAnalyst: boolean;
}

export interface SelectTenantRequest {
  tenantId: string;
  remember?: boolean;
}

export interface SignOutRequest {
  reason?: 'manual' | 'idle_timeout';
}

// ─── § 2 error class taxonomy (FR-015) ──────────────────────────────────

export type ErrorClass =
  | 'NoCardInserted'
  | 'CertExpired'
  | 'CertNotYetValid'
  | 'CertRevoked'
  | 'ClockSkew'
  | 'NoTenantAssignment'
  | 'AccountDisabled'
  | 'MfaFailure'
  | 'ConditionalAccessBlock'
  | 'NetworkFailure';

export interface ErrorPageProps {
  errorClass: ErrorClass;
  correlationId: string;
  supportEmail: string;
}

// ─── § 4.4 + § 4.5 — FR-008 unsaved-changes restore (analysis C1) ───────

/**
 * A registered serializer for a single form. `serialize` MUST be safe to
 * call synchronously inside the `ato:idle-warning` event handler.
 */
export interface FormSnapshotSerializer<T> {
  formId: string;
  serialize: () => T;
}

export interface UseIdleFormStateBackupResult {
  register: <T>(s: FormSnapshotSerializer<T>) => void;
  unregister: (formId: string) => void;
}

export interface UnsavedSnapshot<T = unknown> {
  formId: string;
  /** ISO-8601 */
  savedAt: string;
  data: T;
}

// ─── Cross-window CustomEvent map ───────────────────────────────────────

declare global {
  interface WindowEventMap {
    /**
     * Dispatched on every non-silent-renewal 2xx API response. Listened
     * to by `useIdleTimer` to reset the inactivity counter.
     */
    'ato:user-input': CustomEvent<{ source: 'api-success' }>;

    /**
     * Dispatched by `useIdleTimer` 60s before idle expiry so a modal can
     * render and `useIdleFormStateBackup` can persist snapshots.
     */
    'ato:idle-warning': CustomEvent<{ secondsUntilSignOut: number }>;

    /**
     * Dispatched by `RestoreUnsavedChangesPrompt` when the user clicks
     * "Restore" for a given form.
     */
    'ato:restore-unsaved': CustomEvent<UnsavedSnapshot>;

    /**
     * Dispatched by the tenant picker after `POST /api/auth/select-tenant`.
     * Listened to by `useMe` so the SPA re-fetches effective tenant.
     */
    'ato:tenant-changed': CustomEvent<{ tenantId: string }>;
  }
}

export {};
