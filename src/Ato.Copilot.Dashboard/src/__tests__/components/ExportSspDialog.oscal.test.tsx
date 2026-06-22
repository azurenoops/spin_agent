/**
 * Issue #419 — Unit tests for ExportSspDialog OSCAL section upgrades
 * Covers: OSCAL SSP card rendering, schema version badge, removal of 'OSCAL JSON' from picker
 */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import ExportSspDialog from '../../components/ExportSspDialog';

// --- Mocks -----------------------------------------------------------------

vi.mock('../../../api/exports', () => ({
  requestExport: vi.fn(),
  downloadExportUrl: vi.fn(() => '/download/test'),
  listTemplates: vi.fn(() => Promise.resolve({ items: [] })),
}));

vi.mock('../../../api/packages', () => ({
  enqueuePackage: vi.fn(),
  getPackageStatus: vi.fn(),
  getPackageDownloadUrl: vi.fn(() => '/pkg/download'),
}));

vi.mock('../../../api/client', () => ({
  default: {
    get: vi.fn(() => Promise.resolve({ data: new Blob() })),
  },
}));

vi.mock('@microsoft/signalr', () => ({
  HubConnectionBuilder: vi.fn(() => ({
    withUrl: vi.fn().mockReturnThis(),
    withAutomaticReconnect: vi.fn().mockReturnThis(),
    build: vi.fn(() => ({
      on: vi.fn(),
      start: vi.fn(() => Promise.resolve()),
      invoke: vi.fn(() => Promise.resolve()),
      stop: vi.fn(() => Promise.resolve()),
    })),
  })),
}));

vi.mock('../../../features/auth/msalInstance', () => ({
  acquireBearer: vi.fn(() => Promise.resolve('mock-bearer-token')),
  getMsalInstance: vi.fn(() => null),
  DEFAULT_API_SCOPES: [],
}));

// ---------------------------------------------------------------------------

const defaultProps = {
  systemId: 'sys-export-test',
  onClose: vi.fn(),
};

describe('ExportSspDialog — OSCAL section upgrades (#419)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders the "OSCAL Documents" section header', () => {
    render(<ExportSspDialog {...defaultProps} />);
    expect(screen.getByText('OSCAL Documents')).toBeInTheDocument();
  });

  it('renders "OSCAL SSP" as the first-class export card label', () => {
    render(<ExportSspDialog {...defaultProps} />);
    expect(screen.getByText('OSCAL SSP')).toBeInTheDocument();
  });

  it('shows at least one "OSCAL 1.1.2" schema version badge', () => {
    render(<ExportSspDialog {...defaultProps} />);
    const badges = screen.getAllByText('OSCAL 1.1.2');
    expect(badges.length).toBeGreaterThanOrEqual(1);
  });

  it('does NOT render "OSCAL JSON (.json)" in the format picker', () => {
    render(<ExportSspDialog {...defaultProps} />);
    expect(screen.queryByText('OSCAL JSON (.json)')).not.toBeInTheDocument();
  });

  it('renders supplemental OSCAL artifacts: POA&M, Assessment Results, SAP', () => {
    render(<ExportSspDialog {...defaultProps} />);
    expect(screen.getByText('OSCAL POA&M')).toBeInTheDocument();
    expect(screen.getByText('OSCAL Assessment Results')).toBeInTheDocument();
    expect(screen.getByText('OSCAL SAP')).toBeInTheDocument();
  });

  it('renders a Download button for the OSCAL SSP card', () => {
    render(<ExportSspDialog {...defaultProps} />);
    // Should be inside the OSCAL SSP card
    const downloadBtns = screen.getAllByText('Download');
    expect(downloadBtns.length).toBeGreaterThanOrEqual(1);
  });

  it('still renders DOCX and PDF in the format picker', () => {
    render(<ExportSspDialog {...defaultProps} />);
    expect(screen.getByText('Word (.docx)')).toBeInTheDocument();
    expect(screen.getByText('PDF (.pdf)')).toBeInTheDocument();
  });
});
