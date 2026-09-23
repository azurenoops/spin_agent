import { StrictMode, type ReactNode } from 'react';
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { UseMeResult } from '../../features/auth/useMe';
import type { NotificationCapabilities } from '../../features/notifications/capabilities';
import type { PackageDetail } from '../../api/package';
import type { ExportDetail } from '../../api/exports';
import { workspaceSession } from '../helpers/domainPermissions';
import PackageGenerationDialog from '../../components/PackageGenerationDialog';
import ExportSspDialog from '../../components/ExportSspDialog';
import ScanImportProgressBar from '../../features/scan-import/ScanImportProgressBar';
import * as packages from '../../api/package';
import * as exportsApi from '../../api/exports';
import * as scans from '../../api/scanImport';
import * as packageShortcuts from '../../api/packages';
import { downloadAuthenticatedFile } from '../../api/downloads';

const mocks = vi.hoisted(() => ({
  me: { data: null, isLoading: false, error: null, refetch: () => {} } as UseMeResult,
  capabilities: vi.fn<() => Promise<NotificationCapabilities>>(),
  withUrl: vi.fn(), start: vi.fn(), stop: vi.fn(), invoke: vi.fn(),
  handlers: new Map<string, (payload: unknown) => void>(),
}));
vi.mock('../../features/auth/useMe', () => ({ useMe: () => mocks.me }));
vi.mock('../../features/auth/msalInstance', () => ({
  getMsalInstance: () => ({
    getAllAccounts: () => [], getActiveAccount: () => null,
    addEventCallback: () => 'observer', removeEventCallback: vi.fn(),
  }),
  acquireBearer: vi.fn().mockResolvedValue('synthetic-bearer'), DEFAULT_API_SCOPES: [],
}));
vi.mock('../../features/notifications/capabilities', () => ({ getNotificationCapabilities: mocks.capabilities }));
vi.mock('../../api/package', () => ({
  generatePackage: vi.fn(), getPackageDetail: vi.fn(), validatePackage: vi.fn(),
  downloadPackageUrl: () => '/api/v1/systems/system-a/packages/package-a/download',
}));
vi.mock('../../api/packages', () => ({
  enqueuePackage: vi.fn(), getPackageStatus: vi.fn(),
  getPackageDownloadUrl: () => '/api/v1/systems/system-a/packages/package-a/download',
}));
vi.mock('../../api/downloads', () => ({ downloadAuthenticatedFile: vi.fn() }));
vi.mock('../../api/exports', () => ({
  requestExport: vi.fn(), getExport: vi.fn(), listTemplates: vi.fn(),
  downloadExportUrl: () => '/api/dashboard/systems/system-a/exports/export-a/download',
}));
vi.mock('../../api/scanImport', () => ({
  getScanImportStatus: vi.fn(), cancelScanImport: vi.fn(),
}));
vi.mock('../../api/client', () => ({ default: { get: vi.fn(), post: vi.fn(), defaults: { baseURL: '/api/dashboard' } } }));
vi.mock('../../components/AuthenticatedDownload', () => ({
  default: ({ children, url }: { children: ReactNode; url: string }) => <a href={url}>{children}</a>,
}));
vi.mock('@microsoft/signalr', () => ({
  HubConnectionBuilder: class {
    withUrl(...args: unknown[]) { mocks.withUrl(...args); return this; }
    withAutomaticReconnect() { return this; }
    build() {
      return { start: mocks.start, stop: mocks.stop, invoke: mocks.invoke,
        on: (name: string, handler: (payload: unknown) => void) => mocks.handlers.set(name, handler),
        onreconnecting: vi.fn(), onreconnected: vi.fn(), onclose: vi.fn() };
    }
  },
}));

const cookieCapabilities: NotificationCapabilities = {
  recipientId: 'actor-a', rest: { available: true, reasonCode: null },
  realtime: { available: false, authentication: 'bearer', cookieSessionSupported: false,
    reasonCode: 'REALTIME_BEARER_REQUIRED', hubPaths: ['/hubs/notifications', '/hubs/package', '/hubs/import-progress'] },
  fallback: { transport: 'rest-polling', pollIntervalSeconds: 30 },
};
const packageDetail: PackageDetail = {
  packageId: 'package-a', systemId: 'system-a', status: 'Completed', evidenceMode: 'Embedded',
  artifacts: [], validation: null, fileSize: 100, failureReason: null, failedArtifactType: null,
  generatedBy: 'synthetic', generatedAt: '2026-09-21T12:00:00Z', completedAt: '2026-09-21T12:01:00Z', expiresAt: '2026-12-21T12:00:00Z',
};
const exportDetail: ExportDetail = {
  exportId: 'export-a', systemId: 'system-a', status: 'Completed', format: 'docx',
  fileSize: 100, controlCount: 1, generatedBy: 'synthetic', generatedAt: '2026-09-21T12:00:00Z',
  completedAt: '2026-09-21T12:01:00Z', expiresAt: '2026-12-21T12:00:00Z', templateName: null, contentHash: 'synthetic',
};
function mount(children: ReactNode) {
  return render(<MemoryRouter>{children}</MemoryRouter>);
}
function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>(accept => { resolve = accept; });
  return { promise, resolve };
}

beforeEach(() => {
  vi.resetAllMocks();
  mocks.handlers.clear();
  mocks.me = { data: workspaceSession('system-a').identity, isLoading: false, error: null, refetch: vi.fn() };
  mocks.capabilities.mockResolvedValue(cookieCapabilities);
  mocks.start.mockResolvedValue(undefined);
  mocks.stop.mockResolvedValue(undefined);
  mocks.invoke.mockResolvedValue(undefined);
  vi.mocked(packages.validatePackage).mockResolvedValue({ isValid: true, errorCount: 0, warningCount: 0,
    validatedAt: '2026-09-21T12:00:00Z', findings: [] });
  vi.mocked(packages.generatePackage).mockResolvedValue({ packageId: 'package-a', status: 'Pending', message: 'Queued' });
  vi.mocked(packages.getPackageDetail).mockResolvedValue(packageDetail);
  vi.mocked(exportsApi.requestExport).mockResolvedValue({ ...exportDetail, status: 'Pending' });
  vi.mocked(exportsApi.getExport).mockResolvedValue(exportDetail);
  vi.mocked(exportsApi.listTemplates).mockResolvedValue({ items: [], totalCount: 0 });
  vi.mocked(downloadAuthenticatedFile).mockResolvedValue(undefined);
  vi.mocked(scans.getScanImportStatus).mockResolvedValue({
    id: 'scan-a', status: 'Completed', processedCount: 2, totalCount: 2, errorMessage: null, cancelRequested: false,
  });
  window.history.replaceState({}, '', '/workspaces/organizations/11111111-1111-1111-1111-111111111111/systems/system-a');
});
afterEach(() => { cleanup(); vi.useRealTimers(); });

describe('cookie-session progress clients', () => {
  it('completes package generation through authorized status polling without a bearer hub', async () => {
    // Arrange
    const onComplete = vi.fn();
    mount(<PackageGenerationDialog systemId="system-a" onClose={vi.fn()} onPackageComplete={onComplete} />);
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Generate Package' }));
    // Assert
    expect(await screen.findByText('Package Generated Successfully')).toBeInTheDocument();
    expect(packages.getPackageDetail).toHaveBeenCalledWith('system-a', 'package-a', expect.any(AbortSignal));
    expect(mocks.start).not.toHaveBeenCalled();
    expect(onComplete).toHaveBeenCalledTimes(1);
    expect(screen.getByText(/REALTIME_BEARER_REQUIRED/)).toBeInTheDocument();
  });

  it('completes DOCX/PDF export using the existing export detail status endpoint', async () => {
    // Arrange
    const onComplete = vi.fn();
    mount(<ExportSspDialog systemId="system-a" onClose={vi.fn()} onExportComplete={onComplete} />);
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Export' }));
    // Assert
    expect(await screen.findByRole('link', { name: 'Download Word (.docx)' })).toBeInTheDocument();
    expect(exportsApi.getExport).toHaveBeenCalledWith('system-a', 'export-a', expect.any(AbortSignal));
    expect(mocks.start).not.toHaveBeenCalled();
    expect(onComplete).toHaveBeenCalledTimes(1);
  });

  it('reports SSP failure without fabricating a failure detail absent from the REST contract', async () => {
    // Arrange
    vi.mocked(exportsApi.getExport).mockResolvedValue({ ...exportDetail, status: 'Failed' });
    const onComplete = vi.fn();
    mount(<ExportSspDialog systemId="system-a" onClose={vi.fn()} onExportComplete={onComplete} />);
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Export' }));
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent(/Export failed/i);
    expect(onComplete).not.toHaveBeenCalled();
    expect(screen.queryByRole('link', { name: /Download Word/ })).not.toBeInTheDocument();
  });

  it('shows the actual polled SSP status without inventing detailed progress percentages', async () => {
    // Arrange
    vi.mocked(exportsApi.getExport).mockResolvedValue({ ...exportDetail, status: 'Processing', completedAt: null });
    mount(<ExportSspDialog systemId="system-a" onClose={vi.fn()} />);
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Export' }));
    // Assert
    expect(await screen.findByText('Processing')).toBeInTheDocument();
    expect(screen.getByText('Detailed progress is unavailable from the status endpoint.')).toBeInTheDocument();
    expect(screen.getByRole('status', { name: 'Export processing' })).toBeInTheDocument();
    expect(screen.queryByRole('progressbar')).not.toBeInTheDocument();
    expect(mocks.start).not.toHaveBeenCalled();
  });

  it('loads scan status immediately for a cookie session and announces completion once', async () => {
    // Arrange
    const onComplete = vi.fn();
    // Act
    mount(<ScanImportProgressBar systemId="system-a" importJobId="scan-a" onComplete={onComplete} />);
    // Assert
    expect(await screen.findByText('Import complete')).toBeInTheDocument();
    expect(scans.getScanImportStatus).toHaveBeenCalledWith('system-a', 'scan-a', expect.any(AbortSignal));
    expect(mocks.start).not.toHaveBeenCalled();
    expect(onComplete).toHaveBeenCalledExactlyOnceWith('Completed');
  });

  it('surfaces failed scan cancellation rather than swallowing the error', async () => {
    // Arrange
    vi.mocked(scans.getScanImportStatus).mockResolvedValue({
      id: 'scan-a', status: 'Processing', processedCount: 0, totalCount: 10, errorMessage: null, cancelRequested: false,
    });
    vi.mocked(scans.cancelScanImport).mockRejectedValue(new Error('Cancellation denied.'));
    mount(<ScanImportProgressBar systemId="system-a" importJobId="scan-a" />);
    await waitFor(() => expect(scans.getScanImportStatus).toHaveBeenCalled());
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Cancel import' }));
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Cancellation denied.');
  });

  it('rejects a status response for a different scan job', async () => {
    // Arrange
    vi.mocked(scans.getScanImportStatus).mockResolvedValue({
      id: 'other-scan', status: 'Completed', processedCount: 2, totalCount: 2, errorMessage: null, cancelRequested: false,
    });
    const onComplete = vi.fn();
    // Act
    mount(<ScanImportProgressBar systemId="system-a" importJobId="scan-a" onComplete={onComplete} />);
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent(/unexpected.*status/i);
    expect(onComplete).not.toHaveBeenCalled();
  });

  it('discards a previous-system package submission before it can start monitoring', async () => {
    // Arrange
    const pending = deferred<Awaited<ReturnType<typeof packages.generatePackage>>>();
    vi.mocked(packages.generatePackage).mockReturnValueOnce(pending.promise);
    const onComplete = vi.fn();
    const view = mount(<PackageGenerationDialog systemId="system-a" onClose={vi.fn()} onPackageComplete={onComplete} />);
    fireEvent.click(await screen.findByRole('button', { name: 'Generate Package' }));
    // Act
    view.rerender(<MemoryRouter><PackageGenerationDialog systemId="system-b" onClose={vi.fn()} onPackageComplete={onComplete} /></MemoryRouter>);
    await act(async () => pending.resolve({ packageId: 'package-a', status: 'Pending', message: 'Queued' }));
    // Assert
    expect(packages.getPackageDetail).not.toHaveBeenCalled();
    expect(mocks.start).not.toHaveBeenCalled();
    expect(onComplete).not.toHaveBeenCalled();
    const signal = vi.mocked(packages.generatePackage).mock.calls[0]?.[2];
    expect(signal?.aborted).toBe(true);
  });

  it('discards export completion after its authenticated workspace changes', async () => {
    // Arrange
    const pending = deferred<ExportDetail>();
    vi.mocked(exportsApi.getExport).mockReturnValueOnce(pending.promise);
    const onComplete = vi.fn();
    const view = mount(<ExportSspDialog systemId="system-a" onClose={vi.fn()} onExportComplete={onComplete} />);
    fireEvent.click(screen.getByRole('button', { name: 'Export' }));
    await waitFor(() => expect(exportsApi.getExport).toHaveBeenCalled());
    // Act
    window.history.replaceState({}, '', '/workspaces/organizations/22222222-2222-2222-2222-222222222222/systems/system-a');
    view.rerender(<MemoryRouter><ExportSspDialog systemId="system-a" onClose={vi.fn()} onExportComplete={onComplete} /></MemoryRouter>);
    await act(async () => pending.resolve(exportDetail));
    // Assert
    expect(onComplete).not.toHaveBeenCalled();
    expect(screen.queryByRole('link', { name: /Download Word/ })).not.toBeInTheDocument();
    expect(vi.mocked(exportsApi.getExport).mock.calls[0]?.[2]?.aborted).toBe(true);
  });

  it('uses the bound server recipient for SSP realtime and treats final REST status as authoritative', async () => {
    // Arrange
    mocks.capabilities.mockResolvedValue({ ...cookieCapabilities, realtime: { ...cookieCapabilities.realtime, available: true, reasonCode: null } });
    vi.mocked(exportsApi.getExport).mockResolvedValue({ ...exportDetail, status: 'Processing' });
    const complete = vi.fn();
    mount(<ExportSspDialog systemId="system-a" onClose={vi.fn()} onExportComplete={complete} />);
    fireEvent.click(screen.getByRole('button', { name: 'Export' }));
    await waitFor(() => expect(mocks.invoke).toHaveBeenCalledWith('RegisterUser', 'actor-a'));
    const progress = mocks.handlers.get('SspExportProgress');
    const ready = mocks.handlers.get('SspExportReady');
    if (!progress || !ready) throw new Error('Expected SSP progress subscriptions.');
    // Act
    await act(async () => progress({ exportId: 'export-a', step: 'Rendering document', percentage: 60 }));
    expect(screen.getByText('Rendering document')).toBeInTheDocument();
    vi.mocked(exportsApi.getExport).mockResolvedValue(exportDetail);
    await act(async () => ready({ exportId: 'export-a', format: 'docx' }));
    // Assert
    expect(await screen.findByRole('link', { name: 'Download Word (.docx)' })).toBeInTheDocument();
    expect(complete).toHaveBeenCalledTimes(1);
  });

  it('adapts PackageFailed event hints through the distinct authorized polling response', async () => {
    // Arrange
    mocks.capabilities.mockResolvedValue({ ...cookieCapabilities, realtime: { ...cookieCapabilities.realtime, available: true, reasonCode: null } });
    vi.mocked(packages.getPackageDetail).mockResolvedValue({ ...packageDetail, status: 'Generating', completedAt: null });
    const complete = vi.fn();
    mount(<PackageGenerationDialog systemId="system-a" onClose={vi.fn()} onPackageComplete={complete} />);
    fireEvent.click(await screen.findByRole('button', { name: 'Generate Package' }));
    await waitFor(() => expect(mocks.invoke).toHaveBeenCalledWith('SubscribeToPackage', 'package-a'));
    const failed = mocks.handlers.get('PackageFailed');
    if (!failed) throw new Error('Expected a package failure subscription.');
    vi.mocked(packages.getPackageDetail).mockResolvedValue({
      ...packageDetail, status: 'Failed', failureReason: 'Stored SAR generation failure.', failedArtifactType: 'Sar',
    });
    // Act
    await act(async () => failed({
      packageId: 'package-a', failedArtifact: 'Sar', error: 'Realtime error hint.',
      remediation: 'Review the assessment inputs.', failedAt: '2026-09-21T12:01:00Z',
    }));
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Stored SAR generation failure.');
    expect(screen.getByRole('alert')).toHaveTextContent('Security Assessment Report');
    expect(complete).not.toHaveBeenCalled();
  });

  it('does not use PackageComplete event download URLs as authority for the download destination', async () => {
    // Arrange
    mocks.capabilities.mockResolvedValue({ ...cookieCapabilities, realtime: { ...cookieCapabilities.realtime, available: true, reasonCode: null } });
    vi.mocked(packages.getPackageDetail).mockResolvedValue({ ...packageDetail, status: 'Generating', completedAt: null });
    mount(<PackageGenerationDialog systemId="system-a" onClose={vi.fn()} />);
    fireEvent.click(await screen.findByRole('button', { name: 'Generate Package' }));
    await waitFor(() => expect(mocks.invoke).toHaveBeenCalledWith('SubscribeToPackage', 'package-a'));
    const complete = mocks.handlers.get('PackageComplete');
    if (!complete) throw new Error('Expected a package completion subscription.');
    vi.mocked(packages.getPackageDetail).mockResolvedValue(packageDetail);
    // Act
    await act(async () => complete({
      packageId: 'package-a', downloadUrl: 'https://untrusted.example.test/download', completedAt: packageDetail.completedAt,
    }));
    // Assert
    expect(await screen.findByRole('link', { name: 'Download ZIP' }))
      .toHaveAttribute('href', '/api/v1/systems/system-a/packages/package-a/download');
  });

  it('filters scan realtime by job ID and does not announce duplicate terminal events', async () => {
    // Arrange
    mocks.capabilities.mockResolvedValue({ ...cookieCapabilities, realtime: { ...cookieCapabilities.realtime, available: true, reasonCode: null } });
    vi.mocked(scans.getScanImportStatus).mockResolvedValue({
      id: 'scan-a', status: 'Processing', processedCount: 0, totalCount: 10, errorMessage: null, cancelRequested: false,
    });
    const complete = vi.fn();
    mount(<ScanImportProgressBar systemId="system-a" importJobId="scan-a" onComplete={complete} />);
    await waitFor(() => expect(mocks.invoke).toHaveBeenCalledWith('JoinImportGroup', 'scan-a'));
    const progress = mocks.handlers.get('ImportProgress');
    if (!progress) throw new Error('Expected an import progress subscription.');
    // Act
    await act(async () => progress({ jobId: 'other-job', status: 'Completed', processedCount: 10, totalCount: 10, errorMessage: null }));
    expect(complete).not.toHaveBeenCalled();
    await act(async () => {
      progress({ jobId: 'scan-a', status: 'Completed', processedCount: 10, totalCount: 10, errorMessage: null });
      progress({ jobId: 'scan-a', status: 'Completed', processedCount: 10, totalCount: 10, errorMessage: null });
    });
    // Assert
    expect(screen.getByText('Import complete')).toBeInTheDocument();
    expect(complete).toHaveBeenCalledExactlyOnceWith('Completed');
  });

  it('handles package shortcut completion with an authenticated cancellable download', async () => {
    // Arrange
    vi.mocked(packageShortcuts.enqueuePackage).mockResolvedValue({ packageId: 'package-a', status: 'Pending', message: 'Queued' });
    vi.mocked(packageShortcuts.getPackageStatus).mockResolvedValue(packageDetail);
    mount(<ExportSspDialog systemId="system-a" onClose={vi.fn()} />);
    // Act
    fireEvent.click(screen.getByRole('button', { name: /ATO Package PDF/ }));
    // Assert
    await waitFor(() => expect(downloadAuthenticatedFile).toHaveBeenCalledWith(
      '/api/v1/systems/system-a/packages/package-a/download', 'ato-package-pdf-system-a.zip', expect.any(AbortSignal),
    ));
    expect(packageShortcuts.getPackageStatus).toHaveBeenCalledWith('system-a', 'package-a', expect.any(AbortSignal));
    expect(mocks.start).not.toHaveBeenCalled();
  });

  it('does not trigger a package shortcut download after the scope changes', async () => {
    // Arrange
    const pending = deferred<PackageDetail>();
    vi.mocked(packageShortcuts.enqueuePackage).mockResolvedValue({ packageId: 'package-a', status: 'Pending', message: 'Queued' });
    vi.mocked(packageShortcuts.getPackageStatus).mockReturnValue(pending.promise);
    const view = mount(<ExportSspDialog systemId="system-a" onClose={vi.fn()} />);
    fireEvent.click(screen.getByRole('button', { name: /ATO Package PDF/ }));
    await waitFor(() => expect(packageShortcuts.getPackageStatus).toHaveBeenCalled());
    // Act
    window.history.replaceState({}, '', '/workspaces/organizations/22222222-2222-2222-2222-222222222222/systems/system-a');
    view.rerender(<MemoryRouter><ExportSspDialog systemId="system-a" onClose={vi.fn()} /></MemoryRouter>);
    await act(async () => pending.resolve(packageDetail));
    // Assert
    expect(downloadAuthenticatedFile).not.toHaveBeenCalled();
    expect(vi.mocked(packageShortcuts.getPackageStatus).mock.calls[0]?.[2]?.aborted).toBe(true);
  });

  it('surfaces a mismatched package status without announcing success', async () => {
    // Arrange
    vi.mocked(packages.getPackageDetail).mockResolvedValue({ ...packageDetail, systemId: 'other-system' });
    const complete = vi.fn();
    mount(<PackageGenerationDialog systemId="system-a" onClose={vi.fn()} onPackageComplete={complete} />);
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Generate Package' }));
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Unexpected package status response.');
    expect(complete).not.toHaveBeenCalled();
  });

  it.each(['package', 'ssp', 'shortcut'] as const)('rejects a missing %s job identifier instead of leaving progress stuck', async kind => {
    // Arrange
    if (kind === 'package') vi.mocked(packages.generatePackage).mockResolvedValue({ packageId: '', status: 'Pending', message: 'Queued' });
    if (kind === 'ssp') vi.mocked(exportsApi.requestExport).mockResolvedValue({ ...exportDetail, exportId: '', status: 'Pending' });
    if (kind === 'shortcut') vi.mocked(packageShortcuts.enqueuePackage).mockResolvedValue({ packageId: '', status: 'Pending', message: 'Queued' });
    mount(kind === 'package'
      ? <PackageGenerationDialog systemId="system-a" onClose={vi.fn()} />
      : <ExportSspDialog systemId="system-a" onClose={vi.fn()} />);
    // Act
    fireEvent.click(await screen.findByRole('button', { name: kind === 'package' ? 'Generate Package' : kind === 'ssp' ? 'Export' : /ATO Package PDF/ }));
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent(/unexpected.*response/i);
    expect(mocks.start).not.toHaveBeenCalled();
  });

  it('survives StrictMode effect replay without stale scan completion or duplicate callbacks', async () => {
    // Arrange
    const complete = vi.fn();
    // Act
    mount(<StrictMode><ScanImportProgressBar systemId="system-a" importJobId="scan-a" onComplete={complete} /></StrictMode>);
    // Assert
    expect(await screen.findByText('Import complete')).toBeInTheDocument();
    expect(complete).toHaveBeenCalledExactlyOnceWith('Completed');
    expect(mocks.start).not.toHaveBeenCalled();
  });
});
