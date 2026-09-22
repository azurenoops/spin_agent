/**
 * Issue #419 — Unit tests for ExportSspDialog OSCAL section upgrades (contract-aligned)
 *
 * Contract: specs/077-enhanced-evidence-automation/oscal-api-contract.md
 *
 * Covers:
 *   - "OSCAL Documents" section header rendered
 *   - "OSCAL SSP" first-class card label
 *   - "OSCAL 1.1.2" schema version badge present
 *   - 'OSCAL JSON (.json)' absent from format picker (removed in #419)
 *   - Supplemental artifacts (POA&M, Assessment Results, SAP) rendered
 *   - Download button present in OSCAL SSP card
 *   - DOCX and PDF still in format picker
 *   - ValidationBadge uses isValid/errorCount/warningCount (not legacy valid= prop)
 */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { act, render, screen } from '@testing-library/react';
import ExportSspDialog from '../../components/ExportSspDialog';
import { workspaceSession } from '../helpers/domainPermissions';

// --- Mocks ------------------------------------------------------------------

vi.mock('../../api/exports', () => ({
  requestExport: vi.fn(),
  getExport: vi.fn(),
  downloadExportUrl: vi.fn(() => '/download/test'),
  listTemplates: vi.fn(() => Promise.resolve({ items: [], totalCount: 0 })),
}));
vi.mock('../../features/auth/useMe', () => ({
  useMe: () => ({ data: workspaceSession('sys-export-test').identity, isLoading: false, error: null, refetch: vi.fn() }),
}));

vi.mock('../../api/packages', () => ({
  enqueuePackage: vi.fn(),
  getPackageStatus: vi.fn(),
  getPackageDownloadUrl: vi.fn(() => '/pkg/download'),
}));

vi.mock('../../api/client', () => ({
  default: {
    get: vi.fn(() => Promise.resolve({ data: new Blob() })),
    post: vi.fn(() => Promise.resolve({ data: {} })),
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

vi.mock('../../features/auth/msalInstance', () => ({
  acquireBearer: vi.fn(() => Promise.resolve('mock-bearer-token')),
  getMsalInstance: vi.fn(() => null),
  DEFAULT_API_SCOPES: [],
}));

// ---------------------------------------------------------------------------

const defaultProps = {
  systemId: 'sys-export-test',
  onClose: vi.fn(),
};
async function openDialog() {
  await act(async () => { render(<ExportSspDialog {...defaultProps} />); });
}

describe('ExportSspDialog — OSCAL section upgrades (#419)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders the "OSCAL Documents" section header', async () => {
    await openDialog();
    expect(screen.getByText('OSCAL Documents')).toBeInTheDocument();
  });

  it('renders "OSCAL SSP" as the first-class export card label', async () => {
    await openDialog();
    expect(screen.getByText('OSCAL SSP')).toBeInTheDocument();
  });

  it('shows at least one "OSCAL 1.1.2" schema version badge', async () => {
    await openDialog();
    const badges = screen.getAllByText('OSCAL 1.1.2');
    expect(badges.length).toBeGreaterThanOrEqual(1);
  });

  it('does NOT render "OSCAL JSON (.json)" in the format picker (removed in #419)', async () => {
    await openDialog();
    expect(screen.queryByText('OSCAL JSON (.json)')).not.toBeInTheDocument();
  });

  it('renders supplemental OSCAL artifacts: POA&M, Assessment Results, SAP', async () => {
    await openDialog();
    expect(screen.getByText('OSCAL POA&M')).toBeInTheDocument();
    expect(screen.getByText('OSCAL Assessment Results')).toBeInTheDocument();
    expect(screen.getByText('OSCAL SAP')).toBeInTheDocument();
  });

  it('renders a Download button for the OSCAL SSP card', async () => {
    await openDialog();
    const downloadBtns = screen.getAllByText('Download');
    expect(downloadBtns.length).toBeGreaterThanOrEqual(1);
  });

  it('still renders DOCX and PDF in the format picker', async () => {
    await openDialog();
    expect(screen.getByText('Word (.docx)')).toBeInTheDocument();
    expect(screen.getByText('PDF (.pdf)')).toBeInTheDocument();
  });

  it('ValidationBadge is not rendered with hardcoded valid=true (no static ✓ Valid badge on load)', async () => {
    await openDialog();
    // Before any API call completes, the live badge is absent or shows skeleton
    // The old hardcoded "✓ Valid OSCAL 1.1.2" badge (from valid=true) must not be present
    // (it only appears after the GET /exports/oscal-ssp response arrives)
    const staticValidBadge = screen.queryByText('✓ Valid OSCAL 1.1.2');
    expect(staticValidBadge).not.toBeInTheDocument();
  });
});
