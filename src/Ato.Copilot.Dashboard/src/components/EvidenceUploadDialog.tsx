import { useState, useEffect, useRef, useCallback } from 'react';
import { uploadEvidence } from '../api/evidence';
import apiClient from '../api/client';
import type { ArtifactCategory, CollectionMethod } from '../types/evidence';

interface SystemControl {
  controlId: string;
  controlTitle: string;
}

interface Props {
  systemId: string;
  controlImplementationId?: string;
  securityCapabilityId?: string;
  onClose: () => void;
  onUploaded: () => void;
}

const ARTIFACT_CATEGORIES: { value: ArtifactCategory; label: string }[] = [
  { value: 'Screenshot', label: 'Screenshot' },
  { value: 'ScanResult', label: 'Scan Result' },
  { value: 'ConfigurationExport', label: 'Configuration Export' },
  { value: 'PolicyDocument', label: 'Policy Document' },
  { value: 'AuditLog', label: 'Audit Log' },
  { value: 'TestResult', label: 'Test Result' },
  { value: 'Other', label: 'Other' },
];

const COLLECTION_METHODS: { value: CollectionMethod; label: string }[] = [
  { value: 'Manual', label: 'Manual' },
  { value: 'AutomatedScan', label: 'Automated Scan' },
  { value: 'ApiExport', label: 'API Export' },
  { value: 'Other', label: 'Other' },
];

const ALLOWED_EXTENSIONS = [
  '.pdf', '.png', '.jpg', '.jpeg', '.gif', '.csv', '.xlsx', '.xls',
  '.docx', '.doc', '.json',
];

const MAX_SIZE_MB = 25;
const MAX_SIZE_BYTES = MAX_SIZE_MB * 1024 * 1024;

function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

export default function EvidenceUploadDialog({
  systemId,
  controlImplementationId,
  securityCapabilityId,
  onClose,
  onUploaded,
}: Props) {
  const [file, setFile] = useState<File | null>(null);
  const [category, setCategory] = useState<ArtifactCategory>('Screenshot');
  const [collectionMethod, setCollectionMethod] = useState<CollectionMethod>('Manual');
  const [description, setDescription] = useState('');
  const [uploading, setUploading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [progress, setProgress] = useState(0);
  const fileInputRef = useRef<HTMLInputElement>(null);

  // T280: Searchable Control dropdown
  const [controls, setControls] = useState<SystemControl[]>([]);
  const [controlSearch, setControlSearch] = useState('');
  const [selectedControlId, setSelectedControlId] = useState<string>('');
  const [controlDropdownOpen, setControlDropdownOpen] = useState(false);
  const controlInputRef = useRef<HTMLInputElement>(null);

  // Load system controls on mount
  useEffect(() => {
    let cancelled = false;
    apiClient
      .get<SystemControl[] | { items: SystemControl[] }>(`/systems/${systemId}/controls`)
      .then(({ data }) => {
        if (!cancelled) {
          const list = Array.isArray(data) ? data : (data as { items: SystemControl[] }).items ?? [];
          setControls(list);
        }
      })
      .catch(() => {
        // Controls load is non-blocking; upload still works without it
      });
    return () => { cancelled = true; };
  }, [systemId]);

  const filteredControls = useCallback(() => {
    const q = controlSearch.toLowerCase();
    return controls.filter(
      (c) => c.controlId.toLowerCase().includes(q) || c.controlTitle.toLowerCase().includes(q),
    ).slice(0, 50);
  }, [controls, controlSearch]);

  useEffect(() => {
    const handleKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape' && !uploading) {
        if (controlDropdownOpen) setControlDropdownOpen(false);
        else onClose();
      }
    };
    document.addEventListener('keydown', handleKey);
    return () => document.removeEventListener('keydown', handleKey);
  }, [onClose, uploading, controlDropdownOpen]);

  const handleBackdrop = (e: React.MouseEvent) => {
    if (e.target === e.currentTarget && !uploading) onClose();
  };

  const validateFile = (f: File): string | null => {
    if (f.size === 0) return 'File is empty.';
    if (f.size > MAX_SIZE_BYTES) return `File exceeds the ${MAX_SIZE_MB} MB limit (${formatBytes(f.size)}).`;
    const ext = f.name.substring(f.name.lastIndexOf('.')).toLowerCase();
    if (!ALLOWED_EXTENSIONS.includes(ext))
      return `File type "${ext}" is not allowed. Supported: ${ALLOWED_EXTENSIONS.join(', ')}`;
    return null;
  };

  const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const selected = e.target.files?.[0] ?? null;
    setError(null);
    if (selected) {
      const validationError = validateFile(selected);
      if (validationError) {
        setError(validationError);
        setFile(null);
        return;
      }
    }
    setFile(selected);
  };

  const handleSubmit = async () => {
    if (!file) return;
    setUploading(true);
    setError(null);
    setProgress(0);
    try {
      setProgress(30);
      await uploadEvidence({
        systemId,
        file,
        artifactCategory: category,
        controlImplementationId: controlImplementationId ?? undefined,
        securityCapabilityId: securityCapabilityId ?? undefined,
        description: description.trim() || undefined,
        collectionMethod,
        // T280: pass selected controlId if present
        ...(selectedControlId ? { controlId: selectedControlId } : {}),
      } as Parameters<typeof uploadEvidence>[0]);
      setProgress(100);
      onUploaded();
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Upload failed';
      setError(msg);
    } finally {
      setUploading(false);
    }
  };

  const isValid = file !== null;
  const selectedControl = controls.find((c) => c.controlId === selectedControlId);

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center" onClick={handleBackdrop}>
      <div className="absolute inset-0 bg-black/40" />
      <div className="relative z-10 flex max-h-[90vh] w-full max-w-lg flex-col rounded-lg bg-white shadow-xl">
        {/* Header */}
        <div className="flex items-center justify-between border-b px-6 py-4">
          <h2 className="text-lg font-semibold text-gray-900">Attach Evidence</h2>
          <button
            onClick={onClose}
            disabled={uploading}
            className="text-gray-400 hover:text-gray-600 disabled:opacity-50"
          >
            <svg className="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
            </svg>
          </button>
        </div>

        {/* Body */}
        <div className="flex-1 space-y-4 overflow-y-auto px-6 py-4">
          {error && (
            <div className="rounded-md border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
              {error}
            </div>
          )}

          {/* File picker */}
          <div>
            <label className="mb-1 block text-sm font-medium text-gray-700">
              File <span className="text-red-500">*</span>
            </label>
            <input
              ref={fileInputRef}
              type="file"
              accept={ALLOWED_EXTENSIONS.join(',')}
              onChange={handleFileChange}
              className="hidden"
            />
            <div
              onClick={() => fileInputRef.current?.click()}
              className="cursor-pointer rounded-md border-2 border-dashed border-gray-300 px-4 py-6 text-center transition-colors hover:border-indigo-400 hover:bg-indigo-50"
            >
              {file ? (
                <div className="space-y-1">
                  <p className="text-sm font-medium text-gray-900">{file.name}</p>
                  <p className="text-xs text-gray-500">{formatBytes(file.size)}</p>
                </div>
              ) : (
                <div className="space-y-1">
                  <svg className="mx-auto h-8 w-8 text-gray-400" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M12 4v16m8-8H4" />
                  </svg>
                  <p className="text-sm text-gray-500">Click to select a file</p>
                  <p className="text-xs text-gray-400">
                    Max {MAX_SIZE_MB} MB &middot; {ALLOWED_EXTENSIONS.join(', ')}
                  </p>
                </div>
              )}
            </div>
          </div>

          {/* Category */}
          <div>
            <label className="mb-1 block text-sm font-medium text-gray-700">
              Category <span className="text-red-500">*</span>
            </label>
            <select
              value={category}
              onChange={(e) => setCategory(e.target.value as ArtifactCategory)}
              className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500"
            >
              {ARTIFACT_CATEGORIES.map((c) => (
                <option key={c.value} value={c.value}>{c.label}</option>
              ))}
            </select>
          </div>

          {/* T280: Searchable Control dropdown */}
          <div className="relative">
            <label className="mb-1 block text-sm font-medium text-gray-700">
              Link to Control <span className="text-xs font-normal text-gray-400">(optional)</span>
            </label>
            <div className="relative">
              <input
                ref={controlInputRef}
                type="text"
                placeholder={selectedControl ? `${selectedControl.controlId} — ${selectedControl.controlTitle}` : 'Search controls…'}
                value={controlSearch}
                onFocus={() => setControlDropdownOpen(true)}
                onChange={(e) => { setControlSearch(e.target.value); setControlDropdownOpen(true); }}
                className="w-full rounded-md border border-gray-300 px-3 py-2 pr-8 text-sm focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500"
              />
              {selectedControlId && (
                <button
                  type="button"
                  onClick={() => { setSelectedControlId(''); setControlSearch(''); }}
                  className="absolute right-2 top-1/2 -translate-y-1/2 text-gray-400 hover:text-gray-600"
                  aria-label="Clear control selection"
                >
                  ✕
                </button>
              )}
            </div>

            {controlDropdownOpen && (
              <div
                className="absolute z-20 mt-1 max-h-48 w-full overflow-y-auto rounded-md border border-gray-200 bg-white shadow-lg"
                onMouseDown={(e) => e.preventDefault()}
              >
                {filteredControls().length === 0 ? (
                  <div className="px-3 py-2 text-sm text-gray-400">
                    {controls.length === 0 ? 'Loading controls…' : 'No controls match.'}
                  </div>
                ) : (
                  filteredControls().map((ctrl) => (
                    <button
                      key={ctrl.controlId}
                      type="button"
                      onClick={() => {
                        setSelectedControlId(ctrl.controlId);
                        setControlSearch('');
                        setControlDropdownOpen(false);
                      }}
                      className={`flex w-full items-center gap-2 px-3 py-2 text-left text-sm hover:bg-indigo-50 ${
                        selectedControlId === ctrl.controlId ? 'bg-indigo-50 font-medium' : ''
                      }`}
                    >
                      <span className="shrink-0 font-mono text-xs text-indigo-700">{ctrl.controlId}</span>
                      <span className="truncate text-gray-700">{ctrl.controlTitle}</span>
                    </button>
                  ))
                )}
              </div>
            )}
          </div>

          {/* Collection Method */}
          <div>
            <label className="mb-1 block text-sm font-medium text-gray-700">Collection Method</label>
            <select
              value={collectionMethod}
              onChange={(e) => setCollectionMethod(e.target.value as CollectionMethod)}
              className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500"
            >
              {COLLECTION_METHODS.map((m) => (
                <option key={m.value} value={m.value}>{m.label}</option>
              ))}
            </select>
          </div>

          {/* Description */}
          <div>
            <label className="mb-1 block text-sm font-medium text-gray-700">Description</label>
            <textarea
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              rows={3}
              placeholder="Optional description of this evidence artifact…"
              className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500"
            />
          </div>

          {/* Upload progress */}
          {uploading && (
            <div className="h-2 w-full overflow-hidden rounded-full bg-gray-100">
              <div
                className="h-full rounded-full bg-indigo-500 transition-all duration-300"
                style={{ width: `${progress}%` }}
              />
            </div>
          )}
        </div>

        {/* Footer */}
        <div className="flex items-center justify-end gap-3 border-t px-6 py-4">
          <button
            onClick={onClose}
            disabled={uploading}
            className="rounded-md border border-gray-300 px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 disabled:opacity-50"
          >
            Cancel
          </button>
          <button
            onClick={handleSubmit}
            disabled={!isValid || uploading}
            className="inline-flex items-center gap-2 rounded-md bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-700 disabled:opacity-50"
          >
            {uploading ? (
              <>
                <span className="h-4 w-4 animate-spin rounded-full border-2 border-white border-t-transparent" />
                Uploading…
              </>
            ) : 'Upload Evidence'}
          </button>
        </div>
      </div>
    </div>
  );
}
