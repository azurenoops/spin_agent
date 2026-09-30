import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import SystemNextActions from '../../features/systems/SystemNextActions';
import { getSystemNextActions } from '../../api/systemNextActions';

vi.mock('../../api/systemNextActions', () => ({ getSystemNextActions: vi.fn() }));
const workspace = vi.hoisted(() => ({ role: 'MissionOwner', actor: 'owner-a' }));
vi.mock('../../features/workspaces/WorkspaceBoundary', () => ({
  useWorkspaceSession: () => ({ identity: { oid: workspace.actor }, target: { kind: 'organization', organizationId: 'org-a' }, roles: [workspace.role] }),
}));
type Result = Awaited<ReturnType<typeof getSystemNextActions>>;
const response = (title = 'Complete data profile', role: Result['items'][number]['responsibleRole'] = 'MissionOwner'): Result => ({
  systemId: 'a', checkedAt: '2026-09-28T18:00:00Z', effectiveRoles: [role],
  items: [{ id: title, title, description: 'Current saved state requires this action.',
    path: 'profile/DataTypes', actionLabel: 'Open', responsibleRole: role }],
  waitingOnOtherRoles: [{ role: 'Issm', count: 2 }],
});
beforeEach(() => {
  vi.clearAllMocks(); workspace.role = 'MissionOwner'; workspace.actor = 'owner-a';
});
describe('Role-specific next actions', () => {
  it('loads server-owned tasks and keeps other-role waiting separate from actionable links', async () => {
    // Arrange
    vi.mocked(getSystemNextActions).mockResolvedValue(response());
    const nextPath = vi.fn();
    // Act
    render(<MemoryRouter><SystemNextActions systemId="a" onNextPathChange={nextPath} /></MemoryRouter>);
    // Assert
    expect(await screen.findByRole('heading', { name: 'Complete data profile' })).toBeVisible();
    expect(screen.getByRole('link', { name: 'Open: Complete data profile' })).toHaveAttribute('href', '/systems/a/profile/DataTypes');
    expect(screen.getByText(/ISSM: 2/)).toBeVisible();
    expect(screen.queryByRole('link', { name: /approve/i })).not.toBeInTheDocument();
    expect(nextPath).toHaveBeenLastCalledWith('profile/DataTypes');
  });
  it('replaces completed actions with subsequent saved work when the window regains focus', async () => {
    // Arrange
    vi.mocked(getSystemNextActions).mockResolvedValueOnce(response()).mockResolvedValueOnce(response('Submit mission profile'));
    render(<MemoryRouter><SystemNextActions systemId="a" /></MemoryRouter>);
    await screen.findByRole('heading', { name: 'Complete data profile' });
    // Act
    fireEvent(window, new Event('focus'));
    // Assert
    expect(await screen.findByRole('heading', { name: 'Submit mission profile' })).toBeVisible();
    expect(screen.queryByRole('heading', { name: 'Complete data profile' })).not.toBeInTheDocument();
  });
  it('does not grant ISSM tasks from a browser role label when the server returned MissionOwner tasks', async () => {
    // Arrange
    workspace.role = 'ISSM';
    vi.mocked(getSystemNextActions).mockResolvedValue(response());
    // Act
    render(<MemoryRouter><SystemNextActions systemId="a" /></MemoryRouter>);
    // Assert
    expect(await screen.findByText('Your system roles: MissionOwner')).toBeVisible();
    expect(screen.queryByRole('link', { name: /approve/i })).not.toBeInTheDocument();
  });
  it('refetches when the active actor or effective roles change', async () => {
    // Arrange
    vi.mocked(getSystemNextActions).mockResolvedValueOnce(response())
      .mockResolvedValueOnce({ ...response('Review submitted mission profile', 'Issm'), waitingOnOtherRoles: [] });
    const view = render(<MemoryRouter><SystemNextActions systemId="a" /></MemoryRouter>);
    await screen.findByRole('heading', { name: 'Complete data profile' });
    // Act
    workspace.role = 'Issm'; workspace.actor = 'reviewer-a';
    view.rerender(<MemoryRouter><SystemNextActions systemId="a" /></MemoryRouter>);
    // Assert
    expect(await screen.findByRole('heading', { name: 'Review submitted mission profile' })).toBeVisible();
    expect(screen.queryByRole('heading', { name: 'Complete data profile' })).not.toBeInTheDocument();
  });
  it('shows loading and failures instead of default tasks or a false completion state', async () => {
    // Arrange
    let fail!: (reason: Error) => void;
    vi.mocked(getSystemNextActions).mockImplementationOnce(() => new Promise((_resolve, reject) => { fail = reject; }));
    render(<MemoryRouter><SystemNextActions systemId="a" /></MemoryRouter>);
    expect(screen.getByText('Loading your next actions…')).toBeVisible();
    // Act
    await act(async () => fail(new Error('Role-specific tasks unavailable')));
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Role-specific tasks unavailable');
    expect(screen.queryByText('Confirm system boundary')).not.toBeInTheDocument();
    expect(screen.queryByText(/No actions currently require/)).not.toBeInTheDocument();
    // Act
    vi.mocked(getSystemNextActions).mockResolvedValue({ ...response(), items: [] });
    fireEvent.click(screen.getByRole('button', { name: 'Refresh my tasks' }));
    // Assert
    expect(await screen.findByText('No actions currently require your system roles.')).toBeVisible();
    expect(screen.getByText(/Package readiness may still depend on other roles/)).toBeVisible();
  });
  it('deduplicates pending refreshes and ignores a stale system response', async () => {
    // Arrange
    let finish!: (data: Result) => void;
    vi.mocked(getSystemNextActions).mockImplementationOnce(() => new Promise(resolve => { finish = resolve; }))
      .mockResolvedValueOnce({ ...response('System B task'), systemId: 'b' });
    const view = render(<MemoryRouter><SystemNextActions systemId="a" /></MemoryRouter>);
    // Act
    fireEvent(window, new Event('focus'));
    expect(getSystemNextActions).toHaveBeenCalledOnce();
    view.rerender(<MemoryRouter><SystemNextActions systemId="b" /></MemoryRouter>);
    await screen.findByRole('heading', { name: 'System B task' });
    await act(async () => finish(response('Old system task')));
    // Assert
    expect(screen.queryByText('Old system task')).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Open: System B task' })).toHaveAttribute('href', '/systems/b/profile/DataTypes');
  });
  it('shows the first three current actions, so subsequent ones can replace completed work', async () => {
    // Arrange
    const current = response();
    vi.mocked(getSystemNextActions).mockResolvedValue({ ...current,
      items: Array.from({ length: 4 }, (_, index) => ({ ...current.items[0]!, id: `${index}`, title: `Action ${index}` })) });
    // Act
    render(<MemoryRouter><SystemNextActions systemId="a" /></MemoryRouter>);
    // Assert
    await waitFor(() => expect(screen.getAllByRole('link')).toHaveLength(3));
    expect(screen.queryByRole('heading', { name: 'Action 3' })).not.toBeInTheDocument();
    expect(screen.getByText('Showing the next 3 of 4 actions for your roles.')).toBeVisible();
  });
});
