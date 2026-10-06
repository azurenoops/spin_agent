import type { ProfileSectionType } from '../../types/dashboard';

export const systemProfileTasks: Record<ProfileSectionType, { title: string; description: string; record: string; contribution: string; guidance: string; next: { label: string; path: string }[] }> = {
  MissionAndPurpose: {
    title: 'Mission & purpose', description: 'Describe what this system does, who depends on it, and why it matters.',
    record: 'System record', contribution: 'SSP · System description',
    guidance: 'Saved drafts are reviewed before they become part of the approved document baseline.',
    next: [{ label: 'System team', path: 'roles' }, { label: 'Users & access', path: 'profile/UsersAndAccess' }],
  },
  UsersAndAccess: {
    title: 'Users & access', description: 'Document user populations and their access needs. Assign application roles in Team & permissions.',
    record: 'User categories', contribution: 'SSP · Users and access',
    guidance: 'A documented user category does not grant application access. Use System team for permitted RMF role assignments.',
    next: [{ label: 'System team', path: 'roles' }, { label: 'Control responsibilities', path: 'inheritance/subscriptions' }],
  },
  EnvironmentAndDeployment: {
    title: 'Environment & hosting', description: 'Describe where this system runs, then manage provider services and subscription connections separately.',
    record: 'Deployment description', contribution: 'SSP · Environment and hosting scope',
    guidance: 'Scope association documents where the system runs. Review applied capabilities and customer duties separately.',
    next: [{ label: 'Applied capabilities', path: 'security-capabilities' }, { label: 'Components & system scope', path: 'boundaries' }],
  },
  DataTypes: {
    title: 'Data types & sensitivity', description: 'Describe the information handled by the system and review its categorization inputs.',
    record: 'Information types', contribution: 'SSP · Information types / Privacy screening',
    guidance: 'Recording information types does not approve categorization or privacy determinations. Review those records separately.',
    next: [{ label: 'Categorization & baseline', path: 'baseline' }, { label: 'Policies', path: 'legal' }],
  },
  PortsProtocolsAndServices: {
    title: 'Ports & interconnections', description: 'Record permitted communication and the external systems your system depends on.',
    record: 'Ports and services', contribution: 'SSP · Network interfaces / Interconnection register',
    guidance: 'Port entries describe permitted communication. An external connection may also require a reviewed interconnection agreement.',
    next: [{ label: 'Components & system scope', path: 'boundaries' }, { label: 'Interconnection documents', path: 'documents' }],
  },
  LeveragedAuthorizations: {
    title: 'Leveraged authorization records', description: 'Inspect retained authorization context and its sources.',
    record: 'Retained source records', contribution: 'SSP · Leveraged authorization references',
    guidance: 'Provider service names are not authorization decisions. Review the source-backed hosting context before relying on inherited coverage.',
    next: [{ label: 'Environment & hosting', path: 'profile/EnvironmentAndDeployment' }, { label: 'Document previews', path: 'documents/preview' }],
  },
};
