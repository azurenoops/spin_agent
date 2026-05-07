import { useState, useEffect, useCallback } from 'react';
import { getControlEvidence, downloadEvidence, collectEvidence, deleteEvidence } from '../api/evidence';
import type { EvidenceArtifactDto, ControlEvidenceDto } from '../types/evidence';
import EvidenceUploadDialog from './EvidenceUploadDialog';

interface Props {
  systemId: string;
  controlId: string;
  controlImplementationId?: string;
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
  });
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

function CategoryBadge({ category }: { category: string }) {
  const color = CATEGORY_COLORS[category] ?? 'bg-gray-100 text-gray-600';
  const label = category.replace(/([A-Z])/g, ' $1').trim();
  return (
    <span className={`inline-block rounded-full px-2 py-0.5 text-xs font-medium ${color}`}>
      {label}
    </span>
  );
}

function EvidenceRow({
  item,
  systemId,
  inheritedFrom,
  onDeleted,
}: {
  item: EvidenceArtifactDto;
  systemId: string;
  inheritedFrom?: string;
  onDeleted?: () => void;
}) {
  const [downloading, setDownloading] = useState(false);

  const handleDownload = async () => {
    if (!item.fileName) return;
    setDownloading(true);
    try {
      const blob = await downloadEvidence(systemId, item.id);
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = item.fileName;
      document.body.appendChild(a);
      a.click();
      URL.revokeObjectURL(url);
      a.remove();
    } catch {
      // Download failed silently
    } finally {
      setDownloading(false);
    }
  };

  const handleDelete = async () => {
    if (!confirm(`Delete "${item.fileName ?? 'this evidence'}"? This action is soft-delete.`)) return;
    try {
      await deleteEvidence(systemId, item.id);
      onDeleted?.();
    } catch {
      // delete failed silently
    }
  };

  return (
    <div className="flex items-center gap-3 rounded-md border border-gray-200 bg-white px-3 py-2 text-sm">
      {/* File icon */}
      <div className="flex-shrink-0 text-gray-400">
        <svg className="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
          <path
            strokeLinecap="round"
            strokeLinejoin="round"
            strokeWidth={1.5}
            d="M7 21h10a2 2 0 002-2V9.414a1 1 0 00-.293-.707l-5.414-5.414A1 1 0 0012.586 3H7a2 2 0 00-2 2v14a2 2 0 002 2z"
          />
        </svg>
      </div>

      {/* Details */}
      <div className="min-w-0 flex-1">
        <div className="flex items-center gap-2">
          <span className="truncate font-medium text-gray-900">
            {item.fileName ?? 'Automated evidence'}
          </span>
          <CategoryBadge category={item.artifactCategory} />
          {item.source === 'Automated' && (
            <span className="inline-block rounded-full bg-emerald-100 px-2 py-0.5 text-xs font-medium text-emerald-700">
              Auto
            </span>
          )}
          {inheritedFrom && (
            <span className="inline-block rounded-full bg-indigo-100 px-2 py-0.5 text-xs font-medium text-indigo-700">
              Inherited from {inheritedFrom}
            </span>
          )}
        </div>
        <div className="mt-0.5 flex items-center gap-3 text-xs text-gray-500">
          <span>{item.uploadedBy}</span>
          <span>&middot;</span>
          <span>{formatDate(item.uploadedAt)}</span>
          {item.fileSizeBytes && (
            <>
              <span>&middot;</span>
              <span>{formatBytes(item.fileSizeBytes)}</span>
            </>
          )}
        </div>
        {item.description && (
          <p className="mt-1 text-xs text-gray-500 line-clamp-1">{item.description}</p>
        )}
      </div>

      <div className="flex flex-shrink-0 items-center gap-1">
        {/* Download button (only for manual evidence with files) */}
        {item.fileName && (
          <button
            onClick={handleDownload}
            disabled={downloading}
            className="rounded p-1 text-gray-400 hover:bg-gray-100 hover:text-gray-600 disabled:opacity-50"
            title="Download"
          >
            <svg className="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
              <path
                strokeLinecap="round"
                strokeLinejoin="round"
                strokeWidth={2}
                d="M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-4l-4 4m0 0l-4-4m4 4V4"
              />
            </svg>
          </button>
        )}
        {/* Delete button (only for manual evidence) */}
        {item.source === 'Manual' && onDeleted && (
          <button
            onClick={handleDelete}
            className="rounded p-1 text-gray-400 hover:bg-red-100 hover:text-red-600"
            title="Delete"
          >
            <svg className="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
              <path
                strokeLinecap="round"
                strokeLinejoin="round"
                strokeWidth={2}
                d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16"
              />
            </svg>
          </button>
        )}
      </div>
    </div>
  );
}

export default function EvidenceSection({ systemId, controlId, controlImplementationId }: Props) {
  const [evidence, setEvidence] = useState<ControlEvidenceDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [showUpload, setShowUpload] = useState(false);
  const [collecting, setCollecting] = useState(false);

  const fetchEvidence = useCallback(async () => {
    try {
      const data = await getControlEvidence(systemId, controlId);
      setEvidence(data);
    } catch {
      // silently fail — no evidence to show
      setEvidence({ direct: [], inherited: [], automated: [] });
    } finally {
      setLoading(false);
    }
  }, [systemId, controlId]);

  useEffect(() => {
    fetchEvidence();
  }, [fetchEvidence]);

  const totalCount =
    (evidence?.direct.length ?? 0) +
    (evidence?.inherited.length ?? 0) +
    (evidence?.automated.length ?? 0);

  const handleCollect = async () => {
    setCollecting(true);
    try {
      await collectEvidence(systemId, controlId);
      await fetchEvidence();
    } catch {
      // collection failed silently
    } finally {
      setCollecting(false);
    }
  };

  return (
    <div className="mt-4 border-t border-gray-200 pt-4">
      <div className="flex items-center justify-between">
        <h4 className="text-sm font-semibold text-gray-700">
          Evidence
          {totalCount > 0 && (
            <span className="ml-1.5 inline-flex h-5 min-w-[20px] items-center justify-center rounded-full bg-indigo-100 px-1.5 text-xs font-medium text-indigo-700">
              {totalCount}
            </span>
          )}
        </h4>
        <div className="flex items-center gap-2">
          <button
            onClick={handleCollect}
            disabled={collecting}
            className="inline-flex items-center gap-1 rounded-md border border-gray-300 px-3 py-1.5 text-xs font-medium text-gray-700 hover:bg-gray-50 disabled:opacity-50"
          >
            {collecting ? (
              <svg className="h-3.5 w-3.5 animate-spin" fill="none" viewBox="0 0 24 24">
                <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z" />
              </svg>
            ) : (
              <svg className="h-3.5 w-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15" />
              </svg>
            )}
            {collecting ? 'Collecting...' : 'Collect Evidence'}
          </button>
          <button
            onClick={() => setShowUpload(true)}
            className="inline-flex items-center gap-1 rounded-md bg-indigo-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-indigo-700"
          >
            <svg className="h-3.5 w-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 4v16m8-8H4" />
            </svg>
            Attach Evidence
          </button>
        </div>
      </div>

      {loading ? (
        <p className="mt-3 text-xs text-gray-400">Loading evidence...</p>
      ) : totalCount === 0 ? (
        <p className="mt-3 text-xs text-gray-400">No evidence attached to this control.</p>
      ) : (
        <div className="mt-3 space-y-2">
          {/* Direct evidence */}
          {evidence!.direct.map((item) => (
            <EvidenceRow key={item.id} item={item} systemId={systemId} onDeleted={fetchEvidence} />
          ))}

          {/* Inherited evidence */}
          {evidence!.inherited.map((item) => (
            <EvidenceRow
              key={item.id}
              item={item}
              systemId={systemId}
              inheritedFrom={item.inheritedFromCapability}
              onDeleted={fetchEvidence}
            />
          ))}

          {/* Automated evidence */}
          {evidence!.automated.map((item) => (
            <EvidenceRow key={item.id} item={item} systemId={systemId} />
          ))}
        </div>
      )}

      {showUpload && (
        <EvidenceUploadDialog
          systemId={systemId}
          controlImplementationId={controlImplementationId}
          onClose={() => setShowUpload(false)}
          onUploaded={() => {
            setShowUpload(false);
            fetchEvidence();
          }}
        />
      )}
    </div>
  );
}
