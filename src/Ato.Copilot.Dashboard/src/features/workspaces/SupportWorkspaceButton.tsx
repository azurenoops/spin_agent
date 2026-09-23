import { useEffect, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { startImpersonation } from '../tenancy/api';
import { buildWorkspaceUrl } from './workspaceRoutes';
import { workspaceErrorMessage } from './api';

export default function SupportWorkspaceButton({ tenantId, tenantName, route = '/', disabled = false }: {
  tenantId: string; tenantName: string; route?: string; disabled?: boolean;
}) {
  const navigate = useNavigate();
  const [confirm, setConfirm] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [reason, setReason] = useState('');
  const [reference, setReference] = useState('');
  const [acknowledged, setAcknowledged] = useState(false);
  const invokerRef = useRef<HTMLButtonElement>(null);
  const dialogRef = useRef<HTMLDivElement>(null);
  const validPurpose = reason.trim().length >= 3 && reason.trim().length <= 500
    && reference.trim().length <= 100 && acknowledged;
  const close = () => {
    if (busy) return;
    setConfirm(false);
    invokerRef.current?.focus();
  };
  useEffect(() => {
    if (!confirm) return;
    const dialog = dialogRef.current;
    const keydown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.preventDefault();
        close();
        return;
      }
      if (event.key !== 'Tab' || !dialog) return;
      const focusable = Array.from(dialog.querySelectorAll<HTMLElement>(
        'button:not(:disabled), input:not(:disabled), textarea:not(:disabled), [href], [tabindex]:not([tabindex="-1"])',
      ));
      if (!focusable.length) return;
      const first = focusable[0]!;
      const last = focusable[focusable.length - 1]!;
      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault(); last.focus();
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault(); first.focus();
      }
    };
    dialog?.addEventListener('keydown', keydown);
    return () => dialog?.removeEventListener('keydown', keydown);
  }, [confirm, busy]);
  const start = async () => {
    if (!validPurpose) {
      setError('Reason must contain 3-500 characters, reference is limited to 100 characters, and acknowledgement is required.');
      return;
    }
    setBusy(true);
    setError(null);
    try {
      await startImpersonation(tenantId, tenantName, {
        reason: reason.trim(),
        reference: reference.trim() || undefined,
        acknowledged,
      });
      navigate(buildWorkspaceUrl({ kind: 'organization', tenantId, mode: 'support' }, route));
    } catch (reason) {
      setError(workspaceErrorMessage(reason));
    } finally {
      setBusy(false);
    }
  };
  return (
    <span onClick={event => event.stopPropagation()}>
      <button ref={invokerRef} type="button" disabled={disabled} onClick={() => setConfirm(true)}
        className="rounded border border-amber-400 px-2 py-1 text-amber-900 dark:border-amber-600 dark:text-amber-200">Audited support</button>
      {confirm && <div ref={dialogRef} role="dialog" aria-modal="true" aria-label={`Audited support for ${tenantName}`}
        className="fixed inset-0 z-[100] flex items-center justify-center whitespace-normal bg-black/50 p-4">
        <div className="block max-w-md space-y-4 rounded bg-white p-6 text-gray-900 dark:bg-gray-900 dark:text-gray-100">
          <strong className="block">Enter audited support for {tenantName}?</strong>
          <span className="block">This starts a time-limited, audited impersonation session, not ordinary membership. Save your work first; continuing discards unsaved changes in this tab.</span>
          <label className="block text-sm font-medium">Support reason
            <textarea autoFocus required maxLength={500} value={reason}
              onChange={event => setReason(event.target.value)}
              className="mt-1 block w-full rounded border border-gray-300 bg-white p-2 text-gray-900 dark:border-gray-600 dark:bg-gray-800 dark:text-gray-100" />
          </label>
          <label className="block text-sm font-medium">Ticket or reference (optional)
            <input maxLength={100} value={reference} onChange={event => setReference(event.target.value)}
              className="mt-1 block w-full rounded border border-gray-300 bg-white p-2 text-gray-900 dark:border-gray-600 dark:bg-gray-800 dark:text-gray-100" />
          </label>
          <label className="flex items-start gap-2 text-sm">
            <input type="checkbox" className="accent-indigo-700 dark:accent-indigo-400" checked={acknowledged}
              onChange={event => setAcknowledged(event.target.checked)} />
            I acknowledge that support access is time-limited, audited, and restricted to this organization.
          </label>
          {error && <span role="alert" className="block text-red-700 dark:text-red-300">{error}</span>}
          <div className="flex gap-4">
            <button type="button" className="rounded border border-gray-300 bg-white px-3 py-2 text-gray-800 dark:border-gray-600 dark:bg-gray-800 dark:text-gray-100"
              disabled={busy} onClick={close}>Cancel</button>
            <button type="button" disabled={busy || !validPurpose}
              className="rounded bg-indigo-700 px-3 py-2 text-white disabled:opacity-50 dark:bg-indigo-500 dark:text-gray-950"
              onClick={() => { void start(); }}>{busy ? 'Starting support…' : 'Start audited support'}</button>
          </div>
        </div>
      </div>}
    </span>
  );
}
