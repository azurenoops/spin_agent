/**
 * #1017: system-domain mutations must require the matching server permission,
 * including callbacks retained by an already-open dialog. Browser role settings
 * are presentation preferences, not authorization. Legacy routes remain explicit.
 *
 * The dashboard boundary/component/categorization routes currently inherit only
 * authentication. These tests document the bounded frontend canManageSystem gate;
 * they do not assert backend enforcement that those routes do not yet provide.
 */
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import BoundaryManagement from '../../pages/BoundaryManagement';
import ComponentInventory from '../../pages/ComponentInventory';
import BaselineManagement from '../../pages/BaselineManagement';
import * as boundaries from '../../api/boundaries';
import * as components from '../../api/components';
import * as baseline from '../../api/systemDetail';
import { useWorkspaceSession, type WorkspaceSession } from '../../features/workspaces/WorkspaceBoundary';
import type { SystemWorkspacePermissions } from '../../features/workspaces/types';
import { SettingsContext, useSettingsProvider } from '../../hooks/useSettings';
import { onboarding } from '../../features/onboarding/api/onboardingApi';
import apiClient from '../../api/client';
import type { BoundaryComponentDto, BoundaryDefinitionDto, CategorizationInfo, SystemComponentDto, SystemDetailResponse } from '../../types/dashboard';
import { invokeClick, requireElement, workspaceSession } from '../helpers/domainPermissions';
import { systemDetail as assessmentSystemDetail } from '../fixtures/assessmentEnvironment';

vi.mock('../../features/workspaces/WorkspaceBoundary', () => ({ useWorkspaceSession: vi.fn() }));
vi.mock('../../hooks/usePolling', async () => {
  const { useEffect } = await import('react');
  return { usePolling: (fn: () => void) => useEffect(() => { fn(); }, [fn]) };
});
vi.mock('../../api/boundaries', () => ({
  fetchBoundaryDefinitions: vi.fn(), createBoundaryDefinition: vi.fn(), updateBoundaryDefinition: vi.fn(),
  deleteBoundaryDefinition: vi.fn(), fetchBoundaryResources: vi.fn(), fetchBoundaryComponents: vi.fn(),
  removeComponentFromBoundary: vi.fn(), listBoundaryComponents: vi.fn(), listBoundaryComponentCandidates: vi.fn(),
  assignComponent: vi.fn(), updateAssignment: vi.fn(), removeAssignment: vi.fn(),
  acquireLock: vi.fn(), releaseLock: vi.fn(), checkLockStatus: vi.fn(),
}));
vi.mock('../../api/components', () => ({
  getComponents: vi.fn(), createComponent: vi.fn(), updateComponent: vi.fn(), deleteComponent: vi.fn(),
  discoverSystemAzureResources: vi.fn(), importSystemAzureComponents: vi.fn(), relinkComponentFindings: vi.fn(),
  listComponents: vi.fn(), assignToSystem: vi.fn(), generateComponentDescription: vi.fn(),
}));
vi.mock('../../api/systemDetail', () => ({
  getBaselineDetail: vi.fn(), getSystemDetail: vi.fn(), selectBaseline: vi.fn(), setCategorization: vi.fn(),
}));
vi.mock('../../api/client', () => ({
  default: { get: vi.fn().mockResolvedValue({ data: { openCount: 0, overdueCount: 0 } }) },
}));
vi.mock('../../features/onboarding/api/onboardingApi', () => ({
  onboarding: { listAzureRegistrations: vi.fn().mockResolvedValue([]) },
}));

const systemId = 'sys-domain';
const boundary: BoundaryDefinitionDto = {
  id: 'boundary-a', name: 'Production', description: 'Test boundary', boundaryType: 'Logical',
  registeredSystemId: systemId, isPrimary: false, componentCount: 1, coveragePercent: 50,
  resourceCount: 0, createdAt: '2026-09-01T00:00:00Z',
};
const component: SystemComponentDto = {
  id: 'component-a', name: 'Server', componentType: 'Thing', status: 'Active', scopeLevel: 'System',
  linkedCapabilities: [], boundaryDefinitionId: boundary.id, azureResourceId: 'azure-a',
  subType: null, description: null, owner: null, personName: null, email: null, rmfRole: null,
  boundaryDefinitionName: boundary.name, createdAt: '2026-09-01T00:00:00Z', modifiedAt: null,
};
const orgComponent: components.OrgComponentDto = {
  id: component.id, name: component.name, componentType: component.componentType,
  status: component.status, createdAt: component.createdAt, capabilityLinks: [],
  systemAssignments: [{ id: 'legacy-a', boundaryDefinitionId: boundary.id, registeredSystemId: systemId }],
};
const assignment: BoundaryComponentDto = {
  assignmentId: 'assignment-a', componentId: component.id, componentName: component.name,
  componentType: 'Thing', source: 'System', isInScope: false, exclusionRationale: 'External', inheritanceProvider: null,
  subType: null, azureResourceId: null, azureResourceType: null, azureResourceGroup: null, azureLocation: null,
  createdAt: component.createdAt, createdBy: 'synthetic-user',
};
const categorization: CategorizationInfo = {
  confidentiality: 'Low', integrity: 'Low', availability: 'Low', overall: 'Low',
  formalNotation: 'SC = {(confidentiality, Low), (integrity, Low), (availability, Low)}',
  dodImpactLevel: 'IL2', isNationalSecuritySystem: false,
  informationTypes: [{ name: 'Health Care', confidentiality: 'Low', integrity: 'Low', availability: 'Low' }],
};
const systemDetail: SystemDetailResponse = {
  ...assessmentSystemDetail, systemId, categorization,
  keyMetrics: {
    ...assessmentSystemDetail.keyMetrics, complianceScoreDelta: 5, atoDaysRemaining: null,
    atoSeverity: 'none', atoExpirationDate: null, atoStatus: 'NotAuthorized',
    catIFindings: 0, catIIFindings: 1, catIIIFindings: 0,
  },
};
const baselineDetail: baseline.BaselineDetailResponse = {
  baselineId: 'baseline-a', baselineLevel: 'Low', totalControls: 1, inheritedControls: 0,
  sharedControls: 0, customerControls: 1, overlayApplied: null, tailoredInControls: 0, tailoredOutControls: 0,
  familyBreakdown: [], tailorings: [], controlIds: ['AC-1'],
  createdAt: component.createdAt, createdBy: 'synthetic-user', modifiedAt: null,
};

function session(permissions: Partial<SystemWorkspacePermissions> = {}, roles = ['MissionOwner']): WorkspaceSession {
  return workspaceSession(systemId, permissions, roles);
}

const pages = {
  boundaries: BoundaryManagement,
  components: ComponentInventory,
  baseline: BaselineManagement,
};
function Settings({ children }: { children: React.ReactNode }) {
  const settings = useSettingsProvider();
  return <SettingsContext.Provider value={settings}>{children}</SettingsContext.Provider>;
}
function page(kind: keyof typeof pages, legacy = false) {
  const Page = pages[kind];
  const prefix = legacy ? '' : '/workspaces/organizations/tenant-a';
  return <MemoryRouter initialEntries={[`${prefix}/systems/${systemId}/${kind}`]}>
    <Settings><Routes><Route path={`${prefix}/systems/:id/${kind}`} element={<Page />} /></Routes></Settings>
  </MemoryRouter>;
}

async function openBoundary() {
  fireEvent.click(await screen.findByText('Production'));
  await screen.findByRole('button', { name: 'Excluded' });
}

beforeEach(() => {
  vi.resetAllMocks();
  vi.mocked(onboarding.listAzureRegistrations).mockResolvedValue([]);
  vi.mocked(apiClient.get).mockResolvedValue({ data: { openCount: 0, overdueCount: 0 } });
  vi.mocked(useWorkspaceSession).mockReturnValue(session());
  vi.mocked(boundaries.fetchBoundaryDefinitions).mockResolvedValue([boundary]);
  vi.mocked(boundaries.fetchBoundaryResources).mockResolvedValue([]);
  vi.mocked(boundaries.fetchBoundaryComponents).mockResolvedValue([orgComponent]);
  vi.mocked(boundaries.listBoundaryComponents).mockResolvedValue({ items: [assignment], totalCount: 1, page: 1, pageSize: 50 });
  vi.mocked(boundaries.listBoundaryComponentCandidates).mockResolvedValue([
    { id: component.id, name: component.name, componentType: component.componentType, description: null, source: 'System' },
  ]);
  vi.mocked(boundaries.checkLockStatus).mockResolvedValue({ locked: false, lockedBy: null, lockedAt: null, expiresAt: null });
  vi.mocked(boundaries.acquireLock).mockResolvedValue({
    locked: true, lockedBy: 'synthetic-user', lockedAt: '2026-09-01T00:00:00Z', expiresAt: '2099-01-01T00:00:00Z',
  });
  vi.mocked(boundaries.deleteBoundaryDefinition).mockResolvedValue({
    deletedId: boundary.id, reassignedComponents: 1, reassignedMappings: 0, reassignedResources: 0, primaryBoundaryId: 'primary',
  });
  vi.mocked(components.getComponents).mockResolvedValue({
    systemId, totalCount: 1, nextCursor: null,
    items: [component], summary: { totalCount: 1, thingCount: 1, personCount: 0, placeCount: 0, policyCount: 0 },
  });
  vi.mocked(components.listComponents).mockResolvedValue({ items: [{ ...orgComponent, id: 'org-a' }], totalCount: 1, page: 1, pageSize: 200 });
  vi.mocked(components.deleteComponent).mockResolvedValue({ deletedId: component.id, flaggedCapabilities: [] });
  vi.mocked(components.discoverSystemAzureResources).mockResolvedValue({ resources: [
    { resourceId: 'azure-new', name: 'New server', type: 'VM', resourceGroup: 'rg-a', location: 'test', alreadyImported: false },
  ], totalCount: 1, nextCursor: null, failedResourceGroups: [] });
  vi.mocked(baseline.getBaselineDetail).mockRejectedValue({ response: { status: 404 } });
  vi.mocked(baseline.getSystemDetail).mockResolvedValue(systemDetail);
  vi.mocked(baseline.setCategorization).mockResolvedValue({
    id: 'categorization-a', overallCategorization: 'Low', confidentialityImpact: 'Low', integrityImpact: 'Low',
    availabilityImpact: 'Low', dodImpactLevel: 'IL2', nistBaseline: 'Low', informationTypeCount: 1,
    baselineReselected: null, baselineControls: null, inheritancesReapplied: null,
  });
});
afterEach(() => { cleanup(); localStorage.clear(); });

describe('System-domain permission boundary (#1017)', () => {
  it.each(['AO', 'ISSM'])('ignores forged %s preferences for a MissionOwner', async role => {
    // Arrange
    localStorage.setItem('ato-dashboard-settings', JSON.stringify({ role }));
    const view = render(page('boundaries'));
    await screen.findByRole('button', { name: '+ Add Boundary' });
    // Act
    await invokeClick(screen.getByRole('button', { name: '+ Add Boundary' }));
    // Assert
    expect(screen.getByRole('button', { name: '+ Add Boundary' })).toBeDisabled();
    expect(screen.getByTitle('Edit boundary')).toBeDisabled();
    expect(screen.getByTitle('Delete boundary')).toBeDisabled();
    expect(screen.queryByRole('button', { name: 'Create Boundary' })).not.toBeInTheDocument();
    expect(screen.getByRole('alert')).toHaveTextContent(/permission|not authorized/i);
    view.unmount();

    // Arrange
    const inventory = render(page('components'));
    await screen.findByRole('button', { name: 'Edit' });
    // Act
    await invokeClick(screen.getByRole('button', { name: 'Edit' }));
    // Assert
    for (const name of ['+ Add Component', 'Discover from Azure', 'Edit', 'Delete', '+ Capability']) {
      expect(screen.getByRole('button', { name })).toBeDisabled();
    }
    expect(screen.queryByRole('button', { name: 'Update' })).not.toBeInTheDocument();
    inventory.unmount();

    // Arrange
    render(page('baseline'));
    const select = await screen.findByRole('button', { name: 'Select Baseline' });
    // Act
    await invokeClick(select);
    // Assert
    expect(select).toBeDisabled();
    expect(baseline.selectBaseline).not.toHaveBeenCalled();
  });

  it.each([
    null, session(), session({ canManageSystem: false }),
    { ...session({ canManageSystem: true }), systemAccess: { ...session({ canManageSystem: true }).systemAccess!, systemId: 'other' } },
  ])('fails closed for missing, denied or mismatched canonical scope', async access => {
    // Arrange
    vi.mocked(useWorkspaceSession).mockReturnValue(access);
    render(page('components'));
    await screen.findByRole('button', { name: 'Edit' });
    // Act
    await invokeClick(screen.getByRole('button', { name: 'Delete' }));
    // Assert
    expect(screen.getByRole('button', { name: '+ Add Component' })).toBeDisabled();
    expect(screen.queryByText('Delete Component?')).not.toBeInTheDocument();
    expect(components.deleteComponent).not.toHaveBeenCalled();
  });

  it('allows a true server management grant with multiple roles', async () => {
    // Arrange
    vi.mocked(useWorkspaceSession).mockReturnValue(session({ canManageSystem: true }, ['MissionOwner', 'ISSM']));
    render(page('boundaries'));
    // Act
    fireEvent.click(await screen.findByRole('button', { name: '+ Add Boundary' }));
    fireEvent.change(screen.getByPlaceholderText('e.g., Production Environment'), { target: { value: 'New boundary' } });
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Create Boundary' })); });
    // Assert
    expect(boundaries.createBoundaryDefinition).toHaveBeenCalledWith(systemId, expect.objectContaining({ name: 'New boundary' }));
  });

  it.each(['create', 'edit', 'delete', 'assign'] as const)('permits component %s with a matching server grant', async mode => {
    // Arrange
    vi.mocked(useWorkspaceSession).mockReturnValue(session({ canManageSystem: true }, ['MissionOwner', 'ISSM']));
    render(page('components'));
    await screen.findByRole('button', { name: 'Edit' });
    // Act
    fireEvent.click(screen.getByRole('button', { name: mode === 'edit' ? 'Edit' : mode === 'delete' ? 'Delete' : '+ Add Component' }));
    if (mode === 'create') {
      fireEvent.change(screen.getByPlaceholderText('e.g., Microsoft Entra ID'), { target: { value: 'New component' } });
      await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Create' })); });
    } else if (mode === 'edit') {
      await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Update' })); });
    } else if (mode === 'delete') {
      await invokeClick(screen.getByText('Delete Component?').parentElement!.querySelector<HTMLButtonElement>('button.bg-red-600')!);
    } else {
      fireEvent.click(screen.getByRole('button', { name: 'Add Existing (Org-Level)' }));
      await invokeClick(await screen.findByRole('button', { name: 'Assign' }));
    }
    // Assert
    const method = {
      create: components.createComponent, edit: components.updateComponent,
      delete: components.deleteComponent, assign: components.assignToSystem,
    }[mode];
    expect(method).toHaveBeenCalledTimes(1);
  });

  it('permits discovery and import with an explicit matching management flag', async () => {
    // Arrange
    vi.mocked(useWorkspaceSession).mockReturnValue(session({ canManageSystem: true }));
    render(page('components'));
    fireEvent.click(await screen.findByRole('button', { name: 'Discover from Azure' }));
    fireEvent.change(screen.getByPlaceholderText('Azure Subscription ID'), { target: { value: 'subscription-a' } });
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Scan' }));
    await invokeClick(await screen.findByRole('button', { name: 'Import 1 Selected' }));
    // Assert
    expect(components.discoverSystemAzureResources).toHaveBeenCalledWith(systemId, { subscriptionId: 'subscription-a' });
    expect(components.importSystemAzureComponents).toHaveBeenCalledWith(systemId, expect.objectContaining({
      resources: [expect.objectContaining({ resourceId: 'azure-new' })],
    }));
  });

  it.each([false, true])('gates grouped component re-linking with the server flag %s', async allowed => {
    // Arrange
    vi.mocked(useWorkspaceSession).mockReturnValue(session({ canManageSystem: allowed }));
    vi.mocked(boundaries.fetchBoundaryDefinitions).mockResolvedValue([boundary, { ...boundary, id: 'boundary-b' }]);
    vi.mocked(components.relinkComponentFindings).mockResolvedValue({ linkedCount: 1 });
    render(page('components'));
    const relink = await screen.findByRole('button', { name: 'Re-link' });
    // Act
    await invokeClick(relink);
    // Assert
    if (allowed) {
      expect(relink).not.toBeDisabled();
      expect(components.relinkComponentFindings).toHaveBeenCalledWith(systemId, component.id);
    } else {
      expect(relink).toBeDisabled();
      expect(components.relinkComponentFindings).not.toHaveBeenCalled();
    }
  });

  it('does not fall back to legacy authority when a workspace session exists', async () => {
    // Arrange
    vi.mocked(useWorkspaceSession).mockReturnValue(session({ canManageSystem: false }));
    render(page('components', true));
    const add = await screen.findByRole('button', { name: '+ Add Component' });
    // Act
    await invokeClick(add);
    // Assert
    expect(add).toBeDisabled();
    expect(screen.queryByRole('button', { name: 'Create' })).not.toBeInTheDocument();
  });

  it.each(['edit', 'assign', 'scope', 'remove', 'release'] as const)('permits boundary %s with the management flag', async operation => {
    // Arrange
    vi.mocked(useWorkspaceSession).mockReturnValue(session({ canManageSystem: true }));
    render(page('boundaries'));
    await screen.findByTitle('Edit boundary');
    // Act
    if (operation === 'edit') {
      fireEvent.click(screen.getByTitle('Edit boundary'));
      await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Update Boundary' })); });
    } else {
      await openBoundary();
      if (operation === 'assign') {
        fireEvent.click(screen.getByRole('button', { name: '+ Assign Component' }));
        await invokeClick(await screen.findByRole('button', { name: 'Add' }));
      } else if (operation === 'remove') {
        await invokeClick(screen.getByRole('button', { name: 'Remove' }));
      } else {
        fireEvent.click(screen.getByRole('button', { name: 'Excluded' }));
        await screen.findByRole('button', { name: 'Save' });
        await invokeClick(screen.getByRole('button', { name: operation === 'scope' ? 'Save' : 'Release Lock' }));
      }
    }
    // Assert
    const method = {
      edit: boundaries.updateBoundaryDefinition, assign: boundaries.assignComponent,
      scope: boundaries.updateAssignment, remove: boundaries.removeAssignment, release: boundaries.releaseLock,
    }[operation];
    expect(method).toHaveBeenCalledTimes(1);
  });

  it.each(['create', 'edit', 'delete'] as const)('rechecks boundary %s in an open dialog after revocation', async mode => {
    // Arrange
    vi.mocked(useWorkspaceSession).mockReturnValue(session({ canManageSystem: true }));
    const view = render(page('boundaries'));
    await screen.findByRole('button', { name: '+ Add Boundary' });
    if (mode === 'create') {
      fireEvent.click(screen.getByRole('button', { name: '+ Add Boundary' }));
      fireEvent.change(screen.getByPlaceholderText('e.g., Production Environment'), { target: { value: 'New' } });
    } else fireEvent.click(screen.getByTitle(`${mode === 'edit' ? 'Edit' : 'Delete'} boundary`));
    // Act
    vi.mocked(useWorkspaceSession).mockReturnValue(session());
    view.rerender(page('boundaries'));
    const submit = screen.getByRole('button', { name: mode === 'create' ? 'Create Boundary' : mode === 'edit' ? 'Update Boundary' : 'Delete' });
    if (mode === 'delete') await invokeClick(submit);
    else await act(async () => { fireEvent.submit(submit.closest('form')!); });
    // Assert
    expect(submit).toBeDisabled();
    expect(boundaries.createBoundaryDefinition).not.toHaveBeenCalled();
    expect(boundaries.updateBoundaryDefinition).not.toHaveBeenCalled();
    expect(boundaries.deleteBoundaryDefinition).not.toHaveBeenCalled();
    expect(screen.getByRole('alert')).toHaveTextContent(/permission|not authorized/i);
  });

  it('blocks boundary assignment, scope, removal and lock acquisition for read-only users', async () => {
    // Arrange
    render(page('boundaries'));
    await openBoundary();
    // Act
    await invokeClick(screen.getByRole('button', { name: 'Excluded' }));
    await invokeClick(screen.getByRole('button', { name: 'Remove' }));
    await invokeClick(screen.getByRole('button', { name: '+ Assign Component' }));
    // Assert
    for (const name of ['Excluded', 'Remove', '+ Assign Component']) expect(screen.getByRole('button', { name })).toBeDisabled();
    expect(boundaries.acquireLock).not.toHaveBeenCalled();
    expect(boundaries.removeAssignment).not.toHaveBeenCalled();
    expect(screen.queryByPlaceholderText('Search eligible components...')).not.toBeInTheDocument();
  });

  it('rechecks candidate assignment after the picker loses permission', async () => {
    // Arrange
    vi.mocked(useWorkspaceSession).mockReturnValue(session({ canManageSystem: true }));
    const view = render(page('boundaries'));
    await openBoundary();
    fireEvent.click(screen.getByRole('button', { name: '+ Assign Component' }));
    await screen.findByRole('button', { name: 'Add' });
    // Act
    vi.mocked(useWorkspaceSession).mockReturnValue(session());
    view.rerender(page('boundaries'));
    await invokeClick(screen.getByRole('button', { name: 'Add' }));
    // Assert
    expect(screen.getByRole('button', { name: 'Add' })).toBeDisabled();
    expect(boundaries.assignComponent).not.toHaveBeenCalled();
  });

  it('rechecks scope-save and lock-release after revocation', async () => {
    // Arrange
    vi.mocked(useWorkspaceSession).mockReturnValue(session({ canManageSystem: true }));
    const view = render(page('boundaries'));
    await openBoundary();
    fireEvent.click(screen.getByRole('button', { name: 'Excluded' }));
    await screen.findByRole('button', { name: 'Save' });
    // Act
    vi.mocked(useWorkspaceSession).mockReturnValue(session());
    view.rerender(page('boundaries'));
    await invokeClick(screen.getByRole('button', { name: 'Save' }));
    await invokeClick(screen.getByRole('button', { name: 'Release Lock' }));
    // Assert
    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Release Lock' })).toBeDisabled();
    expect(boundaries.updateAssignment).not.toHaveBeenCalled();
    expect(boundaries.releaseLock).not.toHaveBeenCalled();
  });

  it('surfaces lock failures and does not open scope editing without a lock', async () => {
    // Arrange
    vi.mocked(useWorkspaceSession).mockReturnValue(session({ canManageSystem: true }));
    vi.mocked(boundaries.acquireLock).mockRejectedValue(new Error('Locked'));
    render(page('boundaries'));
    await openBoundary();
    // Act
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Excluded' })); });
    // Assert
    expect(screen.getByRole('alert')).toHaveTextContent(/lock/i);
    expect(screen.queryByRole('button', { name: 'Save' })).not.toBeInTheDocument();
  });

  it('blocks the legacy assignment removal path in a canonical workspace', async () => {
    // Arrange
    vi.mocked(boundaries.listBoundaryComponents).mockResolvedValue({ items: [], totalCount: 0, page: 1, pageSize: 50 });
    render(page('boundaries'));
    fireEvent.click(await screen.findByText('Production'));
    const remove = await screen.findByRole('button', { name: 'Remove' });
    // Act
    await invokeClick(remove);
    // Assert
    expect(remove).toBeDisabled();
    expect(boundaries.removeComponentFromBoundary).not.toHaveBeenCalled();
  });

  it.each(['create', 'edit', 'delete', 'assign'] as const)('rechecks component %s in an open dialog', async mode => {
    // Arrange
    vi.mocked(useWorkspaceSession).mockReturnValue(session({ canManageSystem: true }));
    const view = render(page('components'));
    await screen.findByRole('button', { name: 'Edit' });
    fireEvent.click(screen.getByRole('button', { name: mode === 'edit' ? 'Edit' : mode === 'delete' ? 'Delete' : '+ Add Component' }));
    if (mode === 'create') fireEvent.change(screen.getByPlaceholderText('e.g., Microsoft Entra ID'), { target: { value: 'New component' } });
    if (mode === 'assign') {
      fireEvent.click(screen.getByRole('button', { name: 'Add Existing (Org-Level)' }));
      await screen.findByRole('button', { name: 'Assign' });
    }
    // Act
    vi.mocked(useWorkspaceSession).mockReturnValue(session());
    view.rerender(page('components'));
    const name = { create: 'Create', edit: 'Update', delete: 'Delete', assign: 'Assign' }[mode];
    const submit = mode === 'delete'
      ? screen.getByText('Delete Component?').parentElement!.querySelector<HTMLButtonElement>('button.bg-red-600')!
      : screen.getByRole('button', { name });
    if (mode === 'create' || mode === 'edit') await act(async () => { fireEvent.submit(submit.closest('form')!); });
    else await invokeClick(submit);
    // Assert
    expect(submit).toBeDisabled();
    for (const method of [components.createComponent, components.updateComponent, components.deleteComponent, components.assignToSystem]) {
      expect(method).not.toHaveBeenCalled();
    }
    expect(screen.getByRole('alert')).toHaveTextContent(/permission|not authorized/i);
  });

  it('rechecks discovery scanning and import after revocation', async () => {
    // Arrange
    vi.mocked(useWorkspaceSession).mockReturnValue(session({ canManageSystem: true }));
    const view = render(page('components'));
    fireEvent.click(await screen.findByRole('button', { name: 'Discover from Azure' }));
    fireEvent.change(screen.getByPlaceholderText('Azure Subscription ID'), { target: { value: 'subscription-a' } });
    fireEvent.click(screen.getByRole('button', { name: 'Scan' }));
    await screen.findByText('New server');
    vi.mocked(components.discoverSystemAzureResources).mockClear();
    // Act
    vi.mocked(useWorkspaceSession).mockReturnValue(session());
    view.rerender(page('components'));
    await invokeClick(screen.getByRole('button', { name: 'Scan' }));
    await invokeClick(screen.getByRole('button', { name: 'Import 1 Selected' }));
    // Assert
    expect(screen.getByRole('button', { name: 'Scan' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Import 1 Selected' })).toBeDisabled();
    expect(components.discoverSystemAzureResources).not.toHaveBeenCalled();
    expect(components.importSystemAzureComponents).not.toHaveBeenCalled();
  });

  it('rechecks AI description generation in a component form after revocation', async () => {
    // Arrange
    vi.mocked(useWorkspaceSession).mockReturnValue(session({ canManageSystem: true }));
    const view = render(page('components'));
    fireEvent.click(await screen.findByRole('button', { name: 'Edit' }));
    // Act
    vi.mocked(useWorkspaceSession).mockReturnValue(session());
    view.rerender(page('components'));
    const summary = screen.getByRole('button', { name: 'AI Summary' });
    await invokeClick(summary);
    // Assert
    expect(summary).toBeDisabled();
    expect(components.generateComponentDescription).not.toHaveBeenCalled();
    expect(screen.getByRole('alert')).toHaveTextContent(/permission/i);
  });

  it('does not infer organization capability authority from system management', async () => {
    // Arrange
    vi.mocked(useWorkspaceSession).mockReturnValue(session({ canManageSystem: true }));
    render(page('components'));
    const create = await screen.findByRole('button', { name: '+ Capability' });
    // Act
    await invokeClick(create);
    // Assert
    expect(create).toBeDisabled();
    expect(screen.getByRole('alert')).toHaveTextContent(/permission|not authorized/i);
  });

  it('blocks role assignment via a Person component without an explicit role-assignment projection', async () => {
    // Arrange
    vi.mocked(useWorkspaceSession).mockReturnValue(session({ canManageSystem: true }));
    render(page('components'));
    fireEvent.click(await screen.findByRole('button', { name: '+ Add Component' }));
    fireEvent.change(screen.getByPlaceholderText('e.g., Microsoft Entra ID'), { target: { value: 'Officer' } });
    fireEvent.click(screen.getByRole('radio', { name: 'Person' }));
    // Act
    const role = screen.getByText('RMF Role').parentElement!.querySelector('select')!;
    fireEvent.change(role, { target: { value: 'AuthorizingOfficial' } });
    await act(async () => { fireEvent.submit(screen.getByRole('button', { name: 'Create' }).closest('form')!); });
    // Assert
    expect(role).toBeDisabled();
    expect(components.createComponent).not.toHaveBeenCalledWith(systemId, expect.objectContaining({ rmfRole: 'AuthorizingOfficial' }));
  });

  it('rechecks baseline selection inside an open dialog and surfaces denial', async () => {
    // Arrange
    vi.mocked(useWorkspaceSession).mockReturnValue(session({ canManageSystem: true }));
    const view = render(page('baseline'));
    fireEvent.click(await screen.findByRole('button', { name: 'Select Baseline' }));
    // Act
    vi.mocked(useWorkspaceSession).mockReturnValue(session());
    view.rerender(page('baseline'));
    const submit = requireElement(screen.getAllByRole('button', { name: 'Select Baseline' })[1]);
    await invokeClick(submit);
    // Assert
    expect(submit).toBeDisabled();
    expect(baseline.selectBaseline).not.toHaveBeenCalled();
    expect(screen.getByRole('alert')).toHaveTextContent(/permission|not authorized/i);
  });

  it('rechecks categorization save inside an open dialog', async () => {
    // Arrange
    vi.mocked(useWorkspaceSession).mockReturnValue(session({ canManageSystem: true }));
    vi.mocked(baseline.getBaselineDetail).mockResolvedValue(baselineDetail);
    const view = render(page('baseline'));
    fireEvent.click(await screen.findByRole('button', { name: 'Re-categorize' }));
    // Act
    vi.mocked(useWorkspaceSession).mockReturnValue(session());
    view.rerender(page('baseline'));
    const submit = screen.getByRole('button', { name: /save categorization/i });
    await invokeClick(submit);
    // Assert
    expect(submit).toBeDisabled();
    expect(baseline.setCategorization).not.toHaveBeenCalled();
    expect(screen.getByRole('alert')).toHaveTextContent(/permission|not authorized/i);
  });

  it.each([false, true])('gates initial categorization with the explicit management flag %s', async allowed => {
    // Arrange
    vi.mocked(useWorkspaceSession).mockReturnValue(session({ canManageSystem: allowed }));
    vi.mocked(baseline.getSystemDetail).mockResolvedValue({ ...systemDetail, categorization: null });
    render(page('baseline'));
    const open = await screen.findByRole('button', { name: 'Select Categorization' });
    // Act
    await invokeClick(open);
    // Assert
    if (allowed) {
      expect(open).not.toBeDisabled();
      expect(screen.getByText('Re-categorize System')).toBeInTheDocument();
    } else {
      expect(open).toBeDisabled();
      expect(screen.queryByText('Re-categorize System')).not.toBeInTheDocument();
    }
    expect(baseline.setCategorization).not.toHaveBeenCalled();
  });

  it('permits saving categorization and baseline from explicit management authority', async () => {
    // Arrange
    vi.mocked(useWorkspaceSession).mockReturnValue(session({ canManageSystem: true }, ['MissionOwner', 'ISSM']));
    vi.mocked(baseline.getBaselineDetail).mockResolvedValue(baselineDetail);
    const view = render(page('baseline'));
    fireEvent.click(await screen.findByRole('button', { name: 'Re-categorize' }));
    // Act
    await invokeClick(screen.getByRole('button', { name: /save categorization/i }));
    // Assert
    expect(baseline.setCategorization).toHaveBeenCalledWith(systemId, expect.objectContaining({
      informationTypes: [expect.objectContaining({ name: 'Health Care' })],
    }));
    view.unmount();
    // Arrange
    vi.mocked(baseline.getBaselineDetail).mockRejectedValue({ response: { status: 404 } });
    render(page('baseline'));
    fireEvent.click(await screen.findByRole('button', { name: 'Select Baseline' }));
    // Act
    await invokeClick(screen.getAllByRole('button', { name: 'Select Baseline' })[1]);
    // Assert
    expect(baseline.selectBaseline).toHaveBeenCalledWith(systemId, expect.objectContaining({ applyOverlay: true }));
  });

  it('surfaces baseline selection errors instead of silently discarding them', async () => {
    // Arrange
    vi.mocked(useWorkspaceSession).mockReturnValue(session({ canManageSystem: true }));
    vi.mocked(baseline.selectBaseline).mockRejectedValue(new Error('Selection rejected'));
    render(page('baseline'));
    fireEvent.click(await screen.findByRole('button', { name: 'Select Baseline' }));
    // Act
    await invokeClick(screen.getAllByRole('button', { name: 'Select Baseline' })[1]);
    // Assert
    expect(screen.getByRole('alert')).toHaveTextContent(/Selection rejected|Failed to select/i);
  });

  it('retains deliberate legacy management without a workspace session', async () => {
    // Arrange
    vi.mocked(useWorkspaceSession).mockReturnValue(null);
    const view = render(page('boundaries', true));
    // Act
    fireEvent.click(await screen.findByTitle('Delete boundary'));
    await invokeClick(screen.getByRole('button', { name: 'Delete' }));
    // Assert
    expect(boundaries.deleteBoundaryDefinition).toHaveBeenCalledWith(boundary.id);
    view.unmount();
    // Arrange
    const inventory = render(page('components', true));
    await screen.findByRole('button', { name: 'Edit' });
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Delete' }));
    await invokeClick(screen.getByText('Delete Component?').parentElement!.querySelector<HTMLButtonElement>('button.bg-red-600')!);
    // Assert
    expect(components.deleteComponent).toHaveBeenCalledWith(component.id);
    inventory.unmount();
    // Arrange
    render(page('baseline', true));
    fireEvent.click(await screen.findByRole('button', { name: 'Select Baseline' }));
    // Act
    await invokeClick(screen.getAllByRole('button', { name: 'Select Baseline' })[1]);
    // Assert
    expect(baseline.selectBaseline).toHaveBeenCalledWith(systemId, expect.objectContaining({ applyOverlay: true }));
  });
});
