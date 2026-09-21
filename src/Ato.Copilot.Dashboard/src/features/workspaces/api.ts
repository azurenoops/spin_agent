import axios, { type AxiosResponse } from 'axios';
import type {
  GrantWorkspaceMembershipRequest,
  MembershipPerson,
  SystemWorkspaceAccess,
  WorkspaceMembership,
  WorkspaceOption,
  WorkspacePage,
} from './types';

type Envelope<T> =
  | { status: 'success'; data: T }
  | { status: 'error'; error: { errorCode: string; message: string } };

function payload<T>(response: AxiosResponse<Envelope<T>>): T {
  const body = response.data;
  if (body?.status === 'success' && body.data != null) return body.data;
  if (body?.status === 'error' && typeof body.error?.message === 'string') {
    throw new Error(body.error.message);
  }
  throw new Error('Unexpected workspace API response.');
}

export function workspaceErrorMessage(error: unknown): string {
  if (error && typeof error === 'object' && 'response' in error) {
    const response = error.response;
    if (response && typeof response === 'object' && 'data' in response) {
      const data = response.data;
      if (data && typeof data === 'object' && 'error' in data) {
        const detail = data.error;
        if (detail && typeof detail === 'object' && 'message' in detail && typeof detail.message === 'string') {
          return detail.message;
        }
      }
    }
  }
  return error instanceof Error ? error.message : 'Unable to complete the workspace request.';
}

export async function getWorkspaceOptions(page = 1): Promise<WorkspacePage<WorkspaceOption>> {
  return payload(await axios.get<Envelope<WorkspacePage<WorkspaceOption>>>('/api/auth/workspaces',
    { params: { page, pageSize: 50 } }));
}

export async function getSystemWorkspaceAccess(systemId: string, signal?: AbortSignal): Promise<SystemWorkspaceAccess> {
  return payload(await axios.get<Envelope<SystemWorkspaceAccess>>(
    `/api/dashboard/systems/${encodeURIComponent(systemId)}/workspace-access`,
    { signal, timeout: 10_000 },
  ));
}

function tenantPath(tenantId: string): string {
  return `/api/tenants/${encodeURIComponent(tenantId)}`;
}

export async function getMembershipPersons(tenantId: string, query = ''): Promise<MembershipPerson[]> {
  return payload(await axios.get<Envelope<MembershipPerson[]>>(`${tenantPath(tenantId)}/membership-persons`,
    { params: { query } }));
}

export async function createMembershipPerson(
  tenantId: string,
  request: Omit<MembershipPerson, 'id'>,
): Promise<MembershipPerson> {
  return payload(await axios.post<Envelope<MembershipPerson>>(`${tenantPath(tenantId)}/membership-persons`, request));
}

export async function getWorkspaceMemberships(tenantId: string, page = 1): Promise<WorkspacePage<WorkspaceMembership>> {
  return payload(await axios.get<Envelope<WorkspacePage<WorkspaceMembership>>>(`${tenantPath(tenantId)}/memberships`,
    { params: { page, pageSize: 50 } }));
}

export async function grantWorkspaceMembership(
  tenantId: string,
  request: GrantWorkspaceMembershipRequest,
): Promise<WorkspaceMembership> {
  return payload(await axios.post<Envelope<WorkspaceMembership>>(`${tenantPath(tenantId)}/memberships`, request));
}

export async function revokeWorkspaceMembership(tenantId: string, membershipId: string): Promise<void> {
  const response = await axios.delete(`${tenantPath(tenantId)}/memberships/${encodeURIComponent(membershipId)}`);
  if (response.status !== 204) throw new Error('Unexpected membership revocation response.');
}
