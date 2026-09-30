import type { ReactNode } from 'react';
import { render, screen } from '@testing-library/react';
import { MemoryRouter, Outlet } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
import ApplicationRoutes from '../../ApplicationRoutes';
import { SYSTEM_SCREEN_GROUPS } from '../../features/systems/systemScreenRoutes';

vi.mock('../../features/auth/RequireAuth', () => ({ default: ({ children }: { children: ReactNode }) => children }));
vi.mock('../../components/layout/SystemLayout', () => ({
  default: () => <section data-testid="system-shell"><Outlet /></section>,
  useSystemContext: () => ({ detail: { systemId: 'a', name: 'Mission A' } }),
}));
vi.mock('../../pages/SystemDetail', () => ({ default: () => <h1>Readiness content</h1> }));
vi.mock('../../pages/SystemProfile', () => ({ default: () => <h1>Profile content</h1> }));
vi.mock('../../pages/BoundaryManagement', () => ({ default: () => <h1>Boundary content</h1> }));
vi.mock('../../pages/BaselineManagement', () => ({ default: () => <h1>Baseline content</h1> }));
vi.mock('../../features/workspace-operations/system-capabilities/SystemSecurityCapabilitiesPage', () => ({ default: () => <h1>Capability content</h1> }));
vi.mock('../../pages/CapabilityResponsibilityReview', () => ({ default: () => <h1>Responsibility content</h1> }));
vi.mock('../../pages/ControlInheritance', () => ({ default: () => <h1>Inheritance content</h1> }));
vi.mock('../../pages/NarrativeWorkspace', () => ({ default: () => <h1>Narrative content</h1> }));
vi.mock('../../pages/EvidenceRepository', () => ({ default: () => <h1>Evidence content</h1> }));
vi.mock('../../pages/LegalRegulatory', () => ({ default: () => <h1>Policy content</h1> }));
vi.mock('../../pages/Assessments', () => ({ default: () => <h1>Assessment content</h1> }));
vi.mock('../../pages/Remediation', () => ({ default: () => <h1>Remediation content</h1> }));
vi.mock('../../pages/PoamManagement', () => ({ default: () => <h1>POAM content</h1> }));
vi.mock('../../pages/DeviationsPage', () => ({ default: () => <h1>Exception content</h1> }));
vi.mock('../../pages/Documents', () => ({ default: () => <h1>Document content</h1> }));
vi.mock('../../features/systems/SystemDocumentPreview', () => ({ default: () => <h1>Document preview content</h1> }));
vi.mock('../../pages/EmassStatus', () => ({ default: () => <h1>eMASS content</h1> }));
vi.mock('../../pages/AuthorizationPage', () => ({ default: () => <h1>Decision content</h1> }));
vi.mock('../../pages/ConMon', () => ({ default: () => <h1>Monitoring content</h1> }));
vi.mock('../../pages/ScopedMonitoring', () => ({ default: () => <h1>Scoped monitoring content</h1> }));
vi.mock('../../pages/RolesManagementPage', () => ({ default: () => <h1>Team content</h1> }));
vi.mock('../../features/systems/SystemHistoryPage', () => ({ default: () => <h1>History content</h1> }));

const destinations: Record<string, string> = {
  '': 'Readiness', 'security-capabilities': 'Capability', roles: 'Team', boundaries: 'Boundary',
  'profile/MissionAndPurpose': 'Profile', 'profile/UsersAndAccess': 'Profile', 'profile/EnvironmentAndDeployment': 'Profile',
  'profile/DataTypes': 'Profile', 'profile/PortsProtocolsAndServices': 'Profile', 'profile/LeveragedAuthorizations': 'Profile',
  baseline: 'Baseline', inheritance: 'Inheritance', narratives: 'Narrative', 'narratives/library': 'Narrative',
  legal: 'Policy', assessments: 'Assessment', remediation: 'Remediation', poam: 'POAM', evidence: 'Evidence',
  deviations: 'Exception', authorize: 'Decision', documents: 'Document', conmon: 'Scoped monitoring', 'emass/status': 'eMASS',
};
const additional: Record<string, string> = {
  history: 'History',
  'inheritance/subscriptions': 'Responsibility', 'assessments?tab=plan': 'Assessment',
  'documents?tab=exports': 'Document', 'documents/preview': 'Document preview', 'conmon/reports': 'Monitoring',
  'conmon/rules': 'Scoped monitoring', 'conmon/changes': 'Scoped monitoring', 'conmon/impacts': 'Scoped monitoring',
};

describe('Systems screen route coverage', () => {
  it.each(Object.entries(destinations))('preserves existing destination %s', async (path, expected) => {
    // Arrange / Act
    render(<MemoryRouter initialEntries={[`/systems/a${path ? `/${path}` : ''}`]}><ApplicationRoutes /></MemoryRouter>);
    // Assert
    expect(await screen.findByRole('heading', { name: `${expected} content` })).toBeVisible();
    expect(screen.getByTestId('system-shell')).toBeInTheDocument();
  });

  it.each(SYSTEM_SCREEN_GROUPS.flatMap(group => group.items).filter(item => !item.unavailable))(
    'routes the available mock task $label to existing production content',
    async ({ path }) => {
      // Arrange / Act
      render(<MemoryRouter initialEntries={[`/systems/a${path ? `/${path}` : ''}`]}><ApplicationRoutes /></MemoryRouter>);
      // Assert
      expect(await screen.findByRole('heading', { name: `${additional[path] ?? destinations[path]} content` })).toBeVisible();
      expect(screen.getByTestId('system-shell')).toBeInTheDocument();
    },
  );
});
