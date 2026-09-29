import { describe, expect, it, vi } from 'vitest';
import apiClient from '../../../api/client';
import { getPoamWorkspace, linkPoamWorkspaceTask, unlinkPoamWorkspaceTask, createPoamWorkspaceTask } from '../../../api/poamWorkspace';
vi.mock('../../../api/client', () => ({ default: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() } }));
describe('Connected POA&M API contract', () => {
  it('forwards optional revision checks on both relationship methods', async () => {
    // Arrange
    const revisions = { expectedPoamRevision: 'poam-version', expectedTaskRevision: 'task-version' };
    vi.mocked(apiClient.put).mockResolvedValue({ data: {} });
    vi.mocked(apiClient.delete).mockResolvedValue({ data: {} });
    // Act
    await linkPoamWorkspaceTask('system-a', 'poam-a', 'task-a', revisions);
    await unlinkPoamWorkspaceTask('system-a', 'poam-a', 'task-a', revisions);
    // Assert
    expect(apiClient.put).toHaveBeenCalledWith('/systems/system-a/remediation-workspace/poams/poam-a/tasks/task-a', revisions);
    expect(apiClient.delete).toHaveBeenCalledWith('/systems/system-a/remediation-workspace/poams/poam-a/tasks/task-a', { data: revisions });
  });
  it('rejects mismatched system projections', async () => {
    // Arrange
    vi.mocked(apiClient.get).mockResolvedValue({ data: { systemId: 'other' } });
    // Act / Assert
    await expect(getPoamWorkspace('system-a')).rejects.toThrow(/system/i);
  });
  it('rejects malformed relationship collections instead of rendering empty work', async () => {
    // Arrange
    vi.mocked(apiClient.get).mockResolvedValue({ data: {
      systemId: 'system-a', findings: [], tasks: [{ id: 'task-a', poamIds: null }], poams: [], exceptions: [],
      permissions: { canManageRemediation: false },
    } });
    // Act / Assert
    await expect(getPoamWorkspace('system-a')).rejects.toThrow(/incomplete/i);
  });
  it('uses explicit pair links, and does not write lifecycle statuses', async () => {
    // Arrange
    vi.mocked(apiClient.put).mockResolvedValue({ data: {} });
    vi.mocked(apiClient.delete).mockResolvedValue({ data: {} });
    // Act
    await linkPoamWorkspaceTask('system-a', 'poam-a', 'task-a');
    await unlinkPoamWorkspaceTask('system-a', 'poam-a', 'task-b');
    // Assert
    expect(apiClient.put).toHaveBeenCalledWith('/systems/system-a/remediation-workspace/poams/poam-a/tasks/task-a');
    expect(apiClient.delete).toHaveBeenCalledWith('/systems/system-a/remediation-workspace/poams/poam-a/tasks/task-b');
  });
  it('retains the creation idempotency token', async () => {
    // Arrange
    vi.mocked(apiClient.post).mockResolvedValue({ data: { id: 'task-a' } });
    const request = { requestId: 'operation-a', title: 'Repair', description: 'Repair configuration', controlId: 'AC-2', severity: 'High' };
    // Act
    await createPoamWorkspaceTask('system-a', request);
    // Assert
    expect(apiClient.post).toHaveBeenCalledWith('/systems/system-a/remediation-workspace/tasks', request);
  });
});
