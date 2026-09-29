import { beforeEach, describe, expect, it, vi } from 'vitest';
import { getSspPreview, retainSspPreview, requestPreviewExport } from '../../api/exports';
import api from '../../api/client';

vi.mock('../../api/client', () => ({ default: { get: vi.fn(), post: vi.fn() } }));
const response = {
  systemId: 'system-a', format: 'json', contentType: 'application/json', content: '{}',
  contentHash: 'hash-a', generatedAt: '2026-09-26T00:00:00Z', isPreview: true, sourceState: 'CurrentWorkingData',
  sourceGaps: [], sourceManifest: { scope: 'ProfileAndProvider', profiles: [], providerSources: [], narratives: [], otherSources: 'CurrentWorkingDataAtGeneration' },
};
beforeEach(() => vi.resetAllMocks());

describe('source-backed preview API boundary', () => {
  it('requires explicit review-only authority for working-profile previews', async () => {
    // Arrange
    const working = { ...response, sourceManifest: { ...response.sourceManifest, scope: 'WorkingProfilePreview', previewOnly: true } };
    vi.mocked(api.get).mockResolvedValueOnce({ data: { ...working, canGenerate: true } })
      .mockResolvedValueOnce({ data: { ...working, canGenerate: false } });
    // Act / Assert
    await expect(getSspPreview('system-a')).rejects.toThrow('working-preview export authority');
    expect((await getSspPreview('system-a')).canGenerate).toBe(false);
  });

  it('rejects a response belonging to a different system', async () => {
    // Arrange
    vi.mocked(api.get).mockResolvedValue({ data: { ...response, systemId: 'foreign-system' } });
    // Act / Assert
    await expect(getSspPreview('system-a')).rejects.toThrow('differently scoped');
  });

  it('does not render malformed source references as provenance', async () => {
    // Arrange
    vi.mocked(api.get).mockResolvedValue({ data: { ...response, sourceManifest: { ...response.sourceManifest, profiles: [null] } } });
    // Act / Assert
    await expect(getSspPreview('system-a')).rejects.toThrow('invalid document source manifest');
  });

  it('retains one request identity and exports the selected preview only', async () => {
    // Arrange
    const signal = new AbortController().signal;
    vi.mocked(api.post).mockResolvedValueOnce({ data: { ...response, previewId: 'preview-a' } })
      .mockResolvedValueOnce({ data: { exportId: 'export-a' } });
    // Act
    const preview = await retainSspPreview('system-a', 'retain-key', signal);
    await requestPreviewExport('system-a', preview.previewId!, 'export-key', signal);
    // Assert
    expect(api.post).toHaveBeenNthCalledWith(1, '/systems/system-a/documents/ssp/preview', {},
      { signal, headers: { 'Idempotency-Key': 'retain-key' } });
    expect(api.post).toHaveBeenNthCalledWith(2, '/systems/system-a/exports', { format: 'json', sourcePreviewId: 'preview-a' },
      { signal, headers: { 'Idempotency-Key': 'export-key' } });
  });

  it('does not claim an unretained response is ready for exact-content export', async () => {
    // Arrange
    vi.mocked(api.post).mockResolvedValue({ data: response });
    // Act / Assert
    await expect(retainSspPreview('system-a', 'retain-key')).rejects.toThrow('retained preview identity');
  });
});
