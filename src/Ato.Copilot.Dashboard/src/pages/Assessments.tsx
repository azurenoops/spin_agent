import { useState, useCallback, useEffect } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import Markdown from 'react-markdown';
import remarkGfm from 'remark-gfm';
import { usePolling } from '../hooks/usePolling';
import { useSystemContext } from '../components/layout/SystemLayout';
import { getAssessments, runAssessment, getAssessmentDetail } from '../api/assessments';
import { getAssessmentComponentRisks } from '../api/components';
import { generateSap, getLatestSap, finalizeSap } from '../api/sap';
import { createSar, getLatestSar } from '../api/sar';
import type { AssessmentListItem, AssessmentDetail, AssessmentFinding } from '../api/assessments';
import type { AssessmentComponentRisks, ComponentRiskSummary } from '../types/dashboard';
import type { SapResponse } from '../api/sap';
import type { SarResponse } from '../api/sar';

// ─── Helpers ────────────────────────────────────────────────────────────────

function StatusBadge({ status }: { status: string }) {
  const colors: Record<string, string> = {
    completed: 'bg-green-100 text-green-700',
    running: 'bg-indigo-100 text-indigo-700',
    pending: 'bg-amber-100 text-amber-700',
    failed: 'bg-red-100 text-red-700',
  };
  return (
    <span className={`inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium ${colors[status.toLowerCase()] ?? 'bg-gray-100 text-gray-500'}`}>
      {status}
    </span>
  );
}

function ScoreBadge({ score }: { score: number }) {
  let color = 'bg-red-100 text-red-700';
  if (score >= 80) color = 'bg-green-100 text-green-700';
  else if (score >= 60) color = 'bg-amber-100 text-amber-700';
  return (
    <span className={`inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-semibold ${color}`}>
      {score}%
    </span>
  );
}

function formatDate(dt: string | null | undefined): string {
  if (!dt) return '—';
  return new Date(dt).toLocaleDateString('en-US', { year: 'numeric', month: 'short', day: 'numeric' });
}

function SeverityBadge({ severity }: { severity: string }) {
  const colors: Record<string, string> = {
    critical: 'bg-purple-100 text-purple-800',
    high: 'bg-red-100 text-red-700',
    medium: 'bg-amber-100 text-amber-700',
    low: 'bg-indigo-100 text-indigo-700',
  };
  return (
    <span className={`inline-flex items-center rounded px-1.5 py-0.5 text-[10px] font-semibold uppercase ${colors[severity.toLowerCase()] ?? 'bg-gray-100 text-gray-500'}`}>
      {severity}
    </span>
  );
}

// ─── Component ──────────────────────────────────────────────────────────────

export default function Assessments() {
  const { detail } = useSystemContext();
  const systemId = detail.systemId;
  const navigate = useNavigate();
  const [filter, setFilter] = useState('');
  const [showRunDialog, setShowRunDialog] = useState(false);
  const [runLoading, setRunLoading] = useState(false);
  const [runError, setRunError] = useState<string | null>(null);
  const [detailData, setDetailData] = useState<AssessmentDetail | null>(null);
  const [detailLoading, setDetailLoading] = useState(false);
  const [detailError, setDetailError] = useState<string | null>(null);
  const [findingFilter, setFindingFilter] = useState('');
  const [expandedFamilies, setExpandedFamilies] = useState<Set<string>>(new Set());
  const [componentRisks, setComponentRisks] = useState<AssessmentComponentRisks | null>(null);

  // SAP state
  const [showSapDialog, setShowSapDialog] = useState(false);
  const [sapLoading, setSapLoading] = useState(false);
  const [sapError, setSapError] = useState<string | null>(null);
  const [sapData, setSapData] = useState<SapResponse | null>(null);

  // SAR state
  const [showSarDialog, setShowSarDialog] = useState(false);
  const [sarLoading, setSarLoading] = useState(false);
  const [sarError, setSarError] = useState<string | null>(null);
  const [sarData, setSarData] = useState<SarResponse | null>(null);

  // View detail modals
  const [showSapView, setShowSapView] = useState(false);
  const [showSarView, setShowSarView] = useState(false);

  const fetchAssessments = useCallback(() => getAssessments(), []);
  const { data: allAssessments, loading, error, refresh } = usePolling<AssessmentListItem[]>(fetchAssessments, 30_000);

  // Fetch component risk summary when assessment detail is loaded
  useEffect(() => {
    if (!detailData?.assessmentId || !systemId) { setComponentRisks(null); return; }
    getAssessmentComponentRisks(systemId, detailData.assessmentId)
      .then(setComponentRisks)
      .catch(() => setComponentRisks(null));
  }, [detailData?.assessmentId, systemId]);

  // Fetch latest SAP & SAR status on mount
  useEffect(() => {
    if (!systemId) return;
    getLatestSap(systemId).then(setSapData).catch(() => setSapData(null));
    getLatestSar(systemId).then(setSarData).catch(() => setSarData(null));
  }, [systemId]);

  // Filter to current system
  const assessments = (allAssessments ?? []).filter(a => a.systemId === systemId);

  const handleRunAssessment = async () => {
    setRunLoading(true);
    setRunError(null);
    try {
      await runAssessment(systemId);
      setShowRunDialog(false);
      refresh();
    } catch (err: unknown) {
      if (err && typeof err === 'object' && 'response' in err) {
        const axiosErr = err as { response?: { data?: { error?: string } } };
        setRunError(axiosErr.response?.data?.error ?? 'Assessment failed');
      } else {
        setRunError(err instanceof Error ? err.message : 'Assessment failed');
      }
    } finally {
      setRunLoading(false);
    }
  };

  const handleGenerateSap = async () => {
    setSapLoading(true);
    setSapError(null);
    try {
      const sap = await generateSap(systemId);
      setSapData(sap);
      setShowSapDialog(false);
    } catch (err: unknown) {
      if (err && typeof err === 'object' && 'response' in err) {
        const axiosErr = err as { response?: { data?: { error?: string } } };
        setSapError(axiosErr.response?.data?.error ?? 'SAP generation failed');
      } else {
        setSapError(err instanceof Error ? err.message : 'SAP generation failed');
      }
    } finally {
      setSapLoading(false);
    }
  };

  const handleFinalizeSap = async () => {
    if (!sapData) return;
    setSapLoading(true);
    setSapError(null);
    try {
      const sap = await finalizeSap(systemId, sapData.sapId);
      setSapData(sap);
    } catch (err: unknown) {
      if (err && typeof err === 'object' && 'response' in err) {
        const axiosErr = err as { response?: { data?: { error?: string } } };
        setSapError(axiosErr.response?.data?.error ?? 'SAP finalization failed');
      } else {
        setSapError(err instanceof Error ? err.message : 'SAP finalization failed');
      }
    } finally {
      setSapLoading(false);
    }
  };

  const handleGenerateSar = async () => {
    setSarLoading(true);
    setSarError(null);
    try {
      const sar = await createSar(systemId, { title: `Security Assessment Report — ${detail.name}` });
      setSarData(sar);
      setShowSarDialog(false);
    } catch (err: unknown) {
      if (err && typeof err === 'object' && 'response' in err) {
        const axiosErr = err as { response?: { data?: { error?: string } } };
        setSarError(axiosErr.response?.data?.error ?? 'SAR generation failed');
      } else {
        setSarError(err instanceof Error ? err.message : 'SAR generation failed');
      }
    } finally {
      setSarLoading(false);
    }
  };

  const openDetail = async (assessmentId: string) => {
    setDetailData(null);
    setDetailError(null);
    setDetailLoading(true);
    setFindingFilter('');
    setExpandedFamilies(new Set());
    try {
      const detail = await getAssessmentDetail(assessmentId);
      setDetailData(detail);
    } catch {
      setDetailError('Failed to load assessment details');
    } finally {
      setDetailLoading(false);
    }
  };

  const toggleFamily = (code: string) => {
    setExpandedFamilies(prev => {
      const next = new Set(prev);
      if (next.has(code)) next.delete(code); else next.add(code);
      return next;
    });
  };

  const filtered = assessments.filter((a) => {
    if (!filter) return true;
    const q = filter.toLowerCase();
    return (
      (a.systemName?.toLowerCase().includes(q)) ||
      a.framework.toLowerCase().includes(q) ||
      a.status.toLowerCase().includes(q) ||
      a.scanType.toLowerCase().includes(q)
    );
  });

  return (
    <div className="space-y-6">
        {/* Header */}
        <div className="flex items-center justify-between">
          <div>
            <h2 className="text-xl font-bold text-gray-900">Compliance Assessments</h2>
            <p className="mt-1 text-sm text-gray-500">Assessments run against this system.</p>
          </div>
          <div className="flex items-center gap-3">
            <input
              type="text"
              placeholder="Filter assessments..."
              value={filter}
              onChange={(e) => setFilter(e.target.value)}
              className="rounded-md border border-gray-300 px-3 py-1.5 text-sm shadow-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
            />
            <button
              onClick={() => { setShowSapDialog(true); setSapError(null); }}
              className="inline-flex items-center gap-1.5 rounded-md bg-indigo-600 px-3.5 py-1.5 text-sm font-medium text-white shadow-sm hover:bg-indigo-700 transition-colors"
            >
              <svg className="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" />
              </svg>
              Generate SAP
            </button>
            <button
              onClick={() => { setShowSarDialog(true); setSarError(null); }}
              disabled={assessments.filter(a => a.status === 'Completed').length === 0}
              className="inline-flex items-center gap-1.5 rounded-md bg-emerald-600 px-3.5 py-1.5 text-sm font-medium text-white shadow-sm hover:bg-emerald-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
              title={assessments.filter(a => a.status === 'Completed').length === 0 ? 'Run at least one assessment first' : 'Generate Security Assessment Report'}
            >
              <svg className="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M9 17v-2m3 2v-4m3 4v-6m2 10H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" />
              </svg>
              Generate SAR
            </button>
            <button
              onClick={() => { setShowRunDialog(true); setRunError(null); }}
              className="inline-flex items-center gap-1.5 rounded-md bg-indigo-600 px-3.5 py-1.5 text-sm font-medium text-white shadow-sm hover:bg-indigo-700 transition-colors"
            >
              <svg className="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M14.752 11.168l-3.197-2.132A1 1 0 0010 9.87v4.263a1 1 0 001.555.832l3.197-2.132a1 1 0 000-1.664z" />
                <path strokeLinecap="round" strokeLinejoin="round" d="M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
              </svg>
              Run Assessment
            </button>
          </div>
        </div>

        {/* Summary cards */}
        {assessments.length > 0 && (
          <div className="grid grid-cols-4 gap-4">
            <div className="rounded-lg border border-gray-200 bg-white p-4">
              <p className="text-xs font-medium text-gray-500 uppercase tracking-wider">Total</p>
              <p className="mt-1 text-2xl font-bold text-gray-900">{assessments.length}</p>
            </div>
            <div className="rounded-lg border border-gray-200 bg-white p-4">
              <p className="text-xs font-medium text-gray-500 uppercase tracking-wider">Completed</p>
              <p className="mt-1 text-2xl font-bold text-green-600">{assessments.filter(a => a.status === 'Completed').length}</p>
            </div>
            <div className="rounded-lg border border-gray-200 bg-white p-4">
              <p className="text-xs font-medium text-gray-500 uppercase tracking-wider">Avg Score</p>
              <p className="mt-1 text-2xl font-bold text-indigo-600">
                {assessments.length > 0 ? Math.round(assessments.reduce((sum, a) => sum + a.complianceScore, 0) / assessments.length) : 0}%
              </p>
            </div>
            <div className="rounded-lg border border-gray-200 bg-white p-4">
              <p className="text-xs font-medium text-gray-500 uppercase tracking-wider">Total Findings</p>
              <p className="mt-1 text-2xl font-bold text-amber-600">{assessments.reduce((sum, a) => sum + a.totalFindings, 0)}</p>
            </div>
          </div>
        )}

        {/* SAP / SAR Status */}
        {(sapData || sarData) && (
          <div className="grid grid-cols-2 gap-4">
            {sapData && (
              <div className="rounded-lg border border-indigo-200 bg-indigo-50 p-4">
                <div className="flex items-center justify-between">
                  <div>
                    <p className="text-xs font-medium text-indigo-500 uppercase tracking-wider">Security Assessment Plan</p>
                    <p className="mt-1 text-sm font-semibold text-indigo-900">{sapData.title}</p>
                  </div>
                  <span className={`inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium ${sapData.status === 'Finalized' ? 'bg-green-100 text-green-700' : 'bg-amber-100 text-amber-700'}`}>
                    {sapData.status}
                  </span>
                </div>
                <div className="mt-2 flex items-center gap-4 text-xs text-indigo-600">
                  <span>{sapData.totalControls} controls</span>
                  <span>{sapData.baselineLevel} baseline</span>
                  <span>{new Date(sapData.generatedAt).toLocaleDateString()}</span>
                </div>
                <div className="mt-2 flex items-center gap-2">
                  <button
                    onClick={() => setShowSapView(true)}
                    className="inline-flex items-center gap-1 rounded-md bg-white border border-indigo-300 px-2.5 py-1 text-xs font-medium text-indigo-700 hover:bg-indigo-100 transition-colors"
                  >
                    <svg className="h-3.5 w-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                      <path strokeLinecap="round" strokeLinejoin="round" d="M15 12a3 3 0 11-6 0 3 3 0 016 0z" />
                      <path strokeLinecap="round" strokeLinejoin="round" d="M2.458 12C3.732 7.943 7.523 5 12 5c4.478 0 8.268 2.943 9.542 7-1.274 4.057-5.064 7-9.542 7-4.477 0-8.268-2.943-9.542-7z" />
                    </svg>
                    View
                  </button>
                  {sapData.status === 'Draft' && (
                  <button
                    onClick={() => void handleFinalizeSap()}
                    disabled={sapLoading}
                    className="inline-flex items-center rounded-md bg-indigo-600 px-2.5 py-1 text-xs font-medium text-white hover:bg-indigo-700 disabled:opacity-50 transition-colors"
                  >
                    {sapLoading ? 'Finalizing...' : 'Finalize SAP'}
                  </button>
                  )}
                </div>
                {sapError && (
                  <p className="mt-2 text-xs text-red-600">{sapError}</p>
                )}
              </div>
            )}
            {sarData && (
              <div className="rounded-lg border border-emerald-200 bg-emerald-50 p-4">
                <div className="flex items-center justify-between">
                  <div>
                    <p className="text-xs font-medium text-emerald-500 uppercase tracking-wider">Security Assessment Report</p>
                    <p className="mt-1 text-sm font-semibold text-emerald-900">{sarData.title}</p>
                  </div>
                  <span className={`inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium ${
                    sarData.status === 'Approved' ? 'bg-green-100 text-green-700' :
                    sarData.status === 'UnderReview' ? 'bg-indigo-100 text-indigo-700' :
                    'bg-amber-100 text-amber-700'
                  }`}>
                    {sarData.status === 'UnderReview' ? 'Under Review' : sarData.status}
                  </span>
                </div>
                <div className="mt-2 flex items-center gap-4 text-xs text-emerald-600">
                  <span>{sarData.satisfiedCount} satisfied</span>
                  <span>{sarData.notSatisfiedCount} not satisfied</span>
                  <span>{sarData.totalControlsAssessed} assessed</span>
                </div>
                <div className="mt-2">
                  <button
                    onClick={() => setShowSarView(true)}
                    className="inline-flex items-center gap-1 rounded-md bg-white border border-emerald-300 px-2.5 py-1 text-xs font-medium text-emerald-700 hover:bg-emerald-100 transition-colors"
                  >
                    <svg className="h-3.5 w-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                      <path strokeLinecap="round" strokeLinejoin="round" d="M15 12a3 3 0 11-6 0 3 3 0 016 0z" />
                      <path strokeLinecap="round" strokeLinejoin="round" d="M2.458 12C3.732 7.943 7.523 5 12 5c4.478 0 8.268 2.943 9.542 7-1.274 4.057-5.064 7-9.542 7-4.477 0-8.268-2.943-9.542-7z" />
                    </svg>
                    View
                  </button>
                </div>
              </div>
            )}
          </div>
        )}

        {/* Loading / error */}
        {loading && !assessments && (
          <div className="flex items-center justify-center py-16">
            <div className="h-8 w-8 animate-spin rounded-full border-4 border-indigo-500 border-t-transparent" />
          </div>
        )}
        {error && (
          <div className="rounded-md bg-red-50 p-4 text-sm text-red-700">{String(error)}</div>
        )}

        {/* Table */}
        {assessments && (
          <div className="overflow-hidden rounded-lg border border-gray-200 bg-white shadow-sm">
            <table className="min-w-full divide-y divide-gray-200 text-sm">
              <thead className="bg-gray-50">
                <tr>
                  <th className="px-4 py-3 text-left font-medium text-gray-500">System</th>
                  <th className="px-4 py-3 text-left font-medium text-gray-500">Framework</th>
                  <th className="px-4 py-3 text-left font-medium text-gray-500">Type</th>
                  <th className="px-4 py-3 text-center font-medium text-gray-500">Score</th>
                  <th className="px-4 py-3 text-center font-medium text-gray-500">Controls</th>
                  <th className="px-4 py-3 text-center font-medium text-gray-500">Findings</th>
                  <th className="px-4 py-3 text-left font-medium text-gray-500">Status</th>
                  <th className="px-4 py-3 text-left font-medium text-gray-500">Date</th>
                  <th className="px-4 py-3 text-left font-medium text-gray-500">By</th>
                  <th className="px-4 py-3 text-center font-medium text-gray-500">Actions</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100">
                {filtered.length === 0 ? (
                  <tr>
                    <td colSpan={10} className="px-4 py-12 text-center text-gray-400">
                      {filter ? 'No assessments match your filter.' : 'No assessments found.'}
                    </td>
                  </tr>
                ) : filtered.map((a) => (
                  <tr key={a.assessmentId} className="hover:bg-gray-50">
                    <td className="px-4 py-3 font-medium text-gray-900">
                      {a.systemId ? (
                        <Link to={`/systems/${a.systemId}`} className="text-indigo-600 hover:text-indigo-800 hover:underline">
                          {a.systemName ?? a.systemId}
                        </Link>
                      ) : (
                        <span className="text-gray-400">Unlinked</span>
                      )}
                    </td>
                    <td className="px-4 py-3 text-gray-600">{a.framework}</td>
                    <td className="px-4 py-3 text-gray-600 capitalize">{a.scanType}</td>
                    <td className="px-4 py-3 text-center"><ScoreBadge score={a.complianceScore} /></td>
                    <td className="px-4 py-3 text-center">
                      <span className="text-green-600">{a.passedControls}</span>
                      <span className="text-gray-400"> / </span>
                      <span className="text-red-600">{a.failedControls}</span>
                      <span className="text-gray-400"> / </span>
                      <span className="text-gray-600">{a.totalControls}</span>
                    </td>
                    <td className="px-4 py-3 text-center text-gray-600">{a.totalFindings}</td>
                    <td className="px-4 py-3"><StatusBadge status={a.status} /></td>
                    <td className="px-4 py-3 text-gray-500 whitespace-nowrap">{formatDate(a.assessedAt)}</td>
                    <td className="px-4 py-3 text-gray-500">{a.initiatedBy}</td>
                    <td className="px-4 py-3 text-center">
                      <button
                        onClick={() => void openDetail(a.assessmentId)}
                        className="inline-flex items-center gap-1 rounded-md px-2.5 py-1 text-xs font-medium text-indigo-600 hover:bg-indigo-50 transition-colors"
                      >
                        <svg className="h-3.5 w-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                          <path strokeLinecap="round" strokeLinejoin="round" d="M15 12a3 3 0 11-6 0 3 3 0 016 0z" />
                          <path strokeLinecap="round" strokeLinejoin="round" d="M2.458 12C3.732 7.943 7.523 5 12 5c4.478 0 8.268 2.943 9.542 7-1.274 4.057-5.064 7-9.542 7-4.477 0-8.268-2.943-9.542-7z" />
                        </svg>
                        View
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        {/* Assessment Detail Modal */}
        {(detailData || detailLoading || detailError) && (
          <div
            className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 backdrop-blur-sm"
            onClick={(e) => { if (e.target === e.currentTarget) { setDetailData(null); setDetailError(null); } }}
          >
            <div className="w-full max-w-4xl max-h-[90vh] rounded-xl bg-white shadow-2xl border border-gray-200 flex flex-col overflow-hidden">
              {/* Header */}
              <div className="flex items-center justify-between px-6 pt-5 pb-3 flex-shrink-0">
                <div>
                  <h3 className="text-lg font-semibold text-gray-900">Assessment Results</h3>
                  {detailData && (
                    <p className="text-sm text-gray-500 mt-0.5">
                      {detailData.systemName ?? detailData.systemId ?? 'Unknown'} &bull; {detailData.framework} &bull; {formatDate(detailData.assessedAt)}
                    </p>
                  )}
                </div>
                <button
                  type="button"
                  onClick={() => { setDetailData(null); setDetailError(null); }}
                  className="rounded-lg p-1 text-gray-400 hover:bg-gray-100 hover:text-gray-600 transition-colors"
                  aria-label="Close"
                >
                  <svg className="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                    <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" />
                  </svg>
                </button>
              </div>

              <div className="border-t border-gray-100" />

              {/* Body */}
              <div className="px-6 py-5 overflow-y-auto flex-1 space-y-5">
                {detailLoading && (
                  <div className="flex items-center justify-center py-16">
                    <div className="h-8 w-8 animate-spin rounded-full border-4 border-indigo-500 border-t-transparent" />
                  </div>
                )}
                {detailError && (
                  <div className="rounded-md bg-red-50 p-4 text-sm text-red-700">{detailError}</div>
                )}
                {detailData && (
                  <>
                    {/* Score + metrics cards */}
                    <div className="grid grid-cols-2 sm:grid-cols-4 gap-3">
                      <div className="rounded-lg border border-gray-200 bg-white p-3 text-center">
                        <p className="text-xs font-medium text-gray-500 uppercase tracking-wider">Score</p>
                        <p className={`mt-1 text-2xl font-bold ${detailData.complianceScore >= 80 ? 'text-green-600' : detailData.complianceScore >= 60 ? 'text-amber-600' : 'text-red-600'}`}>
                          {detailData.complianceScore}%
                        </p>
                      </div>
                      <div className="rounded-lg border border-gray-200 bg-white p-3 text-center">
                        <p className="text-xs font-medium text-gray-500 uppercase tracking-wider">Passed</p>
                        <p className="mt-1 text-2xl font-bold text-green-600">{detailData.passedControls}</p>
                        <p className="text-xs text-gray-400">of {detailData.totalControls}</p>
                      </div>
                      <div className="rounded-lg border border-gray-200 bg-white p-3 text-center">
                        <p className="text-xs font-medium text-gray-500 uppercase tracking-wider">Failed</p>
                        <p className="mt-1 text-2xl font-bold text-red-600">{detailData.failedControls}</p>
                      </div>
                      <div className="rounded-lg border border-gray-200 bg-white p-3 text-center">
                        <p className="text-xs font-medium text-gray-500 uppercase tracking-wider">Findings</p>
                        <p className="mt-1 text-2xl font-bold text-amber-600">{detailData.findings.length}</p>
                      </div>
                    </div>

                    {/* Severity breakdown */}
                    {(detailData.criticalCount > 0 || detailData.highCount > 0 || detailData.mediumCount > 0 || detailData.lowCount > 0) && (
                      <div className="flex flex-wrap gap-3">
                        {detailData.criticalCount > 0 && (
                          <span className="inline-flex items-center gap-1.5 rounded-full bg-purple-100 px-3 py-1 text-xs font-semibold text-purple-800">
                            Critical: {detailData.criticalCount}
                          </span>
                        )}
                        {detailData.highCount > 0 && (
                          <span className="inline-flex items-center gap-1.5 rounded-full bg-red-100 px-3 py-1 text-xs font-semibold text-red-700">
                            High: {detailData.highCount}
                          </span>
                        )}
                        {detailData.mediumCount > 0 && (
                          <span className="inline-flex items-center gap-1.5 rounded-full bg-amber-100 px-3 py-1 text-xs font-semibold text-amber-700">
                            Medium: {detailData.mediumCount}
                          </span>
                        )}
                        {detailData.lowCount > 0 && (
                          <span className="inline-flex items-center gap-1.5 rounded-full bg-indigo-100 px-3 py-1 text-xs font-semibold text-indigo-700">
                            Low: {detailData.lowCount}
                          </span>
                        )}
                      </div>
                    )}

                    {/* Executive Summary */}
                    {detailData.executiveSummary && (
                      <div className="rounded-lg border border-gray-200 bg-gray-50 p-4">
                        <h4 className="text-sm font-semibold text-gray-700 mb-2">Executive Summary</h4>
                        <div className="prose prose-sm max-w-none text-gray-600 [&_table]:w-full [&_table]:text-xs [&_th]:border [&_th]:border-gray-300 [&_th]:px-2 [&_th]:py-1 [&_th]:bg-gray-100 [&_td]:border [&_td]:border-gray-300 [&_td]:px-2 [&_td]:py-1 [&_h1]:text-base [&_h1]:font-bold [&_h2]:text-sm [&_h2]:font-semibold [&_h2]:mt-3 [&_h2]:mb-1 [&_strong]:font-semibold">
                          <Markdown remarkPlugins={[remarkGfm]}>{detailData.executiveSummary}</Markdown>
                        </div>
                      </div>
                    )}

                    {/* Family breakdown */}
                    {detailData.familyResults.length > 0 && (
                      <div>
                        <h4 className="text-sm font-semibold text-gray-700 mb-2">Control Family Breakdown</h4>
                        <div className="overflow-hidden rounded-lg border border-gray-200">
                          <table className="min-w-full divide-y divide-gray-200 text-xs">
                            <thead className="bg-gray-50">
                              <tr>
                                <th className="px-3 py-2 text-left font-medium text-gray-500">Family</th>
                                <th className="px-3 py-2 text-center font-medium text-gray-500">Passed</th>
                                <th className="px-3 py-2 text-center font-medium text-gray-500">Failed</th>
                                <th className="px-3 py-2 text-center font-medium text-gray-500">Score</th>
                              </tr>
                            </thead>
                            <tbody className="divide-y divide-gray-100 bg-white">
                              {detailData.familyResults
                                .sort((a, b) => a.complianceScore - b.complianceScore)
                                .map(f => (
                                  <tr key={f.familyCode} className="hover:bg-gray-50">
                                    <td className="px-3 py-2 font-medium text-gray-800">
                                      {f.familyCode}{f.familyName !== f.familyCode ? ` — ${f.familyName}` : ''}
                                    </td>
                                    <td className="px-3 py-2 text-center text-green-600">{f.passedControls}</td>
                                    <td className="px-3 py-2 text-center text-red-600">{f.failedControls}</td>
                                    <td className="px-3 py-2 text-center">
                                      <ScoreBadge score={f.complianceScore} />
                                    </td>
                                  </tr>
                                ))}
                            </tbody>
                          </table>
                        </div>
                      </div>
                    )}

                    {/* Component Risk Summary (Feature 040 US6) */}
                    {componentRisks && (componentRisks.componentRisks.length > 0 || componentRisks.unlinkedFindingCount > 0) && (
                      <div>
                        <h4 className="text-sm font-semibold text-gray-700 mb-2">Component Risk Summary</h4>
                        {componentRisks.componentRisks.length > 0 && (
                          <div className="overflow-hidden rounded-lg border border-gray-200 mb-3">
                            <table className="min-w-full divide-y divide-gray-200 text-xs">
                              <thead className="bg-gray-50">
                                <tr>
                                  <th className="px-3 py-2 text-left font-medium text-gray-500">Component</th>
                                  <th className="px-3 py-2 text-center font-medium text-gray-500">Type</th>
                                  <th className="px-3 py-2 text-center font-medium text-gray-500">Open Findings</th>
                                  <th className="px-3 py-2 text-center font-medium text-gray-500">Highest Severity</th>
                                  <th className="px-3 py-2 text-center font-medium text-gray-500">Overdue</th>
                                </tr>
                              </thead>
                              <tbody className="divide-y divide-gray-100 bg-white">
                                {componentRisks.componentRisks.map((cr: ComponentRiskSummary) => (
                                  <tr key={cr.componentId} className="hover:bg-gray-50">
                                    <td className="px-3 py-2 font-medium text-gray-800">{cr.componentName}</td>
                                    <td className="px-3 py-2 text-center text-gray-500">{cr.componentType}</td>
                                    <td className="px-3 py-2 text-center">{cr.openFindingCount}</td>
                                    <td className="px-3 py-2 text-center"><SeverityBadge severity={cr.highestSeverity} /></td>
                                    <td className="px-3 py-2 text-center text-red-600">{cr.overdueRemediationCount}</td>
                                  </tr>
                                ))}
                              </tbody>
                            </table>
                          </div>
                        )}
                        {componentRisks.unlinkedFindingCount > 0 && (
                          <div className="rounded-md bg-amber-50 border border-amber-200 p-3 text-sm text-amber-800">
                            <strong>{componentRisks.unlinkedFindingCount}</strong> finding{componentRisks.unlinkedFindingCount !== 1 ? 's' : ''} not linked to any component.
                            Import the affected resources as components to enable per-component tracking.
                          </div>
                        )}
                      </div>
                    )}

                    {/* Findings by control family */}
                    {detailData.findings.length > 0 && (
                      <div>
                        <div className="flex items-center justify-between mb-2">
                          <h4 className="text-sm font-semibold text-gray-700">
                            Findings by Control Family ({detailData.findings.length})
                          </h4>
                          <input
                            type="text"
                            placeholder="Filter findings..."
                            value={findingFilter}
                            onChange={(e) => setFindingFilter(e.target.value)}
                            className="rounded-md border border-gray-300 px-2.5 py-1 text-xs shadow-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500 w-48"
                          />
                        </div>
                        {(() => {
                          const fq = findingFilter.toLowerCase();
                          const filteredFindings = fq
                            ? detailData.findings.filter(f =>
                                (f.controlId?.toLowerCase().includes(fq)) ||
                                f.title.toLowerCase().includes(fq) ||
                                f.severity.toLowerCase().includes(fq) ||
                                f.description.toLowerCase().includes(fq))
                            : detailData.findings;

                          const grouped = filteredFindings.reduce<Record<string, AssessmentFinding[]>>((acc, f) => {
                            const family = f.controlFamily || 'Unknown';
                            (acc[family] ??= []).push(f);
                            return acc;
                          }, {});

                          const sortedFamilies = Object.entries(grouped)
                            .sort((a, b) => b[1].length - a[1].length);

                          return (
                            <div className="space-y-2">
                              {sortedFamilies.map(([family, items]) => (
                                <div key={family} className="rounded-lg border border-gray-200 overflow-hidden">
                                  <button
                                    onClick={() => toggleFamily(family)}
                                    className="flex w-full items-center justify-between px-4 py-2.5 bg-gray-50 hover:bg-gray-100 transition-colors text-left"
                                  >
                                    <span className="text-sm font-medium text-gray-800">
                                      {family} <span className="text-gray-400 font-normal">({items.length})</span>
                                    </span>
                                    <svg
                                      className={`h-4 w-4 text-gray-400 transition-transform ${expandedFamilies.has(family) ? 'rotate-180' : ''}`}
                                      fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}
                                    >
                                      <path strokeLinecap="round" strokeLinejoin="round" d="M19 9l-7 7-7-7" />
                                    </svg>
                                  </button>
                                  {expandedFamilies.has(family) && (
                                    <div className="divide-y divide-gray-100">
                                      {items.map(f => (
                                        <div key={f.findingId} className="px-4 py-2.5 text-xs">
                                          <div className="flex items-center gap-2">
                                            <SeverityBadge severity={f.severity} />
                                            <span className="font-mono text-gray-500">{f.controlId}</span>
                                            <span className="text-gray-800">{f.title}</span>
                                            {f.deviationId && f.deviationType && (
                                              <span className="inline-flex items-center rounded-full border border-dashed border-purple-300 bg-purple-50 px-2 py-0.5 text-[10px] font-medium text-purple-700">
                                                {f.deviationType === 'FalsePositive' ? 'False Positive' : 'Risk Accepted'}
                                              </span>
                                            )}
                                          </div>
                                          <p className="mt-1 text-gray-500 pl-1">{f.description}</p>
                                          {f.remediationGuidance && (
                                            <p className="mt-1 text-indigo-600 pl-1">Remediation: {f.remediationGuidance}</p>
                                          )}
                                        </div>
                                      ))}
                                    </div>
                                  )}
                                </div>
                              ))}
                              {sortedFamilies.length === 0 && (
                                <p className="text-sm text-gray-400 text-center py-4">No findings match your filter.</p>
                              )}
                            </div>
                          );
                        })()}
                      </div>
                    )}

                    {detailData.findings.length === 0 && (
                      <div className="rounded-lg border border-green-200 bg-green-50 p-4 text-center">
                        <p className="text-sm font-medium text-green-700">No findings — all assessed controls are compliant.</p>
                      </div>
                    )}

                    {/* Assessment metadata */}
                    <div className="rounded-md border border-gray-200 bg-gray-50 p-3 text-xs space-y-1">
                      <div className="flex justify-between">
                        <span className="text-gray-500">Assessment ID</span>
                        <span className="font-mono text-gray-600">{detailData.assessmentId.substring(0, 8)}...</span>
                      </div>
                      <div className="flex justify-between">
                        <span className="text-gray-500">Scan Type</span>
                        <span className="font-medium text-gray-800 capitalize">{detailData.scanType}</span>
                      </div>
                      <div className="flex justify-between">
                        <span className="text-gray-500">Initiated By</span>
                        <span className="font-medium text-gray-800">{detailData.initiatedBy ?? '—'}</span>
                      </div>
                      {detailData.completedAt && (
                        <div className="flex justify-between">
                          <span className="text-gray-500">Completed</span>
                          <span className="font-medium text-gray-800">{formatDate(detailData.completedAt)}</span>
                        </div>
                      )}
                    </div>
                  </>
                )}
              </div>

              {/* Footer */}
              <div className="border-t border-gray-100 px-6 py-3 bg-gray-50 flex justify-end flex-shrink-0">
                <button
                  type="button"
                  onClick={() => { setDetailData(null); setDetailError(null); }}
                  className="rounded-lg px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-200 transition-colors"
                >
                  Close
                </button>
              </div>
            </div>
          </div>
        )}

        {/* Run Assessment Dialog */}
        {showRunDialog && (
          <div
            className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 backdrop-blur-sm"
            onClick={(e) => { if (e.target === e.currentTarget) setShowRunDialog(false); }}
          >
            <div className="w-full max-w-md rounded-xl bg-white shadow-2xl border border-gray-200 overflow-hidden">
              {/* Header */}
              <div className="flex items-center justify-between px-6 pt-5 pb-3">
                <h3 className="text-lg font-semibold text-gray-900">Run Compliance Assessment</h3>
                <button
                  type="button"
                  onClick={() => setShowRunDialog(false)}
                  className="rounded-lg p-1 text-gray-400 hover:bg-gray-100 hover:text-gray-600 transition-colors"
                  aria-label="Close"
                >
                  <svg className="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                    <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" />
                  </svg>
                </button>
              </div>

              <div className="border-t border-gray-100" />

              {/* Body */}
              <div className="px-6 py-5 space-y-4">
                <p className="text-sm text-gray-600">
                  Run a NIST 800-53 compliance assessment against <strong>{detail.name}</strong>.
                </p>

                <div className="rounded-md border border-gray-200 bg-gray-50 p-3 text-xs space-y-1">
                  <div className="flex justify-between">
                    <span className="text-gray-500">System</span>
                    <span className="font-medium text-gray-800">{detail.name}{detail.acronym ? ` (${detail.acronym})` : ''}</span>
                  </div>
                  <div className="flex justify-between">
                    <span className="text-gray-500">Impact Level</span>
                    <span className="font-medium text-gray-800">{detail.categorization?.overall ?? '—'}</span>
                  </div>
                </div>

                {runError && (
                  <div className="rounded-md border border-red-200 bg-red-50 p-3 text-sm text-red-700">
                    {runError}
                  </div>
                )}
              </div>

              {/* Footer */}
              <div className="border-t border-gray-100 px-6 py-3 bg-gray-50 flex justify-end gap-2">
                <button
                  type="button"
                  onClick={() => setShowRunDialog(false)}
                  className="rounded-lg px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-200 transition-colors"
                >
                  Cancel
                </button>
                <button
                  type="button"
                  onClick={() => void handleRunAssessment()}
                  disabled={runLoading}
                  className="rounded-lg px-4 py-2 text-sm font-medium text-white bg-indigo-600 hover:bg-indigo-700 disabled:opacity-50 transition-colors"
                >
                  {runLoading ? 'Running...' : 'Run Assessment'}
                </button>
              </div>
            </div>
          </div>
        )}

        {/* Generate SAP Dialog */}
        {showSapDialog && (
          <div
            className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 backdrop-blur-sm"
            onClick={(e) => { if (e.target === e.currentTarget) setShowSapDialog(false); }}
          >
            <div className="w-full max-w-md rounded-xl bg-white shadow-2xl border border-gray-200 overflow-hidden">
              <div className="flex items-center justify-between px-6 pt-5 pb-3">
                <h3 className="text-lg font-semibold text-gray-900">Generate Security Assessment Plan</h3>
                <button
                  type="button"
                  onClick={() => setShowSapDialog(false)}
                  className="rounded-lg p-1 text-gray-400 hover:bg-gray-100 hover:text-gray-600 transition-colors"
                  aria-label="Close"
                >
                  <svg className="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                    <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" />
                  </svg>
                </button>
              </div>
              <div className="border-t border-gray-100" />
              <div className="px-6 py-5 space-y-4">
                <p className="text-sm text-gray-600">
                  Generate a Security Assessment Plan (SAP) for <strong>{detail.name}</strong>.
                  The SAP documents the assessment scope, methodology, and team for RMF Step 4.
                </p>
                <div className="rounded-md border border-gray-200 bg-gray-50 p-3 text-xs space-y-1">
                  <div className="flex justify-between">
                    <span className="text-gray-500">System</span>
                    <span className="font-medium text-gray-800">{detail.name}{detail.acronym ? ` (${detail.acronym})` : ''}</span>
                  </div>
                  <div className="flex justify-between">
                    <span className="text-gray-500">Impact Level</span>
                    <span className="font-medium text-gray-800">{detail.categorization?.overall ?? '—'}</span>
                  </div>
                  {sapData && (
                    <div className="flex justify-between">
                      <span className="text-gray-500">Existing SAP</span>
                      <span className="font-medium text-amber-600">{sapData.status} — will be replaced</span>
                    </div>
                  )}
                </div>
                {sapError && (
                  <div className="rounded-md border border-red-200 bg-red-50 p-3 text-sm text-red-700">{sapError}</div>
                )}
              </div>
              <div className="border-t border-gray-100 px-6 py-3 bg-gray-50 flex justify-end gap-2">
                <button type="button" onClick={() => setShowSapDialog(false)} className="rounded-lg px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-200 transition-colors">Cancel</button>
                <button type="button" onClick={() => void handleGenerateSap()} disabled={sapLoading} className="rounded-lg px-4 py-2 text-sm font-medium text-white bg-indigo-600 hover:bg-indigo-700 disabled:opacity-50 transition-colors">
                  {sapLoading ? 'Generating...' : 'Generate SAP'}
                </button>
              </div>
            </div>
          </div>
        )}

        {/* Generate SAR Dialog */}
        {showSarDialog && (
          <div
            className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 backdrop-blur-sm"
            onClick={(e) => { if (e.target === e.currentTarget) setShowSarDialog(false); }}
          >
            <div className="w-full max-w-md rounded-xl bg-white shadow-2xl border border-gray-200 overflow-hidden">
              <div className="flex items-center justify-between px-6 pt-5 pb-3">
                <h3 className="text-lg font-semibold text-gray-900">Generate Security Assessment Report</h3>
                <button
                  type="button"
                  onClick={() => setShowSarDialog(false)}
                  className="rounded-lg p-1 text-gray-400 hover:bg-gray-100 hover:text-gray-600 transition-colors"
                  aria-label="Close"
                >
                  <svg className="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                    <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" />
                  </svg>
                </button>
              </div>
              <div className="border-t border-gray-100" />
              <div className="px-6 py-5 space-y-4">
                <p className="text-sm text-gray-600">
                  Generate a Security Assessment Report (SAR) for <strong>{detail.name}</strong>.
                  The SAR aggregates findings from <strong>all</strong> completed assessments into an RMF Step 4 deliverable.
                </p>
                <div className="rounded-md border border-gray-200 bg-gray-50 p-3 text-xs space-y-1">
                  <div className="flex justify-between">
                    <span className="text-gray-500">System</span>
                    <span className="font-medium text-gray-800">{detail.name}{detail.acronym ? ` (${detail.acronym})` : ''}</span>
                  </div>
                  <div className="flex justify-between">
                    <span className="text-gray-500">Completed Assessments</span>
                    <span className="font-medium text-gray-800">{assessments.filter(a => a.status === 'Completed').length}</span>
                  </div>
                  {sapData && (
                    <div className="flex justify-between">
                      <span className="text-gray-500">Linked SAP</span>
                      <span className="font-medium text-gray-800">{sapData.sapId.substring(0, 8)}... ({sapData.status})</span>
                    </div>
                  )}
                </div>
                {sarError && (
                  <div className="rounded-md border border-red-200 bg-red-50 p-3 text-sm text-red-700">{sarError}</div>
                )}
              </div>
              <div className="border-t border-gray-100 px-6 py-3 bg-gray-50 flex justify-end gap-2">
                <button type="button" onClick={() => setShowSarDialog(false)} className="rounded-lg px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-200 transition-colors">Cancel</button>
                <button type="button" onClick={() => void handleGenerateSar()} disabled={sarLoading} className="rounded-lg px-4 py-2 text-sm font-medium text-white bg-emerald-600 hover:bg-emerald-700 disabled:opacity-50 transition-colors">
                  {sarLoading ? 'Generating...' : 'Generate SAR'}
                </button>
              </div>
            </div>
          </div>
        )}

        {/* SAP Detail View Modal */}
        {showSapView && sapData && (() => {
          const fams = sapData.familySummaries ?? [];
          const firstFam = fams[0];
          const allMethodsSame = firstFam != null &&
            fams.every(f => JSON.stringify(f.methods) === JSON.stringify(firstFam.methods));
          const commonMethods = allMethodsSame ? firstFam.methods : null;

          return (
          <div
            className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 backdrop-blur-sm"
            onClick={(e) => { if (e.target === e.currentTarget) setShowSapView(false); }}
          >
            <div className="w-full max-w-3xl max-h-[90vh] rounded-xl bg-white shadow-2xl border border-gray-200 flex flex-col overflow-hidden">
              {/* Header */}
              <div className="flex items-center justify-between px-6 pt-5 pb-3 flex-shrink-0">
                <div>
                  <h3 className="text-lg font-semibold text-gray-900">Security Assessment Plan</h3>
                  <p className="text-sm text-gray-500 mt-0.5">{sapData.title}</p>
                </div>
                <div className="flex items-center gap-2">
                  <span className={`inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium ${sapData.status === 'Finalized' ? 'bg-green-100 text-green-700' : 'bg-amber-100 text-amber-700'}`}>
                    {sapData.status}
                  </span>
                  <button
                    type="button"
                    onClick={() => setShowSapView(false)}
                    className="rounded-lg p-1 text-gray-400 hover:bg-gray-100 hover:text-gray-600 transition-colors"
                    aria-label="Close"
                  >
                    <svg className="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                      <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" />
                    </svg>
                  </button>
                </div>
              </div>
              <div className="border-t border-gray-100" />

              {/* Body */}
              <div className="px-6 py-5 overflow-y-auto flex-1 space-y-5">
                {/* Explainer */}
                <div className="rounded-md border border-indigo-200 bg-indigo-50 p-3">
                  <p className="text-xs text-indigo-800">
                    The <strong>Security Assessment Plan (SAP)</strong> defines the scope, methodology, and schedule for your system&apos;s security assessment (RMF Step 4).
                    It documents which controls will be assessed, how they&apos;ll be tested, and who is on the assessment team.
                  </p>
                </div>

                {/* Next Steps */}
                <div className={`rounded-md border p-3 ${sapData.status === 'Draft' ? 'border-indigo-200 bg-indigo-50' : 'border-green-200 bg-green-50'}`}>
                  <p className="text-xs font-semibold text-gray-700 mb-1.5">Next Steps</p>
                  {sapData.status === 'Draft' ? (
                    <ul className="text-xs text-gray-600 space-y-1.5">
                      {sapData.evidenceGaps > 0 && (
                        <li className="flex items-start gap-1.5">
                          <span className="text-amber-500 mt-0.5">⚠</span>
                          <span>
                            <strong>{sapData.evidenceGaps} controls</strong> have evidence gaps.
                            <button onClick={() => { setShowSapView(false); navigate(`/systems/${systemId}/evidence`); }} className="text-indigo-600 hover:underline ml-1 font-medium">Go to Evidence →</button>
                          </span>
                        </li>
                      )}
                      <li className="flex items-start gap-1.5">
                        <span className="text-indigo-500 mt-0.5">1</span>
                        <span>Review the assessment scope and control family breakdown below.</span>
                      </li>
                      <li className="flex items-start gap-1.5">
                        <span className="text-indigo-500 mt-0.5">2</span>
                        <span>
                          When ready, click <strong>Finalize SAP</strong> on the status card to lock the plan.
                        </span>
                      </li>
                      <li className="flex items-start gap-1.5">
                        <span className="text-indigo-500 mt-0.5">3</span>
                        <span>
                          After finalizing, run an assessment and then generate a <strong>SAR</strong> to document results.
                        </span>
                      </li>
                    </ul>
                  ) : (
                    <ul className="text-xs text-gray-600 space-y-1.5">
                      <li className="flex items-start gap-1.5">
                        <span className="text-green-500 mt-0.5">✓</span>
                        <span>SAP is finalized and locked. Assessment scope is set.</span>
                      </li>
                      <li className="flex items-start gap-1.5">
                        <span className="text-green-500 mt-0.5">→</span>
                        <span>
                          Run an assessment, then generate a <strong>SAR</strong> above. Both are required for the authorization package.
                          <button onClick={() => { setShowSapView(false); navigate(`/systems/${systemId}/documents`); }} className="text-indigo-600 hover:underline ml-1 font-medium">View Documents →</button>
                        </span>
                      </li>
                    </ul>
                  )}
                </div>

                {/* Metrics cards */}
                <div className="grid grid-cols-2 sm:grid-cols-4 gap-3">
                  <div className="rounded-lg border border-gray-200 bg-white p-3 text-center">
                    <p className="text-xs font-medium text-gray-500 uppercase tracking-wider">Total Controls</p>
                    <p className="mt-1 text-2xl font-bold text-gray-900">{sapData.totalControls}</p>
                  </div>
                  <div className="rounded-lg border border-gray-200 bg-white p-3 text-center">
                    <p className="text-xs font-medium text-gray-500 uppercase tracking-wider">Customer</p>
                    <p className="mt-1 text-2xl font-bold text-indigo-600">{sapData.customerControls}</p>
                    <p className="text-[10px] text-gray-400">Your responsibility</p>
                  </div>
                  <div className="rounded-lg border border-gray-200 bg-white p-3 text-center">
                    <p className="text-xs font-medium text-gray-500 uppercase tracking-wider">Inherited</p>
                    <p className="mt-1 text-2xl font-bold text-green-600">{sapData.inheritedControls}</p>
                    <p className="text-[10px] text-gray-400">Provider-assessed</p>
                  </div>
                  <div className="rounded-lg border border-gray-200 bg-white p-3 text-center">
                    <p className="text-xs font-medium text-gray-500 uppercase tracking-wider">Shared</p>
                    <p className="mt-1 text-2xl font-bold text-purple-600">{sapData.sharedControls}</p>
                    <p className="text-[10px] text-gray-400">Split responsibility</p>
                  </div>
                </div>

                {/* Assessment scope details */}
                <div className="grid grid-cols-3 gap-3">
                  <div className="rounded-lg border border-gray-200 bg-gray-50 p-3 text-center">
                    <p className="text-xs font-medium text-gray-500 uppercase tracking-wider">STIG Benchmarks</p>
                    <p className="mt-1 text-xl font-bold text-gray-800">{sapData.stigBenchmarkCount}</p>
                    <p className="text-[10px] text-gray-400">Imported checklists</p>
                  </div>
                  <div className="rounded-lg border border-gray-200 bg-gray-50 p-3 text-center">
                    <p className="text-xs font-medium text-gray-500 uppercase tracking-wider">OSCAL Objectives</p>
                    <p className="mt-1 text-xl font-bold text-gray-800">{sapData.controlsWithObjectives}</p>
                    <p className="text-[10px] text-gray-400">Controls with test criteria</p>
                  </div>
                  <div className={`rounded-lg border p-3 text-center ${sapData.evidenceGaps > 0 ? 'border-amber-200 bg-amber-50' : 'border-gray-200 bg-gray-50'}`}>
                    <p className="text-xs font-medium text-gray-500 uppercase tracking-wider">Evidence Gaps</p>
                    <p className={`mt-1 text-xl font-bold ${sapData.evidenceGaps > 0 ? 'text-amber-600' : 'text-green-600'}`}>{sapData.evidenceGaps}</p>
                    {sapData.evidenceGaps > 0 ? (
                      <button onClick={() => { setShowSapView(false); navigate(`/systems/${systemId}/evidence`); }} className="text-[10px] text-indigo-600 hover:underline">Upload evidence →</button>
                    ) : (
                      <p className="text-[10px] text-gray-400">All controls covered</p>
                    )}
                  </div>
                </div>

                {/* Warnings */}
                {sapData.warnings && sapData.warnings.length > 0 && (
                  <div className="rounded-md border border-amber-200 bg-amber-50 p-3 space-y-1">
                    <p className="text-xs font-semibold text-amber-800 uppercase tracking-wider">Warnings</p>
                    {sapData.warnings.map((w, i) => (
                      <p key={i} className="text-xs text-amber-700">• {w}</p>
                    ))}
                  </div>
                )}

                {/* Assessment Methods summary (if all families use same methods) */}
                {commonMethods && (
                  <div className="rounded-md border border-gray-200 bg-gray-50 p-3">
                    <p className="text-xs font-semibold text-gray-700 mb-1">Assessment Methods</p>
                    <p className="text-xs text-gray-500 mb-2">All {fams.length} control families use the same assessment methodology:</p>
                    <div className="flex gap-2">
                      {commonMethods.map(m => (
                        <span key={m} className="inline-flex items-center rounded-md bg-white border border-gray-200 px-2.5 py-1 text-xs font-medium text-gray-700">{m}</span>
                      ))}
                    </div>
                  </div>
                )}

                {/* Family Breakdown */}
                {fams.length > 0 && (
                  <div>
                    <h4 className="text-sm font-semibold text-gray-700 mb-2">Control Family Breakdown ({fams.length} families)</h4>
                    <div className="overflow-hidden rounded-lg border border-gray-200">
                      <table className="min-w-full divide-y divide-gray-200 text-xs">
                        <thead className="bg-gray-50">
                          <tr>
                            <th className="px-3 py-2 text-left font-medium text-gray-500">Family</th>
                            <th className="px-3 py-2 text-center font-medium text-gray-500">Controls</th>
                            <th className="px-3 py-2 text-center font-medium text-gray-500">Customer</th>
                            <th className="px-3 py-2 text-center font-medium text-gray-500">Inherited</th>
                            {!commonMethods && (
                              <th className="px-3 py-2 text-left font-medium text-gray-500">Methods</th>
                            )}
                          </tr>
                        </thead>
                        <tbody className="divide-y divide-gray-100 bg-white">
                          {fams.map(f => (
                            <tr key={f.family} className="hover:bg-gray-50">
                              <td className="px-3 py-2 font-medium text-gray-800">{f.family}</td>
                              <td className="px-3 py-2 text-center text-gray-600">{f.controlCount}</td>
                              <td className="px-3 py-2 text-center text-indigo-600">{f.customerCount}</td>
                              <td className="px-3 py-2 text-center text-green-600">{f.inheritedCount}</td>
                              {!commonMethods && (
                                <td className="px-3 py-2 text-gray-500">
                                  {f.methods.map(m => (
                                    <span key={m} className="inline-flex items-center rounded bg-gray-100 px-1.5 py-0.5 text-[10px] font-medium text-gray-600 mr-1">{m}</span>
                                  ))}
                                </td>
                              )}
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  </div>
                )}

                {/* Metadata */}
                <div className="rounded-md border border-gray-200 bg-gray-50 p-3 text-xs space-y-1">
                  <div className="flex justify-between">
                    <span className="text-gray-500">SAP ID</span>
                    <span className="font-mono text-gray-600">{sapData.sapId.substring(0, 8)}...</span>
                  </div>
                  <div className="flex justify-between">
                    <span className="text-gray-500">Baseline</span>
                    <span className="font-medium text-gray-800">{sapData.baselineLevel}</span>
                  </div>
                  <div className="flex justify-between">
                    <span className="text-gray-500">Generated</span>
                    <span className="font-medium text-gray-800">{formatDate(sapData.generatedAt)}</span>
                  </div>
                  {sapData.finalizedAt && (
                    <div className="flex justify-between">
                      <span className="text-gray-500">Finalized</span>
                      <span className="font-medium text-gray-800">{formatDate(sapData.finalizedAt)}</span>
                    </div>
                  )}
                  {sapData.contentHash && (
                    <div className="flex justify-between">
                      <span className="text-gray-500">Content Hash</span>
                      <span className="font-mono text-gray-600 text-[10px]">{sapData.contentHash.substring(0, 16)}...</span>
                    </div>
                  )}
                </div>
              </div>

              {/* Footer */}
              <div className="border-t border-gray-100 px-6 py-3 bg-gray-50 flex justify-end flex-shrink-0">
                <button
                  type="button"
                  onClick={() => setShowSapView(false)}
                  className="rounded-lg px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-200 transition-colors"
                >
                  Close
                </button>
              </div>
            </div>
          </div>
          );
        })()}

        {/* SAR Detail View Modal */}
        {showSarView && sarData && (() => {
          const complianceRate = sarData.totalControlsAssessed > 0 ? Math.round((sarData.satisfiedCount / sarData.totalControlsAssessed) * 100) : 0;
          const emptySections = sarData.sections?.filter(s => !s.hasContent) ?? [];

          return (
          <div
            className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 backdrop-blur-sm"
            onClick={(e) => { if (e.target === e.currentTarget) setShowSarView(false); }}
          >
            <div className="w-full max-w-3xl max-h-[90vh] rounded-xl bg-white shadow-2xl border border-gray-200 flex flex-col overflow-hidden">
              {/* Header */}
              <div className="flex items-center justify-between px-6 pt-5 pb-3 flex-shrink-0">
                <div>
                  <h3 className="text-lg font-semibold text-gray-900">Security Assessment Report</h3>
                  <p className="text-sm text-gray-500 mt-0.5">{sarData.title}</p>
                </div>
                <div className="flex items-center gap-2">
                  <span className={`inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium ${
                    sarData.status === 'Approved' ? 'bg-green-100 text-green-700' :
                    sarData.status === 'UnderReview' ? 'bg-indigo-100 text-indigo-700' :
                    'bg-amber-100 text-amber-700'
                  }`}>
                    {sarData.status === 'UnderReview' ? 'Under Review' : sarData.status}
                  </span>
                  <button
                    type="button"
                    onClick={() => setShowSarView(false)}
                    className="rounded-lg p-1 text-gray-400 hover:bg-gray-100 hover:text-gray-600 transition-colors"
                    aria-label="Close"
                  >
                    <svg className="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                      <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" />
                    </svg>
                  </button>
                </div>
              </div>
              <div className="border-t border-gray-100" />

              {/* Body */}
              <div className="px-6 py-5 overflow-y-auto flex-1 space-y-5">
                {/* Explainer */}
                <div className="rounded-md border border-indigo-200 bg-indigo-50 p-3">
                  <p className="text-xs text-indigo-800">
                    The <strong>Security Assessment Report (SAR)</strong> documents the results of your security assessment (RMF Step 4).
                    It aggregates findings from all completed assessments, showing which controls are satisfied and which have gaps that need remediation.
                  </p>
                </div>

                {/* Next Steps */}
                <div className={`rounded-md border p-3 ${sarData.status === 'Approved' ? 'border-green-200 bg-green-50' : 'border-emerald-200 bg-emerald-50'}`}>
                  <p className="text-xs font-semibold text-gray-700 mb-1.5">Next Steps</p>
                  {sarData.status === 'Draft' ? (
                    <ul className="text-xs text-gray-600 space-y-1.5">
                      {sarData.notSatisfiedCount > 0 && (
                        <li className="flex items-start gap-1.5">
                          <span className="text-red-500 mt-0.5">⚠</span>
                          <span>
                            <strong>{sarData.notSatisfiedCount} controls</strong> are not satisfied.
                            <button onClick={() => { setShowSarView(false); navigate(`/systems/${systemId}/remediation`); }} className="text-indigo-600 hover:underline ml-1 font-medium">Go to Remediation →</button>
                          </span>
                        </li>
                      )}
                      {emptySections.length > 0 && (
                        <li className="flex items-start gap-1.5">
                          <span className="text-amber-500 mt-0.5">✎</span>
                          <span>
                            <strong>{emptySections.length} section{emptySections.length !== 1 ? 's' : ''}</strong> need content ({emptySections.map(s => s.title).join(', ')}).
                            Use the chat to author them with the <code className="text-[10px] bg-gray-100 rounded px-1">compliance_edit_sar_section</code> tool.
                          </span>
                        </li>
                      )}
                      <li className="flex items-start gap-1.5">
                        <span className="text-emerald-500 mt-0.5">1</span>
                        <span>Review the findings summary and complete any manual sections above.</span>
                      </li>
                      <li className="flex items-start gap-1.5">
                        <span className="text-emerald-500 mt-0.5">2</span>
                        <span>Submit the SAR for review via the chat tool <code className="text-[10px] bg-gray-100 rounded px-1">compliance_submit_sar</code>.</span>
                      </li>
                      <li className="flex items-start gap-1.5">
                        <span className="text-emerald-500 mt-0.5">3</span>
                        <span>
                          Once approved, the SAR will be included when you
                          <button onClick={() => { setShowSarView(false); navigate(`/systems/${systemId}/documents`); }} className="text-indigo-600 hover:underline ml-1 font-medium">generate the authorization package →</button>
                        </span>
                      </li>
                    </ul>
                  ) : sarData.status === 'UnderReview' ? (
                    <ul className="text-xs text-gray-600 space-y-1.5">
                      <li className="flex items-start gap-1.5">
                        <span className="text-indigo-500 mt-0.5">⏳</span>
                        <span>SAR is under review. The AO or ISSO will approve or request revisions.</span>
                      </li>
                    </ul>
                  ) : (
                    <ul className="text-xs text-gray-600 space-y-1.5">
                      <li className="flex items-start gap-1.5">
                        <span className="text-green-500 mt-0.5">✓</span>
                        <span>SAR is approved. It will be included in the authorization package.</span>
                      </li>
                      <li className="flex items-start gap-1.5">
                        <span className="text-green-500 mt-0.5">→</span>
                        <span>
                          <button onClick={() => { setShowSarView(false); navigate(`/systems/${systemId}/documents`); }} className="text-indigo-600 hover:underline font-medium">Go to Documents →</button>
                          {' '}to generate and export the full authorization package.
                        </span>
                      </li>
                    </ul>
                  )}
                </div>

                {/* Metrics cards */}
                <div className="grid grid-cols-2 sm:grid-cols-4 gap-3">
                  <div className="rounded-lg border border-gray-200 bg-white p-3 text-center">
                    <p className="text-xs font-medium text-gray-500 uppercase tracking-wider">Total Assessed</p>
                    <p className="mt-1 text-2xl font-bold text-gray-900">{sarData.totalControlsAssessed}</p>
                  </div>
                  <div className="rounded-lg border border-gray-200 bg-white p-3 text-center">
                    <p className="text-xs font-medium text-gray-500 uppercase tracking-wider">Satisfied</p>
                    <p className="mt-1 text-2xl font-bold text-green-600">{sarData.satisfiedCount}</p>
                    <p className="text-[10px] text-gray-400">Controls passing</p>
                  </div>
                  <div className="rounded-lg border border-gray-200 bg-white p-3 text-center">
                    <p className="text-xs font-medium text-gray-500 uppercase tracking-wider">Not Satisfied</p>
                    <p className="mt-1 text-2xl font-bold text-red-600">{sarData.notSatisfiedCount}</p>
                    <p className="text-[10px] text-gray-400">
                      {sarData.notSatisfiedCount > 0 ? (
                        <button onClick={() => { setShowSarView(false); navigate(`/systems/${systemId}/poam`); }} className="text-indigo-600 hover:underline">View POA&Ms →</button>
                      ) : 'None'}
                    </p>
                  </div>
                  <div className="rounded-lg border border-gray-200 bg-white p-3 text-center">
                    <p className="text-xs font-medium text-gray-500 uppercase tracking-wider">Compliance Rate</p>
                    <p className={`mt-1 text-2xl font-bold ${complianceRate >= 80 ? 'text-green-600' : complianceRate >= 60 ? 'text-amber-600' : 'text-red-600'}`}>
                      {complianceRate}%
                    </p>
                    <p className="text-[10px] text-gray-400">{complianceRate >= 80 ? 'Strong' : complianceRate >= 60 ? 'Needs work' : 'At risk'}</p>
                  </div>
                </div>

                {/* Pending controls */}
                {sarData.totalControlsPending > 0 && (
                  <div className="rounded-md border border-amber-200 bg-amber-50 p-3 text-sm text-amber-800">
                    <strong>{sarData.totalControlsPending}</strong> control{sarData.totalControlsPending !== 1 ? 's' : ''} pending assessment.
                    Run another assessment to include them.
                  </div>
                )}

                {/* Sections */}
                {sarData.sections && sarData.sections.length > 0 && (
                  <div>
                    <h4 className="text-sm font-semibold text-gray-700 mb-2">Report Sections</h4>
                    <div className="overflow-hidden rounded-lg border border-gray-200">
                      <table className="min-w-full divide-y divide-gray-200 text-xs">
                        <thead className="bg-gray-50">
                          <tr>
                            <th className="px-3 py-2 text-left font-medium text-gray-500">Section</th>
                            <th className="px-3 py-2 text-center font-medium text-gray-500">Source</th>
                            <th className="px-3 py-2 text-center font-medium text-gray-500">Status</th>
                          </tr>
                        </thead>
                        <tbody className="divide-y divide-gray-100 bg-white">
                          {sarData.sections.map(s => (
                            <tr key={s.sectionType} className="hover:bg-gray-50">
                              <td className="px-3 py-2 font-medium text-gray-800">{s.title}</td>
                              <td className="px-3 py-2 text-center">
                                {s.isAutoGenerated ? (
                                  <span className="inline-flex items-center rounded bg-indigo-100 px-1.5 py-0.5 text-[10px] font-medium text-indigo-700">Auto-generated</span>
                                ) : (
                                  <span className="inline-flex items-center rounded bg-gray-100 px-1.5 py-0.5 text-[10px] font-medium text-gray-500">Manual entry</span>
                                )}
                              </td>
                              <td className="px-3 py-2 text-center">
                                {s.hasContent ? (
                                  <span className="inline-flex items-center rounded bg-green-100 px-1.5 py-0.5 text-[10px] font-medium text-green-700">Complete</span>
                                ) : (
                                  <span className="inline-flex items-center rounded bg-amber-100 px-1.5 py-0.5 text-[10px] font-medium text-amber-700">Needs content</span>
                                )}
                              </td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  </div>
                )}

                {/* Lifecycle / Metadata */}
                <div className="rounded-md border border-gray-200 bg-gray-50 p-3 text-xs space-y-1">
                  <div className="flex justify-between">
                    <span className="text-gray-500">SAR ID</span>
                    <span className="font-mono text-gray-600">{sarData.sarId.substring(0, 8)}...</span>
                  </div>
                  <div className="flex justify-between">
                    <span className="text-gray-500">Created By</span>
                    <span className="font-medium text-gray-800">{sarData.createdBy}</span>
                  </div>
                  <div className="flex justify-between">
                    <span className="text-gray-500">Created</span>
                    <span className="font-medium text-gray-800">{formatDate(sarData.createdAt)}</span>
                  </div>
                  {sarData.modifiedBy && (
                    <div className="flex justify-between">
                      <span className="text-gray-500">Last Modified</span>
                      <span className="font-medium text-gray-800">{sarData.modifiedBy} — {formatDate(sarData.modifiedAt)}</span>
                    </div>
                  )}
                  {sarData.reviewedBy && (
                    <div className="flex justify-between">
                      <span className="text-gray-500">Reviewed By</span>
                      <span className="font-medium text-gray-800">{sarData.reviewedBy} — {formatDate(sarData.reviewedAt)}</span>
                    </div>
                  )}
                  {sarData.approvedBy && (
                    <div className="flex justify-between">
                      <span className="text-gray-500">Approved By</span>
                      <span className="font-medium text-gray-800">{sarData.approvedBy} — {formatDate(sarData.approvedAt)}</span>
                    </div>
                  )}
                </div>
              </div>

              {/* Footer */}
              <div className="border-t border-gray-100 px-6 py-3 bg-gray-50 flex justify-end flex-shrink-0">
                <button
                  type="button"
                  onClick={() => setShowSarView(false)}
                  className="rounded-lg px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-200 transition-colors"
                >
                  Close
                </button>
              </div>
            </div>
          </div>
          );
        })()}
    </div>
  );
}
