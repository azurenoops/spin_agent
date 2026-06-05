import { useEffect, useId, useRef, useState } from 'react';

/**
 * Task #247 — SkipStepModal
 *
 * Accessible modal dialog that collects a mandatory free-text reason before
 * the user skips an optional step in the TenantWizard onboarding flow.
 *
 * Accessibility contract:
 *  - `role="dialog"` + `aria-modal="true"` + `aria-labelledby` pointing at the heading.
 *  - Textarea receives focus on open via `requestAnimationFrame` (after paint).
 *  - Escape key triggers `onCancel`.
 *  - Confirm button is `aria-busy` during the async call.
 */

interface SkipStepModalProps {
  /** Controls whether the modal is visible. */
  open: boolean;
  /** Human-readable name of the step being skipped (shown in the heading). */
  stepName: string;
  /**
   * Called when the user confirms skip.  Receives the trimmed reason string.
   * Should call `onboarding.skipStep(step)` (Task #247) and advance the wizard.
   * Throwing rejects the in-progress spinner and surfaces an inline error.
   */
  onConfirm: (reason: string) => Promise<void>;
  /** Called when the user cancels or presses Escape. */
  onCancel: () => void;
}

export default function SkipStepModal({
  open,
  stepName,
  onConfirm,
  onCancel,
}: SkipStepModalProps) {
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const textareaRef = useRef<HTMLTextAreaElement>(null);
  const titleId = useId();

  // Reset form state + focus textarea each time the modal opens.
  useEffect(() => {
    if (open) {
      setReason('');
      setError(null);
      setBusy(false);
      const raf = requestAnimationFrame(() => textareaRef.current?.focus());
      return () => cancelAnimationFrame(raf);
    }
  }, [open]);

  if (!open) return null;

  const handleConfirm = async () => {
    const trimmed = reason.trim();
    if (!trimmed) return;
    setBusy(true);
    setError(null);
    try {
      await onConfirm(trimmed);
    } catch (err) {
      setError((err as Error).message ?? 'Failed to skip step. Please try again.');
      setBusy(false);
    }
    // On success the parent closes the modal by setting open=false, so we
    // intentionally do NOT setBusy(false) here — the spinner persists until
    // unmount, providing visual feedback during the wizard advance animation.
  };

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'Escape' && !busy) onCancel();
  };

  return (
    <div
      className="fixed inset-0 z-[60] flex items-center justify-center"
      onKeyDown={handleKeyDown}
    >
      {/* Scrim — clicking dismisses (unless busy) */}
      <div
        className="absolute inset-0 bg-black/40"
        aria-hidden="true"
        onClick={() => !busy && onCancel()}
      />

      {/* Dialog */}
      <div
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        className="relative z-10 w-full max-w-md rounded-xl border border-slate-200 bg-white p-6 shadow-xl"
      >
        <h2 id={titleId} className="text-base font-semibold text-slate-900">
          Skip &ldquo;{stepName}&rdquo;?
        </h2>
        <p className="mt-1 text-sm text-slate-500">
          Provide a reason for skipping this step. You can return and complete it later.
        </p>

        <div className="mt-4">
          <label
            htmlFor="skip-reason"
            className="block text-sm font-medium text-slate-700"
          >
            Reason <span className="text-rose-600" aria-hidden="true">*</span>
          </label>
          <textarea
            id="skip-reason"
            ref={textareaRef}
            value={reason}
            onChange={(e) => setReason(e.target.value)}
            rows={3}
            placeholder="e.g. Address not yet confirmed — will update before ATO submission."
            className="mt-1 w-full rounded-md border border-slate-300 px-3 py-2 text-sm shadow-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500 disabled:cursor-not-allowed disabled:bg-slate-50 disabled:text-slate-400"
            aria-required="true"
            disabled={busy}
          />
        </div>

        {error && (
          <p role="alert" className="mt-2 text-sm text-rose-700">
            {error}
          </p>
        )}

        <div className="mt-5 flex justify-end gap-3">
          <button
            type="button"
            onClick={onCancel}
            disabled={busy}
            className="rounded-md border border-slate-300 bg-white px-4 py-2 text-sm font-medium text-slate-700 hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-50"
          >
            Cancel
          </button>
          <button
            type="button"
            onClick={handleConfirm}
            disabled={busy || reason.trim().length === 0}
            aria-busy={busy}
            className="inline-flex items-center gap-2 rounded-md bg-amber-500 px-4 py-2 text-sm font-medium text-white hover:bg-amber-600 disabled:cursor-not-allowed disabled:opacity-50"
          >
            {busy && (
              <svg
                className="h-4 w-4 animate-spin"
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                strokeWidth={2.5}
                aria-hidden="true"
              >
                <path strokeLinecap="round" d="M12 2a10 10 0 0 1 10 10" />
              </svg>
            )}
            {busy ? 'Skipping…' : 'Skip step'}
          </button>
        </div>
      </div>
    </div>
  );
}
