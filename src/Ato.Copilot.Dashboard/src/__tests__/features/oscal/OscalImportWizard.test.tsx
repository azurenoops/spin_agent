/**
 * Issue #419 — Unit tests for OscalImportWizard (Feature 076 T011)
 * Covers: step-1 file validation (accept/reject), CTA rendering, cancel behaviour
 */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import OscalImportWizard from '../../../features/oscal/OscalImportWizard';

// Minimal axios mock — tests only exercise Step 1 (no network calls)
vi.mock('axios', () => ({
  default: {
    post: vi.fn(),
    get: vi.fn(),
  },
}));

const defaultProps = {
  systemId: 'sys-unit-test',
  onClose: vi.fn(),
  onImportComplete: vi.fn(),
};

describe('OscalImportWizard — Step 1 (Upload)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders the Import OSCAL SSP heading and Upload step', () => {
    render(<OscalImportWizard {...defaultProps} />);
    expect(screen.getByText('Import OSCAL SSP')).toBeInTheDocument();
    // Step label
    expect(screen.getByText('Upload')).toBeInTheDocument();
    // Primary action button
    expect(screen.getByText('Upload & Validate')).toBeInTheDocument();
  });

  it('Upload & Validate is disabled with no file selected', () => {
    render(<OscalImportWizard {...defaultProps} />);
    const btn = screen.getByText('Upload & Validate');
    expect(btn).toBeDisabled();
  });

  it('rejects .xml file with a clear error message', () => {
    render(<OscalImportWizard {...defaultProps} />);
    const input = document.getElementById('oscal-file-input') as HTMLInputElement;
    const xmlFile = new File(['<oscal/>'], 'ssp.xml', { type: 'text/xml' });
    Object.defineProperty(input, 'files', { value: [xmlFile], configurable: true });
    fireEvent.change(input);
    expect(screen.getByText(/Only .json files are accepted/)).toBeInTheDocument();
    // Button must remain disabled
    expect(screen.getByText('Upload & Validate')).toBeDisabled();
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

  it('accepts a valid .json file and enables Upload & Validate', () => {
    render(<OscalImportWizard {...defaultProps} />);
    const input = document.getElementById('oscal-file-input') as HTMLInputElement;
    const validFile = new File(
      [JSON.stringify({ 'oscal-complete': { 'system-security-plans': [] } })],
      'ssp.json',
      { type: 'application/json' },
    );
    Object.defineProperty(input, 'files', { value: [validFile], configurable: true });
    fireEvent.change(input);
    const btn = screen.getByText('Upload & Validate');
    expect(btn).not.toBeDisabled();
    expect(screen.getByText(/ssp\.json/)).toBeInTheDocument();
  });

  it('shows a non-blocking amber warning for files > 10 MB', () => {
    render(<OscalImportWizard {...defaultProps} />);
    const input = document.getElementById('oscal-file-input') as HTMLInputElement;
    // Create a 11 MB file
    const bigContent = 'x'.repeat(11 * 1024 * 1024);
    const bigFile = new File([bigContent], 'large.json', { type: 'application/json' });
    Object.defineProperty(input, 'files', { value: [bigFile], configurable: true });
    fireEvent.change(input);
    expect(screen.getByText(/larger than 10/)).toBeInTheDocument();
    // Warning does NOT block upload
    expect(screen.getByText('Upload & Validate')).not.toBeDisabled();
  });

  it('calls onClose when Cancel is clicked', () => {
    const onClose = vi.fn();
    render(<OscalImportWizard {...defaultProps} onClose={onClose} />);
    fireEvent.click(screen.getByText('Cancel'));
    expect(onClose).toHaveBeenCalledTimes(1);
  });
});
