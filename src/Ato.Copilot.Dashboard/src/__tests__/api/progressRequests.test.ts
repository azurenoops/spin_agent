import { beforeEach, describe, expect, it, vi } from 'vitest';
import { generatePackage, getPackageDetail, validatePackage, type PackageDetail } from '../../api/package';
import { getExport, listTemplates, requestExport, type ExportDetail } from '../../api/exports';
import { cancelScanImport, getScanImportStatus, type ScanImportStatusDto } from '../../api/scanImport';

const mocks = vi.hoisted(() => ({
  v1: { get: vi.fn(), post: vi.fn() },
  dashboard: { get: vi.fn(), post: vi.fn(), delete: vi.fn() },
}));
vi.mock('axios', () => ({ default: { create: () => mocks.v1 } }));
vi.mock('../../api/client', () => ({ default: mocks.dashboard }));
vi.mock('../../features/auth/interceptors', () => ({ attachAuthInterceptor: vi.fn() }));
vi.mock('../../features/auth/msalInstance', () => ({ getMsalInstance: vi.fn(), DEFAULT_API_SCOPES: [] }));

const packageDetail: PackageDetail = {
  packageId: 'package-a', systemId: 'system-a', status: 'Pending', evidenceMode: 'Embedded',
  artifacts: [], validation: null, fileSize: null, failureReason: null, failedArtifactType: null,
  generatedBy: 'synthetic', generatedAt: '2026-09-21T12:00:00Z', completedAt: null, expiresAt: '2026-12-21T12:00:00Z',
};
const exportDetail: ExportDetail = {
  exportId: 'export-a', systemId: 'system-a', status: 'Pending', format: 'docx', fileSize: null, controlCount: null,
  generatedBy: 'synthetic', generatedAt: '2026-09-21T12:00:00Z', completedAt: null, templateName: null,
  contentHash: null, expiresAt: '2026-12-21T12:00:00Z',
};
const scanStatus: ScanImportStatusDto = {
  id: 'scan-a', status: 'Processing', processedCount: 0, totalCount: 10, errorMessage: null, cancelRequested: false,
};
beforeEach(() => vi.resetAllMocks());

describe('progress HTTP request cancellation', () => {
  it('carries the cancellation signal through package readiness, submission, and detail', async () => {
    // Arrange
    const signal = new AbortController().signal;
    mocks.v1.post.mockResolvedValueOnce({ data: { isValid: true, errorCount: 0, warningCount: 0,
      validatedAt: '2026-09-21T12:00:00Z', findings: [] } })
      .mockResolvedValueOnce({ data: { packageId: 'package-a', status: 'Pending', message: 'Queued' } });
    mocks.v1.get.mockResolvedValue({ data: packageDetail });
    // Act
    await validatePackage('system-a', signal);
    await generatePackage('system-a', 'Embedded', signal);
    const detail = await getPackageDetail('system-a', 'package-a', signal);
    // Assert
    expect(mocks.v1.post).toHaveBeenNthCalledWith(1, '/systems/system-a/packages/validate', undefined, { signal });
    expect(mocks.v1.post).toHaveBeenNthCalledWith(2, '/systems/system-a/packages', { evidenceMode: 'Embedded', includeEvidence: true }, { signal });
    expect(mocks.v1.get).toHaveBeenCalledExactlyOnceWith('/systems/system-a/packages/package-a', { signal });
    expect(detail).toBe(packageDetail);
  });

  it('carries the cancellation signal through SSP templates, submission, and status', async () => {
    // Arrange
    const signal = new AbortController().signal;
    mocks.dashboard.get.mockResolvedValueOnce({ data: { items: [], totalCount: 0 } })
      .mockResolvedValueOnce({ data: exportDetail });
    mocks.dashboard.post.mockResolvedValue({ data: exportDetail });
    // Act
    await listTemplates({ limit: 50 }, signal);
    await requestExport('system-a', 'docx', 'template-a', signal);
    const detail = await getExport('system-a', 'export-a', signal);
    // Assert
    expect(mocks.dashboard.get).toHaveBeenNthCalledWith(1, '/templates', { params: { limit: 50 }, signal });
    expect(mocks.dashboard.post).toHaveBeenCalledExactlyOnceWith('/systems/system-a/exports', { format: 'docx', templateId: 'template-a' }, { signal });
    expect(mocks.dashboard.get).toHaveBeenNthCalledWith(2, '/systems/system-a/exports/export-a', { signal });
    expect(detail).toBe(exportDetail);
  });

  it('carries the cancellation signal through scan status and cancellation', async () => {
    // Arrange
    const signal = new AbortController().signal;
    mocks.dashboard.get.mockResolvedValue({ data: scanStatus });
    mocks.dashboard.delete.mockResolvedValue({ data: { id: 'scan-a', cancelRequested: true } });
    // Act
    const status = await getScanImportStatus('system-a', 'scan-a', signal);
    await cancelScanImport('system-a', 'scan-a', signal);
    // Assert
    expect(mocks.dashboard.get).toHaveBeenCalledExactlyOnceWith('/systems/system-a/scans/import/scan-a/status', { signal });
    expect(mocks.dashboard.delete).toHaveBeenCalledExactlyOnceWith('/systems/system-a/scans/import/scan-a', { signal });
    expect(status).toBe(scanStatus);
  });
});
