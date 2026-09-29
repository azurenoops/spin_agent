import { beforeEach, describe, expect, it, vi } from 'vitest';
import apiClient from '../../api/client';
import { getAssessmentPlan, getAssessmentResults, getAssessmentResult, previewAssessmentPlan, prepareAssessmentReport, type AssessmentResultsQuery } from '../../api/assessmentWorkspace';
import { planWorkspace, resultsWorkspace, resultDetail, report } from '../fixtures/assessmentWorkspace';
vi.mock('../../api/client', () => ({ default: { get: vi.fn(), post: vi.fn() } }));
beforeEach(() => vi.clearAllMocks());
const query: AssessmentResultsQuery = { planId: 'sap-a', search: '', page: 1, pageSize: 25, selectedResultIds: [] };
describe('Connected assessment transport', () => {
  it('passes an explicit empty report selection instead of invoking the legacy select-all default', async () => {
    // Arrange
    vi.mocked(apiClient.get).mockResolvedValue({ data: resultsWorkspace });
    const controller = new AbortController();
    // Act
    await getAssessmentResults('system-a', query, controller.signal);
    // Assert
    expect(apiClient.get).toHaveBeenCalledWith('/systems/system-a/assessment-workspace/results',
      { params: { planId: 'sap-a', page: 1, pageSize: 25, selectedResultIds: '' }, signal: controller.signal });
  });
  it('rejects wrong system and selected plan identities', async () => {
    // Arrange
    vi.mocked(apiClient.get).mockResolvedValueOnce({ data: { ...planWorkspace, systemId: 'system-b' } })
      .mockResolvedValueOnce({ data: planWorkspace });
    // Act / Assert
    await expect(getAssessmentPlan('system-a', 'sap-a')).rejects.toThrow('mismatched');
    await expect(getAssessmentPlan('system-a', 'sap-b')).rejects.toThrow('selected plan');
  });
  it('rejects wrong result identities and incomplete permissions', async () => {
    // Arrange
    vi.mocked(apiClient.get).mockResolvedValueOnce({ data: resultDetail })
      .mockResolvedValueOnce({ data: { ...resultsWorkspace, permissions: {} } });
    // Act / Assert
    await expect(getAssessmentResult('system-a', 'assessment:foreign', 'sap-a')).rejects.toThrow('mismatched');
    await expect(getAssessmentResults('system-a', query)).rejects.toThrow('mismatched');
  });
  it('reads the exact persisted preview without a write', async () => {
    // Arrange
    vi.mocked(apiClient.get).mockResolvedValue({ data: { systemId: 'system-a', sapId: 'sap-a', revision: 2,
      contentHash: 'saved-hash', content: 'Saved approach, scope and team.' } });
    // Act
    const result = await previewAssessmentPlan('system-a', 'sap-a');
    // Assert
    expect(result.content).toBe('Saved approach, scope and team.');
    expect(apiClient.post).not.toHaveBeenCalled();
  });
  it('preserves the selected plan/result preconditions and report request key', async () => {
    // Arrange
    vi.mocked(apiClient.post).mockResolvedValue({ data: report });
    const input = { planId: 'sap-a', expectedPlanHash: 'plan-hash', resultIds: ['assessment:run-a'],
      expectedResultRevisions: { 'assessment:run-a': 'result-revision' }, requestId: 'stable-report-request', title: 'Draft report' };
    // Act
    await prepareAssessmentReport('system-a', input);
    // Assert
    expect(apiClient.post).toHaveBeenCalledWith('/systems/system-a/assessment-workspace/reports', input);
  });
});
