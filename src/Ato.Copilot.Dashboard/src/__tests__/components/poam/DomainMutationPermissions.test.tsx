import { cloneElement, type ReactElement } from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import PoamManagement from '../../../pages/PoamManagement';
import PoamLifecycleActions from '../../../components/poam/PoamLifecycleActions';
import PoamDetailDrawer from '../../../components/poam/PoamDetailDrawer';
import PostImportPoamPrompt from '../../../components/poam/PostImportPoamPrompt';
import TicketingConfig from '../../../components/poam/TicketingConfig';
import CreateRemediationTaskModal from '../../../components/remediation/CreateRemediationTaskModal';
import type { PoamDetail } from '../../../types/poam';
import type { SystemWorkspacePermissions } from '../../../features/workspaces/types';
import { invokeClick } from '../../helpers/domainPermissions';

const state = vi.hoisted(() => ({
  session: null as { roles?: string[]; systemAccess?: { systemId: string; permissions?: Partial<SystemWorkspacePermissions> } } | null,
  role: 'AO',
  detail: null as PoamDetail | null,
  create: vi.fn(),
  update: vi.fn(),
  createTask: vi.fn(),
  sync: vi.fn(),
  configure: vi.fn(),
  link: vi.fn(),
  unlink: vi.fn(),
  linkTask: vi.fn(),
  unlinkTask: vi.fn(),
  fromPoam: vi.fn(),
}));
vi.mock('../../../features/workspaces/WorkspaceBoundary', () => ({ useWorkspaceSession: () => state.session }));
vi.mock('../../../components/layout/SystemLayout', () => ({ useSystemContext: () => ({ detail: { systemId: 'system-a' } }) }));
vi.mock('../../../hooks/useSettings', () => ({ useSettings: () => ({ settings: { role: state.role } }) }));
vi.mock('../../../hooks/usePoam', () => ({
  usePoamList: () => ({ data: { items: [], totalCount: 0 }, loading: false, refresh: vi.fn() }),
  usePoamMetrics: () => ({ data: null, loading: false, refresh: vi.fn() }),
  useCreatePoam: () => ({ create: state.create, loading: false }),
  usePoamDetail: () => ({ data: state.detail, loading: false, refresh: vi.fn() }),
}));
vi.mock('../../../api/poam', () => ({
  updatePoamStatus: (...args: unknown[]) => state.update(...args),
  getTicketingConfig: async () => ({ provider: 'jira', baseUrl: 'https://example.test', projectKeyOrTableName: 'POAM' }),
  configureTicketing: (...args: unknown[]) => state.configure(...args),
  syncTicket: (...args: unknown[]) => state.sync(...args),
  linkComponents: (...args: unknown[]) => state.link(...args),
  unlinkComponents: (...args: unknown[]) => state.unlink(...args),
  createTaskFromPoam: (...args: unknown[]) => state.fromPoam(...args),
  linkTask: (...args: unknown[]) => state.linkTask(...args),
  unlinkTask: (...args: unknown[]) => state.unlinkTask(...args),
}));
vi.mock('../../../api/remediation', () => ({ createTask: (...args: unknown[]) => state.createTask(...args) }));
vi.mock('../../../components/poam/ComponentPicker', () => ({
  default: ({ onChange }: { onChange: (ids: string[]) => void }) => <button type="button" onClick={() => onChange(['component-b'])}>Choose component</button>,
}));
vi.mock('../../../components/poam/PoamTrendCharts', () => ({ default: () => null }));

const canonical = '/workspaces/organizations/org-a/systems/system-a/poam';
const fixture: PoamDetail = {
  id: 'poam-a', systemId: 'system-a', systemName: 'System A', controlId: 'AC-2',
  weakness: 'Weakness', status: 'Ongoing', catSeverity: 'II', poc: 'Owner',
  scheduledCompletionDate: '2026-10-01', rowVersion: 'version-a',
  dueDate: '2026-10-01', daysRemaining: 10, milestoneProgress: { completed: 0, total: 0 },
  deviationType: null, externalTicketRef: null, remediationTaskId: null, remediationTaskStatus: null,
  isOverdue: false, weaknessSource: 'Manual', pocEmail: null, resourcesRequired: null, costEstimate: null,
  actualCompletionDate: null, comments: null, findingId: null, deviationId: null,
  createdAt: '2026-09-01', modifiedAt: null, createdBy: null,
  components: [{ id: 'component-a', name: 'Component A', type: 'Server' }],
  milestones: [], history: [], ticketSync: null,
};
const findings = [{ id: 'finding-a', controlId: 'AC-2', title: 'Finding A', severity: 'High', hasActivePoam: false }];
function allow() {
  state.session = { roles: ['MissionOwner', 'Isso'], systemAccess: { systemId: 'system-a', permissions: { canManageRemediation: true } } };
}
function mount(element: ReactElement, path = canonical) {
  return render(<MemoryRouter initialEntries={[path]}>{element}</MemoryRouter>);
}
beforeEach(() => {
  vi.clearAllMocks();
  state.session = { roles: ['MissionOwner'], systemAccess: { systemId: 'system-a', permissions: { canManageSystem: true, canManageRemediation: false } } };
  state.detail = { ...fixture };
  state.create.mockResolvedValue({});
  state.update.mockResolvedValue({ poam: { rowVersion: 'version-b' } });
});

describe('#1017 POA&M domain mutation permissions', () => {
  it.each(['AO', 'ISSM'])('does not trust forged browser %s authority', role => {
    // Arrange
    state.role = role;
    // Act
    mount(<PoamManagement />);
    // Assert
    expect(screen.getByRole('button', { name: 'Add POA&M' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Export' })).toBeEnabled();
  });

  it.each([null, { systemAccess: { systemId: 'system-a' } }, { systemAccess: { systemId: 'system-b', permissions: { canManageRemediation: true } } }])(
    'fails closed for absent or mismatched canonical permission', session => {
      // Arrange
      state.session = session;
      // Act
      mount(<PoamManagement />);
      // Assert
      expect(screen.getByRole('button', { name: 'Add POA&M' })).toBeDisabled();
    },
  );

  it('honors real multi-role permission and guards an already-open create form on revocation', async () => {
    // Arrange
    allow();
    const element = <PoamManagement />;
    const { container, rerender } = mount(element);
    fireEvent.click(screen.getByRole('button', { name: 'Add POA&M' }));
    // Act
    state.session = null;
    rerender(<MemoryRouter initialEntries={[canonical]}>{cloneElement(element)}</MemoryRouter>);
    fireEvent.submit(container.querySelector('form')!);
    // Assert
    expect(state.create).not.toHaveBeenCalled();
    expect(await screen.findByRole('alert')).toHaveTextContent(/permission/i);
    expect(screen.getByRole('button', { name: 'Create POA&M' })).toBeDisabled();
  });

  it.each([canonical, '/systems/system-a/poam'])('permits lifecycle mutation with real permission or legacy compatibility (%s)', async path => {
    // Arrange
    if (path === canonical) allow(); else state.session = null;
    mount(<PoamLifecycleActions detail={fixture} onStatusChanged={vi.fn()} />, path);
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Mark Completed' }));
    fireEvent.click(screen.getByRole('button', { name: 'Confirm' }));
    // Assert
    await waitFor(() => expect(state.update).toHaveBeenCalledWith('poam-a', expect.objectContaining({ status: 'Completed' })));
  });

  it('disables lifecycle controls for a MissionOwner despite other flags', () => {
    // Arrange
    // Act
    mount(<PoamLifecycleActions detail={fixture} onStatusChanged={vi.fn()} />);
    // Assert
    for (const name of ['Mark Delayed', 'Mark Completed', 'Risk Accepted']) {
      expect(screen.getByRole('button', { name })).toBeDisabled();
    }
  });

  it('revokes an already-open lifecycle confirmation', async () => {
    // Arrange
    allow();
    const element = <PoamLifecycleActions detail={fixture} onStatusChanged={vi.fn()} />;
    const { rerender } = mount(element);
    fireEvent.click(screen.getByRole('button', { name: 'Mark Completed' }));
    // Act
    state.session = null;
    rerender(<MemoryRouter initialEntries={[canonical]}>{cloneElement(element)}</MemoryRouter>);
    await invokeClick(screen.getByRole('button', { name: 'Confirm' }));
    // Assert
    expect(screen.getByRole('button', { name: 'Confirm' })).toBeDisabled();
    expect(state.update).not.toHaveBeenCalled();
  });

  it('revokes a pending cascade confirmation', async () => {
    // Arrange
    allow();
    const element = <PoamLifecycleActions detail={{ ...fixture, remediationTaskId: 'task-a' }} onStatusChanged={vi.fn()} />;
    const { rerender } = mount(element);
    fireEvent.click(screen.getByRole('button', { name: 'Mark Completed' }));
    fireEvent.click(screen.getByRole('button', { name: 'Confirm' }));
    await screen.findByRole('button', { name: 'Apply Cascade' });
    // Act
    state.session = null;
    rerender(<MemoryRouter initialEntries={[canonical]}>{cloneElement(element)}</MemoryRouter>);
    await invokeClick(screen.getByRole('button', { name: 'Apply Cascade' }));
    // Assert
    expect(screen.getByRole('button', { name: 'Apply Cascade' })).toBeDisabled();
    expect(state.update).toHaveBeenCalledTimes(1);
  });

  it('disables nested drawer links, task creation and unprojected ticket sync', () => {
    // Arrange
    // Act
    mount(<PoamDetailDrawer poamId="poam-a" onClose={vi.fn()} />);
    // Assert
    for (const name of ['+ Link', 'Create Task', 'Link Task', 'Sync to Ticketing System']) {
      expect(screen.getByRole('button', { name })).toBeDisabled();
    }
    expect(screen.getByTitle('Unlink component')).toBeDisabled();
  });

  it('allows projected component linkage but disables its open picker after revocation', async () => {
    // Arrange
    allow();
    const element = <PoamDetailDrawer poamId="poam-a" onClose={vi.fn()} />;
    const { rerender } = mount(element);
    fireEvent.click(screen.getByRole('button', { name: '+ Link' }));
    fireEvent.click(screen.getByRole('button', { name: 'Choose component' }));
    // Act
    state.session = null;
    rerender(<MemoryRouter initialEntries={[canonical]}>{cloneElement(element)}</MemoryRouter>);
    await invokeClick(screen.getByRole('button', { name: 'Link 1 component(s)' }));
    // Assert
    expect(screen.getByRole('button', { name: 'Link 1 component(s)' })).toBeDisabled();
    expect(state.link).not.toHaveBeenCalled();
  });

  it('allows authorized component unlinking but never infers ticket sync authority', async () => {
    // Arrange
    allow();
    mount(<PoamDetailDrawer poamId="poam-a" onClose={vi.fn()} />);
    // Act
    fireEvent.click(screen.getByTitle('Unlink component'));
    // Assert
    await waitFor(() => expect(state.unlink).toHaveBeenCalledWith('poam-a', { componentIds: ['component-a'] }));
    expect(screen.getByRole('button', { name: 'Sync to Ticketing System' })).toBeDisabled();
  });

  it('guards the post-import bulk creation prompt after permission loss', async () => {
    // Arrange
    allow();
    const create = vi.fn();
    const element = <PostImportPoamPrompt systemId="system-a" findings={findings} onBulkCreate={create} onClose={vi.fn()} />;
    const { rerender } = mount(element);
    // Act
    state.session = null;
    rerender(<MemoryRouter initialEntries={[canonical]}>{cloneElement(element)}</MemoryRouter>);
    await invokeClick(screen.getByRole('button', { name: 'Create 1 POA&M Item(s)' }));
    // Assert
    expect(screen.getByRole('button', { name: 'Create 1 POA&M Item(s)' })).toBeDisabled();
    expect(create).not.toHaveBeenCalled();
  });

  it('does not infer standalone task creation from remediation or system management', async () => {
    // Arrange
    allow();
    // Act
    mount(<CreateRemediationTaskModal systemId="system-a" findingTitle="Repair" onClose={vi.fn()} />);
    await invokeClick(screen.getByRole('button', { name: 'Create Task' }));
    // Assert
    expect(screen.getByRole('button', { name: 'Create Task' })).toBeDisabled();
    expect(state.createTask).not.toHaveBeenCalled();
    expect(screen.getByRole('alert')).toHaveTextContent(/permission denied/i);
  });

  it('retains legacy standalone task creation', async () => {
    // Arrange
    state.session = null;
    mount(<CreateRemediationTaskModal systemId="system-a" findingTitle="Repair" onClose={vi.fn()} />, '/systems/system-a/remediation');
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Create Task' }));
    // Assert
    await waitFor(() => expect(state.createTask).toHaveBeenCalledWith('system-a', expect.objectContaining({ title: 'Repair' })));
  });

  it.each([canonical, '/systems/system-a/poam'])('does not infer canonical ticket configuration, preserves legacy (%s)', async path => {
    // Arrange
    if (path === canonical) allow(); else state.session = null;
    mount(<TicketingConfig systemId="system-a" />, path);
    const save = await screen.findByRole('button', { name: 'Update Configuration' });
    // Act
    await invokeClick(save);
    // Assert
    if (path === canonical) {
      expect(save).toBeDisabled();
      expect(state.configure).not.toHaveBeenCalled();
    } else {
      await waitFor(() => expect(state.configure).toHaveBeenCalled());
    }
  });

  it('submits a POA&M form using the explicit server permission', async () => {
    // Arrange
    allow();
    const { container } = mount(<PoamManagement />);
    fireEvent.click(screen.getByRole('button', { name: 'Add POA&M' }));
    fireEvent.change(screen.getByPlaceholderText('Describe the security weakness...'), { target: { value: 'Weakness A' } });
    // Act
    fireEvent.submit(container.querySelector('form')!);
    // Assert
    await waitFor(() => expect(state.create).toHaveBeenCalledWith('system-a', expect.objectContaining({ weakness: 'Weakness A' })));
  });

  it('surfaces API errors from an authorized create form', async () => {
    // Arrange
    allow();
    state.create.mockRejectedValueOnce(new Error('Create rejected by server'));
    const { container } = mount(<PoamManagement />);
    fireEvent.click(screen.getByRole('button', { name: 'Add POA&M' }));
    // Act
    fireEvent.submit(container.querySelector('form')!);
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Create rejected by server');
  });

  it.each(['Push', 'Pull'])('guards unprojected %s handler even when invoked directly', async name => {
    // Arrange
    allow();
    state.detail = { ...fixture, ticketSync: { externalTicketId: 'T-1', externalTicketUrl: null, syncStatus: 'Synced', lastSyncAt: '2026-09-01', lastSyncError: null } };
    mount(<PoamDetailDrawer poamId="poam-a" onClose={vi.fn()} />);
    // Act
    await invokeClick(screen.getByRole('button', { name }));
    // Assert
    expect(screen.getByRole('button', { name })).toBeDisabled();
    expect(state.sync).not.toHaveBeenCalled();
    expect(screen.getByRole('alert')).toHaveTextContent(/permission denied/i);
  });

  it('guards task unlink handlers without consulting a browser role', async () => {
    // Arrange
    state.detail = { ...fixture, remediationTaskId: 'task-a' };
    mount(<PoamDetailDrawer poamId="poam-a" onClose={vi.fn()} />);
    // Act
    await invokeClick(screen.getByRole('button', { name: 'Unlink' }));
    // Assert
    expect(state.unlinkTask).not.toHaveBeenCalled();
    expect(screen.getByRole('button', { name: 'Unlink' })).toBeDisabled();
    expect(screen.getAllByRole('alert').some(alert => /permission denied/i.test(alert.textContent ?? ''))).toBe(true);
  });

  it('permits the projected POA&M-to-task creation endpoint', async () => {
    // Arrange
    allow();
    const prompt = vi.spyOn(window, 'prompt').mockReturnValue('board-a');
    mount(<PoamDetailDrawer poamId="poam-a" onClose={vi.fn()} />);
    // Act
    await invokeClick(screen.getByRole('button', { name: 'Create Task' }));
    // Assert
    expect(state.fromPoam).toHaveBeenCalledWith('poam-a', { boardId: 'board-a' });
    prompt.mockRestore();
  });

  it('surfaces component mutation failures instead of silently refreshing', async () => {
    // Arrange
    allow();
    state.unlink.mockRejectedValueOnce(new Error('Link removal rejected'));
    mount(<PoamDetailDrawer poamId="poam-a" onClose={vi.fn()} />);
    // Act
    await invokeClick(screen.getByTitle('Unlink component'));
    // Assert
    expect(screen.getByRole('alert')).toHaveTextContent('Link removal rejected');
  });

  it('permits projected post-import bulk creation', async () => {
    // Arrange
    allow();
    const create = vi.fn().mockResolvedValue({ totalFailed: 0, created: 1, skippedDuplicates: 0, results: [] });
    mount(<PostImportPoamPrompt systemId="system-a" findings={findings} onBulkCreate={create} onClose={vi.fn()} />);
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Create 1 POA&M Item(s)' }));
    // Assert
    await waitFor(() => expect(create).toHaveBeenCalledWith({ findingIds: ['finding-a'] }));
    expect(await screen.findByText(/1 POA&M item\(s\) created/)).toBeInTheDocument();
  });
});
