const profileLabels: Record<string, string> = {
  MissionAndPurpose: 'Mission & purpose', UsersAndAccess: 'Users & access',
  EnvironmentAndDeployment: 'Environment & hosting', DataTypes: 'Data types & sensitivity',
  PortsProtocolsAndServices: 'Ports & interconnections', LeveragedAuthorizations: 'Leveraged authorizations',
};

export function getCategoryRoute(category: string, artifactType?: string | null, description?: string): { path: string; label: string } | null {
  switch (category.toLowerCase()) {
    case 'authorization-decision': return { path: 'authorize', label: 'Recorded decisions' };
    case 'provider-authorization': return { path: 'profile/EnvironmentAndDeployment/hosting', label: 'Provider hosting' };
    case 'profile-approval': {
      // Approved-profile findings identify their source section with this server prefix.
      const section = description?.match(/^Profile ([A-Za-z]+):/)?.[1];
      if (section && Object.hasOwn(profileLabels, section)) return { path: `profile/${section}`, label: profileLabels[section]! };
      return { path: 'profile/MissionAndPurpose', label: 'Mission profile' };
    }
    case 'boundary': return { path: 'boundaries', label: 'Boundaries' };
    case 'ssp': return { path: 'narratives', label: 'Narratives' };
    case 'sar': return { path: 'assessments', label: 'Assessments' };
    case 'sap': return { path: 'assessments?tab=plan', label: 'Assessment plan' };
    case 'poam':
    case 'cross-reference': return { path: 'poam', label: 'POA&M' };
    case 'evidence': return { path: 'evidence', label: 'Evidence' };
    case 'schema':
      if (artifactType?.toLowerCase() === 'ssp') return { path: 'narratives', label: 'Narratives' };
      if (artifactType?.toLowerCase() === 'poam') return { path: 'poam', label: 'POA&M' };
      if (artifactType?.toLowerCase() === 'assessment-plan') return { path: 'assessments?tab=plan', label: 'Assessment plan' };
      if (artifactType?.toLowerCase() === 'assessment-results') return { path: 'assessments', label: 'Assessments' };
      return null;
    default: return null;
  }
}
