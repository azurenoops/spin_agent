import { useEffect, useRef, type ReactNode } from 'react';
import { createPortal } from 'react-dom';
import { X } from 'lucide-react';

export default function SetupDialog({ children, busy, onClose, organizationName, expanded = false, title = 'Add a security capability', description, placement = 'center' }: {
  children: ReactNode; busy: boolean; onClose: () => void; organizationName?: string; expanded?: boolean;
  title?: string; description?: string;
  placement?: 'center' | 'right';
}) {
  const dialog = useRef<HTMLDialogElement>(null);
  useEffect(() => {
    const element = dialog.current!;
    const invoker = document.activeElement;
    const overflow = document.body.style.overflow;
    element.showModal();
    document.body.style.overflow = 'hidden';
    return () => {
      element.close();
      document.body.style.overflow = overflow;
      if (invoker instanceof HTMLElement && invoker.isConnected) invoker.focus();
    };
  }, []);
  return createPortal(<dialog ref={dialog} tabIndex={-1} aria-labelledby="capability-setup-title" aria-describedby="capability-setup-description"
    aria-busy={busy}
    className={`overflow-hidden border border-slate-200 bg-white p-0 text-slate-900 shadow-2xl backdrop:bg-slate-950/50 dark:border-gray-700 dark:bg-gray-900 dark:text-gray-100 ${
      placement === 'right' ? 'm-0 ml-auto h-dvh max-h-dvh w-full max-w-lg'
        : `m-auto max-h-[90dvh] w-[calc(100%-2rem)] ${organizationName !== undefined ? `${expanded ? 'max-w-3xl' : 'max-w-xl'} rounded-lg` : 'max-w-3xl rounded-2xl'}`}`}
    onCancel={event => { event.preventDefault(); if (!busy) onClose(); }}
    onKeyDown={event => {
      if (event.key !== 'Tab') return;
      const focusable = Array.from(event.currentTarget.querySelectorAll<HTMLElement>(
        'button, a[href], input, select, textarea, summary, [tabindex]',
      )).filter(element => element.tabIndex >= 0 && !element.matches(':disabled')
        && element.getClientRects().length > 0 && getComputedStyle(element).visibility !== 'hidden');
      const first = focusable[0];
      const last = focusable[focusable.length - 1];
      if (!first || !last) {
        event.preventDefault(); event.currentTarget.focus();
      } else if (event.shiftKey && (document.activeElement === first || document.activeElement === event.currentTarget)) {
        event.preventDefault(); last.focus();
      } else if (!event.shiftKey && (document.activeElement === last || document.activeElement === event.currentTarget)) {
        event.preventDefault(); first.focus();
      }
    }}
    onClick={event => {
      if (event.target !== event.currentTarget || busy) return;
      const bounds = event.currentTarget.getBoundingClientRect();
      if (event.clientX < bounds.left || event.clientX > bounds.right || event.clientY < bounds.top || event.clientY > bounds.bottom) onClose();
    }}>
    <div className={`flex flex-col ${placement === 'right' ? 'h-dvh max-h-dvh' : 'max-h-[90dvh]'}`}>
      <header className={`flex shrink-0 items-start justify-between gap-3 px-5 pt-4 ${organizationName !== undefined ? 'pb-2' : 'border-b border-slate-200 pb-4 dark:border-gray-700'}`}>
        <div><h2 id="capability-setup-title" className={`${organizationName !== undefined ? 'text-base' : 'text-lg'} font-semibold`}>{title}</h2>
          <p id="capability-setup-description" className={`mt-1 ${organizationName !== undefined ? 'text-xs' : 'text-sm'} text-slate-500 dark:text-gray-400`}>
            {description ?? (organizationName !== undefined ? `Organization: ${organizationName}` : 'Choose a capability, connect its components, and review the changes.')}</p></div>
        <button type="button" aria-label="Close dialog" disabled={busy} onClick={onClose}
          className="rounded p-2 hover:bg-slate-100 disabled:opacity-50 dark:hover:bg-gray-800"><X size={20} aria-hidden="true" /></button>
      </header>
      <div className={`min-h-0 overflow-y-auto px-5 pb-4 ${organizationName !== undefined ? 'pt-2' : 'pt-5'}`}>{children}</div>
    </div>
  </dialog>, document.body);
}
