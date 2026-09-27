import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import Documents from '../../pages/Documents';
import { getSystemDocuments, type SystemDocumentsResponse } from '../../api/documents';
import { listExports } from '../../api/exports';
import { listPackages, validatePackage } from '../../api/package';

vi.mock('../../api/documents', () => ({ getSystemDocuments: vi.fn() }));
vi.mock('../../api/exports', () => ({
  listExports: vi.fn(), downloadExportUrl: vi.fn(),
  oscalPoamUrl: () => '/poam', oscalAssessmentResultsUrl: () => '/results', oscalSapUrl: () => '/sap',
}));
vi.mock('../../api/package', () => ({ listPackages: vi.fn(), downloadPackageUrl: vi.fn(), validatePackage: vi.fn() }));
vi.mock('../../components/ExportSspDialog', () => ({ default: ({ systemId }: { systemId: string }) => <div role="dialog">SSP export for {systemId}</div> }));
vi.mock('../../components/TemplateManagementDialog', () => ({ default: () => <div role="dialog">Template management</div> }));
vi.mock('../../components/PackageGenerationDialog', () => ({ default: ({ systemId }: { systemId: string }) => <div role="dialog">Package generation for {systemId}</div> }));

const catalog: SystemDocumentsResponse = {
  systemId: 'a', systemName: 'Mission Alpha', currentPhase: 'Prepare',
  ssp: { totalNarratives: 3, completedNarratives: 1, narrativeCompletionPct: 33 },
  sap: null, sar: null, authorization: null, poamCount: 0, poamOverdueCount: 0,
  hasBaseline: true, baselineControlCount: 3, pta: null, pia: null, interconnections: [],
  conMon: null, sspSections: [], activeWaiverCount: 0, narrativeGovernance: null, importHistory: [], inventoryItemCount: 0,
};

function mount(url: string) {
  render(<MemoryRouter initialEntries={[url]}><Routes><Route path="/systems/:id/documents" element={<Documents />} /></Routes></MemoryRouter>);
}

describe('System document tasks', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(getSystemDocuments).mockResolvedValue(catalog);
    vi.mocked(listExports).mockResolvedValue({ items: [], totalCount: 0 });
    vi.mocked(listPackages).mockResolvedValue({ items: [], totalCount: 0, limit: 10, offset: 0 });
  });

  it('opens existing package generation for the selected system on the exports task', async () => {
    // Arrange
    mount('/systems/a/documents?tab=exports');
    // Act
    await screen.findByRole('heading', { name: 'Generate & export a package' });
    fireEvent.click(screen.getByRole('button', { name: 'Generate Package' }));
    // Assert
    expect(screen.getByRole('dialog')).toHaveTextContent('Package generation for a');
    expect(screen.queryByRole('heading', { name: 'Authorization Package' })).not.toBeInTheDocument();
    expect(listExports).toHaveBeenCalledWith('a', { limit: 10 });
  });

  it('keeps catalog status distinct from an evaluated submission-readiness verdict', async () => {
    // Arrange / Act
    mount('/systems/a/documents');
    // Assert
    expect(await screen.findByText(/does not determine submission readiness/)).toBeVisible();
    expect(screen.getByRole('link', { name: 'Review responsibilities' })).toHaveAttribute('href', '/systems/a/inheritance/subscriptions');
    expect(screen.getByRole('link', { name: 'Generate & export a package' })).toHaveAttribute('href', '/systems/a/documents?tab=exports');
    expect(screen.queryByRole('button', { name: 'Generate Package' })).not.toBeInTheDocument();
  });

  it('shows failed export history as an error and supports retry, not an empty-success state', async () => {
    // Arrange
    vi.mocked(listExports).mockRejectedValueOnce(new Error('Export history unavailable'));
    mount('/systems/a/documents?tab=exports');
    // Act
    expect(await screen.findByRole('alert')).toHaveTextContent('Export history unavailable');
    fireEvent.click(screen.getByRole('button', { name: 'Retry export history' }));
    // Assert
    expect(await screen.findByText('No SSP exports generated yet.')).toBeVisible();
  });

  it('renders the server validation findings without deriving a new readiness score', async () => {
    // Arrange
    vi.mocked(validatePackage).mockResolvedValue({
      isValid: false, errorCount: 1, warningCount: 0, validatedAt: '2026-09-26T12:00:00Z',
      findings: [{ severity: 'Error', category: 'Sources', artifactType: 'SSP', description: 'Reviewed mission source is missing.', remediation: 'Review mission purpose.' }],
    });
    mount('/systems/a/documents');
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Validate current package' }));
    // Assert
    expect(await screen.findByText('Reviewed mission source is missing.')).toBeVisible();
    expect(screen.getByText('Review mission purpose.')).toBeVisible();
    expect(screen.getByText('Package validation found blockers')).toBeVisible();
    expect(validatePackage).toHaveBeenCalledWith('a', expect.any(AbortSignal));
  });

  it('does not present failed package-history loading as no generated packages', async () => {
    // Arrange
    vi.mocked(listPackages).mockRejectedValue(new Error('Package history denied'));
    mount('/systems/a/documents?tab=exports');
    // Act / Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Package history denied');
    expect(screen.queryByText('No authorization packages generated yet.')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Retry package history' })).toBeVisible();
  });

  it('requires an explicit initial-submission choice and sends it to server validation', async () => {
    // Arrange
    vi.mocked(validatePackage).mockResolvedValue({
      isValid: false, errorCount: 1, warningCount: 0, validatedAt: '2026-09-26T12:00:00Z',
      findings: [{ severity: 'error', category: 'ssp', artifactType: 'SSP', description: 'Approved SSP required.', remediation: null }],
    });
    mount('/systems/a/documents');
    await screen.findByRole('button', { name: 'Validate current package' });
    // Act
    fireEvent.change(screen.getByRole('combobox', { name: 'Package purpose' }), { target: { value: 'InitialSubmission' } });
    fireEvent.click(screen.getByRole('button', { name: 'Validate current package' }));
    // Assert
    expect(await screen.findByText('Approved SSP required.')).toBeVisible();
    expect(validatePackage).toHaveBeenCalledWith('a', expect.any(AbortSignal), 'InitialSubmission');
    expect(screen.getByText(/No prior authorization decision is required/)).toBeVisible();
  });
});
