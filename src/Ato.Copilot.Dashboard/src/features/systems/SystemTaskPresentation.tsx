import type { ReactNode } from 'react';
import WorkspacePageHeader from '../../components/layout/WorkspacePageHeader';

export const systemPanel = 'min-w-0 rounded-[10px] border border-[#dfe4ed] bg-white p-4 min-[651px]:p-[22px] dark:border-slate-700 dark:bg-slate-900';
export const systemPrimaryAction = 'inline-flex items-center justify-center rounded-[7px] bg-[#5143d7] px-4 py-2.5 text-sm font-semibold text-white hover:bg-indigo-700 disabled:opacity-50';
export const systemSecondaryAction = 'inline-flex items-center justify-center rounded-[7px] border border-slate-300 bg-white px-4 py-2.5 text-sm font-medium text-slate-700 hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-50 dark:border-slate-600 dark:bg-slate-900 dark:text-slate-200';

export function SystemTaskHeading({ title, description, status, action, eyebrow }: {
  title: string; description: string; status?: ReactNode; action?: ReactNode; eyebrow?: string;
}) {
  return <>
    <WorkspacePageHeader title={title} description={description} actions={action} eyebrow={eyebrow} />
    {status && <div className="-mt-3 mb-6 text-xs">{status}</div>}
  </>;
}

export function SystemTaskColumns({ children, support, stretch = false }: { children: ReactNode; support?: ReactNode; stretch?: boolean }) {
  if (!support) return <div className={`min-w-0 space-y-5 ${stretch ? 'flex flex-col' : ''}`}>{children}</div>;
  return <div className={`grid min-w-0 ${stretch ? 'items-stretch' : 'items-start'} gap-[25px] min-[1051px]:grid-cols-[minmax(0,2fr)_minmax(260px,1fr)]`}>
    <div className={`min-w-0 space-y-5 ${stretch ? 'flex flex-col' : ''}`}>{children}</div>
    <aside aria-label="Document contribution and next tasks" className="min-w-0 space-y-5">{support}</aside>
  </div>;
}

export function SystemTaskSupport({ title, children }: { title: string; children: ReactNode }) {
  return <section className={systemPanel}><h2 className="text-sm font-semibold text-slate-800 dark:text-slate-100">{title}</h2>
    <div className="mt-3 space-y-3 text-sm leading-relaxed text-slate-500 dark:text-slate-300">{children}</div></section>;
}
