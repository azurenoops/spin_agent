import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import SystemDocumentPreview from '../../features/systems/SystemDocumentPreview';
import * as api from '../../api/exports';

vi.mock('../../api/exports', () => ({ getSspPreview: vi.fn(), retainSspPreview: vi.fn() }));
vi.mock('../../components/ExportSspDialog', () => ({
  default: ({ sourcePreviewId }: { sourcePreviewId: string }) => <div role="dialog">Export retained {sourcePreviewId}</div>,
}));
const preview = {
  systemId: 'system-a', format: 'json' as const, contentType: 'application/json' as const,
  content: JSON.stringify({ 'system-security-plan': {
    metadata: { title: 'Generated DEMO SSP' },
    'system-characteristics': { 'system-name': 'DEMO Mission', description: 'Exact generated mission description' },
  } }),
  contentHash: 'retained-content-hash', generatedAt: '2026-09-26T12:00:00Z',
  sourceGaps: [{ code: 'PROVIDER_SOURCE_MISSING', message: 'Reviewed issuer metadata is missing.' }],
  isPreview: true as const, sourceState: 'CurrentWorkingData' as const,
};
function mount() {
  render(<MemoryRouter initialEntries={['/systems/system-a/documents/preview']}>
    <Routes><Route path="/systems/:id/documents/preview" element={<SystemDocumentPreview />} /></Routes>
  </MemoryRouter>);
}
beforeEach(() => { vi.clearAllMocks(); vi.mocked(api.getSspPreview).mockResolvedValue(preview); });

describe('generated document preview', () => {
  it('renders actual generated fields and source gaps without claiming approved readiness', async () => {
    // Arrange / Act
    mount();
    // Assert
    expect(await screen.findByRole('heading', { name: 'Generated DEMO SSP' })).toBeVisible();
    expect(screen.getByText('Exact generated mission description')).toBeVisible();
    expect(screen.getByText('Reviewed issuer metadata is missing.')).toBeVisible();
    expect(screen.getByText(/Current working data preview/)).toBeVisible();
    expect(screen.getByRole('link', { name: 'Review source mapping' })).toHaveAttribute('href', '/systems/system-a/profile/EnvironmentAndDeployment');
    expect(screen.getByText('retained-content-hash')).toBeVisible();
  });

  it('exposes the original generated OSCAL rather than assembling a browser export', async () => {
    // Arrange
    mount();
    await screen.findByRole('heading', { name: 'Generated DEMO SSP' });
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'OSCAL source' }));
    // Assert
    expect(screen.getByLabelText('Generated OSCAL JSON').textContent).toBe(preview.content);
  });

  it('shows unavailable with retry and never invents document content', async () => {
    // Arrange
    vi.mocked(api.getSspPreview).mockRejectedValueOnce(new Error('Preview access denied'));
    mount();
    expect(await screen.findByRole('alert')).toHaveTextContent('Preview access denied');
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
    // Assert
    expect(await screen.findByRole('heading', { name: 'Generated DEMO SSP' })).toBeVisible();
  });

  it('rejects content that does not contain a generated SSP', async () => {
    // Arrange
    vi.mocked(api.getSspPreview).mockResolvedValue({ ...preview, content: '{}' });
    // Act
    mount();
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('does not contain an OSCAL system security plan');
  });

  it('requires inspection and confirmation of the returned retained snapshot before export', async () => {
    // Arrange
    vi.mocked(api.retainSspPreview).mockResolvedValue({ ...preview, previewId: 'preview-a', sourceGaps: [] });
    mount();
    await screen.findByRole('heading', { name: 'Generated DEMO SSP' });
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Retain generated preview' }));
    // Assert
    const exportButton = await screen.findByRole('button', { name: 'Export retained OSCAL' });
    expect(exportButton).toBeDisabled();
    expect(api.retainSspPreview).toHaveBeenCalledWith('system-a', expect.any(String), expect.any(AbortSignal));
    // Act
    fireEvent.click(screen.getByRole('checkbox', { name: 'I reviewed this exact retained preview and its source diagnostics.' }));
    fireEvent.click(exportButton);
    // Assert
    expect(screen.getByRole('dialog')).toHaveTextContent('Export retained preview-a');
  });
});
