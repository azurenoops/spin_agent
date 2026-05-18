import {
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ChangeEvent,
  type DragEvent,
  type ReactElement,
} from 'react';
import {
  getCspOnboardingAtosState,
  postCspOnboardingAtosUpload,
  type AtoStepState,
  type AtoUploadResponse,
} from '../api';
import ComponentExtractionPreview from './ComponentExtractionPreview';

const MAX_FILE_BYTES = 50 * 1024 * 1024; // 50 MB per file (FR-099)
const ACCEPT = [
  'application/pdf',
  'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
  'application/json',
  'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
  'application/zip',
  '.pdf',
  '.docx',
  '.json',
  '.xlsx',
  '.zip',
].join(',');

interface AtoDocumentsStepProps {
  /** Saving = currently submitting. We hold the boolean even though the
   *  upload happens locally — the wizard relays it for consistency. */
  saving: boolean;
  /** Most-recent error from the parent wizard (e.g. a Continue→Submit
   *  failure). Per-upload errors are surfaced inline by this step. */
  errorMessage: string | null;
  /** Move the wizard forward to Review. Skipping uploads is allowed
   *  (FR-099 — ATO upload is optional). */
  onContinue: () => void;
  /** Step back to Classification. */
  onBack: () => void;
}

interface PendingFile {
  file: File;
  /** Local-only validation error (file too large / unsupported MIME). */
  error?: string;
}

/**
 * `AtoDocumentsStep` — Feature 048 / US9 / T211.
 *
 * Wizard step (between Classification and Review) where the CSP-Admin
 * uploads existing ATO documents. The server parses each file using the
 * appropriate parser (PDF SSP / DOCX / OSCAL JSON / XLSX / eMASS ZIP),
 * extracts candidate components, and runs AI capability mapping when
 * available. The step is **optional** — the operator can skip with the
 * Continue button without uploading any documents.
 *
 * Upload semantics:
 *  - Each file is validated client-side for size (≤ 50 MB) before submit.
 *  - All accepted files are POSTed in a single multipart request.
 *  - The server returns an aggregate tally + per-file row, which we render
 *    via `ComponentExtractionPreview`.
 *  - On step mount we also call `GET /atos/state` so a returning user
 *    sees the running tally from previous uploads.
 */
export default function AtoDocumentsStep({
  saving,
  errorMessage,
  onContinue,
  onBack,
}: AtoDocumentsStepProps): ReactElement {
  const [pending, setPending] = useState<PendingFile[]>([]);
  const [uploading, setUploading] = useState(false);
  const [uploadResult, setUploadResult] = useState<AtoUploadResponse | null>(null);
  const [priorState, setPriorState] = useState<AtoStepState | null>(null);
  const [uploadError, setUploadError] = useState<string | null>(null);
  const [dragOver, setDragOver] = useState(false);
  const fileInputRef = useRef<HTMLInputElement>(null);

  // Re-entrant: pull the running tally on mount so a user resuming the
  // wizard sees the documents they already uploaded.
  useEffect(() => {
    let cancelled = false;
    void (async () => {
      try {
        const state = await getCspOnboardingAtosState();
        if (!cancelled) setPriorState(state);
      } catch {
        // Best-effort — if the state endpoint fails the user can still
        // upload more documents. Don't block the step.
        if (!cancelled) setPriorState(null);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  const validate = useCallback((files: File[]): PendingFile[] => {
    return files.map((file) => {
      if (file.size > MAX_FILE_BYTES) {
        return {
          file,
          error: `${file.name} exceeds the 50 MB per-file limit.`,
        };
      }
      return { file };
    });
  }, []);

  const onFilePick = (e: ChangeEvent<HTMLInputElement>) => {
    const arr = Array.from(e.target.files ?? []);
    setPending((prev) => [...prev, ...validate(arr)]);
    // Allow re-selecting the same file by clearing the input value.
    if (fileInputRef.current) fileInputRef.current.value = '';
  };

  const onDrop = (e: DragEvent<HTMLDivElement>) => {
    e.preventDefault();
    setDragOver(false);
    const arr = Array.from(e.dataTransfer.files ?? []);
    setPending((prev) => [...prev, ...validate(arr)]);
  };

  const removePending = (idx: number) =>
    setPending((prev) => prev.filter((_, i) => i !== idx));

  const validFiles = useMemo(
    () => pending.filter((p) => !p.error).map((p) => p.file),
    [pending],
  );

  const handleUpload = async () => {
    if (validFiles.length === 0) return;
    setUploading(true);
    setUploadError(null);
    try {
      const result = await postCspOnboardingAtosUpload(validFiles);
      setUploadResult(result);
      setPending([]);
      // Refresh prior state so the running tally reflects this upload too.
      try {
        setPriorState(await getCspOnboardingAtosState());
      } catch {
        // ignore
      }
    } catch (err) {
      const e = err as { errorCode?: string; message?: string };
      setUploadError(
        e?.message
          ? `${e.errorCode ? `[${e.errorCode}] ` : ''}${e.message}`
          : 'Upload failed.',
      );
    } finally {
      setUploading(false);
    }
  };

  const isBusy = saving || uploading;

  return (
    <div className="space-y-4" aria-labelledby="ato-documents-step-heading">
      <h2 id="ato-documents-step-heading" className="text-lg font-semibold text-gray-900">
        Upload existing ATO documents
        <span className="ml-2 inline-flex items-center rounded-full bg-gray-100 px-2 py-0.5 text-xs font-medium text-gray-700">
          Optional
        </span>
      </h2>
      <p className="text-sm text-gray-600">
        Upload your existing ATO source artifacts (PDF System Security Plan,
        DOCX, OSCAL JSON, FedRAMP / eMASS XLSX, or eMASS ZIP). The platform
        will extract candidate components and auto-map capabilities to
        NIST 800-53 controls. You can also skip this step and add documents
        later from the <em>Inherited Components</em> page.
      </p>

      {/* Aggregate tally from previous uploads (re-entrancy). */}
      {priorState && priorState.documentsUploaded > 0 && (
        <div className="mt-4 rounded-md border border-indigo-200 bg-indigo-50 px-3 py-2 text-sm text-indigo-900">
          You already have <strong>{priorState.documentsUploaded}</strong>{' '}
          document{priorState.documentsUploaded === 1 ? '' : 's'} uploaded —{' '}
          {priorState.componentsExtracted} component
          {priorState.componentsExtracted === 1 ? '' : 's'} extracted,{' '}
          {priorState.capabilitiesMapped} capabilit
          {priorState.capabilitiesMapped === 1 ? 'y' : 'ies'} mapped,{' '}
          {priorState.capabilitiesNeedsReview} need
          {priorState.capabilitiesNeedsReview === 1 ? 's' : ''} review.
        </div>
      )}

      {/* Dropzone */}
      <div
        onDragOver={(e) => {
          e.preventDefault();
          setDragOver(true);
        }}
        onDragLeave={() => setDragOver(false)}
        onDrop={onDrop}
        className={`mt-4 flex flex-col items-center justify-center rounded-lg border-2 border-dashed px-6 py-10 text-center transition-colors ${
          dragOver
            ? 'border-indigo-500 bg-indigo-50'
            : 'border-gray-300 bg-gray-50'
        }`}
        data-testid="ato-dropzone"
      >
        <p className="text-sm text-gray-700">
          Drag and drop files here, or
        </p>
        <button
          type="button"
          onClick={() => fileInputRef.current?.click()}
          disabled={isBusy}
          className="mt-2 rounded-md bg-indigo-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-indigo-700 disabled:cursor-not-allowed disabled:bg-indigo-300"
        >
          Choose files
        </button>
        <p className="mt-2 text-xs text-gray-500">
          PDF, DOCX, OSCAL JSON, XLSX, or ZIP — up to 50 MB per file.
        </p>
        <input
          ref={fileInputRef}
          type="file"
          multiple
          accept={ACCEPT}
          className="hidden"
          onChange={onFilePick}
          aria-label="Select ATO documents"
        />
      </div>

      {/* Pending file list (pre-upload) */}
      {pending.length > 0 && (
        <ul className="mt-4 divide-y divide-gray-100 rounded-md border border-gray-200 bg-white">
          {pending.map((p, idx) => (
            <li key={`${p.file.name}-${idx}`} className="flex items-start justify-between gap-3 px-3 py-2 text-sm">
              <div className="min-w-0 flex-1">
                <p className="truncate font-medium text-gray-900">{p.file.name}</p>
                <p className="text-xs text-gray-500">
                  {(p.file.size / 1024 / 1024).toFixed(2)} MB
                </p>
                {p.error && (
                  <p className="mt-1 text-xs text-red-700" role="alert">
                    {p.error}
                  </p>
                )}
              </div>
              <button
                type="button"
                onClick={() => removePending(idx)}
                className="text-xs font-medium text-gray-500 hover:text-gray-700"
                aria-label={`Remove ${p.file.name}`}
              >
                Remove
              </button>
            </li>
          ))}
        </ul>
      )}

      {/* Upload button */}
      {pending.length > 0 && (
        <div className="mt-4 flex items-center gap-3">
          <button
            type="button"
            onClick={handleUpload}
            disabled={isBusy || validFiles.length === 0}
            className="rounded-md bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-700 disabled:cursor-not-allowed disabled:bg-indigo-300"
          >
            {uploading ? 'Uploading…' : `Upload ${validFiles.length} file${validFiles.length === 1 ? '' : 's'}`}
          </button>
          <span className="text-xs text-gray-500">
            {pending.length - validFiles.length > 0 &&
              `${pending.length - validFiles.length} file(s) skipped due to validation errors.`}
          </span>
        </div>
      )}

      {/* Upload-level error */}
      {uploadError && (
        <div
          role="alert"
          className="mt-4 rounded-md border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700"
        >
          {uploadError}
        </div>
      )}

      {/* Result preview */}
      {uploadResult && (
        <div className="mt-6">
          <h3 className="text-sm font-semibold text-gray-900">Upload result</h3>
          <div className="mt-2">
            <ComponentExtractionPreview result={uploadResult} />
          </div>
        </div>
      )}

      {/* Wizard error from parent */}
      {errorMessage && (
        <div
          role="alert"
          className="mt-4 rounded-md border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700"
        >
          {errorMessage}
        </div>
      )}

      {/* Wizard nav */}
      <div className="flex items-center justify-between pt-2">
        <button
          type="button"
          onClick={onBack}
          disabled={isBusy}
          className="rounded-md border border-gray-300 bg-white px-4 py-2 text-sm font-medium text-gray-700 shadow-sm hover:bg-gray-50 disabled:cursor-not-allowed disabled:opacity-50"
        >
          Back
        </button>
        <button
          type="button"
          onClick={onContinue}
          disabled={isBusy}
          className="rounded-md bg-indigo-600 px-4 py-2 text-sm font-medium text-white shadow-sm hover:bg-indigo-700 disabled:cursor-not-allowed disabled:bg-indigo-300"
        >
          Continue
        </button>
      </div>
    </div>
  );
}
