import { beforeEach, describe, expect, it, vi } from 'vitest';
import { getAdditionalDocumentPreview } from '../../api/exports';
import api from '../../api/client';

vi.mock('../../api/client', () => ({ default: { get: vi.fn() } }));
beforeEach(() => vi.resetAllMocks());

describe('assessment and POA&M preview contracts', () => {
  it.each(['sap', 'sar', 'poam'] as const)('loads %s through its own read-only endpoint', async documentType => {
    // Arrange
    const signal = new AbortController().signal;
    const response = { systemId: 'system-a', systemName: 'Mission', documentType, available: true,
      format: 'json', contentType: 'application/json', content: '{}', contentHash: 'hash-a', generatedAt: '2026-09-27T12:00:00Z',
      sourceGaps: [], isPreview: true, sourceState: 'CurrentWorkingData', canGenerate: false,
      sourceRecords: [{ kind: documentType, recordId: 'document-a', versionId: null, contentHash: 'source-hash' }] };
    vi.mocked(api.get).mockResolvedValue({ data: response });
    // Act
    expect(await getAdditionalDocumentPreview('system-a', documentType, signal)).toEqual(response);
    // Assert
    expect(api.get).toHaveBeenCalledWith(`/systems/system-a/documents/${documentType}/preview`, { signal });
  });
  it('preserves a genuine missing-document state without inventing a report', async () => {
    // Arrange
    const response = { systemId: 'system-a', systemName: 'Mission', documentType: 'sar', available: false,
      reasonCode: 'SAR_NOT_CREATED', message: 'No saved security assessment report exists.' };
    vi.mocked(api.get).mockResolvedValue({ data: response });
    // Act / Assert
    expect(await getAdditionalDocumentPreview('system-a', 'sar')).toEqual(response);
  });
  it.each([
    { systemId: 'foreign-system', documentType: 'sap', available: false, message: 'Missing', reasonCode: 'MISSING' },
    { systemId: 'system-a', documentType: 'sar', available: false, message: 'Missing', reasonCode: 'MISSING' },
    { systemId: 'system-a', documentType: 'sap', available: false },
  ])('rejects mismatched or incomplete availability responses', async response => {
    // Arrange
    vi.mocked(api.get).mockResolvedValue({ data: { systemName: 'Mission', ...response } });
    // Act / Assert
    await expect(getAdditionalDocumentPreview('system-a', 'sap')).rejects.toThrow('document preview');
  });
  it('does not disguise transport or permission failures as missing documents', async () => {
    // Arrange
    vi.mocked(api.get).mockRejectedValue(new Error('Access denied'));
    // Act / Assert
    await expect(getAdditionalDocumentPreview('system-a', 'sap')).rejects.toThrow('Access denied');
  });
});
