import { useState, useEffect } from 'react';
import { getEvidence, downloadEvidence, deleteEvidence, replaceEvidence, downloadEvidenceVersion } from '../api/evidence';
import type { EvidenceArtifactDto, EvidenceVersionDto } from '../types/evidence';

interface Props {
  systemId: string;
  evidenceId: string;
  onClose: () => void;
  onActionComplete: () => void;
}

function formatBytes(bytes: number | null): string {
  if (!bytes) return '—';
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

function formatDate(dt: string): string {
  return new Date(dt).toLocaleDateString('en-US', {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  });
}

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div>
      <h3 className="mb-2 text-xs font-medium uppercase tracking-wider text-gray-500">{title}</h3>
      {children}
    </div>
  );
}

function InfoRow({ label, value }: { label: string; value: React.ReactNode }) {
  return (
    <div className="flex items-start justify-between py-1">
      <span className="text-sm text-gray-500">{label}</span>
      <span className="text-sm font-medium text-gray-900 text-right">{value}</span>
    </div>
  );
}

const CATEGORY_COLORS: Record<string, string> = {
  Screenshot: 'bg-purple-100 text-purple-700',
  ScanResult: 'bg-indigo-100 text-indigo-700',
  ConfigurationExport: 'bg-teal-100 text-teal-700',
  PolicyDocument: 'bg-amber-100 text-amber-700',
  AuditLog: 'bg-gray-100 text-gray-700',
  TestResult: 'bg-green-100 text-green-700',
  Other: 'bg-gray-100 text-gray-600',
};

export default function EvidenceDetailPanel({ systemId, evidenceId, onClose, onActionComplete }: Props) {
  const [detail, setDetail] = useState<EvidenceArtifactDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [downloading, setDownloading] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const [showReplace, setShowReplace] = useState(false);
  const [replaceFile, setReplaceFile] = useState<File | null>(null);
  const [replacing, setReplacing] = useState(false);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setError(null);
    getEvidence(systemId, evidenceId)
      .then((data) => {
        if (!cancelled) setDetail(data);
      })
      .catch(() => {
        if (!cancelled) setError('Failed to load evidence details.');
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => { cancelled = true; };
  }, [systemId, evidenceId]);

  const handleDownload = async () => {
    if (!detail?.fileName) return;
    setDownloading(true);
    try {
      const blob = await downloadEvidence(systemId, evidenceId);
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = detail.fileName;
      document.body.appendChild(a);
      a.click();
      URL.revokeObjectURL(url);
      a.remove();
    } catch {
      // download failed
    } finally {
      setDownloading(false);
    }
  };

  const handleDelete = async () => {
    if (!confirm(`Delete "${detail?.fileName ?? 'this evidence'}"? This cannot be undone.`)) return;
    setDeleting(true);
    try {
      await deleteEvidence(systemId, evidenceId);
      onActionComplete();
      onClose();
    } catch {
      setError('Failed to delete evidence.');
    } finally {
      setDeleting(false);
    }
  };

  const handleReplace = async () => {
    if (!replaceFile) return;
    setReplacing(true);
    try {
      await replaceEvidence({ systemId, evidenceId, file: replaceFile });
      setShowReplace(false);
      setReplaceFile(null);
      // Reload detail to show updated file + version history
      const data = await getEvidence(systemId, evidenceId);
      setDetail(data);
      onActionComplete();
    } catch {
      setError('Failed to replace evidence.');
    } finally {
      setReplacing(false);
    }
  };

  const handleVersionDownload = async (versionId: string, fileName: string) => {
    try {
      const blob = await downloadEvidenceVersion(systemId, evidenceId, versionId);
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = fileName;
      document.body.appendChild(a);
      a.click();
      URL.revokeObjectURL(url);
      a.remove();
    } catch {
      // download failed
    }
  };

  const isPreviewable = detail?.contentType?.startsWith('image/') ||
    detail?.contentType === 'application/pdf';

  return (
    <div className="fixed inset-y-0 right-0 z-50 w-[480px] overflow-y-auto border-l border-gray-200 bg-white shadow-xl">
      {/* Header */}
      <div className="sticky top-0 z-10 flex items-center justify-between border-b bg-white px-6 py-4">
        <h2 className="truncate text-lg font-semibold text-gray-900">
          {detail?.fileName ?? 'Evidence Detail'}
        </h2>
        <button onClick={onClose} className="text-gray-400 hover:text-gray-600">
          <svg className="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
          </svg>
        </button>
      </div>

      {/* Body */}
      <div className="space-y-6 px-6 py-4">
        {loading && <p className="text-sm text-gray-400">Loading...</p>}
        {error && <p className="text-sm text-red-600">{error}</p>}
        {detail && (
          <>
            {/* Preview */}
            {isPreviewable && detail.source === 'Manual' && (
              <Section title="Preview">
                {detail.contentType?.startsWith('image/') ? (
                  <img
                    src={`/api/dashboard/systems/${systemId}/evidence/${evidenceId}/download`}
                    alt={detail.fileName ?? 'Evidence preview'}
                    className="max-h-64 w-full rounded border border-gray-200 object-contain"
                  />
                ) : (
                  <iframe
                    src={`/api/dashboard/systems/${systemId}/evidence/${evidenceId}/download`}
                    title="PDF Preview"
                    className="h-80 w-full rounded border border-gray-200"
                  />
                )}
              </Section>
            )}

            {/* Actions */}
            <div className="flex gap-2">
              {detail.fileName && (
                <button
                  onClick={handleDownload}
                  disabled={downloading}
                  className="inline-flex flex-1 items-center justify-center gap-2 rounded-md bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-700 disabled:opacity-50"
                >
                  <svg className="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-4l-4 4m0 0l-4-4m4 4V4" />
                  </svg>
                  {downloading ? 'Downloading...' : 'Download'}
                </button>
              )}
              {detail.source === 'Manual' && (
                <>
                  <button
                    onClick={() => setShowReplace(true)}
                    className="inline-flex items-center justify-center gap-1 rounded-md border border-gray-300 px-3 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50"
                  >
                    <svg className="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15" />
                    </svg>
                    Replace
                  </button>
                  <button
                    onClick={handleDelete}
                    disabled={deleting}
                    className="inline-flex items-center justify-center gap-1 rounded-md border border-red-300 px-3 py-2 text-sm font-medium text-red-700 hover:bg-red-50 disabled:opacity-50"
                  >
                    <svg className="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
                    </svg>
                    {deleting ? 'Deleting...' : 'Delete'}
                  </button>
                </>
              )}
            </div>

            {/* Replace File Picker */}
            {showReplace && (
              <div className="rounded-md border border-indigo-200 bg-indigo-50 p-4">
                <p className="mb-2 text-sm font-medium text-gray-700">Select replacement file:</p>
                <input
                  type="file"
                  onChange={(e) => setReplaceFile(e.target.files?.[0] ?? null)}
                  className="block w-full text-sm text-gray-700"
                />
                <div className="mt-3 flex gap-2">
                  <button
                    onClick={handleReplace}
                    disabled={!replaceFile || replacing}
                    className="rounded-md bg-indigo-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-indigo-700 disabled:opacity-50"
                  >
                    {replacing ? 'Replacing...' : 'Upload Replacement'}
                  </button>
                  <button
                    onClick={() => { setShowReplace(false); setReplaceFile(null); }}
                    className="rounded-md border border-gray-300 px-3 py-1.5 text-sm text-gray-600 hover:bg-gray-50"
                  >
                    Cancel
                  </button>
                </div>
              </div>
            )}

            {/* Metadata */}
            <Section title="Metadata">
              <div className="space-y-1">
                <InfoRow label="Source" value={
                  <span className={`inline-block rounded-full px-2 py-0.5 text-xs font-medium ${
                    detail.source === 'Automated' ? 'bg-emerald-100 text-emerald-700' : 'bg-indigo-100 text-indigo-700'
                  }`}>
                    {detail.source}
                  </span>
                } />
                <InfoRow label="Category" value={
                  <span className={`inline-block rounded-full px-2 py-0.5 text-xs font-medium ${
                    CATEGORY_COLORS[detail.artifactCategory] ?? 'bg-gray-100 text-gray-600'
                  }`}>
                    {detail.artifactCategory.replace(/([A-Z])/g, ' $1').trim()}
                  </span>
                } />
                {detail.collectionMethod && (
                  <InfoRow label="Collection Method" value={detail.collectionMethod.replace(/([A-Z])/g, ' $1').trim()} />
                )}
                {detail.controlId && <InfoRow label="Control" value={detail.controlId} />}
                {detail.capabilityName && <InfoRow label="Capability" value={detail.capabilityName} />}
                <InfoRow label="Uploaded By" value={detail.uploadedBy} />
                <InfoRow label="Uploaded At" value={formatDate(detail.uploadedAt)} />
                {detail.fileSizeBytes && <InfoRow label="File Size" value={formatBytes(detail.fileSizeBytes)} />}
                {detail.contentType && <InfoRow label="Content Type" value={detail.contentType} />}
              </div>
            </Section>

            {/* Description */}
            {detail.description && (
              <Section title="Description">
                <p className="text-sm text-gray-700">{detail.description}</p>
              </Section>
            )}

            {/* Integrity */}
            {detail.contentHash && (
              <Section title="Integrity">
                <div className="rounded-md bg-gray-50 p-3">
                  <p className="text-xs text-gray-500">SHA-256</p>
                  <p className="mt-1 break-all font-mono text-xs text-gray-700">{detail.contentHash}</p>
                </div>
              </Section>
            )}

            {/* Version History */}
            {detail.versions && detail.versions.length > 0 && (
              <Section title="Version History">
                <div className="space-y-2">
                  {detail.versions.map((v: EvidenceVersionDto) => (
                    <div key={v.id} className="rounded-md border border-gray-200 px-3 py-2 text-sm">
                      <div className="flex items-center justify-between">
                        <span className="font-medium text-gray-900">{v.fileName}</span>
                        <div className="flex items-center gap-2">
                          <span className="text-xs text-gray-500">{formatBytes(v.fileSizeBytes)}</span>
                          {!v.isFilePurged && (
                            <button
                              onClick={() => handleVersionDownload(v.id, v.fileName)}
                              className="rounded p-1 text-gray-400 hover:bg-gray-100 hover:text-gray-600"
                              title="Download this version"
                            >
                              <svg className="h-3.5 w-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-4l-4 4m0 0l-4-4m4 4V4" />
                              </svg>
                            </button>
                          )}
                        </div>
                      </div>
                      <div className="mt-1 flex items-center gap-2 text-xs text-gray-500">
                        <span>Replaced by {v.replacedBy}</span>
                        <span>&middot;</span>
                        <span>{formatDate(v.replacedAt)}</span>
                        {v.isFilePurged && (
                          <span className="inline-block rounded-full bg-red-100 px-2 py-0.5 text-xs text-red-600">Purged</span>
                        )}
                      </div>
                    </div>
                  ))}
                </div>
              </Section>
            )}
          </>
        )}
      </div>
    </div>
  );
}
