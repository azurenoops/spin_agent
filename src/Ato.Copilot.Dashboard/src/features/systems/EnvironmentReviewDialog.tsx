import { useRef, useState, type ComponentProps, type ReactNode } from 'react';
import UnsavedDesignGuard from '../system-design/UnsavedDesignGuard';
import SetupDialog from '../workspace-operations/SetupDialog';
import { secondaryButtonClass } from '../workspace-operations/workspaceUi';

type Props = Omit<ComponentProps<typeof SetupDialog>, 'children'> & {
  children: ReactNode | ((requestClose: () => void) => ReactNode);
};

export default function EnvironmentReviewDialog({ children, onClose, busy, className = '', ...props }: Props) {
  const [dirty, setDirty] = useState(false);
  const [closing, setClosing] = useState(false);
  const lastFocus = useRef<HTMLElement | null>(null);
  const requestClose = () => {
    if (busy) return;
    if (!dirty) { onClose(); return; }
    lastFocus.current = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    setClosing(true);
  };
  return <SetupDialog {...props} className={`[overflow-wrap:anywhere] ${className}`} busy={busy} onClose={requestClose}>
    <UnsavedDesignGuard dirty={dirty} inline title="Unsaved environment review changes"
      description="Your local review input has not been saved. Keep editing to retain it. Leaving discards only this input; saved records and approved baselines are unchanged." />
    {closing && <section role="alert" className="mb-4 space-y-3 rounded border border-amber-300 p-3 text-sm">
      <h3 className="font-semibold">Keep unsaved review input?</h3>
      <p>No saved records will change. Discard only if you no longer need this input.</p>
      <div className="flex flex-wrap gap-3">
        <button type="button" autoFocus className={secondaryButtonClass} onClick={() => {
          setClosing(false); lastFocus.current?.focus();
        }}>Keep editing</button>
        <button type="button" className={secondaryButtonClass} onClick={onClose}>Discard and close</button>
      </div>
    </section>}
    <div onChangeCapture={() => setDirty(true)}>{typeof children === 'function' ? children(requestClose) : children}</div>
  </SetupDialog>;
}
