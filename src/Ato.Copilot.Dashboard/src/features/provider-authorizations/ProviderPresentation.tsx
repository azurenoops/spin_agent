import type { ReactNode } from 'react';
import './providerPresentation.css';

export function ProviderPanel({ title, children, action, description }: { title: string; children: ReactNode; action?: ReactNode; description?: ReactNode }) {
  return <section className="provider-panel" aria-label={title}>
    <div className="provider-panel-head">{description ? <div><h2>{title}</h2>{description}</div> : <h2>{title}</h2>}{action}</div>{children}
  </section>;
}

export function ProviderSupport({ children, childrenFirst = true }: { children?: ReactNode; childrenFirst?: boolean }) {
  return <aside className="provider-support">
    {childrenFirst && children}
    <ProviderPanel title="Contributes to the system package">
      <ul><li>Hosting and boundary description</li><li>Reviewed implementation statements</li><li>Evidence references and customer duties</li></ul>
    </ProviderPanel>
    <ProviderPanel title="Mission Owner handoff">
      <p>Publish reviewed capabilities for eligible systems. Allocation, adoption and the mission authorization decision remain separate actions.</p>
    </ProviderPanel>
    {!childrenFirst && children}
  </aside>;
}

export function ProviderBadge({ children, tone }: { children: ReactNode; tone?: 'success' | 'attention' | 'neutral' }) {
  return <span className={`provider-badge${tone ? ` provider-badge-${tone}` : ''}`}>{children}</span>;
}

export function ProviderFact({ label, children }: { label: string; children: ReactNode }) {
  return <div className="provider-fact"><dt>{label}</dt><dd>{children}</dd></div>;
}

export function ProviderChecklistRow({ title, description, children, step }: { title: string; description: string; children: ReactNode; step?: number }) {
  return <div className="provider-checklist-row"><div className="provider-checklist-leading">{step && <span className="provider-step" aria-hidden="true">{step}</span>}<div><h3>{title}</h3><p>{description}</p></div></div><div>{children}</div></div>;
}
