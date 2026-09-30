import { beforeEach, describe, expect, it, vi } from 'vitest';
import { getSspPreview, retainSspPreview } from '../../api/exports';
import apiClient from '../../api/client';

vi.mock('../../api/client', () => ({ default: { get: vi.fn(), post: vi.fn() } }));

const approved = {
  systemId: 'synthetic/system', format: 'json', contentType: 'application/json',
  content: '{"system-security-plan":{}}', contentHash: 'synthetic-hash',
  generatedAt: '2026-09-30T12:00:00Z', sourceGaps: [], isPreview: true,
  sourceState: 'ApprovedSources', canGenerate: true,
  sourceManifest: { scope: 'GeneratedOscalContent', profiles: [], providerSources: [], narratives: [],
    otherSources: 'CurrentWorkingDataAtGeneration', previewOnly: false },
};

describe('explicit approved SSP preview adapter', () => {
  beforeEach(() => vi.clearAllMocks());

  it('requests approved source mode without changing the existing argument order', async () => {
    // Arrange
    const signal = new AbortController().signal;
    vi.mocked(apiClient.get).mockResolvedValue({ data: approved });

    // Act
    const result = await getSspPreview('synthetic/system', signal, 'approved');

    // Assert
    expect(apiClient.get).toHaveBeenCalledWith('/systems/synthetic%2Fsystem/documents/ssp/preview?source=approved', { signal });
    expect(result.sourceState).toBe('ApprovedSources');
    expect(result.canGenerate).toBe(true);
  });

  it('retains the same approved mode with its idempotency key', async () => {
    // Arrange
    vi.mocked(apiClient.post).mockResolvedValue({ data: { ...approved, previewId: 'retained-id' } });

    // Act
    const result = await retainSspPreview('synthetic/system', 'key', undefined, 'approved');

    // Assert
    expect(apiClient.post).toHaveBeenCalledWith('/systems/synthetic%2Fsystem/documents/ssp/preview?source=approved', {},
      { headers: { 'Idempotency-Key': 'key' }, signal: undefined });
    expect(result.previewId).toBe('retained-id');
  });

  it('rejects a working response or working pins masquerading as approved output', async () => {
    // Arrange
    vi.mocked(apiClient.get).mockResolvedValueOnce({ data: { ...approved, sourceState: 'CurrentWorkingData' } })
      .mockResolvedValueOnce({ data: { ...approved, sourceManifest: { ...approved.sourceManifest, previewOnly: true } } });

    // Act
    const wrongMode = getSspPreview('synthetic/system', undefined, 'approved');

    // Assert
    await expect(wrongMode).rejects.toThrow();
    await expect(getSspPreview('synthetic/system', undefined, 'approved')).rejects.toThrow();
  });

  it('preserves the default working request and accepts server-denied approved generation', async () => {
    // Arrange
    vi.mocked(apiClient.get).mockResolvedValueOnce({ data: { ...approved, sourceState: 'CurrentWorkingData',
      canGenerate: false, sourceManifest: { ...approved.sourceManifest, scope: 'WorkingProfilePreview', previewOnly: true } } })
      .mockResolvedValueOnce({ data: { ...approved, canGenerate: false } });

    // Act
    const working = await getSspPreview('synthetic/system');
    const readOnlyApproved = await getSspPreview('synthetic/system', undefined, 'approved');

    // Assert
    expect(apiClient.get).toHaveBeenNthCalledWith(1, '/systems/synthetic%2Fsystem/documents/ssp/preview', { signal: undefined });
    expect(working.sourceState).toBe('CurrentWorkingData');
    expect(readOnlyApproved.canGenerate).toBe(false);
  });

  it('rejects malformed design source and artifact pins', async () => {
    // Arrange
    vi.mocked(apiClient.get).mockResolvedValueOnce({ data: { ...approved,
      sourceManifest: { ...approved.sourceManifest, design: { kind: 'ApprovedSystemDesign' } } } })
      .mockResolvedValueOnce({ data: { ...approved,
        sourceManifest: { ...approved.sourceManifest, designArtifacts: 'not-an-array' } } });

    // Act
    const invalidDesign = getSspPreview('synthetic/system', undefined, 'approved');

    // Assert
    await expect(invalidDesign).rejects.toThrow();
    await expect(getSspPreview('synthetic/system', undefined, 'approved')).rejects.toThrow();
  });
});
