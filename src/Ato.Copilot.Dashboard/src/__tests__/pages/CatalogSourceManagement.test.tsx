import { act, fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import CatalogSourceManagement from '../../features/narratives/CatalogSourceManagement';
import * as api from '../../api/catalogSources';

vi.mock('../../api/catalogSources', () => ({
  getCatalogSourceManagement: vi.fn(), captureCatalogSource: vi.fn(), backfillCatalogSources: vi.fn(),
}));
const status = {
  isPlatformAdministrator: true, canManageSources: true, managementPath: '/workspaces/csp/controls', reason: null,
  sources: [{ identifier: 'NIST-800-53-R5', name: 'NIST Rev. 5', definitionVersion: 'legacy',
    sourceAvailable: false, sourceVersion: null, sourceUri: null, capturedAt: null }],
};
beforeEach(() => { vi.clearAllMocks(); vi.mocked(api.getCatalogSourceManagement).mockResolvedValue(status); });

describe('Catalog source administration', () => {
  it('keeps source capture and full import accessible when a catalog already exists', async () => {
    // Arrange
    const open = vi.fn();
    render(<MemoryRouter><CatalogSourceManagement onImportRequested={open} onPermissionsChanged={vi.fn()} /></MemoryRouter>);

    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Import/refresh framework definitions' }));

    // Assert
    expect(open).toHaveBeenCalledOnce();
    expect(screen.getByRole('button', { name: 'Load source for NIST Rev. 5' })).toBeEnabled();
    expect(screen.getByRole('button', { name: 'Load missing sources' })).toBeEnabled();
  });

  it('shows administrator guidance rather than mutation controls to ordinary organization users', async () => {
    // Arrange
    vi.mocked(api.getCatalogSourceManagement).mockResolvedValue({ ...status, isPlatformAdministrator: false,
      canManageSources: false, reason: 'A platform catalog administrator must refresh the source.' });
    render(<MemoryRouter><CatalogSourceManagement onImportRequested={vi.fn()} onPermissionsChanged={vi.fn()} /></MemoryRouter>);

    // Act / Assert
    expect(await screen.findByText('A platform catalog administrator must refresh the source.')).toBeVisible();
    expect(screen.queryByRole('button', { name: /Load source for/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Import/refresh framework definitions' })).not.toBeInTheDocument();
  });

  it('captures source only and reloads authoritative status without binding a system', async () => {
    // Arrange
    vi.mocked(api.getCatalogSourceManagement).mockResolvedValueOnce(status).mockResolvedValue({
      ...status, sources: [{ ...status.sources[0]!, sourceAvailable: true, sourceVersion: 'actual-1',
        sourceUri: 'https://example.invalid/catalog', capturedAt: '2026-01-01' }],
    });
    render(<MemoryRouter><CatalogSourceManagement onImportRequested={vi.fn()} onPermissionsChanged={vi.fn()} /></MemoryRouter>);
    const load = await screen.findByRole('button', { name: 'Load source for NIST Rev. 5' });

    // Act
    await act(async () => { fireEvent.click(load); });

    // Assert
    expect(api.captureCatalogSource).toHaveBeenCalledWith('NIST-800-53-R5');
    expect(screen.getByText(/actual-1/)).toBeVisible();
    expect(screen.getByText(/Control selections and reviewed content were not changed/)).toBeVisible();
  });
});
