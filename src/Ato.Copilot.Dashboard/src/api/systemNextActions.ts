import apiClient from './client';

export type SystemNextActionRole = 'MissionOwner' | 'Issm' | 'Isso' | 'Sca' | 'AuthorizingOfficial';

export type SystemNextActionPath =
  | 'profile/MissionAndPurpose'
  | 'profile/UsersAndAccess'
  | 'profile/EnvironmentAndDeployment'
  | 'profile/DataTypes'
  | 'profile/PortsProtocolsAndServices'
  | 'boundaries'
  | 'narratives'
  | 'evidence'
  | 'assessments'
  | 'assessments?tab=plan'
  | 'authorize';

export interface SystemNextAction {
  id: string;
  title: string;
  description: string;
  path: SystemNextActionPath;
  actionLabel: 'Open';
  responsibleRole: SystemNextActionRole;
}

export interface SystemNextActionsResponse {
  systemId: string;
  checkedAt: string;
  effectiveRoles: string[];
  items: SystemNextAction[];
  waitingOnOtherRoles: { role: SystemNextActionRole; count: number }[];
}

const paths: readonly string[] = [
  'profile/MissionAndPurpose', 'profile/UsersAndAccess', 'profile/EnvironmentAndDeployment',
  'profile/DataTypes', 'profile/PortsProtocolsAndServices', 'boundaries', 'narratives',
  'evidence', 'assessments', 'assessments?tab=plan', 'authorize',
];
const roles: readonly string[] = ['MissionOwner', 'Issm', 'Isso', 'Sca', 'AuthorizingOfficial'];

export async function getSystemNextActions(
  systemId: string, signal?: AbortSignal,
): Promise<SystemNextActionsResponse> {
  const { data } = await apiClient.get<SystemNextActionsResponse>(
    `/systems/${encodeURIComponent(systemId)}/next-actions`, { signal },
  );
  if (!data || data.systemId !== systemId || typeof data.checkedAt !== 'string'
    || !Number.isFinite(Date.parse(data.checkedAt))
    || !Array.isArray(data.effectiveRoles) || data.effectiveRoles.some(role => typeof role !== 'string')
    || !Array.isArray(data.items) || data.items.some(item => !item
      || typeof item.id !== 'string' || !item.id || typeof item.title !== 'string'
      || typeof item.description !== 'string' || !paths.includes(item.path)
      || item.actionLabel !== 'Open' || !roles.includes(item.responsibleRole)
      || !data.effectiveRoles.includes(item.responsibleRole))
    || !Array.isArray(data.waitingOnOtherRoles) || data.waitingOnOtherRoles.some(item => !item
      || !roles.includes(item.role) || !Number.isInteger(item.count) || item.count < 1)) {
    throw new Error('The server did not return valid role-scoped next actions. Refresh before continuing.');
  }
  return data;
}
