import type { ReactNode } from 'react';
import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { MemoryRouter, Outlet, useLocation } from 'react-router-dom';
import ApplicationRoutes from '../../ApplicationRoutes';

vi.mock('../../features/auth/RequireAuth', () => ({ default: ({ children }: { children: ReactNode }) => children }));
vi.mock('../../components/layout/SystemLayout', () => ({
  default: () => <section data-testid="system-layout"><Outlet /></section>,
  useSystemContext: () => ({ detail: { systemId: 'system-a', name: 'Mission Alpha' } }),
}));
vi.mock('../../features/workspaces/WorkspaceBoundary', () => ({
  useWorkspaceSession: () => ({ target: { kind: 'organization', tenantId: 'org-a' }, systemAccess: { systemId: 'system-a', permissions: { canRead: true } } }),
}));
vi.mock('../../features/workspace-operations/WorkspaceOperationsPage', () => ({
  default: () => <h1>Organization library</h1>,
  SetupWizard: () => <h1>Recover legacy operation</h1>,
}));
vi.mock('../../features/workspace-operations/system-capabilities/SystemCapabilityList', () => ({
  default: ({ systemId }: { systemId: string }) => <h1>Applied list for {systemId}</h1>,
}));
vi.mock('../../features/workspace-operations/system-capabilities/SystemCapabilityDetail', () => ({
  default: ({ source, recordId }: { source: string; recordId: string }) => <h1>{source} detail for {recordId}</h1>,
}));
vi.mock('../../features/workspace-operations/system-capabilities/SystemCapabilitySetup', () => ({
  default: ({ systemId }: { systemId: string }) => <h1>Add to {systemId}</h1>,
}));
vi.mock('../../pages/ComponentInventory', () => ({ default: () => <h1>Inventory creation import discovery</h1> }));
vi.mock('../../features/provider-relationships/MissionAssociationWizard', () => ({
  default: ({ environmentEntry }: { environmentEntry?: boolean }) => <h1>{environmentEntry ? 'Environment hosting and capabilities' : 'Legacy hosting association'}</h1>,
}));
vi.mock('../../pages/AssessmentEnvironment', () => ({ default: () => <h1>Assessment configuration</h1> }));

function Location() { const location = useLocation(); return <output aria-label="Route">{location.pathname}{location.search}{location.hash}</output>; }
function mount(route: string) { render(<MemoryRouter initialEntries={[route]}><ApplicationRoutes /><Location /></MemoryRouter>); }

describe('system capability application routing', () => {
  it('redirects the legacy assessment hash before loading an Environment form', async () => {
    // Arrange / Act
    mount('/systems/system-a/profile/EnvironmentAndDeployment#azure-assessment-environment');
    // Assert
    await screen.findByRole('heading', { name: 'Assessment configuration' });
    expect(screen.getByLabelText('Route')).toHaveTextContent('/systems/system-a/assessments/environment#azure-assessment-environment');
    expect(screen.queryByRole('textbox', { name: 'Environment description' })).not.toBeInTheDocument();
  });
  it.each([
    ['/systems/system-a/profile/EnvironmentAndDeployment/hosting', 'Environment hosting and capabilities'],
    ['/systems/system-a/assessments/environment', 'Assessment configuration'],
    ['/systems/system-a/provider-relationships', 'Legacy hosting association'],
    ['/systems/system-a/provider-relationships/setup', 'Legacy hosting association'],
    ['/systems/system-a/security-capabilities', 'Applied list for system-a'],
    ['/systems/system-a/security-capabilities/add', 'Add to system-a'],
    ['/systems/system-a/security-capabilities/provider/cap-a', 'provider detail for cap-a'],
    ['/systems/system-a/security-capabilities/local/cap-b', 'local detail for cap-b'],
    ['/systems/system-a/security-capabilities/inventory', 'Inventory creation import discovery'],
  ])('keeps %s inside the system layout', async (path, title) => {
    // Arrange / Act
    mount(path);
    // Assert
    expect(await screen.findByRole('heading', { name: title })).toBeVisible();
    expect(screen.getAllByTestId('system-layout')).toHaveLength(1);
    expect(screen.queryByRole('heading', { name: 'Organization library' })).not.toBeInTheDocument();
  });

  it('preserves old component links while rendering the new system view', async () => {
    // Arrange / Act
    mount('/systems/system-a/components?search=SOC#selected');
    // Assert
    await screen.findByRole('heading', { name: 'Applied list for system-a' });
    expect(screen.getByLabelText('Route')).toHaveTextContent('/systems/system-a/security-capabilities?search=SOC&view=component#selected');
  });

  it('routes fresh legacy add links to the guided flow without changing the system', async () => {
    // Arrange / Act
    mount('/systems/system-a/security-capabilities?dialog=capability&source=provider&recordId=cap-a');
    // Assert
    await screen.findByRole('heading', { name: 'Add to system-a' });
    expect(screen.getByLabelText('Route')).toHaveTextContent('/systems/system-a/security-capabilities/add?source=provider&recordId=cap-a');
  });

  it('keeps persisted single-record operations recoverable through the legacy setup link', async () => {
    // Arrange / Act
    mount('/systems/system-a/security-capabilities/setup?operation=legacy-a&step=3');
    // Assert
    expect(await screen.findByRole('heading', { name: 'Recover legacy operation' })).toBeVisible();
    expect(screen.getByTestId('system-layout')).toBeInTheDocument();
  });

  it('does not treat an invalid system detail source as the organization catalog', async () => {
    // Arrange / Act
    mount('/systems/system-a/security-capabilities/unknown/cap-a');
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Invalid system capability link');
    expect(screen.queryByRole('heading', { name: 'Organization library' })).not.toBeInTheDocument();
  });

  it('keeps a selected component from an older detail URL in the new component drawer route', async () => {
    // Arrange / Act
    mount('/systems/system-a/security-capabilities/provider/component-a?recordType=component#placement');
    // Assert
    await screen.findByRole('heading', { name: 'Applied list for system-a' });
    expect(screen.getByLabelText('Route')).toHaveTextContent(
      '/systems/system-a/security-capabilities?view=component&componentId=component-a&componentSource=provider#placement');
  });

  it('does not render private system content when selected-system identity disagrees', async () => {
    // Arrange / Act
    mount('/systems/system-b/security-capabilities');
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('selected organization and system could not be verified');
    expect(screen.queryByRole('heading', { name: /Applied list/ })).not.toBeInTheDocument();
  });
});
