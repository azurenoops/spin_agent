import axios from 'axios';
import apiClient from './client';

const object = (value: unknown): value is Record<string, unknown> => value !== null && typeof value === 'object';

async function surfaceDownloadError(error: unknown): Promise<never> {
  if (object(error) && object(error.response) && error.response.data instanceof Blob
    && error.response.data.type.includes('json')) {
    let body: unknown;
    try {
      body = JSON.parse(await error.response.data.text());
    } catch (decodeError) {
      throw new Error(`Download failed (HTTP ${error.response.status}); the server error could not be read.`, { cause: decodeError });
    }
    if (object(body)) {
      const message = object(body.error) ? body.error.message : body.error ?? body.detail ?? body.message;
      if (typeof message === 'string') throw new Error(message, { cause: error });
    }
  }
  throw error;
}

export async function downloadAuthenticatedFile(url: string, fileName = 'download', signal?: AbortSignal): Promise<void> {
  const apiOrigin = new URL(apiClient.defaults.baseURL ?? '/api/dashboard', window.location.origin).origin;
  const destination = new URL(url, apiOrigin);
  if (destination.origin !== apiOrigin || !destination.pathname.startsWith('/api/')) {
    throw new Error('Download destination is not the configured API.');
  }
  const response = await axios.get<Blob>(destination.href, { baseURL: apiOrigin, responseType: 'blob', signal })
    .catch(surfaceDownloadError);
  if (!(response.data instanceof Blob)) throw new Error('The server did not return a downloadable file.');

  const disposition = response.headers['content-disposition'];
  if (typeof disposition === 'string') {
    const encoded = /filename\*=UTF-8''([^;]+)/i.exec(disposition)?.[1];
    const plain = /filename="([^"]+)"|filename=([^;]+)/i.exec(disposition);
    if (encoded) fileName = decodeURIComponent(encoded);
    else fileName = plain?.[1] ?? plain?.[2] ?? fileName;
  }
  const objectUrl = URL.createObjectURL(response.data);
  const anchor = document.createElement('a');
  anchor.href = objectUrl;
  anchor.download = fileName.split(/[/\\]/).pop()?.trim() || 'download';
  try {
    document.body.appendChild(anchor);
    anchor.click();
  } finally {
    anchor.remove();
    URL.revokeObjectURL(objectUrl);
  }
}
