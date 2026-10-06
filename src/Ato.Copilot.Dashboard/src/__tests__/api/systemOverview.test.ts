import { beforeEach, describe, expect, it, vi } from 'vitest';
import api from '../../api/client';
import { confirmOverviewPhase, explainOverviewGroup, getOverviewWork } from '../../api/systemOverview';
import { overviewWork } from '../fixtures/systemOverview';

vi.mock('../../api/client', () => ({ default: { get: vi.fn(), post: vi.fn() } }));
beforeEach(() => vi.clearAllMocks());
describe('System overview read/write contracts', () => {
  it('reads exact selected run with bounded pagination and assignment filter', async () => {
    // Arrange
    vi.mocked(api.get).mockResolvedValue({ data: overviewWork() });
    const signal = new AbortController().signal;
    // Act
    const work = await getOverviewWork('a', 'run-a', { mine: false }, signal);
    // Assert
    expect(work.counts.total).toBe(5);
    expect(api.get).toHaveBeenCalledWith('/systems/a/package-readiness/runs/run-a/work',
      { params: { purpose: 'InitialSubmission', mine: false, limit: 10, offset: 0 }, signal });
  });
  it.each([
    null, { ...overviewWork(), systemId: 'foreign' }, { ...overviewWork(), runId: 'wrong' },
    { ...overviewWork(), purpose: 'Legacy' }, { ...overviewWork(), counts: { total: 5, blocking: 5, warnings: 1 } },
    { ...overviewWork(), groups: { ...overviewWork().groups, limit: 1000 } },
  ])('rejects mismatched scope or totals', async data => {
    // Arrange
    vi.mocked(api.get).mockResolvedValue({ data });
    // Act / Assert
    await expect(getOverviewWork('a', 'run-a')).rejects.toThrow('incomplete or mismatched');
  });
  it('rejects a role-based or foreign assignment represented as personal work', async () => {
    // Arrange
    vi.mocked(api.get).mockResolvedValue({ data: overviewWork() });
    // Act / Assert
    await expect(getOverviewWork('a', 'run-a', { mine: true })).rejects.toThrow();
  });
  it('rejects unsafe workflow destinations', async () => {
    // Arrange
    const result = overviewWork();
    result.groups.items[0]!.action = { ...result.groups.items[0]!.action, path: 'https://example.org' };
    vi.mocked(api.get).mockResolvedValue({ data: result });
    // Act / Assert
    await expect(getOverviewWork('a', 'run-a')).rejects.toThrow();
  });
  it('rejects complete group pages whose totals do not reconcile with raw findings', async () => {
    // Arrange
    vi.mocked(api.get).mockResolvedValue({ data: { ...overviewWork(), counts: { total: 6, blocking: 5, warnings: 1 } } });
    // Act / Assert
    await expect(getOverviewWork('a', 'run-a')).rejects.toThrow();
  });
  it('rejects the same individual finding being assigned to two work groups', async () => {
    // Arrange
    const data = overviewWork();
    data.groups.items[1]!.findings.items[0]!.id = data.groups.items[0]!.findings.items[0]!.id;
    vi.mocked(api.get).mockResolvedValue({ data });
    // Act / Assert
    await expect(getOverviewWork('a', 'run-a')).rejects.toThrow();
  });
  it.each([-1, 0, 1000, 1.5])('rejects invalid page size %s without sending requests', async limit => {
    // Arrange / Act / Assert
    await expect(getOverviewWork('a', 'run-a', { limit })).rejects.toThrow();
    expect(api.get).not.toHaveBeenCalled();
  });
  it('requires server confirmation of the exact explicitly recorded phase', async () => {
    // Arrange
    const input = { phase: 'Prepare', expectedPhase: 'Prepare', notes: 'Reviewed preparation' };
    vi.mocked(api.post).mockResolvedValue({ data: { phase: 'Prepare', confirmed: false } });
    // Act / Assert
    await expect(confirmOverviewPhase('a', input)).rejects.toThrow();
    expect(api.post).toHaveBeenCalledWith('/systems/a/package-readiness/rmf-phase', input, { signal: undefined });
  });
  it('rejects an explanation from another run or with unsafe provenance links', async () => {
    // Arrange
    vi.mocked(api.post).mockResolvedValue({ data: { systemId: 'a', runId: 'wrong', groupId: 'design',
      origin: 'AI proposed', sourceHash: 'hash', content: 'Suggestion', sources: [], questions: [] } });
    // Act / Assert
    await expect(explainOverviewGroup('a', 'run-a', { groupId: 'design', controlId: null, scopeId: null })).rejects.toThrow();
  });
  it('accepts a source-versioned read-only explanation and rejects an unsafe source destination', async () => {
    // Arrange
    const result = { systemId: 'a', runId: 'run-a', groupId: 'design', origin: 'AI proposed', sourceHash: 'a'.repeat(64),
      content: 'Review recorded design facts.', sources: [{ id: 'source', title: 'Recorded design',
        origin: 'From system records', version: 'v7', content: 'Review required', href: '/systems/a/profile/SystemDesign' }], questions: [] };
    vi.mocked(api.post).mockResolvedValueOnce({ data: result }).mockResolvedValueOnce({
      data: { ...result, sources: [{ ...result.sources[0], href: 'https://example.org' }] },
    });
    // Act
    const explanation = await explainOverviewGroup('a', 'run-a', { groupId: 'design', controlId: null, scopeId: null });
    // Assert
    expect(explanation.sources[0]?.version).toBe('v7');
    await expect(explainOverviewGroup('a', 'run-a', { groupId: 'design', controlId: null, scopeId: null })).rejects.toThrow();
  });
});
