import { describe, expect, it } from 'vitest';
import { projectPoamQueue } from '../../../components/poam/poamQueueProjection';
import type { PoamWorkspace } from '../../../api/poamWorkspace';

const now = new Date('2026-09-29T12:00:00Z');
const milestone = { id: 'm-a', description: 'Verify correction', targetDate: '2026-09-20T00:00:00Z', completedDate: null, sequence: 1 };
const row = {
  id: 'poam-a', poamId: 'poam-a', weakness: 'Audit logging', securityControlNumber: 'AU-2',
  catSeverity: 'CatII', pointOfContact: 'Security team', status: 'Ongoing', taskIds: [], findingId: null,
  deviationId: null, rowVersion: 'version-a', scheduledCompletionDate: '2026-12-01T00:00:00Z', milestones: [milestone],
};
const source = (poams: unknown[], tasks: unknown[] = []) => ({
  systemId: 'system-a', poams, tasks, findings: [], exceptions: [],
  permissions: { canManageRemediation: true },
}) as unknown as PoamWorkspace;

describe('POA&M complete-array queue projection', () => {
  it('uses the earliest incomplete milestone deadline and sequence only as a tie-breaker', () => {
    // Arrange
    const workspace = source([{ ...row, milestones: [
      { ...milestone, id: 'later', targetDate: '2026-10-01', sequence: 1 },
      { ...milestone, id: 'next', targetDate: '2026-09-15', sequence: 2 },
      { ...milestone, id: 'completed', targetDate: '2026-09-01', completedDate: '2026-09-01', sequence: 3 },
    ] }]);
    // Act
    const result = projectPoamQueue(workspace, {}, now);
    // Assert
    expect(result.items[0]?.nextMilestone?.id).toBe('next');
  });
  it('counts milestone-aware overdue before filtering/pagination and selects first incomplete sequence', () => {
    // Arrange
    const workspace = source([
      { ...row, milestones: [{ ...milestone, id: 'm-2', sequence: 2 }, { ...milestone, id: 'm-1', sequence: 1 }] },
      { ...row, id: 'poam-b', weakness: 'Different weakness', status: 'Completed' },
      { ...row, id: 'poam-c', status: 'RiskAccepted' },
    ]);
    // Act
    const result = projectPoamQueue(workspace, { search: 'audit', pageSize: 1, page: 1 }, now);
    // Assert
    expect(result.counts).toEqual({ all: 3, overdue: 1, readyToVerify: 0, closed: 1 });
    expect(result.items).toHaveLength(1);
    expect(result.items[0]?.nextMilestone?.id).toBe('m-1');
    expect(result.items[0]?.isOverdue).toBe(true);
  });
  it('keeps risk acceptance separate from completed commitments', () => {
    // Arrange
    const workspace = source([{ ...row, id: 'completed', status: 'Completed' }, { ...row, id: 'accepted', status: 'RiskAccepted' }]);
    // Act
    const closed = projectPoamQueue(workspace, { view: 'closed' }, now);
    const accepted = projectPoamQueue(workspace, { status: 'RiskAccepted' }, now);
    // Assert
    expect(closed.items.map(item => item.id)).toEqual(['completed']);
    expect(accepted.items.map(item => item.id)).toEqual(['accepted']);
    expect(closed.counts.closed).toBe(1);
  });
  it('never turns zero milestones, missing tasks, failed verification or unfinished tasks into ready candidates', () => {
    // Arrange
    const complete = [{ ...milestone, completedDate: '2026-09-25T00:00:00Z' }];
    const workspace = source([
      { ...row, id: 'empty', milestones: [] },
      { ...row, id: 'missing', milestones: complete, taskIds: ['missing-task'] },
      { ...row, id: 'failed', milestones: complete, taskIds: ['failed-task'] },
      { ...row, id: 'working', milestones: complete, taskIds: ['working-task'] },
      { ...row, id: 'ready', milestones: complete, taskIds: ['done-task'] },
      { ...row, id: 'manual', milestones: complete },
      { ...row, id: 'closed', status: 'Completed', milestones: complete },
    ], [
      { id: 'failed-task', status: 'Done', verificationStatus: 'Failed' },
      { id: 'working-task', status: 'InReview', verificationStatus: 'Passed' },
      { id: 'done-task', status: 'Done', verificationStatus: 'Passed' },
    ]);
    // Act
    const result = projectPoamQueue(workspace, { view: 'ready' }, now);
    // Assert
    expect(result.counts.readyToVerify).toBe(2);
    expect(result.items.map(item => item.id)).toEqual(['ready', 'manual']);
  });
  it('preserves status/severity/owner search filters and clamps stale pagination', () => {
    // Arrange
    const workspace = source([row, { ...row, id: 'other', catSeverity: 'CatI', status: 'Delayed' }]);
    // Act
    const result = projectPoamQueue(workspace, { status: 'Delayed', catSeverity: 'CatI', search: 'security', page: 4, pageSize: 1 }, now);
    // Assert
    expect(result.page).toBe(1);
    expect(result.items[0]?.id).toBe('other');
    expect(result.counts.all).toBe(2);
  });
  it('rejects missing milestone/deadline data instead of silently producing a clean queue', () => {
    // Arrange
    const workspace = source([{ ...row, milestones: undefined }]);
    // Act / Assert
    expect(() => projectPoamQueue(workspace, {}, now)).toThrow(/incomplete/i);
  });
});
