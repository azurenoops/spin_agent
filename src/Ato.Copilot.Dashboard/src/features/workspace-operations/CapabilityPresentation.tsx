import type { ReactNode } from 'react';
import { FileText, MapPin, Monitor, Users } from 'lucide-react';
import type { SupportingComponent } from './types';

export const workspaceCard = 'rounded-lg border border-slate-200 bg-white p-5 shadow-sm dark:border-gray-700 dark:bg-gray-900';
export const heroAction = 'inline-flex items-center justify-center gap-2 rounded-md border border-white bg-white px-4 py-2 text-sm font-medium text-indigo-700 shadow-sm hover:bg-indigo-50 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-white';

export function reviewStateLabel(state?: string | null) {
  if (state === 'ReviewRequired' || state === 'NeedsReview') return 'Needs review';
  return state?.replace(/([a-z])([A-Z])/g, '$1 $2') || 'Review not recorded';
}

export function StateBadge({ children, tone = 'neutral' }: {
  children: ReactNode; tone?: 'neutral' | 'indigo' | 'amber' | 'green';
}) {
  const colors = {
    neutral: 'border-slate-200 bg-slate-50 text-slate-600 dark:border-gray-600 dark:bg-gray-800 dark:text-gray-300',
    indigo: 'border-indigo-100 bg-indigo-50 text-indigo-700 dark:border-indigo-900 dark:bg-indigo-950 dark:text-indigo-200',
    amber: 'border-amber-100 bg-amber-50 text-amber-800 dark:border-amber-900 dark:bg-amber-950 dark:text-amber-200',
    green: 'border-emerald-100 bg-emerald-50 text-emerald-700 dark:border-emerald-900 dark:bg-emerald-950 dark:text-emerald-200',
  };
  return <span className={`inline-flex rounded border px-2 py-0.5 text-xs font-medium ${colors[tone]}`}>{children}</span>;
}

export function ComponentIcon({ type }: { type: string }) {
  const Icon = type === 'Person' ? Users : type === 'Policy' ? FileText : type === 'Place' ? MapPin : Monitor;
  return <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-lg bg-indigo-50 text-indigo-600 dark:bg-indigo-950 dark:text-indigo-300">
    <Icon size={19} aria-hidden="true" />
  </span>;
}

export function SupportingComponents({ items }: { items: SupportingComponent[] }) {
  return <ul className="divide-y divide-slate-200 dark:divide-gray-700">{items.map(item =>
    <li key={`${item.source}:${item.id}`} className="flex items-center gap-3 py-4">
      <ComponentIcon type={item.componentType} />
      <div className="min-w-0 flex-1"><p className="font-medium text-slate-800 dark:text-gray-100">{item.name}</p>
        <p className="mt-1 text-xs text-slate-500 dark:text-gray-400">
          {item.source === 'provider' ? 'Provider-managed' : 'Organization-managed'}
          {item.description && ` · ${item.description}`}
        </p></div>
      <StateBadge>{item.componentType}</StateBadge>
    </li>)}</ul>;
}

export function WorkspaceFootnote() {
  return <footer className="mt-8 flex flex-wrap justify-between gap-3 border-t border-slate-100 pt-4 text-xs text-slate-500 dark:border-gray-800 dark:text-gray-400">
    <span>SPIN · Security Posture Intelligence Navigator</span>
    <span>Catalog selection does not grant system authorization.</span>
  </footer>;
}
