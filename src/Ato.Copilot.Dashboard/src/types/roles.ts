/**
 * @file Phase 1 / Feature 049 — Unified RMF Role Assignments
 *
 * Canonical TypeScript domain types for the 7-role universe. Mirrors the C# side
 * (see <c>specs/049-unified-rmf-role-assignments/contracts/frontend-types.md § 1</c>).
 *
 * **RBAC_ASSIGNABLE_BY** is for client-side affordance hiding ONLY. The server
 * is the sole enforcement point per FR-027 / SC-009.
 */

export const RMF_ROLES = [
  'AuthorizingOfficial',
  'Issm',
  'Isso',
  'Sca',
  'SystemOwner',
  'MissionOwner',
  'Administrator',
] as const;

export type RmfRole = (typeof RMF_ROLES)[number];

export const RMF_ROLE_LABELS: Record<RmfRole, string> = {
  AuthorizingOfficial: 'Authorizing Official (AO)',
  Issm: 'Information System Security Manager (ISSM)',
  Isso: 'Information System Security Officer (ISSO)',
  Sca: 'Security Control Assessor (SCA)',
  SystemOwner: 'System Owner',
  MissionOwner: 'Mission Owner',
  Administrator: 'Administrator',
};

export const RMF_ROLE_OPTIONS = RMF_ROLES.map((value) => ({
  value,
  label: RMF_ROLE_LABELS[value],
}));

/**
 * FR-027 RBAC matrix. Key = caller's effective role. Value = roles the caller
 * may assign (or remove). Roles not listed cannot assign anything.
 *
 * Keep in sync with `RoleAuthorizationService.Authorize` (C# side).
 */
export const RBAC_ASSIGNABLE_BY: Record<RmfRole, readonly RmfRole[]> = {
  Administrator: [
    'AuthorizingOfficial',
    'Issm',
    'Isso',
    'Sca',
    'SystemOwner',
    'MissionOwner',
    'Administrator',
  ],
  Issm: ['Issm', 'Isso', 'Sca', 'SystemOwner', 'MissionOwner', 'Administrator'],
  Isso: ['MissionOwner', 'SystemOwner'],
  AuthorizingOfficial: [],
  Sca: [],
  SystemOwner: [],
  MissionOwner: [],
} as const;

/**
 * Provenance label for a single role row inside a {@link SystemRolesResponse}.
 *
 * - `not-assigned` — no row exists at any precedence layer.
 * - `override`     — per-system row with IsInherited=false.
 * - `inherited`    — per-system row with IsInherited=true (auto-copied from Org).
 * - `org-fallback` — Org-level row exists but its inherited per-system row has
 *                    not yet been materialized (brief fan-out worker window).
 * - `legacy`       — only the legacy RmfRoleAssignments table has a row (FR-024).
 */
export type RoleAssignmentSource =
  | 'not-assigned'
  | 'override'
  | 'inherited'
  | 'org-fallback'
  | 'legacy';

export type ResolvedRoleAssignment = {
  role: RmfRole;
  person: { id: string; displayName: string } | null;
  source: RoleAssignmentSource;
  /** Originating OrganizationRoleAssignment.Id when source is `inherited` or `org-fallback`. */
  orgRoleId?: string;
};

export type SystemRolesResponse = {
  systemId: string;
  /** Exactly 7 entries — one per {@link RmfRole}. */
  roles: ResolvedRoleAssignment[];
};

export type SoDWarning = {
  code: 'SOD_VIOLATION';
  message: string;
  roleConflict: [RmfRole, RmfRole];
  dodiReference: string;
  suggestedAction: string;
};

export type AssignmentErrorCode =
  | 'RBAC_ROLE_ASSIGN_DENIED'
  | 'ROLE_INHERITED_NOT_REMOVABLE'
  | 'ROLE_WRITE_THROUGH_FAILED'
  | 'INVALID_ROLE';

export type AssignmentResult = {
  status: 'success' | 'error';
  data?: ResolvedRoleAssignment;
  /** Omitted (undefined) when empty — never serialized as `[]`. */
  warnings?: SoDWarning[];
  error?: {
    code: AssignmentErrorCode;
    message: string;
    suggestion?: string;
    callerEffectiveRole?: RmfRole;
    targetRole?: RmfRole;
    orgRoleId?: string;
  };
};

export type EffectiveRoleResponse = {
  /** Caller's highest-privileged effective role, or `null` when the caller holds none. */
  effectiveRole: RmfRole | null;
  /** True when the caller holds an active Organization-scope Administrator row. */
  isTenantAdministrator: boolean;
};

/**
 * Body for the per-system override write.
 * (`POST /api/roles/system/{systemId}`).
 */
export type AssignSystemRoleBody = {
  role: RmfRole;
  personId: string;
};

/**
 * Body for the Org-scope role write.
 * (`POST /api/roles/organization`).
 */
export type AssignOrgRoleBody = {
  role: RmfRole;
  personId: string;
  /** When true, the assignment marks itself the primary holder for the role. */
  isPrimary?: boolean;
  /**
   * Wizard bootstrap flag. The server ONLY honors this when the tenant has
   * zero active OrganizationRoleAssignments — otherwise it falls through to
   * the FR-027 matrix.
   */
  bootstrap?: boolean;
};
