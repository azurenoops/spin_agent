import { useEffect, useRef, type ReactNode } from 'react';
import { X } from 'lucide-react';
import type { PolicySource } from '../../api/policyWorkspace';

export function PolicyDrawer({ title, label, systemName, busy = false, onClose, children, footer }: {
  title: string; label: string; systemName: string; busy?: boolean; onClose: () => void; children: ReactNode; footer: ReactNode;
}) {
  const ref = useRef<HTMLElement>(null);
  const closeRef = useRef<HTMLButtonElement>(null);
  const latest = useRef({ onClose, busy });
  latest.current = { onClose, busy };
  useEffect(() => {
    closeRef.current?.focus({ preventScroll: true });
    const key = (event: KeyboardEvent) => {
      if (event.key === 'Escape' && !latest.current.busy) { event.preventDefault(); latest.current.onClose(); }
      if (event.key !== 'Tab' || !ref.current) return;
      const elements = Array.from(ref.current.querySelectorAll<HTMLElement>(
        'button:not(:disabled), a[href], input:not(:disabled), select:not(:disabled), textarea:not(:disabled), summary',
      )).filter(el => !el.closest('details:not([open])') || el.tagName === 'SUMMARY');
      const first = elements[0], last = elements.at(-1);
      if (!first || !ref.current.contains(document.activeElement)) {
        event.preventDefault();
        (event.shiftKey ? last ?? ref.current : first ?? ref.current).focus();
        return;
      }
      if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last?.focus(); }
      else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first?.focus(); }
    };
    document.addEventListener('keydown', key);
    return () => document.removeEventListener('keydown', key);
  }, []);
  return <div className="pw-layer">
    <button className="pw-backdrop" type="button" tabIndex={-1} disabled={busy} aria-label="Close policy backdrop" onClick={onClose} />
    <aside className="pw-drawer" role="dialog" aria-modal="true" aria-label={label} ref={ref} tabIndex={-1}>
      <header><div><small>{label}</small><h2>{title}</h2><p className="pw-system-name">{systemName}</p></div>
        <button type="button" ref={closeRef} disabled={busy} onClick={onClose} aria-label="Close policy drawer"><X size={18} /></button></header>
      <div className="pw-drawer-body">{children}</div><footer>{footer}</footer>
    </aside>
  </div>;
}

export function PolicySourceCard({ source, heading = 'Policy source' }: { source: PolicySource; heading?: string }) {
  return <section className="pw-source-card" aria-label={heading}><h3>{heading}</h3>
    <strong>{source.name}</strong><p>{source.versionLabel}</p>
    <span className="pw-pill">{source.status} source</span>
    <dl><dt>Topic</dt><dd>{source.subType || 'Not recorded'}</dd>
      <dt>Owner</dt><dd>{source.owner || 'Not recorded'}</dd></dl>
    <p className="pw-prose">{source.description || 'No source description recorded.'}</p>
  </section>;
}
