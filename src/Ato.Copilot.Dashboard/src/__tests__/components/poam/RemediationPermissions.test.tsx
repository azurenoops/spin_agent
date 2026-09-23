import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { invokeClick } from '../../helpers/domainPermissions';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import Remediation from '../../../pages/Remediation';
import type { SystemWorkspacePermissions } from '../../../features/workspaces/types';

const state = vi.hoisted(() => ({
  session: null as { roles?: string[]; systemAccess?: { systemId: string; permissions?: Partial<SystemWorkspacePermissions> } } | null,
  role: 'AO',
  link: vi.fn(),
  move: vi.fn(),
  export: vi.fn(),
}));
vi.mock('../../../features/workspaces/WorkspaceBoundary', () => ({ useWorkspaceSession: () => state.session }));
vi.mock('../../../features/workspaces/workspaceNavigation', async () => {
  const router = await import('react-router-dom');
  return { Link: router.Link, useNavigate: router.useNavigate };
});
vi.mock('../../../components/layout/SystemLayout', () => ({ useSystemContext: () => ({ detail: { systemId: 'system-a' } }) }));
vi.mock('../../../hooks/useSettings', () => ({
  useSettings: () => ({ settings: { role: state.role, defaultRemediationView: 'table', autoRefreshInterval: 60000 } }),
}));
vi.mock('../../../api/deviations', () => ({ getDeviations: async () => ({ items: [] }) }));
vi.mock('../../../api/poam', () => ({
  listPoamItems: async () => ({ items: [{ id: 'poam-a', controlId: 'AC-2', weakness: 'Weakness A', status: 'Ongoing' }] }),
  linkTask: (...args: unknown[]) => state.link(...args),
}));
vi.mock('../../../api/remediation', () => ({
  getRemediationSummary: async () => ({
    totalTasks: 1, tasksByStatus: { backlog: 1, todo: 0, inProgress: 0, inReview: 0, blocked: 0, done: 0 },
  }),
  getRemediationTasks: async () => ({
    items: [{
      id: 'task-a', taskNumber: 'TASK-1', title: 'Repair A', description: 'Repair',
      controlId: 'AC-2', severity: 'High', status: 'Backlog', dueDate: '2026-10-01',
    }],
  }),
  moveTask: (...args: unknown[]) => state.move(...args),
  exportTasks: (...args: unknown[]) => state.export(...args),
}));
const canonical = '/workspaces/organizations/org-a/systems/system-a/remediation';
function mount(path = canonical) {
  return render(<MemoryRouter initialEntries={[path]}><Remediation /></MemoryRouter>);
}
function allow() {
  state.session = { roles: ['MissionOwner', 'ISSO'], systemAccess: { systemId: 'system-a', permissions: { canManageRemediation: true } } };
}
beforeEach(() => {
  vi.clearAllMocks();
  state.session = { roles: ['MissionOwner'], systemAccess: { systemId: 'system-a', permissions: { canManageSystem: true } } };
  state.link.mockResolvedValue({});
  state.move.mockResolvedValue({});
});

describe('#1017 remediation page permissions', () => {
  it.each(['AO', 'ISSM'])('denies forged %s authority while preserving task reads and CSV export', async role => {
    // Arrange
    state.role = role;
    mount();
    await screen.findByText('Repair A');
    // Act
    fireEvent.click(screen.getByText('Repair A'));
    fireEvent.click(screen.getByRole('button', { name: 'Export CSV' }));
    await invokeClick(screen.getByRole('button', { name: 'Create Task' }));
    // Assert
    expect(screen.getByRole('button', { name: 'Create Task' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Link to POA&M' })).toBeDisabled();
    expect(state.export).toHaveBeenCalledWith('system-a');
    expect(screen.queryByRole('dialog', { name: 'Create Remediation Task' })).not.toBeInTheDocument();
    expect(screen.getByRole('alert')).toHaveTextContent(/permission denied/i);
  });

  it.each([null, { systemAccess: { systemId: 'system-b', permissions: { canManageRemediation: true } } }])(
    'fails closed with missing or mismatched canonical access', async session => {
      // Arrange
      state.session = session;
      mount();
      // Act
      fireEvent.click(await screen.findByText('Repair A'));
      // Assert
      expect(screen.getByRole('button', { name: 'Link to POA&M' })).toBeDisabled();
    },
  );

  it('allows real multi-role POA&M linking without enabling unprojected task creation', async () => {
    // Arrange
    allow();
    mount();
    fireEvent.click(await screen.findByText('Repair A'));
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Link to POA&M' }));
    fireEvent.change(screen.getByPlaceholderText('Search by control ID or weakness...'), { target: { value: 'AC' } });
    fireEvent.click(await screen.findByRole('button', { name: /Weakness A/ }));
    // Assert
    await waitFor(() => expect(state.link).toHaveBeenCalledWith('poam-a', { taskId: 'task-a' }));
    expect(screen.getByRole('button', { name: 'Create Task' })).toBeDisabled();
  });

  it('revokes a picker already opened by an authorized user', async () => {
    // Arrange
    allow();
    const { rerender } = mount();
    fireEvent.click(await screen.findByText('Repair A'));
    fireEvent.click(screen.getByRole('button', { name: 'Link to POA&M' }));
    fireEvent.change(screen.getByPlaceholderText('Search by control ID or weakness...'), { target: { value: 'AC' } });
    await screen.findByRole('button', { name: /Weakness A/ });
    // Act
    state.session = null;
    rerender(<MemoryRouter initialEntries={[canonical]}><Remediation /></MemoryRouter>);
    await invokeClick(screen.getByRole('button', { name: /Weakness A/ }));
    // Assert
    expect(screen.getByRole('button', { name: /Weakness A/ })).toBeDisabled();
    expect(state.link).not.toHaveBeenCalled();
    expect(screen.getByRole('alert')).toHaveTextContent(/permission denied/i);
  });

  it('rejects synthetic drag/drop even with a broader remediation flag', async () => {
    // Arrange
    allow();
    const { container } = mount();
    await screen.findByText('Repair A');
    fireEvent.click(screen.getByRole('button', { name: 'Kanban' }));
    const card = container.querySelector('[draggable]')!;
    const destination = screen.getByText('To Do').parentElement!.parentElement!.parentElement!;
    // Act
    fireEvent.dragStart(card, { dataTransfer: { setData: vi.fn() } });
    fireEvent.drop(destination);
    // Assert
    expect(card).toHaveAttribute('draggable', 'false');
    expect(state.move).not.toHaveBeenCalled();
    expect(screen.getByRole('alert')).toHaveTextContent(/permission denied/i);
  });

  it('retains legacy creation and drag/drop with no workspace session', async () => {
    // Arrange
    state.session = null;
    const { container } = mount('/systems/system-a/remediation');
    await screen.findByText('Repair A');
    expect(screen.getByRole('button', { name: 'Create Task' })).toBeEnabled();
    fireEvent.click(screen.getByRole('button', { name: 'Kanban' }));
    const card = container.querySelector('[draggable]')!;
    const destination = screen.getByText('To Do').parentElement!.parentElement!.parentElement!;
    // Act
    fireEvent.dragStart(card, { dataTransfer: { setData: vi.fn() } });
    fireEvent.drop(destination);
    // Assert
    expect(card).toHaveAttribute('draggable', 'true');
    await waitFor(() => expect(state.move).toHaveBeenCalledWith('task-a', 'ToDo'));
  });
});
