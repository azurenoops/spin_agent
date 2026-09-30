import { workspaceRequest } from './workspaceRequest';
import type { ProvisioningResult } from './types';

export interface OrganizationDraftValues {
  organizationChoice: 'create' | 'existing';
  existingTenantId?: string | null;
  displayName?: string | null;
  legalEntityName?: string | null;
  primaryPocName?: string | null;
  primaryPocEmail?: string | null;
  administratorChoice?: 'existing' | 'other' | 'deferred' | null;
  administrator?: {
    directoryTenantId?: string | null; objectId?: string | null; personId?: string | null;
    newPerson?: { displayName: string; email: string } | null;
  } | null;
  discovery?: { source: 'directory' | 'manual'; connectionId?: string | null } | null;
  deferralReason?: string | null;
}
export interface OrganizationOnboardingDraft {
  draftId: string; revision: number; state: 'Draft' | 'Confirmed' | 'Discarded';
  savedAt: string; displayName: string; currentStep: 'details' | 'administrator' | 'review';
  values: OrganizationDraftValues; creationKey: string; tenantId: string | null;
  operationId: string | null; resumeUrl: string; schemaVersion: number;
}
export interface OrganizationDraftPage { items: OrganizationOnboardingDraft[]; page: number; pageSize: number; total: number }
export interface OrganizationSetupSummary {
  tenant: { id: string; displayName: string; lifecycle: string; onboardingState: string };
  observedAt: string;
  liveAccess: {
    state: 'Available' | 'Missing' | 'Unavailable'; activeMemberCount: number | null;
    administrators: { items: { personId: string; displayName: string; membershipId: string; directoryTenantId: string; objectId: string; assignmentId: string }[];
      page: number; pageSize: number; total: number | null };
  };
  requestedOperation: ProvisioningResult | null;
  reconciliation: 'SameIdentity' | 'DifferentIdentity' | 'Unbound' | 'None';
  actorActions: { canManageMemberships: boolean; canResumeEnrollment: boolean; canEnterOrganization: boolean };
}
const drafts = '/api/csp/organization-onboarding/drafts';
export function listOrganizationDrafts(page = 1, pageSize = 25, signal?: AbortSignal) {
  return workspaceRequest<OrganizationDraftPage>({ method: 'GET', url: drafts, params: { page, pageSize }, signal });
}
export function getOrganizationDraft(id: string, signal?: AbortSignal) {
  return workspaceRequest<OrganizationOnboardingDraft>({ method: 'GET', url: `${drafts}/${encodeURIComponent(id)}`, signal });
}
export function saveOrganizationDraft(id: string, values: OrganizationDraftValues, currentStep: string, expectedRevision: number) {
  return workspaceRequest<OrganizationOnboardingDraft>({ method: 'PUT', url: `${drafts}/${encodeURIComponent(id)}`,
    data: { schemaVersion: 1, expectedRevision, currentStep, values } });
}
export function confirmOrganizationDraft(id: string, expectedRevision: number) {
  return workspaceRequest<OrganizationOnboardingDraft>({ method: 'POST', url: `${drafts}/${encodeURIComponent(id)}/confirm`,
    data: { expectedRevision, confirmed: true } });
}
export function getOrganizationSetupSummary(tenantId: string, operationId?: string | null, signal?: AbortSignal, administratorPage = 1) {
  return workspaceRequest<OrganizationSetupSummary>({ method: 'GET', url: `/api/csp/organizations/${encodeURIComponent(tenantId)}/setup-summary`,
    params: { operationId: operationId || undefined, administratorPage, administratorPageSize: 25 }, signal });
}
