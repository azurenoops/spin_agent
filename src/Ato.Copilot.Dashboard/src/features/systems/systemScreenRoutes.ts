export interface SystemScreen {
  path: string;
  label: string;
  unavailable?: string;
}

export interface SystemScreenGroup {
  label: string;
  items: SystemScreen[];
}

// Labels and grouping follow docs/design/system-overview-mock/pages.html.
// Unavailable tasks remain visible, but must not navigate to a substitute page.
export const SYSTEM_SCREEN_GROUPS: SystemScreenGroup[] = [
  { label: 'Overview', items: [
    { path: '', label: 'Readiness' },
  ] },
  { label: 'System definition', items: [
    { path: 'profile/MissionAndPurpose', label: 'Mission' },
    { path: 'profile/UsersAndAccess', label: 'Users' },
    { path: 'profile/EnvironmentAndDeployment', label: 'Environment & hosting' },
    { path: 'profile/DataTypes', label: 'Data' },
    { path: 'boundaries', label: 'Inventory & boundary' },
    { path: 'profile/PortsProtocolsAndServices', label: 'Ports & interconnections' },
  ] },
  { label: 'Controls & evidence', items: [
    { path: 'baseline', label: 'Categorization & baseline' },
    { path: 'security-capabilities', label: 'Applied capabilities' },
    { path: 'inheritance/subscriptions', label: 'Responsibilities' },
    { path: 'narratives', label: 'Narratives' },
    { path: 'evidence', label: 'Evidence' },
    { path: 'legal', label: 'Policies' },
  ] },
  { label: 'Assessment & risk', items: [
    { path: 'assessments?tab=plan', label: 'Assessment plan' },
    { path: 'assessments', label: 'Assessments & results' },
    { path: 'remediation', label: 'Findings & remediation' },
    { path: 'poam', label: 'POA&M' },
    { path: 'deviations', label: 'Exceptions' },
  ] },
  { label: 'ATO package & eMASS', items: [
    { path: 'documents', label: 'Readiness checklist' },
    { path: 'documents/preview', label: 'Document previews' },
    { path: 'documents?tab=exports', label: 'Export packages' },
    { path: 'emass/status', label: 'eMASS reconciliation' },
    { path: 'authorize', label: 'Recorded decisions' },
  ] },
  { label: 'Continuous monitoring', items: [
    { path: 'conmon', label: 'Coverage & health' },
    { path: 'conmon/rules', label: 'Rules' },
    { path: 'conmon/changes', label: 'Detected changes' },
    { path: 'conmon/impacts', label: 'Impact reviews' },
    { path: 'conmon/reports', label: 'Reports' },
  ] },
  { label: 'Team & permissions', items: [
    { path: 'roles', label: 'System team' },
  ] },
  { label: 'Activity & history', items: [
    { path: 'history', label: 'Audit history' },
  ] },
];

export function isSystemScreenActive(path: string, pathname: string, search: string, basePath: string, includeRelated = false): boolean {
  if (pathname === `${basePath}/security-capabilities/inventory`) {
    if (path === 'security-capabilities') return false;
    if (includeRelated && path === 'boundaries') return true;
  }
  if (includeRelated && path === 'inheritance/subscriptions' && pathname === `${basePath}/inheritance`) return true;
  const [relativePath, query] = path.split('?');
  const target = `${basePath}${relativePath ? `/${relativePath}` : ''}`;
  if (pathname !== target && !(relativePath && pathname.startsWith(`${target}/`))) return false;
  if (query) {
    return [...new URLSearchParams(query)].every(([key, value]) => new URLSearchParams(search).get(key) === value);
  }
  const tab = new URLSearchParams(search).get('tab');
  if (relativePath === 'documents' && tab === 'exports' || relativePath === 'assessments' && tab === 'plan') return false;
  if (relativePath === 'conmon' && pathname !== target) return false;
  if (relativePath === 'documents' && pathname !== target) return false;
  return true;
}
