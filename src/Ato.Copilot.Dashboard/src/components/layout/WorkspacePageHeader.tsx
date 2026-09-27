import type { ReactNode } from 'react';

export default function WorkspacePageHeader({ title, description, eyebrow, actions }: {
  title: string;
  description?: string;
  eyebrow?: string;
  actions?: ReactNode;
}) {
  return <header className="mb-6 flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
    <div className="min-w-0">
      {eyebrow && <p className="mb-2 text-[10px] font-semibold uppercase tracking-[.14em] text-slate-500 dark:text-slate-400">{eyebrow}</p>}
      <h1 className="text-[28px] font-semibold leading-tight tracking-tight text-slate-800 dark:text-slate-100">{title}</h1>
      {description && <p className="mt-2 max-w-[790px] text-[13px] text-slate-500 dark:text-slate-400">{description}</p>}
    </div>
    {actions && <div className="flex shrink-0 flex-wrap items-center gap-2">{actions}</div>}
  </header>;
}
