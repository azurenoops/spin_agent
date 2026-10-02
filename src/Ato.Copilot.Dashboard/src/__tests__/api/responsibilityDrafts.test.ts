import { beforeEach, expect, it, vi } from 'vitest';
import apiClient from '../../api/client';
import { confirmResponsibilityDraft, getResponsibilityDraft, plainResponsibilityValues, prepareResponsibilityDraft,
  responsibilityFields, saveResponsibilityDraft, type ResponsibilityDraft } from '../../api/responsibilityDrafts';

vi.mock('../../api/client', () => ({ default: { get: vi.fn(), post: vi.fn(), put: vi.fn() } }));
const response = () => ({
  systemId: 'system-a', controlId: 'AU-11', baselineId: 'baseline-a', scopeId: null, canPrepare: true,
  sourceHash: 'hash-1', scopes: [], sources: [], questions: [], conflicts: [], draft: null,
  sourceValues: Object.fromEntries(responsibilityFields.map(key => [key, {
    value: key === 'allocation' ? 'NeedsConfirmation' : '', origin: 'From system records',
    sourceIds: [], explanation: '', sourceHash: 'hash-1', userEdited: false,
  }])),
});
beforeEach(() => vi.resetAllMocks());
function saved(): ResponsibilityDraft {
  const values = {
    allocation: { value: 'Customer', origin: 'AI proposed' as const, sourceIds: ['system'], explanation: 'Review required', userEdited: false, sourceHash: 'hash-1' },
    provider: { value: '', origin: 'From system records' as const, sourceIds: [], explanation: '', userEdited: false, sourceHash: 'hash-1' },
  };
  const blank = values.provider;
  const fields = { ...values, providerDuties: blank, customer: { ...blank, value: 'Reviewed duties.' },
    scope: blank, exclusions: blank, source: blank, basis: { ...blank, value: 'Reviewed source.' }, information: blank };
  return { id: 'draft-a', revision: 1, status: 'Proposed', sourceHash: 'hash-1', isStale: false,
    generationState: 'Prepared', generationError: null, preparedAt: '2026-10-01T00:00:00Z',
    generatedAt: '2026-10-01T00:00:00Z', preparedBy: 'fixture', reviewedBy: null, reviewedAt: null,
    values: fields, suggestion: { values: fields, questions: [], conflicts: [] }, sources: [], history: [] };
}

it('saves exact proposed fields without confirming and verifies the final authorized response', async () => {
  // Arrange
  const proposed = saved();
  const accepted = { ...proposed, revision: 3, status: 'Accepted', reviewedBy: 'fixture-reviewer', reviewedAt: '2026-10-01T01:00:00Z' };
  vi.mocked(apiClient.put).mockResolvedValue({ data: { ...proposed, revision: 2 } });
  vi.mocked(apiClient.post).mockResolvedValue({ data: accepted });
  // Act
  const edited = await saveResponsibilityDraft('system-a', 'draft-a', 1, plainResponsibilityValues(proposed.values));
  const result = await confirmResponsibilityDraft('system-a', edited, 'Human reviewed.');
  // Assert
  expect(edited.status).toBe('Proposed');
  expect(result.status).toBe('Accepted');
  expect(apiClient.post).toHaveBeenCalledWith(
    '/systems/system-a/capability-subscriptions/drafts/record/draft-a/confirm',
    expect.objectContaining({ expectedRevision: 2, sourceHash: 'hash-1', reviewNotes: 'Human reviewed.' }), expect.anything());
});

it('rejects a success-shaped confirmation that lacks accepted state and reviewer attribution', async () => {
  // Arrange
  vi.mocked(apiClient.post).mockResolvedValue({ data: saved() });
  // Act / Assert
  await expect(confirmResponsibilityDraft('system-a', saved(), 'Reviewed')).rejects.toThrow(/did not verify/);
});

it('returns a prepared proposal without relabeling its field provenance', async () => {
  // Arrange
  vi.mocked(apiClient.post).mockResolvedValue({ data: { ...response(), draft: saved() } });
  // Act
  const result = await prepareResponsibilityDraft('system-a', 'AU-11', null, 0);
  // Assert
  expect(result.draft?.values.allocation.origin).toBe('AI proposed');
  expect(result.draft?.status).toBe('Proposed');
});

it('reads exact system/control/scope context with no generation side effect', async () => {
  // Arrange
  vi.mocked(apiClient.get).mockResolvedValue({ data: response() });
  // Act
  const result = await getResponsibilityDraft('system-a', 'AU-11', null);
  // Assert
  expect(result.sourceValues.allocation.value).toBe('NeedsConfirmation');
  expect(apiClient.post).not.toHaveBeenCalled();
});

it('requests recorded environment resolution while retaining system and control identity checks', async () => {
  // Arrange
  vi.mocked(apiClient.get).mockResolvedValue({ data: { ...response(), scopeId: 'recorded-environment-scope' } });
  // Act
  const result = await getResponsibilityDraft('system-a', 'AU-11', null, undefined,
    { useEnvironment: true, capabilityId: 'capability-a' });
  // Assert
  expect(result.scopeId).toBe('recorded-environment-scope');
  expect(apiClient.get).toHaveBeenCalledWith(expect.any(String),
    expect.objectContaining({ params: { scopeId: null, useEnvironment: true, capabilityId: 'capability-a' } }));
});

it('rejects preparation that returns no persisted draft instead of reporting it ready', async () => {
  // Arrange
  vi.mocked(apiClient.post).mockResolvedValue({ data: response() });
  // Act / Assert
  await expect(prepareResponsibilityDraft('system-a', 'AU-11', null, 0)).rejects.toThrow(/did not return a saved/);
});

it.each([
  { systemId: 'foreign-system' }, { controlId: 'AC-1' }, { scopeId: 'foreign-scope' },
  { canPrepare: 'true' }, { sourceValues: {} }, { sources: [{ id: 'forged' }] },
])('rejects mismatched or incomplete context', async change => {
  // Arrange
  vi.mocked(apiClient.get).mockResolvedValue({ data: { ...response(), ...change } });
  // Act / Assert
  await expect(getResponsibilityDraft('system-a', 'AU-11', null)).rejects.toThrow(/does not match/);
});

it('surfaces a failed generation as an error while retaining verifiable source context', async () => {
  // Arrange
  vi.mocked(apiClient.post).mockRejectedValue({ status: 503, title: 'AI unavailable', draftContext: response() });
  // Act / Assert
  await expect(prepareResponsibilityDraft('system-a', 'AU-11', null, 0)).rejects.toMatchObject({
    name: 'Error', status: 503, message: 'AI unavailable', context: { systemId: 'system-a' },
  });
});

it('does not retain foreign context attached to a generation error', async () => {
  // Arrange
  vi.mocked(apiClient.post).mockRejectedValue({ status: 503, title: 'AI unavailable', draftContext: { ...response(), systemId: 'foreign' } });
  // Act / Assert
  await expect(prepareResponsibilityDraft('system-a', 'AU-11', null, 0)).rejects.toThrow(/does not match/);
});
