import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import AuthenticatedDownload from '../../components/AuthenticatedDownload';

const download = vi.hoisted(() => vi.fn());
vi.mock('../../api/downloads', () => ({ downloadAuthenticatedFile: download }));
beforeEach(() => { download.mockReset(); });

describe('AuthenticatedDownload', () => {
  it('downloads through the scoped API helper', async () => {
    // Arrange
    download.mockResolvedValue(undefined);
    render(<AuthenticatedDownload url="/api/file-a" fileName="a.pdf">Download PDF</AuthenticatedDownload>);

    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Download PDF' }));

    // Assert
    await waitFor(() => expect(screen.getByRole('button', { name: 'Download PDF' })).toBeEnabled());
    expect(download).toHaveBeenCalledWith('/api/file-a', 'a.pdf', expect.any(AbortSignal));
  });

  it('shows a download failure explicitly', async () => {
    // Arrange
    download.mockRejectedValue(new Error('Download access denied.'));
    render(<AuthenticatedDownload url="/api/file-a">Download</AuthenticatedDownload>);

    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Download' }));

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Download access denied.');
    expect(screen.getByRole('button', { name: 'Download' })).toBeEnabled();
  });

  it('cancels a prior file request and does not keep the new file disabled', async () => {
    // Arrange
    let complete!: () => void;
    download.mockReturnValueOnce(new Promise<void>(resolve => { complete = resolve; }));
    const page = render(<AuthenticatedDownload url="/api/file-a">Download</AuthenticatedDownload>);
    fireEvent.click(screen.getByRole('button', { name: 'Download' }));
    const signal = download.mock.calls[0]?.[2] as AbortSignal;

    // Act
    page.rerender(<AuthenticatedDownload url="/api/file-b">Download</AuthenticatedDownload>);
    await act(async () => complete());

    // Assert
    expect(signal.aborted).toBe(true);
    expect(screen.getByRole('button', { name: 'Download' })).toBeEnabled();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });
});
