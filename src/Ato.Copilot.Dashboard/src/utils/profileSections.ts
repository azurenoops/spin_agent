import type { GovernanceStatus, ProfileSectionType } from '../types/dashboard';

export const PROFILE_SECTION_LABELS: Record<ProfileSectionType, string> = {
  MissionAndPurpose: 'Mission & Purpose',
  UsersAndAccess: 'Users & Access',
  EnvironmentAndDeployment: 'Environment & Deployment',
  DataTypes: 'Data Types & Sensitivity',
  PortsProtocolsAndServices: 'Ports, Protocols & Services',
  LeveragedAuthorizations: 'Leveraged Authorizations',
};

export function formatProfileSectionLabel(sectionType: ProfileSectionType): string {
  return PROFILE_SECTION_LABELS[sectionType] ?? sectionType;
}

const GOVERNANCE_STATUS_LABELS: Record<GovernanceStatus, string> = {
  NotStarted: 'Not started',
  Draft: 'Draft',
  UnderReview: 'Under review',
  Approved: 'Approved',
  NeedsRevision: 'Needs revision',
};

export function formatGovernanceStatusLabel(status: GovernanceStatus): string {
  return GOVERNANCE_STATUS_LABELS[status] ?? status;
}
