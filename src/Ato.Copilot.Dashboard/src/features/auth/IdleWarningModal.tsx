import { useEffect, useRef, useState } from 'react';

/**
 * Feature 051 T060 [US2] — modal that surfaces 60s before idle sign-out
 * (FR-007). Subscribes to the `'ato:idle-warning'` event fired by
 * {@link useIdleTimer} and renders a countdown plus a "Stay signed in"
 * button that dispatches `'ato:user-input'` to reset the idle clock.
 *
 * - `role="alertdialog"` because the modal demands a user response.
 * - `aria-labelledby` points at the heading.
 * - The "Stay signed in" button receives focus on mount so keyboard
 *   users can dismiss with the Enter key.
 * - When the countdown reaches 0 the modal closes itself; the actual
 *   sign-out is {@link useIdleTimer}'s responsibility, which fires at
 *   the same instant.
 */
export default function IdleWarningModal() {
  const [open, setOpen] = useState(false);
  const [remaining, setRemaining] = useState(0);
  const buttonRef = useRef<HTMLButtonElement>(null);
  const tickRef = useRef<ReturnType<typeof setInterval> | null>(null);

  // Subscribe once to the warning event.
  useEffect(() => {
    const onWarning = (e: Event) => {
      const detail = (e as CustomEvent<{ secondsUntilSignOut: number }>).detail;
      const seconds = Math.max(1, detail?.secondsUntilSignOut ?? 60);
      setRemaining(seconds);
      setOpen(true);
    };
    window.addEventListener('ato:idle-warning', onWarning as EventListener);
    return () => {
      window.removeEventListener('ato:idle-warning', onWarning as EventListener);
    };
  }, []);

  // Drive the countdown while the modal is open.
  useEffect(() => {
    if (!open) {
      if (tickRef.current !== null) {
        clearInterval(tickRef.current);
        tickRef.current = null;
      }
      return;
    }
    tickRef.current = setInterval(() => {
      setRemaining((s) => {
        if (s <= 1) {
          setOpen(false);
          return 0;
        }
        return s - 1;
      });
    }, 1_000);
    return () => {
      if (tickRef.current !== null) {
        clearInterval(tickRef.current);
        tickRef.current = null;
      }
    };
  }, [open]);

  // Focus trap — focus the primary action when the modal opens.
  useEffect(() => {
    if (open && buttonRef.current !== null) {
      buttonRef.current.focus();
    }
  }, [open]);

  if (!open) return null;

  const handleStaySignedIn = () => {
    window.dispatchEvent(
      new CustomEvent('ato:user-input', { detail: { source: 'idle-warning-dismiss' } }),
    );
    setOpen(false);
  };

  return (
    <div
      role="alertdialog"
      aria-modal="true"
      aria-labelledby="idle-warning-title"
      aria-describedby="idle-warning-description"
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4"
    >
      <div className="w-full max-w-md rounded-lg bg-white p-6 shadow-xl">
        <h2 id="idle-warning-title" className="text-lg font-semibold text-gray-900">
          You will be signed out soon
        </h2>
        <p id="idle-warning-description" className="mt-2 text-sm text-gray-600">
          For your security, this session will end in{' '}
          <span className="font-medium text-gray-900">{remaining}</span>{' '}
          {remaining === 1 ? 'second' : 'seconds'} unless you continue working.
        </p>
        <div className="mt-6 flex justify-end gap-3">
          <button
            ref={buttonRef}
            type="button"
            onClick={handleStaySignedIn}
            className="inline-flex justify-center rounded-md bg-indigo-600 px-4 py-2 text-sm font-medium text-white shadow-sm hover:bg-indigo-700 focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:ring-offset-2"
          >
            Stay signed in
          </button>
        </div>
      </div>
    </div>
  );
}
