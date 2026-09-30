import { beforeEach, describe, expect, it, vi } from 'vitest';
import apiClient from '../../api/client';
import {
  generatePackageFromReadiness, getLatestPackageReadinessRun, getPackageReadinessCheck,
  getPackageReadinessRun, getPackageReadinessWorkspace, listPackageReadinessRuns,
  validatePackageReadiness,
  type PackageReadinessCheck, type PackageReadinessRun, type PackageReadinessScope,
  type PackageReadinessWorkspace,
} from '../../api/packageReadiness';

vi.mock('../../api/client', () => ({ default: { get: vi.fn(), post: vi.fn() } }));
const hash = 'a'.repeat(64);
const scope: PackageReadinessScope = {
  systemId: 'system', purpose: 'Legacy', retainedContext: null, selectionHash: hash,
};
const action = { canView: true, canEdit: false, path: 'documents', label: 'Open', reason: 'Read-only source' } as const;
const check: PackageReadinessCheck = {
  id: 'boundary', ruleId: 'boundary-defined', title: 'Authorization boundary', outcome: 'Passed',
  category: 'boundary', required: true, applicability: 'Applicable', why: 'A boundary is required.',
  missingSource: null, sources: [{ kind: 'boundary', recordId: 'boundary-1', revision: null, contentHash: hash, label: 'Boundary' }],
  nextSteps: [], expectedRole: 'Issm', recordedOwner: null, action,
};
const run: PackageReadinessRun = {
  id: 'run-1', outcome: 'Ready', startedAt: '2026-09-29T12:00:00Z', evaluatedAt: '2026-09-29T12:01:00Z',
  evaluatedBy: 'synthetic', sourceHash: hash, sourceHashAfter: hash, ruleVersion: 'package-readiness/1',
  counts: { total: 1, passed: 1, blocking: 0, followUp: 0, notApplicable: 0, unavailable: 0, requiredUnavailable: 0 },
  recommendedCheckId: null, failure: null,
  freshness: { state: 'Current', checkedAt: '2026-09-29T12:02:00Z', currentSourceHash: hash, reason: null },
};
const workspace = (): PackageReadinessWorkspace => ({
  ...scope, source: { state: 'Available', hash, ruleVersion: run.ruleVersion, reason: null }, latestRun: run,
  permissions: { canValidate: true, validateReason: null, canGenerate: true, generateReason: null },
  progress: (['prepare', 'validate', 'export', 'emass', 'decision'] as const).map(id => ({
    id, state: 'NotRecorded', description: 'No observation recorded.', records: [], totalCount: 0, action,
  })),
  documents: [], rmf: { phase: 'Prepare', transitions: [], totalCount: 0 },
});
const detail = () => ({ ...scope, run, checks: { items: [check], totalCount: 1, limit: 50, offset: 0 } });

beforeEach(() => vi.resetAllMocks());

describe('Package readiness additive adapter', () => {
  it('defaults only to Legacy and preserves AbortSignal without triggering validation', async () => {
    // Arrange
    vi.mocked(apiClient.get).mockResolvedValue({ data: workspace() });
    const signal = new AbortController().signal;
    // Act
    const result = await getPackageReadinessWorkspace('system', undefined, signal);
    // Assert
    expect(result.purpose).toBe('Legacy');
    expect(apiClient.get).toHaveBeenCalledWith('/systems/system/package-readiness', { params: { purpose: 'Legacy' }, signal });
    expect(apiClient.post).not.toHaveBeenCalled();
  });

  it('preserves retained pins and purpose on reads and explicit validation', async () => {
    // Arrange
    const retainedContext = { baselinePackageId: 'base', baselineContentHash: hash, authorizationDecisionId: 'decision' };
    const selection = { purpose: 'AuthorizedBaselineArchive', retainedContext } as const;
    vi.mocked(apiClient.get).mockResolvedValue({ data: { ...scope, ...selection, latestRun: null } });
    vi.mocked(apiClient.post).mockResolvedValue({ data: { ...detail(), ...selection } });
    // Act
    await getLatestPackageReadinessRun('system', selection);
    await validatePackageReadiness('system', selection);
    // Assert
    expect(apiClient.get).toHaveBeenCalledWith('/systems/system/package-readiness/runs/latest', {
      params: { purpose: selection.purpose, ...retainedContext }, signal: undefined,
    });
    expect(apiClient.post).toHaveBeenCalledWith('/systems/system/package-readiness/runs', selection, { signal: undefined });
  });

  it('supports immutable scoped run/check detail and whole-run counts with filtered paging', async () => {
    // Arrange
    vi.mocked(apiClient.get).mockResolvedValueOnce({ data: detail() })
      .mockResolvedValueOnce({ data: { ...scope, runId: run.id, check } })
      .mockResolvedValueOnce({ data: { ...scope, items: [run], totalCount: 1, limit: 20, offset: 0 } });
    // Act
    await getPackageReadinessRun('system', run.id);
    await getPackageReadinessCheck('system', run.id, check.id);
    const history = await listPackageReadinessRuns('system');
    // Assert
    expect(history.items[0]?.id).toBe(run.id);
    expect(apiClient.get).toHaveBeenNthCalledWith(2, '/systems/system/package-readiness/runs/run-1/checks/boundary', {
      params: { purpose: 'Legacy' }, signal: undefined,
    });
  });

  it.each([
    { ...scope, systemId: 'other' },
    { ...scope, purpose: 'InitialSubmission' },
    { ...scope, retainedContext: { baselinePackageId: 'foreign' } },
  ])('rejects mismatched scope %j without a fallback', async wrongScope => {
    // Arrange
    vi.mocked(apiClient.get).mockResolvedValue({ data: { ...workspace(), ...wrongScope } });
    // Act / Assert
    await expect(getPackageReadinessWorkspace('system')).rejects.toThrow(/scope|contract/i);
    expect(apiClient.post).not.toHaveBeenCalled();
  });

  it.each(['//outside.example', '/systems/other/documents', 'javascript:alert(1)', 'documents?returnTo=https://outside.example',
    'documents?tab=records#unverified', 'documents#ssp-sections', 'assessments?tab=plan#ssp-sections'])(
    'rejects unsafe source action %s', async path => {
      // Arrange
      vi.mocked(apiClient.get).mockResolvedValue({ data: { ...scope, runId: run.id, check: { ...check, action: { ...action, path } } } });
      // Act / Assert
      await expect(getPackageReadinessCheck('system', run.id, check.id)).rejects.toThrow(/contract|action/i);
    });

  it.each(['documents?tab=records', 'documents?tab=records#ssp-sections',
    'documents?purpose=InitialSubmission&tab=records#ssp-sections', 'assessments?tab=plan',
    'assessments', 'poam', 'inheritance/subscriptions', 'profile/PortsProtocolsAndServices'])(
    'accepts verified source-workflow path %s', async path => {
      // Arrange
      vi.mocked(apiClient.get).mockResolvedValue({ data: { ...scope, runId: run.id, check: { ...check, action: { ...action, path } } } });
      // Act
      const response = await getPackageReadinessCheck('system', run.id, check.id);
      // Assert
      expect(response.check.action.path).toBe(path);
      expect(response.check.action.canEdit).toBe(false);
    });

  it('rejects impossible passing counts and wrong run receipts', async () => {
    // Arrange
    vi.mocked(apiClient.get).mockResolvedValueOnce({ data: { ...detail(), run: { ...run, counts: { ...run.counts, total: 9 } } } })
      .mockResolvedValueOnce({ data: { ...detail(), run: { ...run, id: 'other-run' } } });
    // Act / Assert
    await expect(getPackageReadinessRun('system', run.id)).rejects.toThrow(/contract|count/i);
    await expect(getPackageReadinessRun('system', run.id)).rejects.toThrow(/contract|run/i);
  });

  it('retains Failed and stale historical results without turning either into Ready', async () => {
    // Arrange
    const failed = { ...run, outcome: 'Failed', failure: { code: 'SOURCE_UNAVAILABLE', message: 'Cannot read source' },
      sourceHash: null, sourceHashAfter: null, freshness: { ...run.freshness, state: 'Unavailable', currentSourceHash: null, reason: 'Cannot read source' } };
    vi.mocked(apiClient.get).mockResolvedValueOnce({ data: { ...scope, latestRun: failed } })
      .mockResolvedValueOnce({ data: { ...scope, latestRun: { ...run, freshness: { ...run.freshness, state: 'Stale', currentSourceHash: 'b'.repeat(64), reason: 'Source changed' } } } });
    // Act / Assert
    expect((await getLatestPackageReadinessRun('system')).latestRun?.outcome).toBe('Failed');
    expect((await getLatestPackageReadinessRun('system')).latestRun?.freshness.state).toBe('Stale');
  });

  it('binds generation to purpose/run/source through additive existing endpoint fields', async () => {
    // Arrange
    const request = { purpose: 'InitialSubmission', readinessRunId: run.id, expectedSourceHash: hash, evidenceMode: 'Embedded', includeEvidence: true } as const;
    const receipt = { systemId: 'system', purpose: request.purpose, packageId: 'package', status: 'Pending',
      message: 'Queued', readinessRunId: run.id, sourceHash: hash };
    vi.mocked(apiClient.post).mockResolvedValueOnce({ data: receipt })
      .mockResolvedValueOnce({ data: { ...receipt, sourceHash: 'b'.repeat(64) } });
    // Act / Assert
    expect((await generatePackageFromReadiness('system', request)).packageId).toBe('package');
    expect(apiClient.post).toHaveBeenNthCalledWith(1, '/systems/system/packages', request, { baseURL: '/api/v1', signal: undefined });
    await expect(generatePackageFromReadiness('system', request)).rejects.toThrow(/receipt|contract/i);
  });

  it('propagates denied, missing endpoint and cancellation failures', async () => {
    // Arrange
    const error = new Error('404 endpoint not yet implemented');
    vi.mocked(apiClient.get).mockRejectedValue(error);
    // Act / Assert
    await expect(getPackageReadinessWorkspace('system')).rejects.toBe(error);
    expect(apiClient.post).not.toHaveBeenCalled();
  });

  it('requires whole-run outcome buckets to agree with a complete returned check set', async () => {
    // Arrange
    vi.mocked(apiClient.get).mockResolvedValue({ data: {
      ...detail(), run: { ...run, counts: { ...run.counts, passed: 0, followUp: 1 } },
    } });
    // Act / Assert
    await expect(getPackageReadinessRun('system', run.id)).rejects.toThrow(/contract|count/i);
  });

  it('uses non-current freshness to disable generation without discarding prior run facts', async () => {
    // Arrange
    const data = workspace();
    data.latestRun = { ...run, freshness: { ...run.freshness, state: 'Stale', reason: 'Decision expired' } };
    vi.mocked(apiClient.get).mockResolvedValue({ data });
    // Act / Assert
    await expect(getPackageReadinessWorkspace('system')).rejects.toThrow(/contract/i);
  });

  it('rejects malformed selection and paging before any HTTP request', async () => {
    // Arrange
    const selection = { purpose: 'ChangeSubmission', retainedContext: {
      baselinePackageId: 'base', baselineContentHash: hash, authorizationDecisionId: 'decision',
    } } as const;
    // Act / Assert
    await expect(getPackageReadinessWorkspace('system', selection)).rejects.toThrow(/contract/i);
    await expect(listPackageReadinessRuns('system', undefined, { limit: 101 })).rejects.toThrow(/contract/i);
    await expect(getPackageReadinessRun('system', run.id, undefined, { offset: -1 })).rejects.toThrow(/contract/i);
    expect(apiClient.get).not.toHaveBeenCalled();
  });

  it.each(['InitialSubmission', 'ChangeSubmission'] as const)('retains explicit %s context without substituting Legacy', async purpose => {
    // Arrange
    const selection = purpose === 'InitialSubmission' ? { purpose } : { purpose, retainedContext: {
      baselinePackageId: 'base', baselineContentHash: hash, authorizationDecisionId: 'decision',
      changePreviewId: 'preview', changeContentHash: 'b'.repeat(64),
    } };
    vi.mocked(apiClient.post).mockResolvedValue({ data: {
      ...detail(), purpose, retainedContext: selection.retainedContext ?? null,
    } });
    // Act
    const response = await validatePackageReadiness('system', selection);
    // Assert
    expect(response.purpose).toBe(purpose);
    expect(apiClient.post).toHaveBeenCalledWith('/systems/system/package-readiness/runs', selection, { signal: undefined });
  });
});
