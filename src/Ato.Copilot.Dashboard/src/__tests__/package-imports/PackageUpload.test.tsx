import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { PackageUpload } from '../../features/package-imports/PackageUpload';
import { PackageImportError } from '../../features/package-imports/request';
import './crypto';

describe('durable package upload', () => {
  it('keeps the exact files and idempotency key across an uncertain failure', async () => {
    // Arrange
    const upload = vi.fn().mockRejectedValueOnce(new Error('Connection lost; receipt unknown.')).mockResolvedValue(undefined);
    const file = new File(['evidence'], 'package.json');
    render(<PackageUpload upload={upload} />);
    fireEvent.change(screen.getByLabelText('Select source files'), { target: { files: [file] } });
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Upload package' }));
    await screen.findByText(/Connection lost/);
    fireEvent.click(screen.getByRole('button', { name: 'Retry same upload' }));
    // Assert
    await waitFor(() => expect(upload).toHaveBeenCalledTimes(2));
    expect(upload.mock.calls[0]?.[0]).toEqual([file]);
    expect(upload.mock.calls[1]).toEqual(upload.mock.calls[0]);
    expect(upload.mock.calls[0]?.[1]).toMatch(/^[a-z0-9-]{16,}$/i);
  });

  it('prevents concurrent submissions and selection changes before receipt', async () => {
    // Arrange
    let resolve!: () => void;
    const upload = vi.fn(() => new Promise<void>(done => { resolve = done; }));
    render(<PackageUpload upload={upload} />);
    fireEvent.change(screen.getByLabelText('Select source files'), { target: { files: [new File(['evidence'], 'source.pdf')] } });
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Upload package' }));
    fireEvent.click(screen.getByRole('button', { name: 'Uploading package...' }));
    // Assert
    await waitFor(() => expect(upload).toHaveBeenCalledOnce());
    expect(screen.getByLabelText('Select source files')).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Remove source.pdf' })).toBeDisabled();
    expect(screen.queryByText(/received|accepted successfully/i)).not.toBeInTheDocument();
    await act(async () => resolve());
  });

  it('does not partially submit a selection that exceeds the package budget', async () => {
    // Arrange
    const upload = vi.fn();
    const file = new File(['evidence'], 'large.pdf');
    Object.defineProperty(file, 'size', { value: 51 * 1024 * 1024 });
    render(<PackageUpload upload={upload} />);
    // Act
    fireEvent.change(screen.getByLabelText('Select source files'), { target: { files: [file] } });
    // Assert
    expect(screen.getByRole('alert')).toHaveTextContent('50 MiB total');
    expect(screen.getByRole('button', { name: 'Upload package' })).toBeDisabled();
    expect(upload).not.toHaveBeenCalled();
  });

  it('allows correction after a definite server validation rejection without discarding the files', async () => {
    // Arrange
    const upload = vi.fn().mockRejectedValue(new PackageImportError('Archive expansion limit exceeded.', 413));
    render(<PackageUpload upload={upload} />);
    fireEvent.change(screen.getByLabelText('Select source files'), { target: { files: [new File(['source'], 'package.zip')] } });
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Upload package' }));
    await screen.findByText(/Archive expansion limit exceeded/);
    // Assert
    expect(screen.getByRole('button', { name: 'Remove package.zip' })).toBeEnabled();
    expect(screen.getByLabelText('Select source files')).toBeEnabled();
    expect(screen.getByText(/package.zip/)).toBeInTheDocument();
  });

  it('reuses the upload key after remount and source reselection without storing bytes', async () => {
    // Arrange
    const upload = vi.fn().mockRejectedValueOnce(new Error('Receipt unknown.')).mockResolvedValue(undefined);
    const initial = render(<PackageUpload upload={upload} />);
    fireEvent.change(screen.getByLabelText('Select source files'), { target: { files: [new File(['same content'], 'source.json')] } });
    fireEvent.click(screen.getByRole('button', { name: 'Upload package' }));
    await screen.findByText(/Receipt unknown/);
    initial.unmount();
    // Act
    render(<PackageUpload upload={upload} />);
    fireEvent.change(screen.getByLabelText('Select source files'), { target: { files: [new File(['same content'], 'source.json')] } });
    fireEvent.click(screen.getByRole('button', { name: 'Upload package' }));
    // Assert
    await waitFor(() => expect(upload).toHaveBeenCalledTimes(2));
    expect(upload.mock.calls[1]?.[1]).toBe(upload.mock.calls[0]?.[1]);
  });
});
