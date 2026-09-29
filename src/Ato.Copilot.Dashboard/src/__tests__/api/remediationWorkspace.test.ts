import { beforeEach, describe, expect, it, vi } from 'vitest';
import apiClient from '../../api/client';
import { createRemediationFinding, createRemediationTask, getRemediationFinding, getRemediationTask, getRemediationWorkspace,
  linkRemediationEvidence, linkRemediationPoamTask, moveRemediationTask, remediationWorkspaceError,
  saveRemediationTask, unlinkRemediationPoamTask, verifyRemediationTask } from '../../api/remediationWorkspace';
import { rawRemediationWorkspace as remediationWorkspace } from '../fixtures/remediationWorkspace';

vi.mock('../../api/client', () => ({ default: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() } }));
beforeEach(() => vi.clearAllMocks());
describe('Scoped remediation workspace client', () => {
  it('projects details from the same complete aggregate and retains evidence hashes', async () => {
    // Arrange
    vi.mocked(apiClient.get).mockResolvedValue({ data: remediationWorkspace });
    // Act
    const finding = await getRemediationFinding('system-a', 'finding-a');
    const task = await getRemediationTask('system-a', 'task-a');
    // Assert
    expect(finding.tasks).toHaveLength(2);
    expect(finding.evidence).toHaveLength(1);
    expect(finding.exceptions[0]?.status).toBe('Pending');
    expect(task.task.verificationStatus).toBe('NotVerified');
    expect(task.evidence[0]?.hash).toBe('retained-evidence-hash');
    expect(task.task.affectedResources).toEqual(['/resource/a']);
    expect(task.task.validationCriteria).toBe('Retest the timeout.');
    expect(apiClient.get).toHaveBeenCalledTimes(2);
    expect(apiClient.get).toHaveBeenLastCalledWith('/systems/system-a/remediation-workspace', { signal: undefined });
  });
  it('preserves manual sources and warns about ineffective approved exceptions', async () => {
    // Arrange
    const raw = structuredClone(remediationWorkspace);
    raw.findings[0]!.provenance!.sourceType = 'Manual';
    raw.findings[0]!.provenance!.plan = null;
    raw.findings[0]!.status = 'Accepted';
    raw.tasks = [];
    raw.exceptions[0]!.status = 'Approved';
    raw.exceptions[0]!.isEffective = false;
    vi.mocked(apiClient.get).mockResolvedValue({ data: raw });
    // Act
    const value = await getRemediationWorkspace('system-a');
    // Assert
    expect(value.findings[0]?.source?.resultId).toBeNull();
    expect(value.findings[0]?.source?.planRevision).toBeNull();
    expect(value.findings[0]?.isClosed).toBe(true);
    expect(value.findings[0]?.ownerName).toBeNull();
    expect(value.warnings.join(' ')).toMatch(/no longer effective/);
  });
  it('rejects absent task identities and wrong mutation responses', async () => {
    // Arrange
    vi.mocked(apiClient.get).mockResolvedValue({ data: remediationWorkspace });
    vi.mocked(apiClient.post).mockResolvedValue({ data: { id: 'another-task' } });
    vi.mocked(apiClient.put).mockResolvedValue({ data: { poamId: 'another-poam', taskId: 'task-a' } });
    // Act / Assert
    await expect(getRemediationTask('system-a', 'task-missing')).rejects.toThrow(/different record/);
    await expect(moveRemediationTask('system-a', 'task-a', { expectedRowVersion: 'rev', status: 'InReview', comment: '' })).rejects.toThrow(/confirmed/);
    await expect(linkRemediationPoamTask('system-a', 'poam-a', 'task-a', { expectedPoamRevision: 'rev', expectedTaskRevision: 'rev' })).rejects.toThrow(/confirmed/);
  });
  it('serializes retained revisions and actual backend task commands', async () => {
    // Arrange
    const request = { title: 'Correct timeout', description: 'Apply and retest.', controlId: 'AC-12', severity: 'Medium',
      assigneeId: 'person-a', assigneeName: 'Alex', dueDate: '2026-10-05', affectedResources: [], validationCriteria: null,
      expectedRowVersion: 'revision-a', operationId: 'operation-a' };
    vi.mocked(apiClient.post).mockResolvedValue({ data: { id: 'task-a' } });
    vi.mocked(apiClient.put).mockResolvedValue({ data: { id: 'task-a' } });
    // Act
    await createRemediationTask('system-a', request);
    await saveRemediationTask('system-a', 'task-a', request);
    await moveRemediationTask('system-a', 'task-a', { expectedRowVersion: 'revision-a', status: 'InReview', comment: 'Ready for review.' });
    await verifyRemediationTask('system-a', 'task-a', { rowVersion: 'revision-a', status: 'Passed', notes: 'Examined retained retest.' });
    await linkRemediationEvidence('system-a', 'task-a', { rowVersion: 'revision-a', evidenceId: 'evidence-a' });
    // Assert
    expect(apiClient.post).toHaveBeenCalledWith('/systems/system-a/remediation-workspace/tasks', expect.objectContaining({ requestId: 'operation-a' }));
    expect(apiClient.put).toHaveBeenCalledWith('/systems/system-a/remediation-workspace/tasks/task-a', {
      rowVersion: 'revision-a', title: 'Correct timeout', description: 'Apply and retest.', dueDate: '2026-10-05', assigneeId: 'person-a', assigneeName: 'Alex',
    });
    expect(apiClient.post).toHaveBeenCalledWith('/systems/system-a/remediation-workspace/tasks/task-a/move',
      { rowVersion: 'revision-a', status: 'InReview', comment: 'Ready for review.' });
    expect(apiClient.post).toHaveBeenCalledWith('/systems/system-a/remediation-workspace/tasks/task-a/verify',
      { rowVersion: 'revision-a', status: 'Passed', notes: 'Examined retained retest.' });
    expect(apiClient.post).toHaveBeenCalledWith('/systems/system-a/remediation-workspace/tasks/task-a/evidence',
      { rowVersion: 'revision-a', evidenceId: 'evidence-a' });
  });
  it('uses explicit pair operations and includes both expected revisions', async () => {
    // Arrange
    const request = { expectedPoamRevision: 'poam-rev', expectedTaskRevision: 'task-rev' };
    vi.mocked(apiClient.put).mockResolvedValue({ data: { poamId: 'poam-a', taskId: 'task-a' } });
    vi.mocked(apiClient.delete).mockResolvedValue({ data: { poamId: 'poam-a', taskId: 'task-a' } });
    // Act
    await linkRemediationPoamTask('system-a', 'poam-a', 'task-a', request);
    await unlinkRemediationPoamTask('system-a', 'poam-a', 'task-a', request);
    // Assert
    expect(apiClient.put).toHaveBeenCalledWith('/systems/system-a/remediation-workspace/poams/poam-a/tasks/task-a', request);
    expect(apiClient.delete).toHaveBeenCalledWith('/systems/system-a/remediation-workspace/poams/poam-a/tasks/task-a', { data: request });
  });
  it('rejects unconfirmed creation responses instead of closing the editor', async () => {
    // Arrange
    vi.mocked(apiClient.post).mockResolvedValue({ data: '<html>Unavailable proxy</html>' });
    // Act / Assert
    await expect(createRemediationFinding('system-a', { operationId: 'op-a', title: 'Finding',
      description: 'Observed weakness', controlId: 'AC-12', severity: 'Medium' })).rejects.toThrow(/confirmed/);
  });
  it('passes cancellation and validates system identity', async () => {
    // Arrange
    const controller = new AbortController();
    vi.mocked(apiClient.get).mockResolvedValue({ data: remediationWorkspace });
    // Act
    const result = await getRemediationWorkspace('system-a', controller.signal);
    // Assert
    expect(result.findings[0]?.source?.planRevision).toBe(1);
    expect(result.owners).toContainEqual({ id: 'person-b', name: 'Security assessor' });
    expect(apiClient.get).toHaveBeenCalledWith('/systems/system-a/remediation-workspace', { signal: controller.signal });
  });
  it.each([
    { ...remediationWorkspace, systemId: 'system-b' },
    { ...remediationWorkspace, permissions: {} },
    { ...remediationWorkspace, findings: null },
    { ...remediationWorkspace, findings: [remediationWorkspace.findings[0], remediationWorkspace.findings[0]] },
    { ...remediationWorkspace, findings: [{ id: 'incomplete-finding' }] },
  ])('rejects wrong-system, incomplete or duplicate queue responses', async data => {
    // Arrange
    vi.mocked(apiClient.get).mockResolvedValue({ data });
    // Act / Assert
    await expect(getRemediationWorkspace('system-a')).rejects.toThrow();
  });
  it('rejects a wrong-record detail even within the requested system', async () => {
    // Arrange
    vi.mocked(apiClient.get).mockResolvedValue({ data: remediationWorkspace });
    // Act / Assert
    await expect(getRemediationFinding('system-a', 'finding-b')).rejects.toThrow(/different record/);
  });
  it.each([
    { ...remediationWorkspace, tasks: [{ ...remediationWorkspace.tasks[0], evidence: [{ id: 'evidence-a', name: 'Incomplete evidence' }] }] },
    { ...remediationWorkspace, exceptions: [{ id: 'exception-a', status: 'Approved' }] },
    { ...remediationWorkspace, tasks: [{ ...remediationWorkspace.tasks[0], history: [{ action: 'Reviewed' }] }] },
  ])('rejects incomplete provenance rather than rendering guessed states', async data => {
    // Arrange
    vi.mocked(apiClient.get).mockResolvedValue({ data });
    // Act / Assert
    await expect(getRemediationFinding('system-a', 'finding-a')).rejects.toThrow();
  });
  it('preserves server validation and permission errors', () => {
    // Arrange / Act / Assert
    expect(remediationWorkspaceError({ error: 'The revision changed.' })).toBe('The revision changed.');
    expect(remediationWorkspaceError({ error: { message: 'Permission denied.' } })).toBe('Permission denied.');
    expect(remediationWorkspaceError({ response: { data: { error: 'Cross-system link rejected.' } } })).toBe('Cross-system link rejected.');
  });
});
