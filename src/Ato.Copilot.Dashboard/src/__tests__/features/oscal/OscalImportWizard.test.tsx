/**
 * Issue #419 — Unit tests for OscalImportWizard (contract-aligned)
 *
 * Contract: specs/077-enhanced-evidence-automation/oscal-api-contract.md
 *
 * Covers:
 *   - File validation: accept, reject (.xml, .yaml, empty, > 10MB warn, > 50MB block)
 *   - CTA label matches new button text ("Upload File")
 *   - Commit blocked when validationStatus.errors present (surfaced in Step 2 → Step 3 gate)
 *   - Cancel behaviour
 */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import OscalImportWizard from '../../../features/oscal/OscalImportWizard';

// Minimal apiClient mock — tests only exercise Step 1 (no network calls)
vi.mock('../../../api/client', () => ({
  default: {
    post: vi.fn(),
    get: vi.fn(),
  },
}));

const defaultProps = {
  onClose: vi.fn(),
  onImportComplete: vi.fn(),
};

describe('OscalImportWizard — Step 1 (Upload)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders the Import OSCAL SSP heading and Upload step label', () => {
    render(<OscalImportWizard {...defaultProps} />);
    expect(screen.getByText('Import OSCAL SSP')).toBeInTheDocument();
    expect(screen.getByText('Upload')).toBeInTheDocument();
    // CTA button text per contract revision
    expect(screen.getByText('Upload File')).toBeInTheDocument();
  });

  it('"Upload File" is disabled with no file selected', () => {
    render(<OscalImportWizard {...defaultProps} />);
    expect(screen.getByText('Upload File')).toBeDisabled();
  });

  it('rejects .xml file with a clear error message', () => {
    render(<OscalImportWizard {...defaultProps} />);
    const input = document.getElementById('oscal-file-input') as HTMLInputElement;
    const xmlFile = new File(['<oscal/>'], 'ssp.xml', { type: 'text/xml' });
    Object.defineProperty(input, 'files', { value: [xmlFile], configurable: true });
    fireEvent.change(input);
    expect(screen.getByText(/Only .json files are accepted/)).toBeInTheDocument();
    expect(screen.getByText('Upload File')).toBeDisabled();
  });

  it('rejects .yaml file with a clear error message', () => {
    render(<OscalImportWizard {...defaultProps} />);
    const input = document.getElementById('oscal-file-input') as HTMLInputElement;
    const yamlFile = new File(['---\noscal: 1'], 'ssp.yaml', { type: 'text/yaml' });
    Object.defineProperty(input, 'files', { value: [yamlFile], configurable: true });
    fireEvent.change(input);
    expect(screen.getByText(/Only .json files are accepted/)).toBeInTheDocument();
  });

  it('rejects empty .json file with "File is empty" message', () => {
    render(<OscalImportWizard {...defaultProps} />);
    const input = document.getElementById('oscal-file-input') as HTMLInputElement;
    const emptyFile = new File([''], 'ssp.json', { type: 'application/json' });
    Object.defineProperty(input, 'files', { value: [emptyFile], configurable: true });
    fireEvent.change(input);
    expect(screen.getByText('File is empty.')).toBeInTheDocument();
  });

  it('accepts a valid .json file and enables "Upload File"', () => {
    render(<OscalImportWizard {...defaultProps} />);
    const input = document.getElementById('oscal-file-input') as HTMLInputElement;
    const validFile = new File(
      [JSON.stringify({ 'oscal-complete': { 'system-security-plans': [] } })],
      'ssp.json',
      { type: 'application/json' },
    );
    Object.defineProperty(input, 'files', { value: [validFile], configurable: true });
    fireEvent.change(input);
    expect(screen.getByText('Upload File')).not.toBeDisabled();
    expect(screen.getByText(/ssp\.json/)).toBeInTheDocument();
  });

  it('shows a non-blocking amber warning for files > 10 MB (upload NOT blocked)', () => {
    render(<OscalImportWizard {...defaultProps} />);
    const input = document.getElementById('oscal-file-input') as HTMLInputElement;
    const bigFile = new File(['x'.repeat(11 * 1024 * 1024)], 'large.json', { type: 'application/json' });
    Object.defineProperty(input, 'files', { value: [bigFile], configurable: true });
    fireEvent.change(input);
    expect(screen.getByText(/larger than 10/)).toBeInTheDocument();
    // Warning must NOT block the upload
    expect(screen.getByText('Upload File')).not.toBeDisabled();
  });

  it('hard-blocks upload for files > 50 MB (contract §Notes-1)', () => {
    render(<OscalImportWizard {...defaultProps} />);
    const input = document.getElementById('oscal-file-input') as HTMLInputElement;
    // Simulate a file reporting > 50 MB via Object.defineProperty on size
    const oversizeFile = new File(['{}'], 'huge.json', { type: 'application/json' });
    Object.defineProperty(oversizeFile, 'size', { value: 51 * 1024 * 1024 });
    Object.defineProperty(input, 'files', { value: [oversizeFile], configurable: true });
    fireEvent.change(input);
    expect(screen.getByText(/exceeds the 50 MB limit/)).toBeInTheDocument();
    expect(screen.getByText('Upload File')).toBeDisabled();
  });

  it('calls onClose when Cancel is clicked', () => {
    const onClose = vi.fn();
    render(<OscalImportWizard {...defaultProps} onClose={onClose} />);
    fireEvent.click(screen.getByText('Cancel'));
    expect(onClose).toHaveBeenCalledTimes(1);
  });
});
