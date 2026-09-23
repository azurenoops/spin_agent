import { useEffect, useRef, type ReactNode } from 'react';
import { X } from 'lucide-react';

export default function SetupDialog({ children, busy, onClose, organizationName, expanded = false, title = 'Add a security capability', description }: {
  children: ReactNode; busy: boolean; onClose: () => void; organizationName?: string; expanded?: boolean;
  title?: string; description?: string;
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
  return <dialog ref={dialog} aria-labelledby="capability-setup-title" aria-describedby="capability-setup-description"
    aria-busy={busy}
    className={`m-auto max-h-[90dvh] w-[calc(100%-2rem)] overflow-hidden border border-slate-200 bg-white p-0 text-slate-900 shadow-2xl backdrop:bg-slate-950/50 dark:border-gray-700 dark:bg-gray-900 dark:text-gray-100 ${
      organizationName !== undefined ? `${expanded ? 'max-w-3xl' : 'max-w-xl'} rounded-lg` : 'max-w-3xl rounded-2xl'}`}
    onCancel={event => { event.preventDefault(); if (!busy) onClose(); }}
    onClick={event => {
      if (event.target !== event.currentTarget || busy) return;
      const bounds = event.currentTarget.getBoundingClientRect();
      if (event.clientX < bounds.left || event.clientX > bounds.right || event.clientY < bounds.top || event.clientY > bounds.bottom) onClose();
    }}>
    <div className="flex max-h-[90dvh] flex-col">
      <header className={`flex shrink-0 items-start justify-between gap-3 px-5 pt-4 ${organizationName !== undefined ? 'pb-2' : 'border-b border-slate-200 pb-4 dark:border-gray-700'}`}>
        <div><h2 id="capability-setup-title" className={`${organizationName !== undefined ? 'text-base' : 'text-lg'} font-semibold`}>{title}</h2>
          <p id="capability-setup-description" className={`mt-1 ${organizationName !== undefined ? 'text-xs' : 'text-sm'} text-slate-500 dark:text-gray-400`}>
            {description ?? (organizationName !== undefined ? `Organization: ${organizationName}` : 'Choose a capability, connect its components, and review the changes.')}</p></div>
        <button type="button" aria-label="Close dialog" disabled={busy} onClick={onClose}
          className="rounded p-2 hover:bg-slate-100 disabled:opacity-50 dark:hover:bg-gray-800"><X size={20} aria-hidden="true" /></button>
      </header>
      <div className={`min-h-0 overflow-y-auto px-5 pb-4 ${organizationName !== undefined ? 'pt-2' : 'pt-5'}`}>{children}</div>
    </div>
  </dialog>;
}
