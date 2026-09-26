import { useEffect, useRef, useState } from 'react';
import { buttonClass, errorClass, secondaryButtonClass } from '../workspace-operations/workspaceUi';
import { PACKAGE_FILE_ACCEPT, validatePackageFiles } from './validation';
import { PackageImportError } from './request';
import { preparePackageUpload } from './uploadIdentity';

interface Props {
  upload: (files: File[], idempotencyKey: string) => Promise<void>;
  disabled?: boolean;
  onPendingChange?: (pending: boolean) => void;
}

export function PackageUpload({ upload, disabled = false, onPendingChange }: Props) {
  const [files, setFiles] = useState<File[]>([]);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const key = useRef<string | null>(null);
  const preparedFiles = useRef<File[]>([]);
  const submitting = useRef(false);
  const input = useRef<HTMLInputElement>(null);
  const errors = files.length ? validatePackageFiles(files) : [];
  const locked = disabled || busy || key.current !== null;

  useEffect(() => { onPendingChange?.(files.length > 0); }, [files.length, onPendingChange]);
  useEffect(() => {
    if (!files.length) return;
    const warn = (event: BeforeUnloadEvent) => { event.preventDefault(); event.returnValue = ''; };
    window.addEventListener('beforeunload', warn);
    return () => window.removeEventListener('beforeunload', warn);
  }, [files.length]);

  const submit = async () => {
    if (submitting.current || disabled) return;
    const validation = validatePackageFiles(files);
    if (validation.length) { setError(validation.join(' ')); return; }
    submitting.current = true;
    setBusy(true);
    setError(null);
    try {
      if (!key.current) {
        const prepared = await preparePackageUpload(files);
        key.current = prepared.key;
        preparedFiles.current = prepared.files;
      }
      await upload(preparedFiles.current, key.current);
      setFiles([]);
      key.current = null;
      preparedFiles.current = [];
      if (input.current) input.current.value = '';
    } catch (reason) {
      const rejected = reason instanceof PackageImportError && [400, 401, 403, 413, 422].includes(reason.status ?? 0);
      if (rejected) key.current = null;
      setError(`${reason instanceof Error ? reason.message : 'Upload failed.'} ${rejected
        ? 'The server rejected this request. Files are retained so you can correct the selection and retry.'
        : key.current ? 'Files and the upload key are retained. Retry the same upload to recover its receipt.'
          : 'No upload was sent. Files are retained; correct the problem and retry.'}`);
    } finally {
      submitting.current = false;
      setBusy(false);
    }
  };

  return <section aria-label="Upload source package" className="space-y-3">
    <div
      className="rounded-lg border-2 border-dashed border-indigo-200 bg-indigo-50/40 p-6"
      onDragOver={event => event.preventDefault()}
      onDrop={event => {
        event.preventDefault();
        if (!locked) setFiles(previous => [...previous, ...Array.from(event.dataTransfer.files)]);
      }}
    >
      <label className="block text-sm font-medium text-gray-900" htmlFor="package-source-files">Select source files</label>
      <input id="package-source-files" ref={input} type="file" multiple accept={PACKAGE_FILE_ACCEPT} disabled={locked}
        className="mt-2 block w-full min-w-0 text-sm file:mr-3 file:rounded file:border-0 file:bg-indigo-700 file:px-3 file:py-2 file:text-white"
        onChange={event => { setError(null); setFiles(Array.from(event.target.files ?? [])); }} />
      <p className="mt-3 text-xs text-gray-600">PDF, DOCX, JSON, XLSX, ZIP, XML, CSV or TXT. Up to 1000 files and 50 MiB total per package. You may also drag files here.</p>
      <p className="mt-2 text-xs text-gray-600">After a refresh, reselect the same source files to recover the same upload. Identity is content-derived; source bytes are not stored in browser storage.</p>
    </div>
    {files.length > 0 && <ul className="divide-y rounded border border-gray-200 bg-white">
      {files.map((file, index) => <li key={`${file.name}-${index}`} className="flex min-w-0 items-center justify-between gap-2 p-3 text-sm">
        <span className="min-w-0 break-all">{file.name} <span className="text-gray-500">({(file.size / 1024 / 1024).toFixed(2)} MiB)</span></span>
        <button type="button" className={secondaryButtonClass} disabled={locked} aria-label={`Remove ${file.name}`}
          onClick={() => setFiles(previous => previous.filter((_, item) => item !== index))}>Remove</button>
      </li>)}
    </ul>}
    {errors.length > 0 && <div role="alert" className={errorClass}><ul>{errors.map(value => <li key={value}>{value}</li>)}</ul></div>}
    {error && <div role="alert" className={errorClass}>{error}</div>}
    {files.length > 0 && <button type="button" className={buttonClass} disabled={disabled || busy || errors.length > 0}
      aria-busy={busy} onClick={() => void submit()}>{busy ? 'Uploading package...' : key.current ? 'Retry same upload' : 'Upload package'}</button>}
    {key.current && <p className="text-xs text-gray-600">Do not close this page until receipt is confirmed. No approval or publication occurs on upload.</p>}
  </section>;
}
