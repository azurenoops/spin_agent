import { packageRequest } from '../package-imports/request';

export interface ProviderInvitationView {
  invitationId: string;
  providerId: string;
  providerName: string;
  authenticatedIdentity: string;
  targetIdentity: string;
  offeringName?: string | null;
  requestedRoles: string[];
  requestedScope: string;
  status: 'Pending' | 'Accepted' | 'Expired' | 'Revoked';
  expiresAt: string;
}

export interface ProviderAccessRequestView {
  requestId: string;
  status: 'Pending' | 'Approved' | 'Denied';
  requestedProviderId?: string | null;
  justification: string;
  submittedAt: string;
  reviewedAt?: string | null;
  decisionReason?: string | null;
}

export const getInvitation = (id: string) =>
  packageRequest<ProviderInvitationView>({ url: `/api/csp/invitations/${encodeURIComponent(id)}` });

export const acceptInvitation = (id: string) =>
  packageRequest<{ destination: string }>({
    method: 'POST',
    url: `/api/csp/invitations/${encodeURIComponent(id)}/accept`,
    data: { confirmed: true },
  });

export const getCurrentAccessRequest = () =>
  packageRequest<ProviderAccessRequestView | null>({ url: '/api/csp/access-requests/current' });

export const createAccessRequest = (justification: string, requestedProviderId?: string) =>
  packageRequest<ProviderAccessRequestView>({
    method: 'POST',
    url: '/api/csp/access-requests',
    data: { justification, requestedProviderId: requestedProviderId || null },
  });
