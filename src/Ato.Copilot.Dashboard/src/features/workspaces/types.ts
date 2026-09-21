export interface WorkspacePermissions {
  canManageMemberships: boolean;
  canManageOrganization: boolean;
  canAccessCsp: boolean;
}

export interface WorkspaceOption {
  kind: 'csp' | 'organization';
  tenantId: string | null;
  displayName: string;
  status: string;
  onboardingState: string;
}

export interface WorkspaceDescriptor {
  kind: 'csp' | 'organization';
  tenantId: string | null;
  displayName: string;
  mode: 'ordinary' | 'support';
  personId: string | null;
  roles: string[];
  permissions: WorkspacePermissions;
}

export interface WorkspacePage<T> {
  items: T[];
  total: number;
}

export interface MembershipPerson {
  id: string;
  displayName: string;
  email: string;
}

export interface GrantWorkspaceMembershipRequest {
  directoryTenantId: string;
  objectId: string;
  personId: string;
}

export interface WorkspaceMembership extends GrantWorkspaceMembershipRequest {
  id: string;
  tenantId: string;
  grantedAt: string;
  grantedBy: string;
  revokedAt: string | null;
  revokedBy: string | null;
}

export interface SystemWorkspacePermissions {
  canRead: boolean;
  canEditProfile: boolean;
  canManageSystem: boolean;
  canAuthorNarratives: boolean;
  canReviewNarratives: boolean;
  canManageEvidence: boolean;
  canRunAssessments: boolean;
  canManageRemediation: boolean;
  canDecideAuthorization: boolean;
}

export interface SystemWorkspaceAccess {
  systemId: string;
  roles: string[];
  permissions: SystemWorkspacePermissions;
}
