import { useEffect, useState } from 'react';
import { getDeviationDetail, reviewDeviation, revokeDeviation, extendDeviation } from '../api/deviations';
import type { DeviationDetail } from '../types/dashboard';

const SEVERITY_LABELS: Record<number, string> = { 0: 'CAT I', 1: 'CAT II', 2: 'CAT III' };

interface DeviationDetailDrawerProps {
  deviationId: string | null;
  onClose: () => void;
  onActionComplete: () => void;
}

export default function DeviationDetailDrawer({ deviationId, onClose, onActionComplete }: DeviationDetailDrawerProps) {
  const [detail, setDetail] = useState<DeviationDetail | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [actionLoading, setActionLoading] = useState(false);

  // Review form
  const [reviewerRole, setReviewerRole] = useState('ISSM');
  const [comments, setComments] = useState('');
  // Revoke form
  const [revokeReason, setRevokeReason] = useState('');
  // Extend form
  const [newExpDate, setNewExpDate] = useState('');
  const [extendJustification, setExtendJustification] = useState('');

  useEffect(() => {
    if (!deviationId) { setDetail(null); return; }
    setLoading(true);
    setError(null);
    getDeviationDetail(deviationId)
      .then(setDetail)
      .catch(() => setError('Failed to load deviation'))
      .finally(() => setLoading(false));
  }, [deviationId]);

  if (!deviationId) return null;

  const handleReview = async (decision: string) => {
    if (!detail) return;
    setActionLoading(true);
    try {
      await reviewDeviation(detail.id, { decision, comments }, reviewerRole);
      onActionComplete();
    } catch { setError('Review failed'); }
    finally { setActionLoading(false); }
  };

  const handleRevoke = async () => {
    if (!detail || !revokeReason.trim()) return;
    setActionLoading(true);
    try {
      await revokeDeviation(detail.id, { reason: revokeReason });
      onActionComplete();
    } catch { setError('Revoke failed'); }
    finally { setActionLoading(false); }
  };

  const handleExtend = async () => {
    if (!detail || !newExpDate) return;
    setActionLoading(true);
    try {
      await extendDeviation(detail.id, { newExpirationDate: newExpDate, justification: extendJustification || undefined });
      onActionComplete();
    } catch { setError('Extend failed'); }
    finally { setActionLoading(false); }
  };

  return (
    <div className="fixed inset-y-0 right-0 w-[480px] bg-white shadow-xl border-l border-gray-200 z-50 overflow-y-auto">
      {/* Header */}
      <div className="sticky top-0 bg-white border-b border-gray-200 px-6 py-4 flex items-center justify-between">
        <h2 className="text-lg font-semibold text-gray-900">Deviation Detail</h2>
        <button onClick={onClose} className="text-gray-400 hover:text-gray-600 text-xl">&times;</button>
      </div>

      <div className="px-6 py-4 space-y-6">
        {loading && <p className="text-sm text-gray-400">Loading...</p>}
        {error && <div className="text-xs text-red-600 bg-red-50 p-2 rounded">{error}</div>}

        {detail && (
          <>
            {/* Status & Classification */}
            <div className="space-y-2">
              <div className="flex items-center gap-2">
                <span className="text-xl font-mono font-bold">{detail.controlId}</span>
                <span className="rounded px-2 py-0.5 text-xs font-medium bg-gray-100">{detail.deviationType}</span>
                <span className="rounded px-2 py-0.5 text-xs font-medium bg-indigo-100 text-indigo-800">
                  {SEVERITY_LABELS[detail.catSeverity]}
                </span>
              </div>
              <span className={`inline-block rounded px-2 py-0.5 text-xs font-medium ${
                detail.status === 'Approved' ? 'bg-green-100 text-green-800' :
                detail.status === 'Pending' ? 'bg-yellow-100 text-yellow-800' :
                detail.status === 'Denied' ? 'bg-red-100 text-red-800' :
                'bg-gray-100 text-gray-800'
              }`}>{detail.status}</span>
            </div>

            {/* Justification */}
            <Section title="Justification">
              <p className="text-sm text-gray-700">{detail.justification}</p>
            </Section>

            {detail.compensatingControls && (
              <Section title="Compensating Controls">
                <p className="text-sm text-gray-700">{detail.compensatingControls}</p>
              </Section>
            )}

            {/* Metadata */}
            <Section title="Details">
              <InfoRow label="Expiration" value={`${new Date(detail.expirationDate).toLocaleDateString()} (${detail.reviewCycle})`} />
              <InfoRow label="Requested by" value={`${detail.requestedBy} · ${new Date(detail.requestedAt).toLocaleString()}`} />
              {detail.reviewedBy && (
                <InfoRow label="Reviewed by" value={`${detail.reviewedBy} (${detail.reviewerRole}) · ${new Date(detail.reviewedAt!).toLocaleString()}`} />
              )}
              {detail.issmRecommendation && (
                <InfoRow label="ISSM Recommendation" value={`${detail.issmRecommendation} by ${detail.issmRecommendedBy}`} />
              )}
              {detail.revokedBy && (
                <InfoRow label="Revoked by" value={`${detail.revokedBy} · ${detail.revocationReason}`} />
              )}
              {detail.boundaryDefinitionName && (
                <InfoRow label="Boundary" value={detail.boundaryDefinitionName} />
              )}
            </Section>

            {/* Linked Finding */}
            {detail.finding && (
              <Section title="Linked Finding">
                <div className="text-sm rounded border border-gray-200 p-2">
                  <span className="font-mono text-xs">{detail.finding.controlId}</span> · {detail.finding.status} · {detail.finding.severity}
                </div>
              </Section>
            )}

            {/* Linked POA&M */}
            {detail.poamEntry && (
              <Section title="Linked POA&M">
                <div className="text-sm rounded border border-gray-200 p-2">
                  {detail.poamEntry.weakness} · {detail.poamEntry.status}
                </div>
              </Section>
            )}

            {/* Evidence */}
            {detail.evidence.length > 0 && (
              <Section title={`Evidence (${detail.evidence.length})`}>
                <ul className="space-y-1">
                  {detail.evidence.map((e) => (
                    <li key={e.scanImportRecordId} className="text-xs text-gray-600 border border-gray-100 rounded p-1.5">
                      {e.fileName} · {e.scanType} {e.benchmarkTitle ? `· ${e.benchmarkTitle}` : ''}
                    </li>
                  ))}
                </ul>
              </Section>
            )}

            {/* Audit Trail */}
            <Section title="Audit Trail">
              <div className="space-y-2">
                {detail.auditTrail.map((entry, i) => (
                  <div key={i} className="text-xs border-l-2 border-gray-300 pl-3 py-1">
                    <span className="font-medium text-gray-800">{entry.eventType}</span>
                    <span className="text-gray-400"> · {entry.actor} · {new Date(entry.timestamp).toLocaleString()}</span>
                    <p className="text-gray-500">{entry.summary}</p>
                  </div>
                ))}
              </div>
            </Section>

            {/* Actions */}
            {detail.status === 'Pending' && (
              <Section title="Review">
                <div className="space-y-2">
                  <select value={reviewerRole} onChange={(e) => setReviewerRole(e.target.value)}
                    className="rounded border border-gray-300 px-2 py-1 text-sm w-full">
                    <option value="ISSM">ISSM</option>
                    <option value="AO">AO</option>
                  </select>
                  <textarea
                    placeholder="Comments..."
                    value={comments}
                    onChange={(e) => setComments(e.target.value)}
                    className="rounded border border-gray-300 px-2 py-1 text-sm w-full h-16"
                  />
                  <div className="flex gap-2">
                    <button disabled={actionLoading} onClick={() => handleReview('Approve')}
                      className="flex-1 rounded bg-green-600 text-white px-3 py-1.5 text-sm hover:bg-green-700 disabled:opacity-50">
                      Approve
                    </button>
                    <button disabled={actionLoading} onClick={() => handleReview('Deny')}
                      className="flex-1 rounded bg-red-600 text-white px-3 py-1.5 text-sm hover:bg-red-700 disabled:opacity-50">
                      Deny
                    </button>
                  </div>
                </div>
              </Section>
            )}

            {detail.status === 'Approved' && (
              <>
                <Section title="Revoke">
                  <div className="space-y-2">
                    <input
                      placeholder="Revocation reason..."
                      value={revokeReason}
                      onChange={(e) => setRevokeReason(e.target.value)}
                      className="rounded border border-gray-300 px-2 py-1 text-sm w-full"
                    />
                    <button disabled={actionLoading || !revokeReason.trim()} onClick={handleRevoke}
                      className="rounded bg-orange-600 text-white px-3 py-1.5 text-sm hover:bg-orange-700 disabled:opacity-50 w-full">
                      Revoke Deviation
                    </button>
                  </div>
                </Section>
                <Section title="Extend">
                  <div className="space-y-2">
                    <input type="date" value={newExpDate} onChange={(e) => setNewExpDate(e.target.value)}
                      className="rounded border border-gray-300 px-2 py-1 text-sm w-full" />
                    <input placeholder="Extension justification..." value={extendJustification}
                      onChange={(e) => setExtendJustification(e.target.value)}
                      className="rounded border border-gray-300 px-2 py-1 text-sm w-full" />
                    <button disabled={actionLoading || !newExpDate} onClick={handleExtend}
                      className="rounded bg-indigo-600 text-white px-3 py-1.5 text-sm hover:bg-indigo-700 disabled:opacity-50 w-full">
                      Extend Expiration
                    </button>
                  </div>
                </Section>
              </>
            )}
          </>
        )}
      </div>
    </div>
  );
}

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div>
      <h3 className="text-xs font-semibold text-gray-500 uppercase tracking-wide mb-1">{title}</h3>
      {children}
    </div>
  );
}

function InfoRow({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex justify-between text-sm py-0.5">
      <span className="text-gray-500">{label}</span>
      <span className="text-gray-800 text-right">{value}</span>
    </div>
  );
}
