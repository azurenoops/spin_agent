import apiClient from './client';

export interface RoleAssignment {
  id: string;
  role: string;
  userId: string;
  userDisplayName: string | null;
  assignedAt: string;
  assignedBy: string;
}

interface RolesResponse {
  items: RoleAssignment[];
  totalCount: number;
}

export interface AssignRoleBody {
  role: string;
  userDisplayName: string;
  userId?: string;
}

export async function fetchRoles(systemId: string): Promise<RoleAssignment[]> {
  const { data } = await apiClient.get<RolesResponse>(`/systems/${systemId}/roles`);
  return data.items;
}

export async function assignRole(systemId: string, body: AssignRoleBody): Promise<RoleAssignment> {
  const { data } = await apiClient.post<RoleAssignment>(`/systems/${systemId}/roles`, body);
  return data;
}

export async function deleteRole(systemId: string, roleId: string): Promise<void> {
  await apiClient.delete(`/systems/${systemId}/roles/${roleId}`);
}
