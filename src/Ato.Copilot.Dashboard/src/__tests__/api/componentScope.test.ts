import { beforeEach, expect, it, vi } from 'vitest';
import apiClient from '../../api/client';
import { saveComponentScope, type SaveComponentScopeRequest } from '../../api/systemDesign';
import { componentDesignFixture } from '../fixtures/componentReview';

vi.mock('../../api/client', () => ({ default: { put: vi.fn(), get: vi.fn() } }));
const body: SaveComponentScopeRequest = { expectedRevision: 2, source: 'provider', componentId: 'backup',
  sourceRevision: 'r1', decision: 'Included', boundaryId: 'area', usage: '  Human corrected usage  ' };
const saved = () => ({
  ...componentDesignFixture(), revision: 3, componentScopes: [{
    source: 'provider' as const, componentId: 'backup', name: 'Azure Backup', sourceRevision: 'r1',
    decision: 'Included' as const, boundaryId: 'area', boundaryName: 'Mission API', usage: 'Human corrected usage',
  }],
});
beforeEach(() => vi.clearAllMocks());
it('confirms the exact server draft and forwards cancellation without altering entered wording', async () => {
  // Arrange
  vi.mocked(apiClient.put).mockResolvedValue({ data: saved() });
  const signal = new AbortController().signal;
  // Act
  const result = await saveComponentScope('system', body, signal);
  // Assert
  expect(result.componentScopes?.[0]?.usage).toBe('Human corrected usage');
  expect(apiClient.put).toHaveBeenCalledWith('/systems/system/design/component-scope', body, { signal });
});
it.each([null, { ...saved(), governanceStatus: 'Approved' }, { ...saved(), revision: 2 }, { ...saved(), systemId: 'foreign' },
  { ...saved(), componentScopes: [] },
  { ...saved(), componentScopes: [{ ...saved().componentScopes[0], sourceRevision: 'wrong-source' }] },
])('rejects incomplete or mismatched draft responses', async data => {
  // Arrange
  vi.mocked(apiClient.put).mockResolvedValue({ data });
  // Act
  const save = saveComponentScope('system', body);
  // Assert
  await expect(save).rejects.toThrow('The server did not confirm the requested scope draft');
});
it('requires the server to retain the exact wording proposal reference', async () => {
  // Arrange
  vi.mocked(apiClient.put).mockResolvedValue({ data: saved() });
  // Act
  const save = saveComponentScope('system', { ...body, wordingDraftId: 'draft', wordingDraftRevision: 7 });
  // Assert
  await expect(save).rejects.toThrow('The server did not confirm the requested scope draft');
});
it('surfaces permission and save validation errors returned by the server', async () => {
  // Arrange
  vi.mocked(apiClient.put).mockRejectedValue({ error: 'A recorded system area is required.' });
  // Act
  const save = saveComponentScope('system', body);
  // Assert
  await expect(save).rejects.toThrow('A recorded system area is required.');
});
