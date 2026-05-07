import { useState } from 'react';
import type { PoamDetail, PoamStatus } from '../../types/poam';
import { updatePoamStatus } from '../../api/poam';
import CascadeConfirmDialog from './CascadeConfirmDialog';

interface PoamLifecycleActionsProps {
  detail: PoamDetail;
  onStatusChanged: () => void;
}

export default function PoamLifecycleActions({ detail, onStatusChanged }: PoamLifecycleActionsProps) {
  const [dialog, setDialog] = useState<'delay' | 'resume' | 'complete' | 'risk' | null>(null);
  const [loading, setLoading] = useState(false);
  const [delayReason, setDelayReason] = useState('');
  const [revisedDate, setRevisedDate] = useState('');
  const [deviationId, setDeviationId] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [cascadePrompt, setCascadePrompt] = useState<{ newStatus: PoamStatus; rowVersion: string } | null>(null);

  const hasLinkedTask = !!detail.remediationTaskId;

  const canTransitionTo = (target: PoamStatus): boolean => {
    const valid: Record<string, PoamStatus[]> = {
      Ongoing: ['Delayed', 'Completed', 'RiskAccepted'],
      Delayed: ['Ongoing', 'Completed', 'RiskAccepted'],
    };
    return (valid[detail.status] ?? []).includes(target);
  };

  const handleSubmit = async (newStatus: PoamStatus) => {
    setLoading(true);
    setError(null);
    try {
      const resp = await updatePoamStatus(detail.id, {
        status: newStatus,
        rowVersion: detail.rowVersion,
        delayReason: newStatus === 'Delayed' ? delayReason : undefined,
        revisedDate: revisedDate || undefined,
        deviationId: newStatus === 'RiskAccepted' ? deviationId : undefined,
      });
      setDialog(null);
      if (hasLinkedTask) {
        setCascadePrompt({ newStatus, rowVersion: resp.poam?.rowVersion ?? detail.rowVersion });
      } else {
        onStatusChanged();
      }
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : String(err);
      if (msg.includes('409') || msg.includes('CONCURRENCY')) {
        setError('Concurrency conflict. Please reload and try again.');
      } else {
        setError(msg);
      }
    } finally {
      setLoading(false);
    }
  };

  const handleCascadeConfirm = async () => {
    if (!cascadePrompt) return;
    await updatePoamStatus(detail.id, {
      status: cascadePrompt.newStatus,
      rowVersion: cascadePrompt.rowVersion,
      cascadeToTask: true,
    });
    setCascadePrompt(null);
    onStatusChanged();
  };

  return (
    <section>
      <h3 className="mb-3 text-sm font-semibold uppercase tracking-wider text-gray-400">Lifecycle Actions</h3>
      <div className="flex flex-wrap gap-2">
        {canTransitionTo('Delayed') && (
          <button
            onClick={() => setDialog('delay')}
            className="rounded-lg bg-amber-50 px-3 py-1.5 text-xs font-medium text-amber-700 hover:bg-amber-100"
          >
            Mark Delayed
          </button>
        )}
        {detail.status === 'Delayed' && canTransitionTo('Ongoing') && (
          <button
            onClick={() => setDialog('resume')}
            className="rounded-lg bg-indigo-50 px-3 py-1.5 text-xs font-medium text-indigo-700 hover:bg-indigo-100"
          >
            Resume
          </button>
        )}
        {canTransitionTo('Completed') && (
          <button
            onClick={() => setDialog('complete')}
            className="rounded-lg bg-green-50 px-3 py-1.5 text-xs font-medium text-green-700 hover:bg-green-100"
          >
            Mark Completed
          </button>
        )}
        {canTransitionTo('RiskAccepted') && (
          <button
            onClick={() => setDialog('risk')}
            className="rounded-lg bg-purple-50 px-3 py-1.5 text-xs font-medium text-purple-700 hover:bg-purple-100"
          >
            Risk Accepted
          </button>
        )}
      </div>

      {/* Dialog overlay */}
      {dialog && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/30" onClick={() => setDialog(null)}>
          <div className="w-full max-w-md rounded-xl bg-white p-6 shadow-xl" onClick={e => e.stopPropagation()}>
            <h3 className="mb-4 text-lg font-bold text-gray-900">
              {dialog === 'delay' && 'Mark as Delayed'}
              {dialog === 'resume' && 'Resume POA&M'}
              {dialog === 'complete' && 'Mark as Completed'}
              {dialog === 'risk' && 'Risk Accepted'}
            </h3>

            <div className="space-y-3">
              {dialog === 'delay' && (
                <>
                  <div>
                    <label className="mb-1 block text-xs font-medium text-gray-500">Delay Reason *</label>
                    <textarea
                      rows={2}
                      required
                      className="w-full rounded-lg border px-3 py-2 text-sm"
                      value={delayReason}
                      onChange={e => setDelayReason(e.target.value)}
                      placeholder="Explain why the POA&M is delayed..."
                    />
                  </div>
                  <div>
                    <label className="mb-1 block text-xs font-medium text-gray-500">Revised Completion Date *</label>
                    <input type="date" required className="w-full rounded-lg border px-3 py-2 text-sm" value={revisedDate} onChange={e => setRevisedDate(e.target.value)} />
                  </div>
                </>
              )}

              {dialog === 'resume' && (
                <div>
                  <label className="mb-1 block text-xs font-medium text-gray-500">Revised Completion Date *</label>
                  <input type="date" required className="w-full rounded-lg border px-3 py-2 text-sm" value={revisedDate} onChange={e => setRevisedDate(e.target.value)} />
                </div>
              )}

              {dialog === 'complete' && (
                <div className="rounded-lg bg-yellow-50 p-3 text-sm text-yellow-800">
                  {detail.findingId
                    ? 'The linked finding status will be validated.'
                    : 'No linked finding — POA&M will be marked complete.'}
                </div>
              )}

              {dialog === 'risk' && (
                <div>
                  <label className="mb-1 block text-xs font-medium text-gray-500">Deviation Record ID *</label>
                  <input
                    required
                    className="w-full rounded-lg border px-3 py-2 text-sm"
                    value={deviationId}
                    onChange={e => setDeviationId(e.target.value)}
                    placeholder="Enter deviation record ID..."
                  />
                </div>
              )}

              {error && <p className="text-sm text-red-600">{error}</p>}

              <div className="flex justify-end gap-2 pt-2">
                <button onClick={() => setDialog(null)} className="rounded-lg bg-gray-100 px-4 py-2 text-sm hover:bg-gray-200">
                  Cancel
                </button>
                <button
                  onClick={() => handleSubmit(
                    dialog === 'delay' ? 'Delayed' :
                    dialog === 'resume' ? 'Ongoing' :
                    dialog === 'complete' ? 'Completed' : 'RiskAccepted'
                  )}
                  disabled={loading ||
                    (dialog === 'delay' && (!delayReason || !revisedDate)) ||
                    (dialog === 'resume' && !revisedDate) ||
                    (dialog === 'risk' && !deviationId)}
                  className="rounded-lg bg-indigo-600 px-4 py-2 text-sm text-white hover:bg-indigo-700 disabled:opacity-50"
                >
                  {loading ? 'Processing...' : 'Confirm'}
                </button>
              </div>
            </div>
          </div>
        </div>
      )}
      {cascadePrompt && (
        <CascadeConfirmDialog
          message={`Propagate status change to linked remediation task?`}
          detail={`The linked task (${detail.remediationTaskId}) will be updated to reflect the new POA&M status.`}
          onConfirm={handleCascadeConfirm}
          onDismiss={() => { setCascadePrompt(null); onStatusChanged(); }}
        />
      )}
    </section>
  );
}
