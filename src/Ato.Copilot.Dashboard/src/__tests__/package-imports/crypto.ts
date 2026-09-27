import { afterEach, beforeEach, vi } from 'vitest';

beforeEach(async () => {
  const { webcrypto } = await vi.importActual<{ webcrypto: Crypto }>('node:crypto');
  const { Buffer } = await vi.importActual<{ Buffer: { from(bytes: Uint8Array): Uint8Array<ArrayBuffer> } }>('node:buffer');
  vi.stubGlobal('crypto', {
    randomUUID: () => webcrypto.randomUUID(),
    getRandomValues: <T extends ArrayBufferView | null>(array: T) => webcrypto.getRandomValues(array),
    subtle: {
      digest: (algorithm: AlgorithmIdentifier, data: BufferSource) => {
        // Native WebCrypto requires bytes in its realm, unlike jsdom FileReader.
        const bytes = ArrayBuffer.isView(data)
          ? new Uint8Array(data.buffer, data.byteOffset, data.byteLength)
          : new Uint8Array(data);
        return webcrypto.subtle.digest(algorithm, Buffer.from(bytes));
      },
    },
  });
});
afterEach(() => vi.unstubAllGlobals());
