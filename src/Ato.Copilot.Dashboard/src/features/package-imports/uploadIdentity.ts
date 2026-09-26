function readSource(file: File): Promise<ArrayBuffer> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onerror = () => reject(new Error(`Unable to read ${file.name} for upload identity.`));
    reader.onabort = () => reject(new Error(`Reading ${file.name} was cancelled.`));
    reader.onload = () => reader.result instanceof ArrayBuffer
      ? resolve(reader.result) : reject(new Error(`Unable to read the bytes of ${file.name}.`));
    reader.readAsArrayBuffer(file);
  });
}

async function sha256(bytes: BufferSource): Promise<string> {
  const digest = await crypto.subtle.digest('SHA-256', bytes);
  return Array.from(new Uint8Array(digest), byte => byte.toString(16).padStart(2, '0')).join('');
}

export async function preparePackageUpload(files: File[]): Promise<{ files: File[]; key: string }> {
  if (!crypto.subtle) throw new Error('Secure browser hashing is unavailable. Use HTTPS or localhost before uploading.');
  const entries: { file: File; fingerprint: string }[] = [];
  for (const file of files) {
    const hash = await sha256(await readSource(file));
    entries.push({ file, fingerprint: JSON.stringify([file.name, file.type, file.size, hash]) });
  }
  // The backend fingerprint is order-sensitive. Canonicalize both the key and transmitted files.
  entries.sort((left, right) => left.fingerprint < right.fingerprint ? -1 : left.fingerprint > right.fingerprint ? 1 : 0);
  const hash = await sha256(new TextEncoder().encode(JSON.stringify(entries.map(entry => entry.fingerprint))));
  return { files: entries.map(entry => entry.file), key: `source-sha256-${hash}` };
}
