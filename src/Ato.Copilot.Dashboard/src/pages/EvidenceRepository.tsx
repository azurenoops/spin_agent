import { useState, useCallback } from 'react';
import { useParams } from 'react-router-dom';
import { usePolling } from '../hooks/usePolling';
import { listEvidence, getEvidenceSummary, downloadEvidence, deleteEvidence } from '../api/evidence';
import type { EvidenceArtifactDto, EvidenceSummaryDto, ArtifactCategory, EvidenceSource } from '../types/evidence';
import EvidenceUploadDialog from '../components/EvidenceUploadDialog';
import EvidenceDetailPanel from '../components/EvidenceDetailPanel';

// ─── Helpers ────────────────────────────────────────────────────────────────

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

const CATEGORIES: { value: ArtifactCategory | ''; label: string }[] = [
  { value: '', label: 'All Categories' },
  { value: 'Screenshot', label: 'Screenshot' },
  { value: 'ScanResult', label: 'Scan Result' },
  { value: 'ConfigurationExport', label: 'Config Export' },
  { value: 'PolicyDocument', label: 'Policy Document' },
  { value: 'AuditLog', label: 'Audit Log' },
  { value: 'TestResult', label: 'Test Result' },
  { value: 'Other', label: 'Other' },
];

const SOURCES: { value: EvidenceSource | ''; label: string }[] = [
  { value: '', label: 'All Sources' },
  { value: 'Manual', label: 'Manual' },
  { value: 'Automated', label: 'Automated' },
];

// ─── Component ──────────────────────────────────────────────────────────────

export default function EvidenceRepository() {
  const { id: systemId } = useParams<{ id: string }>();
  const [search, setSearch] = useState('');
  const [categoryFilter, setCategoryFilter] = useState('');
  const [sourceFilter, setSourceFilter] = useState('');
  const [familyFilter, setFamilyFilter] = useState('');
  const [page, setPage] = useState(1);
  const [sortBy, setSortBy] = useState('uploadedAt');
  const [sortOrder, setSortOrder] = useState<'asc' | 'desc'>('desc');
  const [showUpload, setShowUpload] = useState(false);
  const [selectedId, setSelectedId] = useState<string | null>(null);

  const PAGE_SIZE = 50;

  const fetchEvidence = useCallback(async () => {
    if (!systemId) return { items: [], totalCount: 0, page: 1, pageSize: PAGE_SIZE };
    return listEvidence({
      systemId,
      page,
      pageSize: PAGE_SIZE,
      search: search || undefined,
      controlFamily: familyFilter || undefined,
      category: (categoryFilter as ArtifactCategory) || undefined,
      source: (sourceFilter as EvidenceSource) || undefined,
      sortBy: sortBy as 'uploadedAt' | 'fileName' | 'controlId' | 'category',
      sortOrder,
    });
  }, [systemId, page, search, familyFilter, categoryFilter, sourceFilter, sortBy, sortOrder]);

  const { data: evidenceData, refresh } = usePolling(fetchEvidence, 30000);

  const fetchSummary = useCallback(async () => {
    if (!systemId) return null;
    return getEvidenceSummary(systemId);
  }, [systemId]);

  const { data: summary } = usePolling<EvidenceSummaryDto | null>(fetchSummary, 60000);

  const items: EvidenceArtifactDto[] = evidenceData?.items ?? [];
  const totalCount = evidenceData?.totalCount ?? 0;
  const totalPages = Math.max(1, Math.ceil(totalCount / PAGE_SIZE));

  const handleSort = (col: string) => {
    if (sortBy === col) {
      setSortOrder((prev) => (prev === 'asc' ? 'desc' : 'asc'));
    } else {
      setSortBy(col);
      setSortOrder('desc');
    }
    setPage(1);
  };

  const SortIcon = ({ col }: { col: string }) => {
    if (sortBy !== col) return null;
    return (
      <svg className="ml-1 inline h-3 w-3" fill="none" viewBox="0 0 24 24" stroke="currentColor">
        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
          d={sortOrder === 'asc' ? 'M5 15l7-7 7 7' : 'M19 9l-7 7-7-7'} />
      </svg>
    );
  };

  const handleDownload = async (item: EvidenceArtifactDto) => {
    if (!systemId || !item.fileName) return;
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
      // download failed silently
    }
  };

  if (!systemId) return null;

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-gray-900">Evidence Repository</h1>
          <p className="mt-1 text-sm text-gray-500">
            All evidence artifacts for this system — manual uploads and automated collections.
          </p>
        </div>
        <button
          onClick={() => setShowUpload(true)}
          className="inline-flex items-center gap-2 rounded-md bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-700"
        >
          <svg className="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 4v16m8-8H4" />
          </svg>
          Upload Evidence
        </button>
      </div>

      {/* Summary Bar */}
      {summary && (
        <div className="grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-5">
          <SummaryCard label="Total Evidence" value={summary.totalCount} />
          <SummaryCard label="Manual" value={summary.manualCount} color="blue" />
          <SummaryCard label="Automated" value={summary.automatedCount} color="green" />
          <SummaryCard label="Controls Covered" value={`${summary.controlsWithEvidence}/${summary.totalControls}`} />
          <SummaryCard label="Coverage" value={`${summary.coveragePercentage.toFixed(1)}%`} color={summary.coveragePercentage >= 80 ? 'green' : summary.coveragePercentage >= 50 ? 'amber' : 'red'} />
        </div>
      )}

      {/* Filters */}
      <div className="flex flex-wrap items-center gap-3">
        <div className="relative flex-1 min-w-[200px]">
          <svg className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-gray-400" fill="none" viewBox="0 0 24 24" stroke="currentColor">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z" />
          </svg>
          <input
            type="text"
            placeholder="Search by filename, control, or description..."
            value={search}
            onChange={(e) => { setSearch(e.target.value); setPage(1); }}
            className="w-full rounded-md border border-gray-300 py-2 pl-10 pr-3 text-sm focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500"
          />
        </div>
        <input
          type="text"
          placeholder="Family (e.g., AC)"
          value={familyFilter}
          onChange={(e) => { setFamilyFilter(e.target.value.toUpperCase()); setPage(1); }}
          className="w-24 rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500"
        />
        <select
          value={categoryFilter}
          onChange={(e) => { setCategoryFilter(e.target.value); setPage(1); }}
          className="rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500"
        >
          {CATEGORIES.map((c) => (
            <option key={c.value} value={c.value}>{c.label}</option>
          ))}
        </select>
        <select
          value={sourceFilter}
          onChange={(e) => { setSourceFilter(e.target.value); setPage(1); }}
          className="rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500"
        >
          {SOURCES.map((s) => (
            <option key={s.value} value={s.value}>{s.label}</option>
          ))}
        </select>
      </div>

      {/* Table */}
      <div className="overflow-hidden rounded-lg border border-gray-200 bg-white shadow-sm">
        <table className="w-full text-sm">
          <thead className="bg-gray-50">
            <tr>
              <th className="px-4 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500 cursor-pointer" onClick={() => handleSort('fileName')}>
                File <SortIcon col="fileName" />
              </th>
              <th className="px-4 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500">Source</th>
              <th className="px-4 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500 cursor-pointer" onClick={() => handleSort('category')}>
                Category <SortIcon col="category" />
              </th>
              <th className="px-4 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500 cursor-pointer" onClick={() => handleSort('controlId')}>
                Control <SortIcon col="controlId" />
              </th>
              <th className="px-4 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500">Size</th>
              <th className="px-4 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500">Uploader</th>
              <th className="px-4 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500 cursor-pointer" onClick={() => handleSort('uploadedAt')}>
                Date <SortIcon col="uploadedAt" />
              </th>
              <th className="px-4 py-3 w-10" />
            </tr>
          </thead>
          <tbody className="divide-y divide-gray-100">
            {items.length === 0 ? (
              <tr>
                <td colSpan={8} className="px-4 py-12 text-center text-gray-400">
                  No evidence found. Upload evidence or adjust your filters.
                </td>
              </tr>
            ) : items.map((item) => (
              <tr
                key={item.id}
                className={`hover:bg-gray-50 cursor-pointer ${selectedId === item.id ? 'bg-indigo-50' : ''}`}
                onClick={() => setSelectedId(item.id)}
              >
                <td className="whitespace-nowrap px-4 py-3 font-medium text-gray-900">
                  {item.fileName ?? <span className="italic text-gray-400">Automated</span>}
                </td>
                <td className="whitespace-nowrap px-4 py-3">
                  <span className={`inline-block rounded-full px-2 py-0.5 text-xs font-medium ${
                    item.source === 'Automated' ? 'bg-emerald-100 text-emerald-700' : 'bg-indigo-100 text-indigo-700'
                  }`}>
                    {item.source}
                  </span>
                </td>
                <td className="whitespace-nowrap px-4 py-3">
                  <span className={`inline-block rounded-full px-2 py-0.5 text-xs font-medium ${
                    CATEGORY_COLORS[item.artifactCategory] ?? 'bg-gray-100 text-gray-600'
                  }`}>
                    {item.artifactCategory.replace(/([A-Z])/g, ' $1').trim()}
                  </span>
                </td>
                <td className="whitespace-nowrap px-4 py-3 text-gray-600">
                  {item.controlId ?? '—'}
                </td>
                <td className="whitespace-nowrap px-4 py-3 text-gray-500">
                  {formatBytes(item.fileSizeBytes)}
                </td>
                <td className="whitespace-nowrap px-4 py-3 text-gray-500">{item.uploadedBy}</td>
                <td className="whitespace-nowrap px-4 py-3 text-gray-500">{formatDate(item.uploadedAt)}</td>
                <td className="whitespace-nowrap px-4 py-3">
                  <div className="flex items-center gap-1">
                    {item.fileName && (
                      <button
                        onClick={(e) => { e.stopPropagation(); handleDownload(item); }}
                        className="rounded p-1 text-gray-400 hover:bg-gray-100 hover:text-gray-600"
                        title="Download"
                      >
                        <svg className="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-4l-4 4m0 0l-4-4m4 4V4" />
                        </svg>
                      </button>
                    )}
                    {item.source === 'Manual' && (
                      <button
                        onClick={async (e) => {
                          e.stopPropagation();
                          if (!confirm(`Delete "${item.fileName ?? 'this evidence'}"?`)) return;
                          await deleteEvidence(systemId, item.id);
                          refresh();
                        }}
                        className="rounded p-1 text-gray-400 hover:bg-red-100 hover:text-red-600"
                        title="Delete"
                      >
                        <svg className="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
                        </svg>
                      </button>
                    )}
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>

        {/* Pagination */}
        {totalPages > 1 && (
          <div className="flex items-center justify-between border-t border-gray-200 bg-gray-50 px-4 py-3">
            <p className="text-sm text-gray-500">
              Showing {((page - 1) * PAGE_SIZE) + 1}–{Math.min(page * PAGE_SIZE, totalCount)} of {totalCount}
            </p>
            <div className="flex gap-2">
              <button
                onClick={() => setPage((p) => Math.max(1, p - 1))}
                disabled={page === 1}
                className="rounded-md border border-gray-300 px-3 py-1 text-sm text-gray-600 hover:bg-gray-100 disabled:opacity-50"
              >
                Previous
              </button>
              <button
                onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
                disabled={page >= totalPages}
                className="rounded-md border border-gray-300 px-3 py-1 text-sm text-gray-600 hover:bg-gray-100 disabled:opacity-50"
              >
                Next
              </button>
            </div>
          </div>
        )}
      </div>

      {/* Upload Dialog */}
      {showUpload && (
        <EvidenceUploadDialog
          systemId={systemId}
          onClose={() => setShowUpload(false)}
          onUploaded={() => {
            setShowUpload(false);
            refresh();
          }}
        />
      )}

      {/* Detail Panel */}
      {selectedId && (
        <EvidenceDetailPanel
          systemId={systemId}
          evidenceId={selectedId}
          onClose={() => setSelectedId(null)}
          onActionComplete={() => {
            setSelectedId(null);
            refresh();
          }}
        />
      )}
    </div>
  );
}

// ─── Summary Card ───────────────────────────────────────────────────────────

function SummaryCard({
  label,
  value,
  color,
}: {
  label: string;
  value: number | string;
  color?: 'blue' | 'green' | 'amber' | 'red';
}) {
  const textColor = color === 'green' ? 'text-green-600'
    : color === 'blue' ? 'text-indigo-600'
    : color === 'amber' ? 'text-amber-600'
    : color === 'red' ? 'text-red-600'
    : 'text-gray-900';

  return (
    <div className="rounded-lg border border-gray-200 bg-white px-4 py-3">
      <p className="text-xs font-medium uppercase tracking-wider text-gray-500">{label}</p>
      <p className={`mt-1 text-xl font-semibold ${textColor}`}>{value}</p>
    </div>
  );
}
