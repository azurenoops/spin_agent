import '../helpers/dialog';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import FindingsWorkspace from '../../features/remediation-workspace/FindingsWorkspace';
import * as api from '../../api/remediationWorkspace';
import { remediationFindingDetail, remediationTaskDetail, remediationWorkspace } from '../fixtures/remediationWorkspace';

vi.mock('../../components/remediation/TaskTicketPanel', () => ({ default: () => <section>Task external tickets</section> }));
vi.mock('../../api/remediationWorkspace', async original => ({
  ...await original<typeof api>(), getRemediationWorkspace: vi.fn(), getRemediationFinding: vi.fn(),
  getRemediationTask: vi.fn(), createRemediationTask: vi.fn(), saveRemediationTask: vi.fn(),
  moveRemediationTask: vi.fn(), createRemediationFinding: vi.fn(), verifyRemediationTask: vi.fn(), linkRemediationEvidence: vi.fn(),
  linkRemediationPoamTask: vi.fn(), unlinkRemediationPoamTask: vi.fn(),
  linkRemediationFindingTask: vi.fn(),
}));
const mount = (query = '') => render(<MemoryRouter initialEntries={[`/systems/system-a/remediation${query}`]}>
  <FindingsWorkspace systemId="system-a" /></MemoryRouter>);
beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(api.getRemediationWorkspace).mockResolvedValue(structuredClone(remediationWorkspace));
  vi.mocked(api.getRemediationFinding).mockResolvedValue(structuredClone(remediationFindingDetail));
  vi.mocked(api.getRemediationTask).mockResolvedValue(structuredClone(remediationTaskDetail));
});

describe('Connected findings workspace', () => {
  it('links unassociated existing work without overwriting another finding or creating a task', async () => {
    // Arrange
    vi.mocked(api.getRemediationWorkspace).mockResolvedValue({ ...remediationWorkspace,
      tasks: [...remediationWorkspace.tasks, { ...remediationWorkspace.tasks[0]!, id: 'task-unassociated',
        title: 'Existing corrective work', findingId: null, rowVersion: 'unassociated-revision' }] });
    mount('?finding=finding-a');
    const drawer = await screen.findByRole('dialog', { name: 'Verify session timeout correction' });
    // Act
    fireEvent.click(within(drawer).getByRole('button', { name: 'Linked work' }));
    fireEvent.click(within(drawer).getByRole('button', { name: 'Link existing task' }));
    fireEvent.change(within(drawer).getByRole('combobox', { name: 'Existing corrective task' }), { target: { value: 'task-unassociated' } });
    fireEvent.click(within(drawer).getByRole('button', { name: 'Link selected corrective task' }));
    // Assert
    await waitFor(() => expect(api.linkRemediationFindingTask).toHaveBeenCalledWith('system-a', 'finding-a', 'task-unassociated', 'unassociated-revision'));
    expect(api.createRemediationTask).not.toHaveBeenCalled();
  });
  it('records explicit task verification without changing its status or the finding', async () => {
    // Arrange
    mount('?task=task-a');
    const drawer = await screen.findByRole('dialog', { name: 'Correct session timeout settings' });
    // Act
    fireEvent.click(within(drawer).getByRole('button', { name: 'Evidence & verification' }));
    fireEvent.change(screen.getByRole('combobox', { name: 'Verification result' }), { target: { value: 'Passed' } });
    fireEvent.change(screen.getByRole('textbox', { name: 'Reviewer notes & retest outcome' }), { target: { value: 'Verified the retained observation against the affected resource.' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save verification' }));
    // Assert
    await waitFor(() => expect(api.verifyRemediationTask).toHaveBeenCalledWith('system-a', 'task-a', {
      rowVersion: 'task-revision-a', status: 'Passed',
      notes: 'Verified the retained observation against the affected resource.',
    }));
    expect(api.moveRemediationTask).not.toHaveBeenCalled();
  });
  it('explains actual server blockers instead of presenting checkboxes as closure proof', async () => {
    // Arrange
    mount('?finding=finding-a');
    const drawer = await screen.findByRole('dialog', { name: 'Verify session timeout correction' });
    // Act
    fireEvent.click(within(drawer).getByRole('button', { name: 'Evidence & verification' }));
    // Assert
    expect(within(drawer).getByText('Complete the remaining verification task.')).toBeVisible();
    expect(within(drawer).queryByRole('button', { name: 'Record verification' })).not.toBeInTheDocument();
    expect(within(drawer).getByRole('button', { name: /Review verification: Correct/ })).toBeVisible();
    expect(api.verifyRemediationTask).not.toHaveBeenCalled();
  });
  it('keeps unassociated tasks available without counting them as findings', async () => {
    // Arrange
    vi.mocked(api.getRemediationWorkspace).mockResolvedValue({ ...remediationWorkspace, findings: [],
      tasks: [{ ...remediationWorkspace.tasks[0]!, findingId: null }] });
    mount();
    // Act
    fireEvent.click(await screen.findByText('Additional remediation work (1)'));
    // Assert
    expect(screen.getByRole('button', { name: 'Correct session timeout settings' })).toBeVisible();
    expect(screen.getByText('No findings recorded')).toBeVisible();
    expect(screen.queryByRole('button', { name: /All findings/ })).not.toBeInTheDocument();
  });
  it('offers explicit manual finding creation and does not fabricate an assessment source', async () => {
    // Arrange
    mount();
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Add finding' }));
    fireEvent.change(screen.getByRole('textbox', { name: 'Finding title' }), { target: { value: 'Manual observation' } });
    fireEvent.change(screen.getByRole('textbox', { name: 'Observed weakness' }), { target: { value: 'Recorded during an interview.' } });
    fireEvent.change(screen.getByRole('textbox', { name: 'Affected control' }), { target: { value: 'AC-12' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save finding' }));
    // Assert
    await waitFor(() => expect(api.createRemediationFinding).toHaveBeenCalledWith('system-a', expect.objectContaining({
      title: 'Manual observation', description: 'Recorded during an interview.', controlId: 'AC-12', operationId: expect.any(String),
    })));
    expect(api.createRemediationTask).not.toHaveBeenCalled();
  });
  it('links an existing shared POA&M without creating or deleting records', async () => {
    // Arrange
    vi.mocked(api.getRemediationTask).mockResolvedValue({ ...remediationTaskDetail, poams: [],
      task: { ...remediationTaskDetail.task, poamIds: [] } });
    mount('?task=task-a');
    const drawer = await screen.findByRole('dialog', { name: 'Correct session timeout settings' });
    // Act
    fireEvent.click(within(drawer).getByRole('button', { name: 'Linked work' }));
    fireEvent.click(within(drawer).getByRole('button', { name: 'Link existing POA&M' }));
    fireEvent.change(within(drawer).getByRole('combobox', { name: 'POA&M commitment' }), { target: { value: 'poam-a' } });
    fireEvent.click(within(drawer).getByRole('button', { name: 'Confirm link' }));
    // Assert
    await waitFor(() => expect(api.linkRemediationPoamTask).toHaveBeenCalledWith('system-a', 'poam-a', 'task-a', {
      expectedPoamRevision: 'poam-revision-a', expectedTaskRevision: 'task-revision-a',
    }));
    expect(api.createRemediationTask).not.toHaveBeenCalled();
  });
  it('previews the unlink impact and only removes the selected relationship', async () => {
    // Arrange
    mount('?task=task-a');
    const drawer = await screen.findByRole('dialog', { name: 'Correct session timeout settings' });
    // Act
    fireEvent.click(within(drawer).getByRole('button', { name: 'Linked work' }));
    fireEvent.click(within(drawer).getByRole('button', { name: 'Unlink from this task' }));
    // Assert
    expect(within(drawer).getByText(/Both records, external tickets/)).toBeVisible();
    expect(api.unlinkRemediationPoamTask).not.toHaveBeenCalled();
    // Act
    fireEvent.click(within(drawer).getByRole('button', { name: 'Confirm unlink' }));
    // Assert
    await waitFor(() => expect(api.unlinkRemediationPoamTask).toHaveBeenCalledWith('system-a', 'poam-a', 'task-a', {
      expectedPoamRevision: 'poam-revision-a', expectedTaskRevision: 'task-revision-a',
    }));
    expect(api.moveRemediationTask).not.toHaveBeenCalled();
  });
  it('keeps corrective drafts and reports concurrency conflicts without silent retry', async () => {
    // Arrange
    vi.mocked(api.saveRemediationTask).mockRejectedValueOnce({ error: 'Task revision changed. Reload first.' });
    mount('?task=task-a');
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Edit task' }));
    fireEvent.change(screen.getByRole('textbox', { name: 'Corrective action' }), { target: { value: 'Uncommitted corrective work.' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save task' }));
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Task revision changed.');
    expect(screen.getByRole('textbox', { name: 'Corrective action' })).toHaveValue('Uncommitted corrective work.');
    expect(api.saveRemediationTask).toHaveBeenCalledTimes(1);
  });
  it('saves corrective work on the retained task with its revision and existing owner', async () => {
    // Arrange
    mount('?task=task-a');
    const drawer = await screen.findByRole('dialog', { name: 'Correct session timeout settings' });
    // Act
    fireEvent.click(within(drawer).getByRole('button', { name: 'Edit task' }));
    fireEvent.change(screen.getByRole('textbox', { name: 'Corrective action' }), { target: { value: 'Updated corrective instructions.' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save task' }));
    // Assert
    await waitFor(() => expect(api.saveRemediationTask).toHaveBeenCalledWith('system-a', 'task-a', expect.objectContaining({
      expectedRowVersion: 'task-revision-a', description: 'Updated corrective instructions.', assigneeId: 'person-a',
    })));
  });
  it('creates a second task for the same finding without generating a POA&M', async () => {
    // Arrange
    mount('?finding=finding-a');
    const drawer = await screen.findByRole('dialog', { name: 'Verify session timeout correction' });
    // Act
    fireEvent.click(within(drawer).getByRole('button', { name: 'Linked work' }));
    fireEvent.click(within(drawer).getByRole('button', { name: 'Create remediation task' }));
    fireEvent.change(screen.getByRole('textbox', { name: 'Task title' }), { target: { value: 'Retest the corrected scope' } });
    fireEvent.change(screen.getByRole('textbox', { name: 'Corrective action' }), { target: { value: 'Review the corrected setting.' } });
    fireEvent.click(screen.getByRole('button', { name: 'Create task' }));
    // Assert
    await waitFor(() => expect(api.createRemediationTask).toHaveBeenCalledWith('system-a', expect.objectContaining({
      findingId: 'finding-a', title: 'Retest the corrected scope', operationId: expect.any(String),
    })));
    expect(api.linkRemediationPoamTask).not.toHaveBeenCalled();
  });
  it('submits only an advertised task transition with a comment and retained revision', async () => {
    // Arrange
    mount('?task=task-a');
    const drawer = await screen.findByRole('dialog', { name: 'Correct session timeout settings' });
    // Act
    fireEvent.click(within(drawer).getByRole('button', { name: 'Change task status' }));
    fireEvent.change(screen.getByRole('combobox', { name: 'New task status' }), { target: { value: 'InProgress' } });
    fireEvent.change(screen.getByRole('textbox', { name: 'Transition notes' }), { target: { value: 'Retest identified further work.' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save task status' }));
    // Assert
    await waitFor(() => expect(api.moveRemediationTask).toHaveBeenCalledWith('system-a', 'task-a', {
      expectedRowVersion: 'task-revision-a', status: 'InProgress', comment: 'Retest identified further work.',
    }));
  });
  it('opens the original task from linked work without duplicating the finding or task', async () => {
    // Arrange
    mount('?finding=finding-a');
    const finding = await screen.findByRole('dialog', { name: 'Verify session timeout correction' });
    // Act
    fireEvent.click(within(finding).getByRole('button', { name: 'Linked work' }));
    fireEvent.click(within(finding).getByRole('button', { name: 'Correct session timeout settings' }));
    // Assert
    const task = await screen.findByRole('dialog', { name: 'Correct session timeout settings' });
    expect(within(task).getByText('Ready to verify')).toBeVisible();
    expect(api.getRemediationTask).toHaveBeenCalledWith('system-a', 'task-a', expect.any(AbortSignal));
    expect(screen.getAllByRole('dialog')).toHaveLength(1);
    // Act
    fireEvent.click(within(task).getByRole('button', { name: 'Close dialog' }));
    // Assert
    expect(await screen.findByRole('dialog', { name: 'Verify session timeout correction' })).toBeVisible();
  });
  it('opens the finding with retained provenance and distinct work disposition', async () => {
    // Arrange
    mount();
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Verify session timeout correction' }));
    // Assert
    const drawer = await screen.findByRole('dialog', { name: 'Verify session timeout correction' });
    expect(within(drawer).getByText('Plan revision 1')).toBeVisible();
    expect(within(drawer).getByText('Open', { exact: true })).toBeVisible();
    expect(within(drawer).getByRole('link', { name: 'View source assessment' })).toHaveAttribute('href',
      '/systems/system-a/assessments?result=assessment%3Aassessment-a&plan=sap-a');
  });
  it('shows shared corrective work and independent exception decisions', async () => {
    // Arrange
    mount('?finding=finding-a');
    const drawer = await screen.findByRole('dialog', { name: 'Verify session timeout correction' });
    // Act
    fireEvent.click(within(drawer).getByRole('button', { name: 'Linked work' }));
    // Assert
    expect(within(drawer).getByText('Correct session timeout settings')).toBeVisible();
    expect(within(drawer).getByText('Verify corrected configuration')).toBeVisible();
    expect(within(drawer).getByText('Session management')).toBeVisible();
    expect(within(drawer).getByText('Pending', { exact: true })).toBeVisible();
    expect(within(drawer).getByText(/Linking does not accept risk/)).toBeVisible();
  });
  it('retains list search when the drawer closes', async () => {
    // Arrange
    mount();
    fireEvent.change(await screen.findByRole('searchbox'), { target: { value: 'timeout' } });
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Verify session timeout correction' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Close dialog' }));
    // Assert
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(screen.getByRole('searchbox')).toHaveValue('timeout');
  });
  it('retains filters during a successful queue refresh', async () => {
    // Arrange
    mount();
    fireEvent.change(await screen.findByRole('searchbox'), { target: { value: 'timeout' } });
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Refresh' }));
    await waitFor(() => expect(api.getRemediationWorkspace).toHaveBeenCalledTimes(2));
    // Assert
    expect(await screen.findByRole('searchbox')).toHaveValue('timeout');
  });
  it('shows failed loads honestly and retries rather than showing no findings', async () => {
    // Arrange
    vi.mocked(api.getRemediationWorkspace).mockRejectedValueOnce(new Error('Permission was revoked.'));
    mount();
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Permission was revoked.');
    expect(screen.queryByText('No findings recorded')).not.toBeInTheDocument();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Retry workspace' }));
    // Assert
    expect(await screen.findByRole('button', { name: 'Verify session timeout correction' })).toBeVisible();
  });
  it('clears the previous system and cancels its obsolete request on context switch', async () => {
    // Arrange
    const { rerender } = mount();
    await screen.findByRole('button', { name: 'Verify session timeout correction' });
    const signal = vi.mocked(api.getRemediationWorkspace).mock.calls[0]![1];
    vi.mocked(api.getRemediationWorkspace).mockImplementation(() => new Promise(() => undefined));
    // Act
    rerender(<MemoryRouter><FindingsWorkspace systemId="system-b" /></MemoryRouter>);
    // Assert
    expect(signal?.aborted).toBe(true);
    expect(screen.queryByText('Verify session timeout correction')).not.toBeInTheDocument();
    expect(screen.getByRole('status')).toHaveTextContent('Loading');
  });
});
