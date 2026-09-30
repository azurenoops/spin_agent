import { act, cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useNavigate } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import EvidenceRepository from '../../pages/EvidenceRepository';
import * as api from '../../api/evidenceCatalog';

vi.mock('../../api/evidenceCatalog', async importOriginal => ({
  ...await importOriginal<typeof api>(),
  getEvidenceCatalog: vi.fn(), getEvidenceCatalogDetail: vi.fn(), linkCatalogEvidence: vi.fn(),
}));
vi.mock('../../components/EvidenceUploadDialog', () => ({ default: () => <div role="dialog">Upload form</div> }));
vi.mock('../../components/AuthenticatedDownload', () => ({
  default: ({ children }: { children: React.ReactNode }) => <button>{children}</button>,
}));
vi.mock('../../api/evidence', () => ({ replaceEvidence: vi.fn(), deleteEvidence: vi.fn(), collectEvidence: vi.fn() }));

const item = {
  id: 'artifact:record-a', recordId: 'record-a', source: 'Manual' as const, name: 'Access review report',
  sourceLabel: 'System upload', recordedAt: '2026-09-25T10:00:00Z', category: 'Other',
  controls: [{ controlId: 'AC-2', title: 'Account Management', kind: 'direct' }], linksKnown: true,
};
const provider = {
  ...item, id: 'provider:share-a', recordId: 'share-a', source: 'Provider' as const,
  name: 'Shared access summary', sourceLabel: 'Provider shared', controls: [], linksKnown: false,
};
const result = {
  systemId: 'system-a', items: [item, provider], totalCount: 2, availableCount: 2, page: 1, pageSize: 25,
  counts: { all: 2, system: 1, provider: 1, missingLinks: 0 },
  sources: [{ source: 'system' as const, state: 'available' as const, message: null },
    { source: 'provider' as const, state: 'available' as const, message: null }],
  permissions: { canUpload: false, uploadReason: 'Your assignments do not authorize evidence management.' },
};
const detail = {
  systemId: 'system-a', item, description: 'Retained access review', owner: null,
  recordedBy: 'System team', version: '1', contentHash: 'retained-hash', contentType: 'application/pdf',
  fileSizeBytes: 1200, availability: 'FileAvailable' as const, availabilityReason: null, summary: null,
  review: null, currency: null, relevance: null, provenance: [{ label: 'Collection method', value: 'Manual' }],
  history: [],
  permissions: { canDownload: true, downloadUrl: '/api/dashboard/systems/system-a/evidence/record-a/download',
    canReplace: false, canDelete: false, canCollect: false, canLink: false,
    manageReason: 'Evidence management is not authorized.', linkReason: 'Control linking is not authorized.' },
};
function mount(query = '') {
  return render(<MemoryRouter initialEntries={[`/systems/system-a/evidence${query}`]}>
    <Routes><Route path="/systems/:id/evidence" element={<EvidenceRepository />} /></Routes>
  </MemoryRouter>);
}
beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(api.getEvidenceCatalog).mockResolvedValue(result);
  vi.mocked(api.getEvidenceCatalogDetail).mockResolvedValue(detail);
});
afterEach(cleanup);

describe('Evidence catalog', () => {
  it('combines sources with accurate server counts and the mock headings', async () => {
    // Arrange / Act
    mount();
    // Assert
    expect(await screen.findByRole('button', { name: 'View evidence Access review report' })).toBeVisible();
    expect(screen.getByRole('heading', { name: 'Evidence' })).toBeVisible();
    expect(screen.getByText('Find supporting records and see what still needs attention.')).toBeVisible();
    expect(screen.getByRole('tab', { name: 'All evidence (2)' })).toBeVisible();
    expect(screen.getByRole('tab', { name: 'Provider shared (1)' })).toBeVisible();
    expect(screen.getByText('Shared access summary')).toBeVisible();
    expect(screen.getByRole('button', { name: 'Upload evidence' })).toBeDisabled();
    expect(screen.queryByText(/record needs a control link/)).not.toBeInTheDocument();
  });

  it('renders provider empty state only in the selected empty tab, without pagination', async () => {
    // Arrange
    vi.mocked(api.getEvidenceCatalog).mockResolvedValue({ ...result, items: [], totalCount: 0,
      availableCount: 0, counts: { ...result.counts, provider: 0 } });
    // Act
    mount('?view=provider');
    // Assert
    expect(await screen.findByText('No provider evidence shared yet.')).toBeVisible();
    expect(screen.getByText('Only records explicitly shared with this system appear here.')).toBeVisible();
    expect(screen.getByRole('button', { name: 'Refresh access' })).toBeVisible();
    expect(screen.getByRole('button', { name: 'Sharing guidance' })).toBeVisible();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
    expect(screen.queryByRole('navigation', { name: 'Evidence pages' })).not.toBeInTheDocument();
  });

  it('shows partial availability and unknown totals instead of invented zero counts', async () => {
    // Arrange
    vi.mocked(api.getEvidenceCatalog).mockResolvedValue({ ...result, items: [item], totalCount: null,
      availableCount: 1, counts: { ...result.counts, all: null, provider: null },
      sources: [result.sources[0]!, { source: 'provider', state: 'unavailable', message: 'Provider service unavailable.' }] });
    // Act
    mount();
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Provider service unavailable.');
    expect(screen.getByRole('tab', { name: 'Provider shared (unavailable)' })).toBeVisible();
    expect(screen.getByText('1 available record · partial results')).toBeVisible();
    expect(screen.queryByText('No provider evidence shared yet.')).not.toBeInTheDocument();
  });

  it('distinguishes request failure and retries', async () => {
    // Arrange
    vi.mocked(api.getEvidenceCatalog).mockRejectedValueOnce(new Error('Evidence access denied.'));
    // Act
    mount();
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Evidence access denied.');
    expect(screen.queryByText('No evidence yet.')).not.toBeInTheDocument();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Retry evidence' }));
    // Assert
    expect(await screen.findByText('Access review report')).toBeVisible();
  });

  it('preserves search and drawer direct links, independent tabs, focus and Escape close', async () => {
    // Arrange
    mount('?search=access');
    const open = await screen.findByRole('button', { name: 'View evidence Access review report' });
    // Act
    open.focus();
    fireEvent.click(open);
    // Assert
    const drawer = await screen.findByRole('dialog', { name: 'Evidence details' });
    expect(within(drawer).getByText('Owner not recorded')).toBeVisible();
    expect(within(drawer).getByText('Relevance not reviewed')).toBeVisible();
    expect(within(drawer).queryByRole('button', { name: 'Delete evidence' })).not.toBeInTheDocument();
    // Act
    fireEvent.click(within(drawer).getByRole('tab', { name: 'Linked controls' }));
    // Assert
    expect(within(drawer).getByText('Account Management')).toBeVisible();
    expect(within(drawer).getByText('Control linking is not authorized.')).toBeVisible();
    // Act
    fireEvent.keyDown(document, { key: 'Escape' });
    // Assert
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(screen.getByRole('textbox', { name: 'Search evidence or control' })).toHaveValue('access');
    expect(open).toHaveFocus();
  });

  it('loads a summary-only provider deep link without offering a private file', async () => {
    // Arrange
    vi.mocked(api.getEvidenceCatalogDetail).mockResolvedValue({ ...detail, item: provider,
      availability: 'SummaryOnly', summary: 'Only the explicitly approved text',
      permissions: { ...detail.permissions, downloadUrl: '/api/dashboard/systems/system-a/provider-evidence/share-a/content' } });
    // Act
    mount('?evidence=provider%3Ashare-a');
    // Assert
    const drawer = await screen.findByRole('dialog', { name: 'Evidence details' });
    expect(await within(drawer).findByText('Summary only')).toBeVisible();
    expect(within(drawer).getByText('Only the explicitly approved text')).toBeVisible();
    expect(within(drawer).getByRole('button', { name: /Download approved summary/ })).toBeVisible();
    expect(within(drawer).queryByRole('button', { name: /Open file/ })).not.toBeInTheDocument();
  });

  it('does not retain protected detail after an access failure', async () => {
    // Arrange
    vi.mocked(api.getEvidenceCatalogDetail).mockRejectedValue(new Error('Sharing is no longer available.'));
    // Act
    mount('?evidence=provider%3Ashare-a');
    // Assert
    expect(await screen.findByText('Sharing is no longer available.')).toBeVisible();
    expect(screen.queryByText('Retained access review')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Download approved summary/ })).not.toBeInTheDocument();
  });

  it('does not call an out-of-range page an empty repository', async () => {
    // Arrange
    vi.mocked(api.getEvidenceCatalog).mockResolvedValue({ ...result, items: [], page: 4 });
    // Act
    mount('?page=4');
    // Assert
    expect(await screen.findByText('No records on this page.')).toBeVisible();
    expect(screen.getByRole('button', { name: 'Return to first page' })).toBeVisible();
    expect(screen.queryByText('No evidence yet.')).not.toBeInTheDocument();
  });

  it('passes all filters to the server and resets pagination', async () => {
    // Arrange
    mount('?page=3&family=AC&category=Other&source=Manual');
    await screen.findByText('Access review report');
    // Act
    fireEvent.change(screen.getByRole('textbox', { name: 'Search evidence or control' }), { target: { value: 'review' } });
    // Assert
    await act(async () => {});
    expect(api.getEvidenceCatalog).toHaveBeenLastCalledWith('system-a',
      expect.objectContaining({ search: 'review', family: 'AC', category: 'Other', source: 'Manual', page: 1 }),
      expect.any(AbortSignal));
  });

  it('uses the existing record hash when linking and surfaces stale conflicts', async () => {
    // Arrange
    vi.mocked(api.getEvidenceCatalogDetail).mockResolvedValue({ ...detail,
      permissions: { ...detail.permissions, canLink: true } });
    vi.mocked(api.linkCatalogEvidence).mockRejectedValue(new Error('Evidence changed. Refresh before linking.'));
    mount('?evidence=artifact%3Arecord-a&detailTab=controls');
    // Act
    fireEvent.change(await screen.findByRole('textbox', { name: 'Control ID' }), { target: { value: 'ac-3' } });
    fireEvent.click(screen.getByRole('button', { name: 'Link control' }));
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Evidence changed. Refresh before linking.');
    expect(api.linkCatalogEvidence).toHaveBeenCalledWith('system-a', 'artifact:record-a', 'AC-3', 'retained-hash');
    expect(screen.getByText('Account Management')).toBeVisible();
  });

  it('cancels old detail reads and never displays their results after switching systems', async () => {
    // Arrange
    let finish!: (value: typeof detail) => void;
    vi.mocked(api.getEvidenceCatalogDetail).mockImplementationOnce(() => new Promise(resolve => { finish = resolve; }));
    function Switch() {
      const navigate = useNavigate();
      return <><button onClick={() => navigate('/systems/system-b/evidence')}>Switch system</button><EvidenceRepository /></>;
    }
    render(<MemoryRouter initialEntries={['/systems/system-a/evidence?evidence=artifact%3Arecord-a']}>
      <Routes><Route path="/systems/:id/evidence" element={<Switch />} /></Routes>
    </MemoryRouter>);
    await screen.findByText('Loading evidence details…');
    const signal = vi.mocked(api.getEvidenceCatalogDetail).mock.calls[0]![2]!;
    vi.mocked(api.getEvidenceCatalog).mockResolvedValue({ ...result, systemId: 'system-b', items: [] });
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Switch system' }));
    await act(async () => finish(detail));
    // Assert
    expect(signal.aborted).toBe(true);
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(screen.queryByText('Retained access review')).not.toBeInTheDocument();
  });
});
