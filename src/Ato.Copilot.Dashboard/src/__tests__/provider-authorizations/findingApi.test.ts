import { beforeEach, expect, it, vi } from 'vitest';
import axios from 'axios';
import * as api from '../../features/provider-authorizations/api';

vi.mock('axios', () => ({ default: { request: vi.fn(), isAxiosError: vi.fn(() => false) } }));
beforeEach(() => vi.mocked(axios.request).mockReset());
it('submits bounded multipart evidence to the existing offering-owned endpoint', async () => {
  // Arrange
  const file = new File(['Synthetic'], 'evidence.txt', { type: 'text/plain' });
  vi.mocked(axios.request).mockResolvedValue({ data: { status: 'success', data: { evidenceId: 'e1' } } });
  // Act
  await api.submitFindingEvidence('offering', 'finding', 7, 'Test source', file, 'intent');
  // Assert
  const config = vi.mocked(axios.request).mock.calls[0]![0];
  const data = config.data as FormData;
  expect(config.url).toBe('/api/csp/offerings/offering/findings/finding/evidence');
  expect(config.headers).toEqual({ 'Idempotency-Key': 'intent' });
  expect(data.get('expectedFindingRevision')).toBe('7');
  expect(data.get('description')).toBe('Test source');
  expect(data.get('file')).toBe(file);
});
it('reviews only explicitly selected evidence at the loaded finding revision', async () => {
  // Arrange
  vi.mocked(axios.request).mockResolvedValue({ data: { status: 'success', data: { workflowState: 'Closed' } } });
  // Act
  await api.reviewFinding('offering', 'finding', { expectedRevision: 9, evidenceIds: ['e1'], disposition: 'AcceptClosure', rationale: 'Reviewed exact evidence' });
  // Assert
  expect(axios.request).toHaveBeenCalledWith(expect.objectContaining({
    method: 'POST', url: '/api/csp/offerings/offering/findings/finding/reviews',
    data: { expectedRevision: 9, evidenceIds: ['e1'], disposition: 'AcceptClosure', rationale: 'Reviewed exact evidence' },
  }));
});
it('resolves evidence identity through all owned metadata pages without downloading content', async () => {
  // Arrange
  vi.mocked(axios.request)
    .mockResolvedValueOnce({ data: { status: 'success', data: { items: [{ evidenceId: 'older' }], page: 1, pageSize: 1, total: 2 } } })
    .mockResolvedValueOnce({ data: { status: 'success', data: { items: [{ evidenceId: 'wanted', offeringId: 'offering', findingId: 'finding' }], page: 2, pageSize: 1, total: 2 } } });
  // Act
  const result = await api.getFindingEvidence('offering', 'finding', 'wanted');
  // Assert
  expect(result?.evidenceId).toBe('wanted');
  expect(axios.request).toHaveBeenCalledTimes(2);
  expect(axios.request).toHaveBeenLastCalledWith(expect.objectContaining({
    url: '/api/csp/offerings/offering/findings/finding/evidence', params: { page: 2, pageSize: 25 },
  }));
});
