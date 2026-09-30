import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
import PoamLinkedWork from '../../../components/poam/PoamLinkedWork';
import { createPoamWorkspaceTask, getPoamWorkspace, linkPoamWorkspaceTask, unlinkPoamWorkspaceTask } from '../../../api/poamWorkspace';
import type { PoamWorkspace } from '../../../api/poamWorkspace';
import { rawRemediationWorkspace } from '../../fixtures/remediationWorkspace';
vi.mock('../../../api/poamWorkspace', () => ({
  getPoamWorkspace: vi.fn(), linkPoamWorkspaceTask: vi.fn(), unlinkPoamWorkspaceTask: vi.fn(), createPoamWorkspaceTask: vi.fn(),
}));
vi.mock('../../../components/remediation/TaskTicketPanel', () => ({ default: ({ taskId }: { taskId: string }) => <div>Ticket for {taskId}</div> }));
const workspace: PoamWorkspace = {
  ...rawRemediationWorkspace, findings: [], exceptions: [],
  poams: [{ ...rawRemediationWorkspace.poams[0]!, taskIds: ['task-a', 'task-b'], findingId: null, deviationId: null }],
  tasks: [
    { ...rawRemediationWorkspace.tasks[0]!, id: 'task-a', taskNumber: 'REM-1', title: 'Apply fix', status: 'Done', poamIds: ['poam-a', 'poam-b'], assigneeName: 'Owner', verificationStatus: 'Passed', evidence: [], history: [] },
    { ...rawRemediationWorkspace.tasks[1]!, id: 'task-b', taskNumber: 'REM-2', title: 'Verify fix', status: 'InReview', poamIds: ['poam-a'], assigneeName: null, verificationStatus: 'NotVerified', evidence: [], history: [] },
  ],
};
describe('Connected POA&M linked work', () => {
  it('shows retained task instructions and exception review details without inferring missing metadata', async () => {
    // Arrange
    vi.mocked(getPoamWorkspace).mockResolvedValue({
      ...workspace,
      tasks: workspace.tasks.map(task => ({ ...task, description: 'Correct the configuration', affectedResources: ['vm-a'], validationCriteria: 'Timeout is ten minutes', remediationScript: 'retained-script', remediationScriptType: 'PowerShell' })),
      exceptions: [{ id: 'exception-a', type: 'Waiver', status: 'Approved', controlId: 'AC-2', justification: 'Temporary maintenance window', expirationDate: '2027-01-01', isEffective: true,
        findingId: null, poamEntryId: 'poam-a', reviewedBy: 'Reviewer A', reviewerRole: 'AO', reviewedAt: '2026-09-29T12:00:00Z', compensatingControls: 'Continuous monitoring' }],
    });
    render(<MemoryRouter><PoamLinkedWork systemId="system-a" poamId="poam-a" controlId="AC-2" weakness="Weakness" canManage onChanged={vi.fn()} /></MemoryRouter>);
    // Act
    await screen.findByText('Linked tasks (2)');
    fireEvent.click(screen.getAllByText('Task scope & instructions')[0]!);
    // Assert
    expect(screen.getAllByText('Timeout is ten minutes')[0]).toBeVisible();
    expect(screen.getAllByText('vm-a')[0]).toBeVisible();
    expect(screen.getByText('Reviewed by Reviewer A · AO')).toBeInTheDocument();
    expect(screen.getByText('Continuous monitoring')).toBeInTheDocument();
  });
  it('retries a failed relationship without creating the task twice', async () => {
    // Arrange
    vi.mocked(getPoamWorkspace).mockResolvedValueOnce(workspace).mockResolvedValue({
      ...workspace, tasks: [...workspace.tasks, { ...workspace.tasks[0]!, id: 'task-new', rowVersion: 'created-task-revision' }],
    });
    vi.mocked(createPoamWorkspaceTask).mockResolvedValue({ id: 'task-new' });
    vi.mocked(linkPoamWorkspaceTask).mockRejectedValueOnce(new Error('Link failed')).mockResolvedValue(undefined);
    render(<MemoryRouter><PoamLinkedWork systemId="system-a" poamId="poam-a" controlId="AC-2" weakness="Weakness" canManage onChanged={vi.fn()} /></MemoryRouter>);
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Create task' }));
    fireEvent.click(screen.getByRole('button', { name: 'Create and link task' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Link failed');
    fireEvent.click(screen.getByRole('button', { name: 'Retry linking created task' }));
    // Assert
    await waitFor(() => expect(linkPoamWorkspaceTask).toHaveBeenCalledTimes(2));
    expect(createPoamWorkspaceTask).toHaveBeenCalledTimes(1);
    expect(linkPoamWorkspaceTask).toHaveBeenLastCalledWith('system-a', 'poam-a', 'task-new', {
      expectedPoamRevision: 'poam-revision-a', expectedTaskRevision: 'created-task-revision',
    });
  });
  it('renders shared tasks once and explicitly unlinks only the selected pair', async () => {
    // Arrange
    vi.mocked(getPoamWorkspace).mockResolvedValue(workspace);
    vi.mocked(unlinkPoamWorkspaceTask).mockResolvedValue(undefined);
    render(<MemoryRouter><PoamLinkedWork systemId="system-a" poamId="poam-a" controlId="AC-2" weakness="Weakness" canManage onChanged={vi.fn()} /></MemoryRouter>);
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Unlink REM-1' }));
    fireEvent.click(screen.getByRole('button', { name: 'Confirm unlink REM-1' }));
    // Assert
    await waitFor(() => expect(unlinkPoamWorkspaceTask).toHaveBeenCalledWith('system-a', 'poam-a', 'task-a', expect.any(Object)));
    expect(screen.getAllByText('Apply fix')).toHaveLength(1);
    expect(screen.getByText('Shared with 1 other commitment')).toBeInTheDocument();
    expect(screen.getByText('Verify fix')).toBeInTheDocument();
  });
  it('keeps readers from creating or linking tasks and shows read failures', async () => {
    // Arrange
    vi.mocked(getPoamWorkspace).mockRejectedValue(new Error('Connected work unavailable'));
    render(<MemoryRouter><PoamLinkedWork systemId="system-a" poamId="poam-a" controlId="AC-2" weakness="Weakness" canManage={false} onChanged={vi.fn()} /></MemoryRouter>);
    // Act / Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Connected work unavailable');
    expect(screen.queryByRole('button', { name: 'Create task' })).toBeNull();
    expect(screen.queryByText('No remediation tasks linked.')).toBeNull();
  });
});
