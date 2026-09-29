import { useState } from 'react';
import type { PoamDetail, PoamStatus } from '../../types/poam';
import { updatePoamStatus } from '../../api/poam';
import CascadeConfirmDialog from './CascadeConfirmDialog';
import { useSystemMutationPermission } from '../permissions/useSystemMutationPermission';
import { poamErrorMessage } from '../../utils/poamErrors';

interface PoamLifecycleActionsProps {
  detail: PoamDetail;
  onStatusChanged: () => void;
  onBusyChange?: (busy: boolean) => void;
}

export default function PoamLifecycleActions({ detail, onStatusChanged, onBusyChange }: PoamLifecycleActionsProps) {
  const canManageRemediation = useSystemMutationPermission(detail.systemId, 'canManageRemediation');
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
    if (loading) return;
    if (!canManageRemediation) {
      setError('Permission denied: you cannot manage remediation for this system.');
      return;
    }
    setLoading(true);
    onBusyChange?.(true);
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
      const msg = poamErrorMessage(err);
      if (msg.includes('409') || msg.includes('CONCURRENCY')) {
        setError('Concurrency conflict. Please reload and try again.');
      } else {
        setError(msg);
      }
    } finally {
      setLoading(false);
      onBusyChange?.(false);
    }
  };

  const handleCascadeConfirm = async () => {
    if (!canManageRemediation) throw new Error('Permission denied: you cannot manage remediation for this system.');
    if (!cascadePrompt) return;
    onBusyChange?.(true);
    try {
      await updatePoamStatus(detail.id, {
        status: cascadePrompt.newStatus,
        rowVersion: cascadePrompt.rowVersion,
        cascadeToTask: true,
      });
      setCascadePrompt(null);
      onStatusChanged();
    } finally {
      onBusyChange?.(false);
    }
  };

  const openDialog = (next: NonNullable<typeof dialog>) => {
    if (!canManageRemediation) {
      setError('Permission denied: you cannot manage remediation for this system.');
      return;
    }
    setDialog(next);
  };

  return (
    <section>
      <h3 className="mb-3 text-sm font-semibold uppercase tracking-wider text-gray-400">Lifecycle Actions</h3>
      {!canManageRemediation && <p role="alert" className="text-sm text-red-600">Permission denied: you cannot manage remediation for this system.</p>}
      {error && !dialog && <p role="alert" className="text-sm text-red-600">{error}</p>}
      <div className="flex flex-wrap gap-2">
        {canTransitionTo('Delayed') && (
          <button
            disabled={!canManageRemediation}
            onClick={() => openDialog('delay')}
            className="rounded-lg bg-amber-50 px-3 py-1.5 text-xs font-medium text-amber-700 hover:bg-amber-100"
          >
            Mark Delayed
          </button>
        )}
        {detail.status === 'Delayed' && canTransitionTo('Ongoing') && (
          <button
            disabled={!canManageRemediation}
            onClick={() => openDialog('resume')}
            className="rounded-lg bg-indigo-50 px-3 py-1.5 text-xs font-medium text-indigo-700 hover:bg-indigo-100"
          >
            Resume
          </button>
        )}
        {canTransitionTo('Completed') && (
          <button
            disabled={!canManageRemediation}
            onClick={() => openDialog('complete')}
            className="rounded-lg bg-green-50 px-3 py-1.5 text-xs font-medium text-green-700 hover:bg-green-100"
          >
            Mark completed (manual disposition)
          </button>
        )}
        {canTransitionTo('RiskAccepted') && (
          <button
            disabled={!canManageRemediation}
            onClick={() => openDialog('risk')}
            className="rounded-lg bg-purple-50 px-3 py-1.5 text-xs font-medium text-purple-700 hover:bg-purple-100"
          >
            Risk Accepted
          </button>
        )}
      </div>

      {/* Dialog overlay */}
      {dialog && (
        <div className="mt-4 rounded-lg border border-slate-200 bg-slate-50 p-4 dark:border-slate-700 dark:bg-slate-800" aria-label="Confirm lifecycle change">
          <div className="[&_input]:dark:bg-slate-900 [&_textarea]:dark:bg-slate-900 [&_label]:dark:text-slate-200">
            <h3 className="mb-4 text-base font-semibold text-slate-900 dark:text-slate-100">
              {dialog === 'delay' && 'Mark as Delayed'}
              {dialog === 'resume' && 'Resume POA&M'}
              {dialog === 'complete' && 'Mark as Completed'}
              {dialog === 'risk' && 'Risk Accepted'}
            </h3>

            <div className="space-y-3">
              {dialog === 'delay' && (
                <>
                  <div>
                    <label htmlFor="poam-delay-reason" className="mb-1 block text-xs font-medium text-gray-500">Delay reason *</label>
                    <textarea
                      id="poam-delay-reason"
                      rows={2}
                      required
                      className="w-full rounded-lg border px-3 py-2 text-sm"
                      value={delayReason}
                      onChange={e => setDelayReason(e.target.value)}
                      placeholder="Explain why the POA&M is delayed..."
                    />
                  </div>
                  <div>
                    <label htmlFor="poam-revised-date" className="mb-1 block text-xs font-medium text-gray-500">Revised completion date *</label>
                    <input id="poam-revised-date" type="date" required className="w-full rounded-lg border px-3 py-2 text-sm" value={revisedDate} onChange={e => setRevisedDate(e.target.value)} />
                  </div>
                </>
              )}

              {dialog === 'resume' && (
                <div>
                  <label htmlFor="poam-resume-date" className="mb-1 block text-xs font-medium text-gray-500">Revised completion date *</label>
                  <input id="poam-resume-date" type="date" required className="w-full rounded-lg border px-3 py-2 text-sm" value={revisedDate} onChange={e => setRevisedDate(e.target.value)} />
                </div>
              )}

              {dialog === 'complete' && (
                <div className="rounded-lg bg-yellow-50 p-3 text-sm text-yellow-800">
                  This records POA&amp;M completion; it does not certify task verification or evidence review.
                  Review retained task evidence and milestone completion first. Server validation and concurrency checks remain authoritative.
                </div>
              )}

              {dialog === 'risk' && (
                <div>
                  <label htmlFor="poam-deviation-id" className="mb-1 block text-xs font-medium text-gray-500">Deviation Record ID *</label>
                  <input
                    id="poam-deviation-id"
                    required
                    className="w-full rounded-lg border px-3 py-2 text-sm"
                    value={deviationId}
                    onChange={e => setDeviationId(e.target.value)}
                    placeholder="Enter deviation record ID..."
                  />
                </div>
              )}

              {error && <p role="alert" className="text-sm text-red-600">{error}</p>}

              <div className="flex justify-end gap-2 pt-2">
                <button disabled={loading} onClick={() => setDialog(null)} className="rounded-lg border border-slate-300 px-4 py-2 text-sm hover:bg-slate-100 dark:border-slate-600 dark:hover:bg-slate-700">
                  Cancel
                </button>
                <button
                  onClick={() => handleSubmit(
                    dialog === 'delay' ? 'Delayed' :
                    dialog === 'resume' ? 'Ongoing' :
                    dialog === 'complete' ? 'Completed' : 'RiskAccepted'
                  )}
                  disabled={!canManageRemediation || loading ||
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
          canConfirm={canManageRemediation}
          message={`Propagate status change to linked remediation task?`}
          detail={`The linked task (${detail.remediationTaskId}) will be updated to reflect the new POA&M status.`}
          onConfirm={handleCascadeConfirm}
          onDismiss={() => { setCascadePrompt(null); onStatusChanged(); }}
        />
      )}
    </section>
  );
}
