import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { downloadAuthenticatedFile } from '../../api/downloads';

const http = vi.hoisted(() => ({ get: vi.fn() }));
const settings = vi.hoisted(() => ({ baseURL: '/api/dashboard' }));
vi.mock('axios', () => ({ default: http }));
vi.mock('../../api/client', () => ({ default: { defaults: settings } }));

beforeEach(() => {
  http.get.mockReset();
  settings.baseURL = '/api/dashboard';
  vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {});
  vi.stubGlobal('URL', class extends URL {
    static createObjectURL = vi.fn(() => 'blob:synthetic-file');
    static revokeObjectURL = vi.fn();
  });
});
afterEach(() => {
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

describe('authenticated API downloads', () => {
  it('uses the authenticated client with the API origin, not dashboard-relative path concatenation', async () => {
    // Arrange
    const blob = new Blob(['synthetic'], { type: 'application/zip' });
    http.get.mockResolvedValue({ data: blob, headers: {} });
    const signal = new AbortController().signal;

    // Act
    await downloadAuthenticatedFile('/api/v1/systems/a/packages/b/download', 'package.zip', signal);

    // Assert
    expect(http.get).toHaveBeenCalledWith(
      `${window.location.origin}/api/v1/systems/a/packages/b/download`,
      { baseURL: window.location.origin, responseType: 'blob', signal },
    );
    expect(HTMLAnchorElement.prototype.click).toHaveBeenCalledOnce();
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:synthetic-file');
    expect(document.querySelector('a[download]')).toBeNull();
  });

  it('supports the explicitly configured API host', async () => {
    // Arrange
    settings.baseURL = 'https://api.example.invalid/api/dashboard';
    http.get.mockResolvedValue({ data: new Blob(['file']), headers: {} });

    // Act
    await downloadAuthenticatedFile('/api/v1/systems/a/file');

    // Assert
    expect(http.get).toHaveBeenCalledWith('https://api.example.invalid/api/v1/systems/a/file',
      expect.objectContaining({ baseURL: 'https://api.example.invalid' }));
  });

  it.each(['https://external.example.invalid/api/file', '/not-an-api/file'])('rejects untrusted download destination %s', async url => {
    // Arrange
    const destination = url;

    // Act
    const downloading = downloadAuthenticatedFile(destination);

    // Assert
    await expect(downloading).rejects.toThrow('Download destination is not the configured API.');
    expect(http.get).not.toHaveBeenCalled();
  });

  it('does not report a non-file response as a successful download', async () => {
    // Arrange
    http.get.mockResolvedValue({ data: { status: 'error' }, headers: {} });

    // Act
    const downloading = downloadAuthenticatedFile('/api/files/a');

    // Assert
    await expect(downloading).rejects.toThrow('The server did not return a downloadable file.');
    expect(HTMLAnchorElement.prototype.click).not.toHaveBeenCalled();
  });
});
