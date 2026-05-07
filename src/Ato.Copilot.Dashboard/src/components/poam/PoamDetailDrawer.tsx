import { useState, useEffect } from 'react';
import { usePoamDetail } from '../../hooks/usePoam';
import { linkComponents, unlinkComponents, createTaskFromPoam, linkTask, unlinkTask, syncTicket } from '../../api/poam';
import { getDeviationDetail } from '../../api/deviations';
import type { DeviationDetail } from '../../types/dashboard';
import { SeverityBadge, StatusBadge } from './PoamTable';
import ComponentPicker from './ComponentPicker';
import PoamLifecycleActions from './PoamLifecycleActions';
import SyncIndicator from './SyncIndicator';

const syncStatusColors: Record<string, string> = {
  Synced: 'bg-green-100 text-green-700',
  Pending: 'bg-yellow-100 text-yellow-700',
  Conflict: 'bg-orange-100 text-orange-700',
  Error: 'bg-red-100 text-red-700',
};

interface PoamDetailDrawerProps {
  poamId: string;
  onClose: () => void;
}

export default function PoamDetailDrawer({ poamId, onClose }: PoamDetailDrawerProps) {
  const { data: detail, loading, refresh } = usePoamDetail(poamId);
  const [showComponentPicker, setShowComponentPicker] = useState(false);
  const [pickerComponentIds, setPickerComponentIds] = useState<string[]>([]);
  const [syncing, setSyncing] = useState(false);
  const [deviation, setDeviation] = useState<DeviationDetail | null>(null);

  useEffect(() => {
    if (detail?.deviationId) {
      getDeviationDetail(detail.deviationId).then(setDeviation).catch(() => setDeviation(null));
    } else {
      setDeviation(null);
    }
  }, [detail?.deviationId]);

  if (loading && !detail) {
    return (
      <div className="fixed inset-0 z-50 flex justify-end bg-black/20" onClick={onClose}>
        <div className="w-full max-w-lg bg-white p-6 shadow-xl" onClick={e => e.stopPropagation()}>
          <div className="flex items-center justify-center py-20 text-gray-400">Loading...</div>
        </div>
      </div>
    );
  }

  if (!detail) return null;

  return (
    <div className="fixed inset-0 z-50 flex justify-end bg-black/20" onClick={onClose}>
      <div className="flex w-full max-w-lg flex-col overflow-y-auto bg-white shadow-xl" onClick={e => e.stopPropagation()}>
        {/* Header */}
        <div className="flex items-center justify-between border-b border-gray-200 px-6 py-4">
          <div>
            <h2 className="text-lg font-bold text-gray-900">{detail.controlId}</h2>
            <p className="mt-0.5 text-sm text-gray-500">{detail.systemName}</p>
          </div>
          <button onClick={onClose} className="text-2xl text-gray-400 hover:text-gray-600">&times;</button>
        </div>

        {/* Content */}
        <div className="flex-1 space-y-6 p-6">
          {/* Overview */}
          <section>
            <h3 className="mb-3 text-sm font-semibold uppercase tracking-wider text-gray-400">Overview</h3>
            <dl className="space-y-2 text-sm">
              <div className="flex justify-between">
                <dt className="text-gray-500">Weakness</dt>
                <dd className="max-w-[65%] text-right text-gray-900">{detail.weakness}</dd>
              </div>
              <div className="flex justify-between">
                <dt className="text-gray-500">Source</dt>
                <dd className="text-gray-900">{detail.weaknessSource}</dd>
              </div>
              <div className="flex justify-between">
                <dt className="text-gray-500">Severity</dt>
                <dd><SeverityBadge severity={detail.catSeverity} /></dd>
              </div>
              <div className="flex justify-between">
                <dt className="text-gray-500">Status</dt>
                <dd><StatusBadge status={detail.status} /></dd>
              </div>
              <div className="flex justify-between">
                <dt className="text-gray-500">POC</dt>
                <dd className="text-gray-900">{detail.poc}{detail.pocEmail ? ` (${detail.pocEmail})` : ''}</dd>
              </div>
              <div className="flex justify-between">
                <dt className="text-gray-500">Due Date</dt>
                <dd className="text-gray-900">{new Date(detail.scheduledCompletionDate).toLocaleDateString()}</dd>
              </div>
              {detail.resourcesRequired && (
                <div className="flex justify-between">
                  <dt className="text-gray-500">Resources</dt>
                  <dd className="text-gray-900">{detail.resourcesRequired}</dd>
                </div>
              )}
              {detail.costEstimate != null && (
                <div className="flex justify-between">
                  <dt className="text-gray-500">Cost Estimate</dt>
                  <dd className="text-gray-900">${detail.costEstimate.toLocaleString()}</dd>
                </div>
              )}
              {detail.comments && (
                <div>
                  <dt className="mb-1 text-gray-500">Comments</dt>
                  <dd className="rounded-lg bg-gray-50 p-2 text-gray-700">{detail.comments}</dd>
                </div>
              )}
            </dl>
          </section>

          {/* Milestones */}
          {detail.milestones && detail.milestones.length > 0 && (
            <section>
              <h3 className="mb-3 text-sm font-semibold uppercase tracking-wider text-gray-400">
                Milestones ({detail.milestones.filter(m => m.completedDate).length}/{detail.milestones.length})
              </h3>
              <div className="space-y-2">
                {detail.milestones.map(m => (
                  <div key={m.id} className={`flex items-center gap-3 rounded-lg border p-3 text-sm ${m.completedDate ? 'border-green-200 bg-green-50' : m.isOverdue ? 'border-red-200 bg-red-50' : 'border-gray-200'}`}>
                    <span className={`flex h-5 w-5 items-center justify-center rounded-full text-xs ${m.completedDate ? 'bg-green-500 text-white' : 'bg-gray-200 text-gray-500'}`}>
                      {m.completedDate ? '✓' : m.sequence}
                    </span>
                    <div className="flex-1">
                      <p className={m.completedDate ? 'text-gray-500 line-through' : ''}>{m.description}</p>
                      <p className="text-xs text-gray-400">Target: {new Date(m.targetDate).toLocaleDateString()}</p>
                    </div>
                  </div>
                ))}
              </div>
            </section>
          )}

          {/* Linked Entities */}
          <section>
            <h3 className="mb-3 text-sm font-semibold uppercase tracking-wider text-gray-400">Linked Entities</h3>
            <div className="space-y-2 text-sm">
              {/* Components with link/unlink */}
              <div>
                <div className="mb-1 flex items-center justify-between">
                  <dt className="text-gray-500">Components ({detail.components?.length ?? 0})</dt>
                  <button
                    type="button"
                    onClick={() => { setShowComponentPicker(p => !p); setPickerComponentIds([]); }}
                    className="text-xs text-indigo-600 hover:underline"
                  >
                    {showComponentPicker ? 'Cancel' : '+ Link'}
                  </button>
                </div>
                {detail.components && detail.components.length > 0 && (
                  <dd className="flex flex-wrap gap-1">
                    {detail.components.map(c => (
                      <span key={c.id} className="inline-flex items-center gap-1 rounded-full bg-gray-100 px-2 py-0.5 text-xs text-gray-700">
                        {c.name}
                        <button
                          type="button"
                          onClick={async () => {
                            await unlinkComponents(poamId, { componentIds: [c.id] });
                            refresh();
                          }}
                          className="text-gray-400 hover:text-red-500"
                          title="Unlink component"
                        >&times;</button>
                      </span>
                    ))}
                  </dd>
                )}
                {showComponentPicker && (
                  <div className="mt-2">
                    <ComponentPicker selectedIds={pickerComponentIds} onChange={setPickerComponentIds} />
                    {pickerComponentIds.length > 0 && (
                      <button
                        type="button"
                        onClick={async () => {
                          await linkComponents(poamId, { componentIds: pickerComponentIds });
                          setShowComponentPicker(false);
                          setPickerComponentIds([]);
                          refresh();
                        }}
                        className="mt-1 rounded bg-indigo-600 px-3 py-1 text-xs text-white hover:bg-indigo-700"
                      >
                        Link {pickerComponentIds.length} component(s)
                      </button>
                    )}
                  </div>
                )}
              </div>
              {/* Remediation Task Sync */}
              <div>
                <dt className="mb-1 text-gray-500">Remediation Task</dt>
                <dd>
                  <SyncIndicator
                    linked={!!detail.remediationTaskId}
                    linkedEntityId={detail.remediationTaskId ?? undefined}
                    linkedEntityName={detail.remediationTaskId ? `Task ${detail.remediationTaskId.slice(0, 8)}...` : undefined}
                  />
                  <div className="mt-1.5 flex flex-wrap gap-1.5">
                    {!detail.remediationTaskId && (
                      <>
                        <button
                          type="button"
                          onClick={async () => {
                            const boardId = prompt('Enter board ID for the new task:');
                            if (!boardId) return;
                            await createTaskFromPoam(poamId, { boardId });
                            refresh();
                          }}
                          className="rounded bg-indigo-50 px-2 py-0.5 text-xs text-indigo-700 hover:bg-indigo-100"
                        >
                          Create Task
                        </button>
                        <button
                          type="button"
                          onClick={async () => {
                            const taskId = prompt('Enter existing task ID to link:');
                            if (!taskId) return;
                            await linkTask(poamId, { taskId });
                            refresh();
                          }}
                          className="rounded bg-gray-50 px-2 py-0.5 text-xs text-gray-700 hover:bg-gray-100"
                        >
                          Link Task
                        </button>
                      </>
                    )}
                    {detail.remediationTaskId && (
                      <button
                        type="button"
                        onClick={async () => {
                          if (!confirm('Unlink the remediation task from this POA&M?')) return;
                          await unlinkTask(poamId);
                          refresh();
                        }}
                        className="rounded bg-red-50 px-2 py-0.5 text-xs text-red-700 hover:bg-red-100"
                      >
                        Unlink
                      </button>
                    )}
                  </div>
                </dd>
              </div>
              {detail.findingId && (
                <div className="flex justify-between">
                  <dt className="text-gray-500">Finding</dt>
                  <dd className="text-gray-900">{detail.findingId}</dd>
                </div>
              )}
              {detail.deviationId && (
                <div>
                  <dt className="mb-1 text-gray-500">Deviation</dt>
                  <dd>
                    {deviation ? (
                      <div className="rounded-lg border border-purple-200 bg-purple-50 p-3 space-y-1.5">
                        <div className="flex items-center gap-2">
                          <span className={`inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium ${
                            deviation.deviationType === 'Waiver' ? 'bg-amber-100 text-amber-700' :
                            deviation.deviationType === 'RiskAcceptance' ? 'bg-orange-100 text-orange-700' :
                            'bg-indigo-100 text-indigo-700'
                          }`}>
                            {deviation.deviationType === 'RiskAcceptance' ? 'Risk Acceptance' : deviation.deviationType}
                          </span>
                          <span className={`inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium ${
                            deviation.status === 'Approved' ? 'bg-green-100 text-green-700' :
                            deviation.status === 'Pending' ? 'bg-yellow-100 text-yellow-700' :
                            deviation.status === 'Expired' ? 'bg-gray-100 text-gray-500' :
                            'bg-red-100 text-red-700'
                          }`}>
                            {deviation.status}
                          </span>
                        </div>
                        <p className="text-xs text-gray-700 line-clamp-2">{deviation.justification}</p>
                        <p className="text-xs text-gray-400">
                          Expires: {new Date(deviation.expirationDate).toLocaleDateString()}
                        </p>
                        {deviation.compensatingControls && (
                          <p className="text-xs text-gray-500">
                            <span className="font-medium">Compensating:</span> {deviation.compensatingControls}
                          </p>
                        )}
                      </div>
                    ) : (
                      <span className="text-xs text-gray-400">{detail.deviationId.slice(0, 8)}...</span>
                    )}
                  </dd>
                </div>
              )}
              {detail.externalTicketRef && (
                <div className="flex justify-between">
                  <dt className="text-gray-500">External Ticket</dt>
                  <dd className="text-gray-900">{detail.externalTicketRef}</dd>
                </div>
              )}
              {/* Ticketing Sync */}
              <div>
                <dt className="mb-1 text-gray-500">Ticketing Sync</dt>
                <dd>
                  {detail.ticketSync ? (
                    <div className="space-y-2">
                      <div className="flex items-center gap-2">
                        <span className={`inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium ${syncStatusColors[detail.ticketSync.syncStatus] ?? 'bg-gray-100 text-gray-700'}`}>
                          {detail.ticketSync.syncStatus}
                        </span>
                        {detail.ticketSync.externalTicketUrl ? (
                          <a
                            href={detail.ticketSync.externalTicketUrl}
                            target="_blank"
                            rel="noopener noreferrer"
                            className="text-xs text-indigo-600 hover:underline"
                          >
                            {detail.ticketSync.externalTicketId}
                          </a>
                        ) : (
                          <span className="text-xs text-gray-600">{detail.ticketSync.externalTicketId}</span>
                        )}
                      </div>
                      <p className="text-xs text-gray-400">
                        Last sync: {new Date(detail.ticketSync.lastSyncAt).toLocaleString()}
                      </p>
                      {detail.ticketSync.lastSyncError && (
                        <p className="text-xs text-red-500">{detail.ticketSync.lastSyncError}</p>
                      )}
                      <div className="flex gap-1.5">
                        <button
                          type="button"
                          disabled={syncing}
                          onClick={async () => {
                            setSyncing(true);
                            try { await syncTicket(poamId, { direction: 'push' }); refresh(); }
                            catch { /* handled by refresh */ }
                            finally { setSyncing(false); }
                          }}
                          className="rounded bg-indigo-50 px-2 py-0.5 text-xs text-indigo-700 hover:bg-indigo-100 disabled:opacity-50"
                        >
                          {syncing ? 'Syncing...' : 'Push'}
                        </button>
                        <button
                          type="button"
                          disabled={syncing}
                          onClick={async () => {
                            setSyncing(true);
                            try { await syncTicket(poamId, { direction: 'pull' }); refresh(); }
                            catch { /* handled by refresh */ }
                            finally { setSyncing(false); }
                          }}
                          className="rounded bg-gray-50 px-2 py-0.5 text-xs text-gray-700 hover:bg-gray-100 disabled:opacity-50"
                        >
                          Pull
                        </button>
                      </div>
                    </div>
                  ) : (
                    <div className="space-y-1.5">
                      <span className="text-xs text-gray-400">Not synced to external system</span>
                      <div>
                        <button
                          type="button"
                          disabled={syncing}
                          onClick={async () => {
                            setSyncing(true);
                            try { await syncTicket(poamId, { direction: 'push' }); refresh(); }
                            catch { /* handled by refresh */ }
                            finally { setSyncing(false); }
                          }}
                          className="rounded bg-indigo-50 px-2 py-0.5 text-xs text-indigo-700 hover:bg-indigo-100 disabled:opacity-50"
                        >
                          {syncing ? 'Creating...' : 'Sync to Ticketing System'}
                        </button>
                      </div>
                    </div>
                  )}
                </dd>
              </div>
            </div>
          </section>

          {/* Lifecycle Actions */}
          {(detail.status === 'Ongoing' || detail.status === 'Delayed') && (
            <PoamLifecycleActions detail={detail} onStatusChanged={refresh} />
          )}

          {/* History Timeline */}
          {detail.history && detail.history.length > 0 && (
            <section>
              <h3 className="mb-3 text-sm font-semibold uppercase tracking-wider text-gray-400">
                History ({detail.history.length})
              </h3>
              <div className="space-y-2">
                {detail.history.map(h => (
                  <div key={h.id} className="rounded-lg border border-gray-100 bg-gray-50 p-3 text-sm">
                    <div className="flex items-center justify-between">
                      <span className="font-medium text-gray-700">{h.eventType}</span>
                      <span className="text-xs text-gray-400">{new Date(h.timestamp).toLocaleString()}</span>
                    </div>
                    {h.oldValue && h.newValue && (
                      <p className="mt-1 text-xs text-gray-600">
                        <span className="text-gray-400">{h.oldValue}</span> → <span className="font-medium">{h.newValue}</span>
                      </p>
                    )}
                    {h.details && <p className="mt-1 text-xs text-gray-500">{h.details}</p>}
                    <div className="mt-0.5 flex items-center gap-2 text-xs text-gray-400">
                      <span>by {h.actingUserName}</span>
                      {h.cascadeOrigin && (
                        <span className="rounded bg-indigo-50 px-1.5 py-0.5 text-indigo-600">
                          cascade: {h.cascadeOrigin}
                        </span>
                      )}
                    </div>
                  </div>
                ))}
              </div>
            </section>
          )}
        </div>
      </div>
    </div>
  );
}
