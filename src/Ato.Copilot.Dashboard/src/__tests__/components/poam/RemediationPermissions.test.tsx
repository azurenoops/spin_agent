import '../../helpers/dialog';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import Remediation from '../../../pages/Remediation';
import * as api from '../../../api/remediationWorkspace';
import { remediationWorkspace, remediationTaskDetail } from '../../fixtures/remediationWorkspace';
import type { SystemWorkspacePermissions } from '../../../features/workspaces/types';

vi.mock('../../../components/remediation/TaskTicketPanel', () => ({ default: () => <section>Task external tickets</section> }));
const state = vi.hoisted(() => ({
  session: null as { roles?: string[]; systemAccess?: { systemId: string; permissions?: Partial<SystemWorkspacePermissions> } } | null,
}));
vi.mock('../../../features/workspaces/WorkspaceBoundary', () => ({ useWorkspaceSession: () => state.session }));
vi.mock('../../../components/layout/SystemLayout', () => ({ useSystemContext: () => ({ detail: { systemId: 'system-a' } }) }));
vi.mock('../../../api/remediationWorkspace', async original => ({
  ...await original<typeof api>(), getRemediationWorkspace: vi.fn(), getRemediationTask: vi.fn(),
  saveRemediationTask: vi.fn(), createRemediationTask: vi.fn(), moveRemediationTask: vi.fn(),
  linkRemediationPoamTask: vi.fn(), unlinkRemediationPoamTask: vi.fn(),
}));
const denied = { canCreateFinding: false, canCreateTask: false, canManageRemediation: false, canVerify: false, reason: 'Permission denied.' };
const canonical = '/workspaces/organizations/org-a/systems/system-a/remediation?task=task-a';
function mount(path = canonical) { return render(<MemoryRouter initialEntries={[path]}><Remediation /></MemoryRouter>); }
beforeEach(() => {
  vi.clearAllMocks();
  state.session = { roles: ['MissionOwner'], systemAccess: { systemId: 'system-a', permissions: { canManageSystem: true } } };
  vi.mocked(api.getRemediationWorkspace).mockResolvedValue({ ...remediationWorkspace, permissions: denied });
  vi.mocked(api.getRemediationTask).mockResolvedValue({ ...remediationTaskDetail, permissions: denied });
});

describe('Remediation record action permissions', () => {
  it.each(['AO', 'ISSM'])('does not trust displayed %s authority over scoped action permissions', async role => {
    // Arrange
    state.session = { ...state.session, roles: [role] };
    mount();
    // Act
    const drawer = await screen.findByRole('dialog', { name: 'Correct session timeout settings' });
    fireEvent.click(within(drawer).getByRole('button', { name: 'Linked work' }));
    // Assert
    expect(screen.queryByRole('button', { name: 'Add finding' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Create standalone task' })).not.toBeInTheDocument();
    expect(within(drawer).queryByRole('button', { name: 'Link existing POA&M' })).not.toBeInTheDocument();
    expect(within(drawer).queryByRole('button', { name: 'Unlink from this task' })).not.toBeInTheDocument();
    expect(api.moveRemediationTask).not.toHaveBeenCalled();
    expect(api.linkRemediationPoamTask).not.toHaveBeenCalled();
  });
  it.each([null, { systemAccess: { systemId: 'system-b', permissions: { canManageRemediation: true } } }])(
    'retains denied record actions when context is missing or mismatched', async session => {
      // Arrange
      state.session = session;
      mount();
      // Act / Assert
      const drawer = await screen.findByRole('dialog', { name: 'Correct session timeout settings' });
      expect(within(drawer).queryByRole('button', { name: 'Edit task' })).not.toBeInTheDocument();
      expect(within(drawer).queryByRole('button', { name: 'Change task status' })).not.toBeInTheDocument();
    });
  it('uses the actual record projection to permit multi-role corrective editing', async () => {
    // Arrange
    state.session = { roles: ['MissionOwner', 'ISSO'], systemAccess: { systemId: 'system-a', permissions: { canManageRemediation: true } } };
    vi.mocked(api.getRemediationWorkspace).mockResolvedValue(remediationWorkspace);
    vi.mocked(api.getRemediationTask).mockResolvedValue(remediationTaskDetail);
    mount();
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Edit task' }));
    fireEvent.click(screen.getByRole('button', { name: 'Save task' }));
    // Assert
    await waitFor(() => expect(api.saveRemediationTask).toHaveBeenCalledWith('system-a', 'task-a', expect.objectContaining({
      expectedRowVersion: 'task-revision-a',
    })));
  });
  it('clears an open editor on permission-context revocation before any write', async () => {
    // Arrange
    state.session = { systemAccess: { systemId: 'system-a', permissions: { canManageRemediation: true } } };
    vi.mocked(api.getRemediationWorkspace).mockResolvedValue(remediationWorkspace);
    vi.mocked(api.getRemediationTask).mockResolvedValue(remediationTaskDetail);
    const { rerender } = mount();
    fireEvent.click(await screen.findByRole('button', { name: 'Edit task' }));
    // Act
    state.session = null;
    vi.mocked(api.getRemediationWorkspace).mockRejectedValue(new Error('Permission revoked.'));
    rerender(<MemoryRouter initialEntries={[canonical]}><Remediation /></MemoryRouter>);
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Permission revoked.');
    expect(screen.queryByRole('button', { name: 'Save task' })).not.toBeInTheDocument();
    expect(api.saveRemediationTask).not.toHaveBeenCalled();
  });
  it('does not offer synthetic drag-to-close on the finding board', async () => {
    // Arrange
    mount(canonical.split('?')[0]);
    await screen.findByRole('button', { name: 'Verify session timeout correction' });
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Board' }));
    // Assert
    expect(document.querySelector('[draggable="true"]')).toBeNull();
    expect(api.moveRemediationTask).not.toHaveBeenCalled();
  });
  it('does not grant legacy-route writes without the server record projection', async () => {
    // Arrange
    state.session = null;
    mount('/systems/system-a/remediation?task=task-a');
    // Act / Assert
    const drawer = await screen.findByRole('dialog', { name: 'Correct session timeout settings' });
    expect(within(drawer).queryByRole('button', { name: 'Edit task' })).not.toBeInTheDocument();
    expect(api.saveRemediationTask).not.toHaveBeenCalled();
  });
});
