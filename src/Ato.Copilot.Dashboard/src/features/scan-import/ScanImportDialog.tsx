import { useCallback, useRef, useState } from 'react';
import { uploadScan, type ScanUploadResponse } from '../../api/scanImport';
import ScanImportProgressBar from './ScanImportProgressBar';

// ─── Types ────────────────────────────────────────────────────────────────────

interface Props {
  systemId: string;
  onClose: () => void;
  onImportComplete?: () => void;
}

// ─── Helpers ──────────────────────────────────────────────────────────────────

const ACCEPTED_TYPES = ['.xml', '.ckl', '.nessus'];

function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
}

// ─── Component ────────────────────────────────────────────────────────────────

/**
 * UF-005 — SCAP/STIG Import Dialog (spec-063 T-063-13 UI portion).
 *
 * Drag-and-drop + file picker modal for uploading XCCDF, CKL, and Nessus
 * scan result files. Shows ScanImportProgressBar while the job is in flight.
 */
export default function ScanImportDialog({ systemId, onClose, onImportComplete }: Props) {
  const [dragOver, setDragOver] = useState(false);
  const [selectedFile, setSelectedFile] = useState<File | null>(null);
  const [uploading, setUploading] = useState(false);
  const [uploadError, setUploadError] = useState<string | null>(null);
  const [importJob, setImportJob] = useState<ScanUploadResponse | null>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);

  const handleFile = useCallback((file: File) => {
    const ext = `.${file.name.split('.').pop()?.toLowerCase() ?? ''}`;
    if (!ACCEPTED_TYPES.includes(ext)) {
      setUploadError(`Unsupported file type "${ext}". Accepted: ${ACCEPTED_TYPES.join(', ')}`);
      return;
    }
    setUploadError(null);
    setSelectedFile(file);
  }, []);

  const handleDrop = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    setDragOver(false);
    const file = e.dataTransfer.files[0];
    if (file) handleFile(file);
  }, [handleFile]);

  const handleInputChange = useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (file) handleFile(file);
  }, [handleFile]);

  const handleUpload = useCallback(async () => {
    if (!selectedFile) return;
    setUploading(true);
    setUploadError(null);
    try {
      const result = await uploadScan(systemId, selectedFile);
      setImportJob(result);
    } catch (err: unknown) {
      const status = (err as { response?: { status?: number } }).response?.status;
      if (status === 415) {
        setUploadError('Unsupported file format. Upload an XCCDF (.xml), CKL (.ckl), or Nessus (.nessus) file.');
      } else if (status === 400) {
        setUploadError('File is too large or invalid. Maximum size is 256 MB.');
      } else {
        setUploadError('Upload failed. Please try again.');
      }
    } finally {
      setUploading(false);
    }
  }, [selectedFile, systemId]);

  const handleImportComplete = useCallback((status: string) => {
    if (status === 'Completed') {
      onImportComplete?.();
    }
  }, [onImportComplete]);

  return (
    /* Backdrop */
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 backdrop-blur-sm">
      <div className="w-full max-w-md rounded-xl bg-white shadow-2xl">
        {/* Header */}
        <div className="flex items-center justify-between border-b border-gray-200 px-5 py-4">
          <h2 className="text-base font-semibold text-gray-900">Import Scan Results</h2>
          <button
            type="button"
            onClick={onClose}
            className="rounded p-1 text-gray-400 hover:bg-gray-100 hover:text-gray-600"
          >
            <svg className="h-5 w-5" viewBox="0 0 20 20" fill="currentColor">
              <path d="M6.28 5.22a.75.75 0 00-1.06 1.06L8.94 10l-3.72 3.72a.75.75 0 101.06 1.06L10 11.06l3.72 3.72a.75.75 0 101.06-1.06L11.06 10l3.72-3.72a.75.75 0 00-1.06-1.06L10 8.94 6.28 5.22z" />
            </svg>
          </button>
        </div>

        <div className="p-5 space-y-4">
          {/* If job is in progress — show progress bar */}
          {importJob ? (
            <div className="space-y-3">
              <div className="rounded-md bg-gray-50 px-3 py-2 text-sm">
                <p className="font-medium text-gray-900">{importJob.fileName}</p>
                <p className="text-gray-500 text-xs mt-0.5">
                  Detected: {importJob.detectedFileType} · {formatBytes(importJob.fileSizeBytes)}
                </p>
              </div>
              <ScanImportProgressBar
                systemId={systemId}
                importJobId={importJob.importJobId}
                onComplete={handleImportComplete}
              />
              <div className="flex justify-end">
                <button
                  type="button"
                  onClick={onClose}
                  className="rounded px-3 py-1.5 text-sm font-medium text-gray-700 hover:bg-gray-100"
                >
                  Close
                </button>
              </div>
            </div>
          ) : (
            <>
              {/* Drop zone */}
              <div
                className={`cursor-pointer rounded-lg border-2 border-dashed px-6 py-10 text-center transition-colors ${
                  dragOver ? 'border-indigo-500 bg-indigo-50' : 'border-gray-300 hover:border-indigo-400'
                }`}
                onDragOver={(e) => { e.preventDefault(); setDragOver(true); }}
                onDragLeave={() => setDragOver(false)}
                onDrop={handleDrop}
                onClick={() => fileInputRef.current?.click()}
              >
                <svg
                  className="mx-auto h-10 w-10 text-gray-400"
                  fill="none"
                  viewBox="0 0 24 24"
                  strokeWidth={1.2}
                  stroke="currentColor"
                >
                  <path strokeLinecap="round" strokeLinejoin="round" d="M3 16.5v2.25A2.25 2.25 0 005.25 21h13.5A2.25 2.25 0 0021 18.75V16.5m-13.5-9L12 3m0 0l4.5 4.5M12 3v13.5" />
                </svg>
                <p className="mt-2 text-sm font-medium text-gray-700">
                  {selectedFile ? selectedFile.name : 'Drop scan file here or click to browse'}
                </p>
                <p className="mt-1 text-xs text-gray-500">
                  XCCDF (.xml), STIG CKL (.ckl), Nessus (.nessus) · max 256 MB
                </p>
                {selectedFile && (
                  <p className="mt-1 text-xs text-gray-400">{formatBytes(selectedFile.size)}</p>
                )}
              </div>

              <input
                ref={fileInputRef}
                type="file"
                accept=".xml,.ckl,.nessus"
                className="sr-only"
                onChange={handleInputChange}
              />

              {uploadError && (
                <p className="text-sm text-red-600">{uploadError}</p>
              )}

              {/* Actions */}
              <div className="flex justify-end gap-3">
                <button
                  type="button"
                  onClick={onClose}
                  className="rounded px-3 py-1.5 text-sm font-medium text-gray-700 hover:bg-gray-100"
                >
                  Cancel
                </button>
                <button
                  type="button"
                  onClick={() => void handleUpload()}
                  disabled={!selectedFile || uploading}
                  className="rounded bg-indigo-600 px-4 py-1.5 text-sm font-medium text-white hover:bg-indigo-700 disabled:opacity-50"
                >
                  {uploading ? 'Uploading…' : 'Import'}
                </button>
              </div>
            </>
          )}
        </div>
      </div>
    </div>
  );
}
