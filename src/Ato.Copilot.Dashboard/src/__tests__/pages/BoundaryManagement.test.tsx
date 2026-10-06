import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import BoundaryManagement from '../../pages/BoundaryManagement';
import * as api from '../../api/boundaries';
import { getComponents } from '../../api/components';
import { useSystemMutationPermission } from '../../components/permissions/useSystemMutationPermission';
import type { BoundaryDefinitionDto, BoundaryComponentDto } from '../../types/dashboard';
import type { ReactNode } from 'react';
import '../helpers/dialog';

vi.mock('../../components/permissions/useSystemMutationPermission', () => ({ useSystemMutationPermission: vi.fn() }));
vi.mock('../../components/layout/SystemLayout', () => ({ useSystemContext: () => ({ detail: { systemId: 'system-a', name: 'Mission Alpha' } }) }));
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
vi.mock('../../api/components', () => ({ getComponents: vi.fn() }));
vi.mock('../../features/systems/GovernedBoundaryInventory', () => ({
  default: ({ children, recordStatus, renderHeading }: { children: ReactNode; recordStatus?: ReactNode; renderHeading?: (actions: ReactNode) => ReactNode }) =>
    <>{renderHeading?.(null)}{recordStatus}{children}</>,
}));

const boundary: BoundaryDefinitionDto = {
  id: 'boundary-a', name: 'Production', description: 'Application and storage.', boundaryType: 'Logical',
  registeredSystemId: 'system-a', isPrimary: true, componentCount: 1, coveragePercent: 50,
  resourceCount: 0, createdAt: '2026-09-01T00:00:00Z',
};
const assignment: BoundaryComponentDto = {
  assignmentId: 'assignment-a', componentId: 'component-a', componentName: 'Mission storage', componentType: 'Thing',
  source: 'System', isInScope: false, exclusionRationale: 'External dependency', inheritanceProvider: null,
  subType: null, azureResourceId: null, azureResourceType: null, azureResourceGroup: null, azureLocation: null,
  createdAt: '2026-09-01T00:00:00Z', createdBy: 'Recorder A',
};
function page() {
  return <MemoryRouter initialEntries={['/systems/system-a/boundaries']}>
    <Routes><Route path="/systems/:id/boundaries" element={<BoundaryManagement />} /></Routes>
  </MemoryRouter>;
}
function expectNoWrites() {
  for (const method of [api.acquireLock, api.releaseLock, api.createBoundaryDefinition, api.updateBoundaryDefinition,
    api.deleteBoundaryDefinition, api.assignComponent, api.updateAssignment, api.removeAssignment, api.removeComponentFromBoundary]) {
    expect(method).not.toHaveBeenCalled();
  }
}
describe('Inventory and boundary mock parity', () => {
  it('offers the authorized canonical create action beside a populated register without the duplicate sidebar', async () => {
    // Arrange
    render(page());
    const register = await screen.findByRole('region', { name: 'Recorded boundary inventory' });
    const create = within(register).getByRole('button', { name: 'Add System Boundary' });
    create.focus();
    // Act
    fireEvent.click(create);
    // Assert
    const dialog = screen.getByRole('dialog', { name: 'Create Boundary' });
    expect(dialog).toHaveTextContent('saving updates the named definition immediately');
    expect(screen.queryByRole('button', { name: 'Review boundary' })).not.toBeInTheDocument();
    expect(screen.queryByText('SSP · Boundary description and inventory')).not.toBeInTheDocument();
    expect(screen.queryByRole('complementary')).not.toBeInTheDocument();
    expect(register).toHaveTextContent('Canonical definition and placement changes take effect immediately');
    expectNoWrites();
    // Act
    fireEvent(dialog, new Event('cancel', { cancelable: true }));
    // Assert
    expect(create).toHaveFocus();
    expectNoWrites();
  });
  it('shows a saved empty boundary in the same register counted by the status', async () => {
    // Arrange
    vi.mocked(api.fetchBoundaryDefinitions).mockResolvedValue([{ ...boundary, name: 'mission-api', componentCount: 0 }]);
    vi.mocked(api.listBoundaryComponents).mockResolvedValue({ items: [], totalCount: 0, page: 1, pageSize: 25 });
    // Act
    render(page());
    // Assert
    const row = await screen.findByRole('row', { name: /mission-api/ });
    expect(within(row).getByRole('cell', { name: 'Logical' })).toBeVisible();
    expect(screen.getByRole('status', { name: 'Boundary record status' })).toHaveTextContent('1 boundary defined');
    expect(api.listBoundaryComponents).not.toHaveBeenCalled();
    // Act
    fireEvent.click(within(row).getByRole('button', { name: 'Open boundary mission-api' }));
    // Assert
    expect(await screen.findByRole('dialog', { name: 'mission-api — Details' })).toBeVisible();
    expect(await screen.findByText('No components assigned to this boundary yet.')).toBeVisible();
    expectNoWrites();
  });
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(useSystemMutationPermission).mockReturnValue(true);
    vi.mocked(api.fetchBoundaryDefinitions).mockResolvedValue([boundary, { ...boundary, id: 'boundary-b', name: 'Recovery', isPrimary: false }]);
    vi.mocked(api.fetchBoundaryComponents).mockResolvedValue([]);
    vi.mocked(api.fetchBoundaryResources).mockResolvedValue([]);
    vi.mocked(api.listBoundaryComponents).mockResolvedValue({ items: [assignment], totalCount: 1, page: 1, pageSize: 25 });
    vi.mocked(api.checkLockStatus).mockResolvedValue({ locked: false, lockedBy: null, lockedAt: null, expiresAt: null });
    vi.mocked(api.updateAssignment).mockResolvedValue(assignment);
    vi.mocked(api.listBoundaryComponentCandidates).mockResolvedValue([]);
    vi.mocked(getComponents).mockResolvedValue({
      systemId: 'system-a', items: [], totalCount: 1, nextCursor: null,
      summary: { totalCount: 1, thingCount: 1, personCount: 0, placeCount: 0, policyCount: 0 },
    });
  });

  it('shows the mock hierarchy with one main table, six tabs and real document/source handoffs', async () => {
    // Arrange / Act
    render(page());
    // Assert
    expect(await screen.findByRole('heading', { name: 'Recorded boundary definitions' })).toBeVisible();
    expect(screen.getByText('Mission Alpha')).toBeVisible();
    expect(screen.getByRole('status', { name: 'Boundary record status' })).toHaveTextContent('2 boundaries defined');
    expect(screen.getByRole('status', { name: 'Boundary record status' })).toHaveTextContent('Viewing does not approve scope');
    expect(within(screen.getByRole('navigation', { name: 'System task views' })).getAllByRole('link')).toHaveLength(7);
    expect(screen.getByRole('button', { name: 'Open boundary Production' })).toBeEnabled();
    expect(await screen.findAllByRole('table')).toHaveLength(1);
    expect(screen.queryByRole('button', { name: 'Manage boundaries' })).not.toBeInTheDocument();
    expect(screen.queryByText('NIST RMF Step P-16: Complete Asset Identification First')).not.toBeInTheDocument();
    expect(screen.queryByRole('textbox', { name: 'Search boundaries' })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Preview contribution' })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'View package readiness' })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Manage component inventory' })).not.toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Review & ownership' })).not.toBeInTheDocument();
    expectNoWrites();
  });
  it('opens the selected row in a right drawer without introducing another boundary table', async () => {
    // Arrange
    render(page());
    await screen.findByRole('table');
    const manage = screen.getByRole('button', { name: 'Open boundary Production' });
    manage.focus();
    // Act
    fireEvent.click(manage);
    // Assert
    const dialog = screen.getByRole('dialog', { name: 'Production — Details' });
    expect(dialog).toHaveClass('ml-auto', 'h-dvh');
    fireEvent.click(await within(dialog).findByText('Component details'));
    expect(within(dialog).getByText('assignment-a')).toBeVisible();
    expect(within(dialog).getByText('component-a')).toBeVisible();
    expect(within(dialog).getByRole('button', { name: 'Create boundary' })).toBeVisible();
    expect(within(dialog).getByRole('button', { name: 'Edit boundary' })).toBeVisible();
    expect(within(dialog).getByRole('button', { name: 'Add components to boundary' })).toBeVisible();
    expect(screen.queryByRole('textbox', { name: 'Search boundaries' })).not.toBeInTheDocument();
    expect(screen.getAllByRole('table')).toHaveLength(1);
    // Act
    fireEvent(dialog, new Event('cancel', { bubbles: false, cancelable: true }));
    // Assert
    expect(screen.getAllByRole('table')).toHaveLength(1);
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(manage).toHaveFocus();
    expectNoWrites();
  });

  it('wraps complete long placement identifiers and names without truncating their values', async () => {
    // Arrange
    const name = `mission-${'application'.repeat(12)}`;
    const resourceId = `/subscriptions/demo/resourceGroups/mission/providers/Microsoft.Web/sites/${'application'.repeat(20)}`;
    vi.mocked(api.listBoundaryComponents).mockResolvedValue({
      items: [{ ...assignment, componentName: name, azureResourceId: resourceId }], totalCount: 1, page: 1, pageSize: 25,
    });
    render(page());
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Open boundary Production' }));
    await screen.findByRole('button', { name: 'Excluded' });
    fireEvent.click(await screen.findByText('Component details'));
    // Assert
    const metadata = screen.getByRole('region', { name: `Placement ${name}` });
    expect(metadata).toHaveClass('min-w-0');
    const title = within(metadata).getByRole('heading', { name });
    expect(title).toHaveClass('break-all');
    expect(title.textContent).toBe(name);
    const value = within(metadata).getByText(resourceId);
    expect(value).toHaveClass('break-all', 'min-w-0');
    expect(value.textContent).toBe(resourceId);
    expect(value.parentElement).toHaveClass('min-w-0');
    expect(value.closest('dl')).toHaveClass('grid-cols-1', 'min-w-0');
  });

  it('persists the selected candidate, guards pending writes and refreshes the register', async () => {
    // Arrange
    let resolveAdd!: (value: BoundaryComponentDto) => void;
    vi.mocked(api.assignComponent).mockReturnValue(new Promise(resolve => { resolveAdd = resolve; }));
    vi.mocked(api.listBoundaryComponentCandidates).mockResolvedValue([
      { id: 'candidate-a', name: 'API service', componentType: 'Thing', source: 'Organization', description: null },
    ]);
    render(page());
    fireEvent.click(await screen.findByRole('button', { name: 'Open boundary Production' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Add components to boundary' }));
    const drawer = screen.getByRole('dialog', { name: 'Production — Details' });
    // Act
    fireEvent.click(await within(drawer).findByRole('button', { name: 'Add' }));
    fireEvent(drawer, new Event('cancel', { cancelable: true }));
    // Assert
    expect(drawer).toHaveAttribute('aria-busy', 'true');
    expect(within(drawer).getByRole('button', { name: 'Close dialog' })).toBeDisabled();
    expect(within(drawer).getByRole('button', { name: 'Edit boundary' })).toBeDisabled();
    expect(api.assignComponent).toHaveBeenCalledWith('system-a', 'boundary-a', {
      componentId: 'candidate-a', source: 'Organization', isInScope: true,
    });
    expect(api.acquireLock).not.toHaveBeenCalled();
    const added = { ...assignment, assignmentId: 'added-assignment', componentId: 'candidate-a', componentName: 'API service', isInScope: true };
    vi.mocked(api.listBoundaryComponents).mockResolvedValue({ items: [assignment, added], totalCount: 2, page: 1, pageSize: 25 });
    // Act
    await act(async () => { resolveAdd(added); });
    // Assert
    expect(drawer).toBeVisible();
    expect(drawer).toHaveAttribute('aria-busy', 'false');
    expect(within(drawer).getByRole('region', { name: 'Placement API service' })).toBeVisible();
    // Act
    fireEvent.click(within(drawer).getByRole('button', { name: 'Close dialog' }));
    // Assert
    expect(screen.queryByRole('cell', { name: 'API service' })).not.toBeInTheDocument();
    expect(screen.getByRole('cell', { name: 'Production' })).toBeVisible();
    fireEvent.click(screen.getByRole('button', { name: 'Open boundary Production' }));
    expect(await screen.findByRole('region', { name: 'Placement API service' })).toBeVisible();
  });

  it('offers the scoped inventory handoff when the component picker has no eligible candidates', async () => {
    // Arrange
    vi.mocked(api.listBoundaryComponents).mockResolvedValue({ items: [], totalCount: 0, page: 1, pageSize: 25 });
    vi.mocked(api.listBoundaryComponentCandidates).mockResolvedValue([]);
    render(page());
    fireEvent.click(await screen.findByRole('button', { name: 'Open boundary Production' }));
    const drawer = screen.getByRole('dialog', { name: 'Production — Details' });
    // Act
    fireEvent.click(within(drawer).getByRole('button', { name: 'Add components to boundary' }));
    // Assert
    const emptyPicker = (await within(drawer).findByText('No eligible components found')).parentElement!;
    expect(emptyPicker).toBeVisible();
    expect(within(emptyPicker).getByRole('link', { name: 'Manage component inventory' }))
      .toHaveAttribute('href', '/systems/system-a/security-capabilities/inventory');
    expect(within(drawer).getByText(/then return here to add eligible components to this boundary/)).toBeVisible();
    expectNoWrites();
  });

  it('preserves the boundary row and restores focus to Open after adding a component', async () => {
    // Arrange
    const added = { ...assignment, assignmentId: 'added-assignment', componentId: 'candidate-a', componentName: 'API service' };
    vi.mocked(api.assignComponent).mockResolvedValue(added);
    vi.mocked(api.listBoundaryComponentCandidates).mockResolvedValue([
      { id: 'candidate-a', name: 'API service', componentType: 'Thing', source: 'System', description: null },
    ]);
    render(page());
    const opener = await screen.findByRole('button', { name: 'Open boundary Production' });
    const review = screen.getByRole('button', { name: 'Add System Boundary' });
    const focusReview = review.focus.bind(review);
    const focus = vi.spyOn(review, 'focus').mockImplementation(() => {
      // Native modal dialogs make background elements inert until they close.
      if (!document.querySelector('dialog[open]')) focusReview();
    });
    opener.focus();
    fireEvent.click(opener);
    const drawer = screen.getByRole('dialog', { name: 'Production — Details' });
    fireEvent.click(within(drawer).getByRole('button', { name: 'Add components to boundary' }));
    const add = await within(drawer).findByRole('button', { name: 'Add' });
    vi.mocked(api.listBoundaryComponents).mockResolvedValue({ items: [assignment, added], totalCount: 2, page: 1, pageSize: 25 });
    // Act
    await act(async () => { fireEvent.click(add); });
    // Assert
    expect(drawer).toBeVisible();
    expect(drawer).toHaveAttribute('aria-busy', 'false');
    expect(opener.isConnected).toBe(true);
    // Act
    fireEvent(drawer, new Event('cancel', { cancelable: true }));
    // Assert
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(opener).toHaveFocus();
    expect(screen.queryByRole('cell', { name: 'API service' })).not.toBeInTheDocument();
    expect(screen.getByRole('cell', { name: 'Production' })).toBeVisible();
    focus.mockRestore();
  });

  it('can explicitly exclude an included component and retains failed scope input', async () => {
    // Arrange
    vi.mocked(api.listBoundaryComponents).mockResolvedValue({ items: [{ ...assignment, isInScope: true, exclusionRationale: null }], totalCount: 1, page: 1, pageSize: 25 });
    vi.mocked(api.acquireLock).mockResolvedValue({ locked: true, lockedBy: 'Recorder A', lockedAt: '2026-09-01', expiresAt: '2099-01-01' });
    vi.mocked(api.updateAssignment).mockRejectedValue(new Error('Conflict'));
    render(page());
    fireEvent.click(await screen.findByRole('button', { name: 'Open boundary Production' }));
    fireEvent.click(await screen.findByRole('button', { name: 'In Scope' }));
    const drawer = screen.getByRole('dialog', { name: 'Production — Details' });
    // Act
    fireEvent.change(await within(drawer).findByRole('combobox', { name: 'Placement scope' }), { target: { value: 'excluded' } });
    fireEvent.change(screen.getByPlaceholderText('Exclusion rationale (required)'), { target: { value: 'Outside authorization scope' } });
    await act(async () => { fireEvent.click(within(drawer).getByRole('button', { name: 'Save' })); });
    // Assert
    expect(api.updateAssignment).toHaveBeenCalledWith('system-a', 'boundary-a', 'assignment-a', {
      isInScope: false, exclusionRationale: 'Outside authorization scope', inheritanceProvider: null,
    });
    expect(within(drawer).getByRole('alert')).toHaveTextContent('Failed to update component scope');
    expect(screen.getByPlaceholderText('Exclusion rationale (required)')).toHaveValue('Outside authorization scope');
  });

  it('keeps candidate search and selection available after assignment failure', async () => {
    // Arrange
    vi.mocked(api.assignComponent).mockRejectedValue(new Error('Conflict'));
    vi.mocked(api.listBoundaryComponentCandidates).mockResolvedValue([
      { id: 'candidate-a', name: 'API service', componentType: 'Thing', source: 'Organization', description: null },
    ]);
    render(page());
    fireEvent.click(await screen.findByRole('button', { name: 'Open boundary Production' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Add components to boundary' }));
    const search = screen.getByPlaceholderText('Search eligible components...');
    fireEvent.change(search, { target: { value: 'API' } });
    const add = await screen.findByRole('button', { name: 'Add' });
    // Act
    await act(async () => { fireEvent.click(add); });
    // Assert
    expect(screen.getByRole('alert')).toHaveTextContent('Failed to assign component');
    expect(search).toHaveValue('API');
    expect(screen.getByRole('button', { name: 'Add' })).toBeEnabled();
    expect(screen.getByRole('button', { name: 'Close dialog' })).toBeEnabled();
    expect(screen.getByRole('dialog')).toHaveAttribute('aria-busy', 'false');
  });

  it('loads all component pages inside the exact boundary drawer', async () => {
    // Arrange
    const neighbor = { ...assignment, assignmentId: 'neighbor', componentId: 'component-b', componentName: 'Other service' };
    vi.mocked(api.listBoundaryComponents).mockImplementation(async (_systemId, _boundaryId, options) => {
      if (options?.pageSize === 25) return { items: [assignment], totalCount: 1, page: 1, pageSize: 25 };
      return { items: options?.page === 2 ? [assignment] : [neighbor], totalCount: 2, page: options?.page ?? 1, pageSize: 1 };
    });
    render(page());
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Open boundary Production' }));
    // Assert
    const selected = await screen.findByRole('region', { name: 'Placement Mission storage' });
    expect(selected).toBeVisible();
    expect(screen.getByRole('region', { name: 'Placement Other service' })).toBeVisible();
    expect(api.listBoundaryComponents).toHaveBeenCalledWith('system-a', 'boundary-a', { page: 2, pageSize: 1 });
    expectNoWrites();
  });

  it('blocks close while an explicit scope-edit lock request is pending', async () => {
    // Arrange
    let resolveLock!: (value: Awaited<ReturnType<typeof api.acquireLock>>) => void;
    vi.mocked(api.acquireLock).mockReturnValue(new Promise(resolve => { resolveLock = resolve; }));
    render(page());
    fireEvent.click(await screen.findByRole('button', { name: 'Open boundary Production' }));
    const drawer = screen.getByRole('dialog', { name: 'Production — Details' });
    expectNoWrites();
    // Act
    fireEvent.click(await within(drawer).findByRole('button', { name: 'Excluded' }));
    fireEvent(drawer, new Event('cancel', { cancelable: true }));
    // Assert
    expect(drawer).toHaveAttribute('aria-busy', 'true');
    expect(within(drawer).getByRole('button', { name: 'Close dialog' })).toBeDisabled();
    // Act
    await act(async () => { resolveLock({ locked: true, lockedBy: 'Recorder A', lockedAt: '2026-09-01', expiresAt: '2099-01-01' }); });
    // Assert
    await waitFor(() => expect(within(drawer).getByRole('button', { name: 'Save' })).toBeEnabled());
    expect(drawer).toHaveAttribute('aria-busy', 'false');
    expect(api.updateAssignment).not.toHaveBeenCalled();
    expect(api.releaseLock).not.toHaveBeenCalled();
  });

  it('prefers the exact legacy boundary assignment over an unplaced system neighbor', async () => {
    // Arrange
    vi.mocked(api.listBoundaryComponents).mockResolvedValue({ items: [], totalCount: 0, page: 1, pageSize: 25 });
    vi.mocked(api.fetchBoundaryComponents).mockResolvedValue([{
      id: 'legacy-component', name: 'Legacy service', componentType: 'Thing', status: 'Active',
      createdAt: '2026-09-01', capabilityLinks: [],
      systemAssignments: [
        { id: 'unplaced', registeredSystemId: 'system-a' },
        { id: 'exact', registeredSystemId: 'system-a', boundaryDefinitionId: 'boundary-a' },
      ],
    }]);
    render(page());
    fireEvent.click(await screen.findByRole('button', { name: 'Open boundary Production' }));
    const remove = await screen.findByRole('button', { name: 'Remove' });
    // Act
    await act(async () => { fireEvent.click(remove); });
    // Assert
    expect(api.removeComponentFromBoundary).toHaveBeenCalledWith('legacy-component', 'exact');
  });

  it.each([true, false])('reviews the explicitly selected boundary without writes with canManage=%s', async allowed => {
    // Arrange
    vi.mocked(useSystemMutationPermission).mockReturnValue(allowed);
    render(page());
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Open boundary Recovery' }));
    // Assert
    expect(await screen.findByRole('dialog', { name: 'Recovery — Details' })).toBeVisible();
    expect(api.fetchBoundaryComponents).toHaveBeenCalledWith('boundary-b');
    expect(api.checkLockStatus).toHaveBeenCalledWith('system-a', 'boundary-b');
    expectNoWrites();
  });

  it('explains cloud-native inventory without treating boundary rows as a complete hardware/software register', async () => {
    // Arrange
    render(page());
    await screen.findByRole('table');
    expect(screen.queryByText('Do cloud-native systems need an inventory?')).not.toBeInTheDocument();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Open boundary Production' }));
    fireEvent.click(await screen.findByText('Inventory & scope guidance'));
    // Assert
    expect(await screen.findByText('Yes, cloud-native systems still require an inventory of in-scope virtual resources, managed services, workloads and software. Document provider-managed infrastructure as a dependency; do not invent physical hardware details.')).toBeVisible();
    expect(screen.getByText('Boundary rows identify component placements, not a complete hardware/software register.')).toBeVisible();
    expect(screen.getByRole('link', { name: 'Manage component inventory' })).toHaveAttribute('href', '/systems/system-a/security-capabilities/inventory');
    expectNoWrites();
  });

  it('opens the exact primary row and restores focus to its action on close', async () => {
    // Arrange
    vi.mocked(api.fetchBoundaryDefinitions).mockResolvedValue([
      { ...boundary, id: 'boundary-b', name: 'Recovery', isPrimary: false }, boundary,
    ]);
    render(page());
    const review = await screen.findByRole('button', { name: 'Open boundary Production' });
    review.focus();
    // Act
    fireEvent.click(review);
    const dialog = await screen.findByRole('dialog', { name: 'Production — Details' });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Close dialog' }));
    // Assert
    expect(screen.getByRole('cell', { name: 'Production' })).toBeVisible();
    expect(screen.getByRole('cell', { name: 'Recovery' })).toBeVisible();
    expect(api.fetchBoundaryComponents).toHaveBeenCalledWith('boundary-a');
    expect(review).toHaveFocus();
    expectNoWrites();
  });

  it.each([
    { allowed: true, populated: false }, { allowed: false, populated: false },
    { allowed: true, populated: true }, { allowed: false, populated: true },
  ])('gates the heading create action with canManage=$allowed and populated=$populated', async ({ allowed, populated }) => {
    // Arrange
    vi.mocked(useSystemMutationPermission).mockReturnValue(allowed);
    vi.mocked(api.fetchBoundaryDefinitions).mockResolvedValue(populated ? [boundary] : []);
    render(page());
    // Assert
    await screen.findByRole('heading', { name: 'Recorded boundary definitions' });
    if (populated) expect(screen.getByRole('button', { name: 'Open boundary Production' })).toBeVisible();
    else expect(screen.getByText('No boundaries defined yet.')).toBeVisible();
    expect(screen.queryByRole('button', { name: 'Review boundary' })).not.toBeInTheDocument();
    expect(screen.getByRole('status', { name: 'Boundary record status' })).toHaveTextContent(populated ? '1 boundary defined' : 'No boundary recorded');
    expect(Boolean(screen.queryByRole('button', { name: 'Add System Boundary' }))).toBe(allowed);
    expectNoWrites();
  });

  it('rejects failed detail reads without presenting another boundary components', async () => {
    // Arrange
    vi.mocked(api.fetchBoundaryComponents).mockRejectedValue(new Error('Unavailable'));
    render(page());
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Open boundary Production' }));
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Failed to load boundary components');
    expectNoWrites();
  });

  it('retains explicit lock acquisition and release for scope editing only', async () => {
    // Arrange
    vi.mocked(api.acquireLock).mockResolvedValue({ locked: true, lockedBy: 'Recorder A', lockedAt: '2026-09-01', expiresAt: '2099-01-01' });
    render(page());
    fireEvent.click(await screen.findByRole('button', { name: 'Open boundary Production' }));
    const scope = await screen.findByRole('button', { name: 'Excluded' });
    expectNoWrites();
    // Act
    await act(async () => { fireEvent.click(scope); });
    // Assert
    expect(api.acquireLock).toHaveBeenCalledWith('system-a', 'boundary-a', 'current-user', 'Current User');
    expect(await screen.findByRole('button', { name: 'Save' })).toBeVisible();
    // Act
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Release Lock' })); });
    // Assert
    expect(api.releaseLock).toHaveBeenCalledWith('system-a', 'boundary-a');
  });

  it('refreshes the main register after an explicit scope edit succeeds', async () => {
    // Arrange
    vi.mocked(api.acquireLock).mockResolvedValue({ locked: true, lockedBy: 'Recorder A', lockedAt: '2026-09-01', expiresAt: '2099-01-01' });
    render(page());
    fireEvent.click(await screen.findByRole('button', { name: 'Open boundary Production' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Excluded' }));
    const dialog = screen.getByRole('dialog', { name: 'Production — Details' });
    const scope = await within(dialog).findByRole('combobox');
    vi.mocked(api.listBoundaryComponents).mockResolvedValue({
      items: [{ ...assignment, isInScope: true, exclusionRationale: null }], totalCount: 1, page: 1, pageSize: 25,
    });
    // Act
    fireEvent.change(scope, { target: { value: 'inScope' } });
    await act(async () => { fireEvent.click(within(dialog).getByRole('button', { name: 'Save' })); });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Close dialog' }));
    // Assert
    expect(screen.getByRole('cell', { name: 'Production' })).toBeVisible();
    fireEvent.click(screen.getByRole('button', { name: 'Open boundary Production' }));
    expect(await screen.findByRole('button', { name: 'In Scope' })).toBeVisible();
    expect(api.updateAssignment).toHaveBeenCalledWith('system-a', 'boundary-a', 'assignment-a',
      { isInScope: true, exclusionRationale: null, inheritanceProvider: null });
  });

  it.each(['create', 'edit'] as const)('opens one native %s dialog and restores focus after Escape', async mode => {
    // Arrange
    if (mode === 'create') vi.mocked(api.fetchBoundaryDefinitions).mockResolvedValue([]);
    render(page());
    await screen.findByRole('heading', { name: 'Recorded boundary definitions' });
    if (mode === 'edit') {
      await screen.findByRole('cell', { name: 'Production' });
      fireEvent.click(screen.getByRole('button', { name: 'Open boundary Production' }));
      await screen.findByRole('button', { name: 'Excluded' });
    }
    const trigger = mode === 'create' ? screen.getByRole('button', { name: 'Add System Boundary' })
      : screen.getAllByTitle('Edit boundary')[0]!;
    trigger.focus();
    // Act
    fireEvent.click(trigger);
    // Assert
    const dialog = screen.getByRole('dialog', { name: mode === 'create' ? 'Create Boundary' : 'Edit Boundary' });
    expect(dialog.tagName).toBe('DIALOG');
    expect(dialog).toHaveAttribute('open');
    expect(screen.getAllByRole('dialog')).toHaveLength(1);
    expect(within(dialog).getByRole('textbox', { name: 'Name *' })).toHaveValue(mode === 'create' ? '' : boundary.name);
    expect(within(dialog).getByRole('radio', { name: 'Logical' })).toBeChecked();
    // Act
    fireEvent(dialog, new Event('cancel', { bubbles: false, cancelable: true }));
    // Assert
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(mode === 'create' ? trigger : screen.getByRole('button', { name: 'Add System Boundary' })).toHaveFocus();
    expectNoWrites();
  });

  it.each(['create', 'edit'] as const)('locks %s form inputs and cancellation while saving without changing its payload', async mode => {
    // Arrange
    let resolveSave!: (value: BoundaryDefinitionDto) => void;
    const pending = new Promise<BoundaryDefinitionDto>(resolve => { resolveSave = resolve; });
    if (mode === 'create') {
      vi.mocked(api.fetchBoundaryDefinitions).mockResolvedValue([]);
      vi.mocked(api.createBoundaryDefinition).mockReturnValue(pending);
    } else vi.mocked(api.updateBoundaryDefinition).mockReturnValue(pending);
    render(page());
    await screen.findByRole('heading', { name: 'Recorded boundary definitions' });
    if (mode === 'edit') fireEvent.click(screen.getByRole('button', { name: 'Open boundary Production' }));
    fireEvent.click(mode === 'create' ? screen.getByRole('button', { name: 'Add System Boundary' }) : screen.getAllByTitle('Edit boundary')[0]!);
    const dialog = screen.getByRole('dialog', { name: mode === 'create' ? 'Create Boundary' : 'Edit Boundary' });
    fireEvent.change(within(dialog).getByRole('textbox', { name: 'Name *' }), { target: { value: 'Documented scope' } });
    fireEvent.click(within(dialog).getByRole('radio', { name: 'Hybrid' }));
    fireEvent.change(within(dialog).getByRole('textbox', { name: 'Description' }), { target: { value: 'Explicit scope description' } });
    // Act
    fireEvent.click(within(dialog).getByRole('button', { name: mode === 'create' ? 'Create Boundary' : 'Update Boundary' }));
    // Assert
    expect(dialog).toHaveAttribute('aria-busy', 'true');
    for (const input of within(dialog).getAllByRole('textbox')) expect(input).toBeDisabled();
    for (const input of within(dialog).getAllByRole('radio')) expect(input).toBeDisabled();
    for (const label of ['Cancel', 'Close dialog', 'Saving...']) expect(within(dialog).getByRole('button', { name: label })).toBeDisabled();
    const method = mode === 'create' ? api.createBoundaryDefinition : api.updateBoundaryDefinition;
    expect(method).toHaveBeenCalledWith(mode === 'create' ? 'system-a' : 'boundary-a',
      { name: 'Documented scope', boundaryType: 'Hybrid', description: 'Explicit scope description' });
    // Act
    fireEvent.submit(within(dialog).getByRole('button', { name: 'Saving...' }).closest('form')!);
    fireEvent(dialog, new Event('cancel', { bubbles: false, cancelable: true }));
    // Assert
    expect(dialog).toBeInTheDocument();
    expect(method).toHaveBeenCalledTimes(1);
    // Act
    await act(async () => { resolveSave(boundary); });
    // Assert
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('keeps the real form open with its values and a visible error after a failed save', async () => {
    // Arrange
    vi.mocked(api.fetchBoundaryDefinitions).mockResolvedValue([]);
    vi.mocked(api.createBoundaryDefinition).mockRejectedValue(new Error('Unavailable'));
    render(page());
    fireEvent.click(await screen.findByRole('button', { name: 'Add System Boundary' }));
    const dialog = screen.getByRole('dialog', { name: 'Create Boundary' });
    fireEvent.change(within(dialog).getByRole('textbox', { name: 'Name *' }), { target: { value: 'Keep my entry' } });
    // Act
    await act(async () => { fireEvent.click(within(dialog).getByRole('button', { name: 'Create Boundary' })); });
    // Assert
    expect(within(dialog).getByRole('alert')).toHaveTextContent('Failed to create boundary');
    expect(within(dialog).getByRole('textbox', { name: 'Name *' })).toHaveValue('Keep my entry');
    expect(within(dialog).getByRole('button', { name: 'Cancel' })).toBeEnabled();
    // Act
    fireEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }));
    // Assert
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('opens a native delete confirmation, blocks closing while busy and restores focus on Escape', async () => {
    // Arrange
    let resolveDelete!: (value: Awaited<ReturnType<typeof api.deleteBoundaryDefinition>>) => void;
    vi.mocked(api.deleteBoundaryDefinition).mockReturnValue(new Promise(resolve => { resolveDelete = resolve; }));
    render(page());
    fireEvent.click(await screen.findByRole('button', { name: 'Open boundary Recovery' }));
    const trigger = screen.getByTitle('Delete boundary');
    trigger.focus();
    fireEvent.click(trigger);
    const dialog = screen.getByRole('dialog', { name: 'Delete Boundary' });
    // Act
    fireEvent(dialog, new Event('cancel', { cancelable: true }));
    // Assert
    expect(screen.getByRole('button', { name: 'Add System Boundary' })).toHaveFocus();
    expect(api.deleteBoundaryDefinition).not.toHaveBeenCalled();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Open boundary Recovery' }));
    fireEvent.click(screen.getByTitle('Delete boundary'));
    const confirmation = screen.getByRole('dialog', { name: 'Delete Boundary' });
    fireEvent.click(within(confirmation).getByRole('button', { name: 'Delete' }));
    fireEvent(confirmation, new Event('cancel', { cancelable: true }));
    // Assert
    expect(confirmation).toHaveAttribute('aria-busy', 'true');
    expect(within(confirmation).getByRole('button', { name: 'Cancel' })).toBeDisabled();
    expect(within(confirmation).getByRole('button', { name: 'Close dialog' })).toBeDisabled();
    expect(api.deleteBoundaryDefinition).toHaveBeenCalledWith('boundary-b');
    // Act
    await act(async () => { resolveDelete({ deletedId: 'boundary-b', primaryBoundaryId: 'boundary-a', reassignedComponents: 0, reassignedMappings: 0, reassignedResources: 0 }); });
    // Assert
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });
});
