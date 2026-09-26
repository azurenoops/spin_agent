import { useEffect, useState, type ReactNode } from 'react';
import { Link } from '../workspaces/workspaceNavigation';
import { Pager, Status, surfaceClass, useRemote, warningClass } from '../workspace-operations/workspaceUi';
import { listPackages, packageImportHref } from './api';
import type { PackageStatus } from './types';

export const packageIsProcessing = (item: PackageStatus) =>
  item.processingState === 'Received' || item.processingState === 'Processing' || item.publicationState === 'Publishing';
export const stateLabel = (value: string) => value.replace(/([a-z])([A-Z])/g, '$1 $2');

export function PackageReceiptCard({ item, showLink = false }: { item: PackageStatus; showLink?: boolean }) {
  const coverage = item.coverage;
  const exceptions = coverage.unsupported + coverage.unreadable + coverage.failed;
  return <article className={`${surfaceClass} space-y-2 p-4`} aria-label={item.name}>
    <div className="flex flex-wrap items-start justify-between gap-2">
      <div className="min-w-0">
        <h3 className="break-words font-semibold text-gray-900">{item.name}</h3>
        <p className="text-xs text-gray-500">Receipt confirmed · Revision {item.revision}</p>
      </div>
      <span className="rounded-full bg-indigo-50 px-3 py-1 text-xs font-medium text-indigo-800">{stateLabel(item.processingState)}</span>
    </div>
    <p className="text-sm text-gray-700">{coverage.processed} of {coverage.total} entries processed; {coverage.pending} pending; {exceptions} exceptions; {coverage.excluded} explicitly excluded.</p>
    {item.analysisProgress && <div role="status" aria-label="Analysis progress" className="space-y-1 text-sm text-gray-700">
      <p>{item.analysisProgress.completedSegments} of {item.analysisProgress.totalSegments} source segments analyzed; {item.analysisProgress.modelCalls} of {item.analysisProgress.modelCallLimit} model calls used.</p>
      {item.analysisProgress.continuingAutomatically && <p>Continuing automatically in bounded passes. Saved progress is retained; you can leave this page.</p>}
    </div>}
    {coverage.excluded > 0 && <p className="text-xs text-amber-800">Excluded content has not been analyzed. This is not a full-coverage claim.</p>}
    {item.lastError && <p role="alert" className={warningClass}>{item.lastError}</p>}
    <p className="text-xs text-gray-500">Publication: {stateLabel(item.publicationState)} · Updated {new Date(item.updatedAt).toLocaleString()}</p>
    {showLink && <Link className="inline-block text-sm font-semibold text-indigo-700 underline" to={packageImportHref(item.packageId)}>Review sources and import</Link>}
  </article>;
}

export function PackageReceipts({ receipt, showLinks = false, refreshKey = 0, renderDetails }: {
  receipt?: PackageStatus | null; showLinks?: boolean; refreshKey?: number;
  renderDetails?: (item: PackageStatus) => ReactNode;
}) {
  const [page, setPage] = useState(1);
  const remote = useRemote(signal => listPackages(page, signal), [page, refreshKey]);
  const unlistedReceipt = receipt && !remote.data?.items.some(item => item.packageId === receipt.packageId) ? receipt : null;
  const pending = remote.data?.items.some(packageIsProcessing) || (unlistedReceipt && packageIsProcessing(unlistedReceipt));
  useEffect(() => {
    if (!pending || remote.error || remote.loading) return;
    const timer = window.setTimeout(remote.retry, 5000);
    return () => window.clearTimeout(timer);
  }, [pending, remote.error, remote.loading, remote.retry]);

  return <section aria-label="Saved source packages" className="space-y-3">
    <h3 className="text-sm font-semibold text-gray-900">Saved source packages</h3>
    <Status loading={remote.loading} error={remote.error} retry={remote.retry} />
    {unlistedReceipt && <PackageReceiptCard item={unlistedReceipt} showLink={showLinks} />}
    {remote.data && <>
      {!remote.data.items.length && !unlistedReceipt && <p className={`${surfaceClass} p-4 text-sm text-gray-600`}>No source packages have been received. Upload is optional.</p>}
      {remote.data.items.map(item => <div key={item.packageId} className="space-y-2">
        <PackageReceiptCard item={item} showLink={showLinks} />{renderDetails?.(item)}
      </div>)}
      {remote.data.total > 25 && <Pager {...remote.data} onPage={setPage} />}
    </>}
  </section>;
}
