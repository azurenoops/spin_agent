import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation, useNavigate } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import SystemDocumentPreview from '../../features/systems/SystemDocumentPreview';
import * as api from '../../api/exports';

vi.mock('../../api/exports', () => ({ getSspPreview: vi.fn(), getAdditionalDocumentPreview: vi.fn(), retainSspPreview: vi.fn() }));
vi.mock('../../components/ExportSspDialog', () => ({ default: () => null }));
const base = { systemId: 'system-a', systemName: 'Mission', format: 'json' as const, contentType: 'application/json' as const,
  contentHash: 'hash', generatedAt: '2026-09-27T12:00:00Z', sourceGaps: [], isPreview: true as const,
  sourceState: 'CurrentWorkingData' as const, canGenerate: false };
function Location() {
  const location = useLocation(), navigate = useNavigate();
  return <><output aria-label="Current document URL">{location.pathname}{location.search}</output><button onClick={() => navigate(-1)}>Browser back</button></>;
}
function mount(query = '') {
  render(<MemoryRouter initialEntries={[`/systems/system-a/documents/preview${query}`]}>
    <Location /><Routes><Route path="/systems/:id/documents/preview" element={<SystemDocumentPreview />} /></Routes>
  </MemoryRouter>);
}
beforeEach(() => {
  vi.resetAllMocks();
  vi.mocked(api.getSspPreview).mockResolvedValue({ ...base, content: JSON.stringify({ 'system-security-plan': {
    metadata: { title: 'Actual SSP' }, 'system-characteristics': { 'system-name': 'Mission' },
  } }) });
});
describe('document preview choices', () => {
  it.each([
    ['sap', 'SAP', 'security-assessment-plan', 'Actual SAP'],
    ['sar', 'SAR', 'security-assessment-report', 'Actual SAR'],
    ['poam', 'POA&M', 'poam-register', 'Actual POAM'],
  ] as const)('renders the complete %s model without SSP retention controls', async (documentType, label, model, title) => {
    // Arrange
    vi.mocked(api.getAdditionalDocumentPreview).mockResolvedValue({ ...base, available: true, documentType,
      documentStatus: 'Draft', sourceRecords: [{ kind: documentType, recordId: 'document-id', versionId: null, contentHash: 'source-hash' }],
      content: JSON.stringify({ [model]: { id: 'document-id', title, status: 'Draft', detail: `${label} authoritative content` } }) });
    // Act
    mount(`?document=${documentType}`);
    // Assert
    expect(await screen.findByRole('heading', { name: title })).toBeVisible();
    expect(screen.getByRole('tab', { name: label })).toHaveAttribute('aria-selected', 'true');
    expect(screen.getByRole('tablist', { name: 'Document types' })).toBeVisible();
    expect(screen.getByText(`${label} authoritative content`)).toBeVisible();
    expect(screen.getByRole('button', { name: 'JSON source' })).toBeVisible();
    expect(screen.getByRole('region', { name: `${label} cover page` })).toHaveTextContent('READ-ONLY PREVIEW');
    expect(screen.queryByRole('button', { name: 'Retain generated preview' })).not.toBeInTheDocument();
    expect(api.getSspPreview).not.toHaveBeenCalled();
    expect(api.getAdditionalDocumentPreview).toHaveBeenCalledWith('system-a', documentType, expect.any(AbortSignal));
  });
  it('uses URL selection and restores SSP on browser back', async () => {
    // Arrange
    vi.mocked(api.getAdditionalDocumentPreview).mockResolvedValue({ ...base, available: true, documentType: 'sap',
      content: '{"security-assessment-plan":{"title":"Actual SAP"}}' });
    mount('?contribution=UsersAndAccess');
    await screen.findByRole('heading', { name: 'Actual SSP' });
    // Act
    fireEvent.click(screen.getByRole('tab', { name: 'SAP' }));
    // Assert
    expect(await screen.findByRole('heading', { name: 'Actual SAP' })).toBeVisible();
    expect(screen.getByLabelText('Current document URL')).toHaveTextContent('?document=sap');
    expect(screen.getByLabelText('Current document URL')).not.toHaveTextContent('contribution');
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Browser back' }));
    // Assert
    await waitFor(() => expect(screen.getByRole('tab', { name: 'SSP' })).toHaveAttribute('aria-selected', 'true'));
    expect(await screen.findByRole('heading', { name: 'Actual SSP' })).toBeVisible();
  });
  it('preserves focus on the document tab when switching panels', async () => {
    // Arrange
    vi.mocked(api.getAdditionalDocumentPreview).mockResolvedValue({ ...base, available: true, documentType: 'sap',
      content: '{"security-assessment-plan":{"title":"Actual SAP"}}' });
    mount();
    await screen.findByRole('heading', { name: 'Actual SSP' });
    const tab = screen.getByRole('tab', { name: 'SAP' });
    // Act
    tab.focus();
    fireEvent.click(tab);
    // Assert
    await screen.findByRole('heading', { name: 'Actual SAP' });
    expect(screen.getByRole('tab', { name: 'SAP' })).toBe(tab);
    expect(tab).toHaveFocus();
  });
  it('ignores a late SAP response after the user switches to SAR', async () => {
    // Arrange
    let resolveSap!: (value: api.AdditionalDocumentPreview) => void;
    vi.mocked(api.getAdditionalDocumentPreview).mockImplementation((_systemId, type) => type === 'sap'
      ? new Promise(resolve => { resolveSap = resolve; })
      : Promise.resolve({ ...base, available: true, documentType: 'sar', content: '{"security-assessment-report":{"title":"Current SAR"}}' }));
    mount();
    await screen.findByRole('heading', { name: 'Actual SSP' });
    // Act
    fireEvent.click(screen.getByRole('tab', { name: 'SAP' }));
    fireEvent.click(screen.getByRole('tab', { name: 'SAR' }));
    await screen.findByRole('heading', { name: 'Current SAR' });
    await act(async () => resolveSap({ ...base, available: true, documentType: 'sap', content: '{"security-assessment-plan":{"title":"Late SAP"}}' }));
    // Assert
    expect(screen.getByRole('heading', { name: 'Current SAR' })).toBeVisible();
    expect(screen.queryByRole('heading', { name: 'Late SAP' })).not.toBeInTheDocument();
  });
  it('shows missing SAR explicitly and links to assessment work without creating it', async () => {
    // Arrange
    vi.mocked(api.getAdditionalDocumentPreview).mockResolvedValue({ systemId: 'system-a', systemName: 'Mission',
      documentType: 'sar', available: false, reasonCode: 'SAR_NOT_CREATED', message: 'No saved SAR exists.' });
    // Act
    mount('?document=sar');
    // Assert
    expect(await screen.findByText('No saved SAR exists.')).toBeVisible();
    expect(screen.getByRole('link', { name: 'Review assessment work' })).toHaveAttribute('href', '/systems/system-a/assessments');
    expect(screen.queryByRole('region', { name: 'SAR cover page' })).not.toBeInTheDocument();
    expect(api.retainSspPreview).not.toHaveBeenCalled();
  });
  it('rejects the wrong document model instead of relabeling an SSP as an SAP', async () => {
    // Arrange
    vi.mocked(api.getAdditionalDocumentPreview).mockResolvedValue({ ...base, available: true, documentType: 'sap',
      content: '{"system-security-plan":{"metadata":{"title":"Wrong document"}}}' });
    // Act
    mount('?document=sap');
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('assessment-plan');
    expect(screen.queryByRole('heading', { name: 'Wrong document' })).not.toBeInTheDocument();
  });
});
