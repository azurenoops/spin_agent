import { FileArchive, ArrowRight } from 'lucide-react';
import { packageIsProcessing, stateLabel } from './PackageReceipts';
import { secondaryButtonClass, surfaceClass } from '../workspace-operations/workspaceUi';
import type { PackageStatus } from './types';

export function PackageSummary({ item, onSources }: { item: PackageStatus; onSources: () => void }) {
  const issues = item.coverage.failed + item.coverage.unreadable + item.coverage.unsupported;
  const attention = issues > 0 || ['Failed', 'NeedsAttention'].includes(item.processingState);
  const processing = packageIsProcessing(item);
  const published = item.publicationState === 'Published';
  return <section aria-label="Package summary" className={`${surfaceClass} overflow-hidden rounded-xl`}>
    <div className="flex flex-wrap items-start gap-4 p-5">
      <span className="rounded-xl bg-indigo-50 p-3 text-indigo-600 dark:bg-indigo-950 dark:text-indigo-300"><FileArchive size={24} aria-hidden="true" /></span>
      <div className="min-w-0 flex-1"><h2 className="break-words text-lg font-semibold">{item.name}</h2>
        <p className="mt-1 text-sm text-slate-500 dark:text-slate-400">{item.coverage.processed} of {item.coverage.total} source entries processed</p>
        <div className="mt-3 flex flex-wrap gap-x-4 gap-y-1 text-xs">
          {issues > 0 && <span className="font-medium text-amber-800 dark:text-amber-300">{issues} need attention</span>}
          {item.coverage.pending > 0 && <span>{item.coverage.pending} pending</span>}
          {item.coverage.excluded > 0 && <span className="text-slate-600 dark:text-slate-300">{item.coverage.excluded} excluded · not analyzed</span>}
        </div>
      </div>
      <div className="flex flex-wrap gap-2 text-xs"><span className="rounded-full bg-slate-100 px-3 py-1 text-slate-700 dark:bg-slate-800 dark:text-slate-200">{stateLabel(item.processingState)}</span>
        <span className="rounded-full border border-slate-200 px-3 py-1 dark:border-slate-700">{stateLabel(item.publicationState)}</span></div>
    </div>
    <div className="flex flex-wrap items-center justify-between gap-3 border-t border-slate-100 bg-slate-50/70 px-5 py-4 dark:border-slate-800 dark:bg-slate-800/40">
      <div className="min-w-0 flex-1"><h3 className="text-sm font-semibold">{processing ? 'Analysis in progress' : attention ? 'Next: resolve analysis issues' : published ? 'Publication recorded' : 'Next: review extracted records'}</h3>
        <p className="mt-1 text-sm text-slate-600 dark:text-slate-300">{processing ? 'Saved progress is retained. This page updates automatically.' : attention ? 'Open the source files to see what needs attention before publication.' : published ? 'View the published records and their supporting source files below.' : 'Review the extracted records, then select the components and capabilities you want to publish.'}</p></div>
      {attention && !processing && <button type="button" onClick={onSources} className={`${secondaryButtonClass} inline-flex items-center gap-2`}>Review source issues<ArrowRight size={15} aria-hidden="true" /></button>}
    </div>
  </section>;
}
