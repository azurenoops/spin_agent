import { useEffect, useRef, useState } from 'react';
import PageLayout from '../../components/layout/PageLayout';
import PageHero from '../../components/layout/PageHero';
import { Link } from '../workspaces/workspaceNavigation';
import { buttonClass, errorClass, message, secondaryButtonClass, Status, surfaceClass, useRemote, warningClass } from '../workspace-operations/workspaceUi';
import * as api from './api';
import { CandidateReview } from './CandidateReview';
import { ClaimReview } from './ClaimReview';
import { isClaimKind } from './claims';
import { ImpactReviewSelection, impactReviewIds, impactSelectionError } from '../provider-authorizations/ImpactReviewSelection';
import { AuthorizationContextSummary } from '../provider-authorizations/AuthorizationContextSummary';
import { PackageSummary } from './PackageSummary';
import { PackageCandidates } from './PackageCandidates';
import { PackageEntries } from './PackageEntries';
import { PackageEnrichment } from './PackageEnrichment';
import { packageIsProcessing, PackageReceiptCard, PackageReceipts, stateLabel } from './PackageReceipts';
import { PackageUpload } from './PackageUpload';
import { PackageImportError } from './request';
import { packagePublicationKey } from './publication';
import { usePublicationIntent } from './usePublicationIntent';
import type { PackageCandidate, PackageDecision, PackagePreview, PackagePublication, PackageSelection, PackageStatus } from './types';

export function PackageImportsPage({ packageId }: { packageId?: string }) {
  return <PageLayout title="Review package imports">
    <PageHero eyebrow="Provider workspace · Source packages" title="Review package imports" showOrgName={false}
      description="Upload once. Review source-supported components and capabilities before explicit approval and publication." />
    <Link className="mb-4 inline-block text-sm text-indigo-700 underline" to="/workspaces/csp/security-capabilities">Back to Security Capabilities</Link>
    <p className={`${warningClass} mb-5`}>Source content is untrusted evidence, not instructions or a verified authorization. Upload, analysis, provider activation and high confidence never approve or publish records.</p>
    {packageId ? <PackageDetail key={packageId} packageId={packageId} /> : <PackageList />}
  </PageLayout>;
}

function PackageList() {
  const [receipt, setReceipt] = useState<PackageStatus | null>(null);
  const [refreshKey, setRefreshKey] = useState(0);
  return <div className="space-y-6">
    <section className={`${surfaceClass} space-y-3 p-5`}><h2 className="text-lg font-semibold">Import source package</h2>
      <PackageUpload upload={async (files, key) => {
        const saved = await api.receivePackage(files, key);
        setReceipt(saved); setRefreshKey(value => value + 1);
      }} />
    </section>
    <PackageReceipts receipt={receipt} refreshKey={refreshKey} showLinks />
  </div>;
}

export function PackageDetail({ packageId, offeringId, packageVersionId, boundaryRevisionId }: {
  packageId: string; offeringId?: string; packageVersionId?: string; boundaryRevisionId?: string;
}) {
  const [refresh, setRefresh] = useState(0);
  const [view, setView] = useState<'records' | 'sources'>('records');
  const remote = useRemote(signal => api.getPackageStatus(packageId, signal), [packageId, refresh]);
  const review = useRemote(signal => api.getPackageReviewState(packageId, signal), [packageId, refresh]);
  const [selected, setSelected] = useState<PackageSelection[]>([]);
  const [impactIds, setImpactIds] = useState('');
  const [preview, setPreview] = useState<PackagePreview | null>(null);
  const [previewIsStale, setPreviewIsStale] = useState(false);
  const [publication, setPublication] = useState<PackagePublication | null>(null);
  const [editing, setEditing] = useState<PackageCandidate | null>(null);
  const [busy, setBusy] = useState(false);
  const [enrichmentPending, setEnrichmentPending] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [conflict, setConflict] = useState(false);
  const publicationIntent = usePublicationIntent(packageId);
  const publicationPending = publicationIntent.pendingPreviewId !== null;
  const operation = useRef(false);
  const retryKey = useRef<string | null>(null);
  const lastRevision = useRef<number | null>(null);
  const processing = remote.data ? packageIsProcessing(remote.data) : false;
  const stalePreview = !!preview && (previewIsStale || preview.revision !== remote.data?.revision);
  const mismatchedReads = !!review.data && !!remote.data && review.data.revision !== remote.data.revision;
  const validPreview = !stalePreview ? preview : null;
  const disabled = busy || processing || !!editing || remote.loading || !!remote.error || review.loading || !!review.error || mismatchedReads || !!publicationIntent.error;
  const mutationDisabled = disabled || publicationPending || enrichmentPending;

  useEffect(() => {
    if (!processing || busy || remote.loading || remote.error) return;
    const timer = window.setTimeout(() => setRefresh(value => value + 1), 5000);
    return () => window.clearTimeout(timer);
  }, [processing, busy, remote.loading, remote.error, remote.retry]);
  useEffect(() => {
    if (!remote.data) return;
    if (lastRevision.current !== null && lastRevision.current !== remote.data.revision) {
      setNotice('Package revision changed. Review current records and generate a fresh preview.');
    }
    lastRevision.current = remote.data.revision;
  }, [remote.data]);
  useEffect(() => {
    if (!review.data) return;
    const saved = review.data;
    setPreview(saved.preview);
    setPreviewIsStale(saved.previewIsStale);
    setPublication(saved.publication);
    setSelected(saved.preview && !saved.previewIsStale && ['Preview', 'Approved'].includes(saved.preview.state)
      ? saved.preview.candidates : []);
    if (saved.preview?.previewId === publicationIntent.pendingPreviewId
      && (saved.previewIsStale || !['Approved', 'Preview'].includes(saved.preview.state))) publicationIntent.clear();
  }, [review.data]);

  const reload = () => { setEditing(null); setError(null); setConflict(false); setRefresh(value => value + 1); };
  const invalidate = () => { setPreview(null); setSelected([]); };
  const run = async (action: () => Promise<void>) => {
    if (operation.current) return;
    operation.current = true; setBusy(true); setError(null);
    try { await action(); }
    catch (reason) {
      setError(message(reason));
      if (reason instanceof PackageImportError && reason.status !== undefined && reason.status >= 400 && reason.status < 500) publicationIntent.clear();
      if (reason instanceof PackageImportError && reason.status === 409) { setConflict(true); invalidate(); }
    } finally { operation.current = false; setBusy(false); }
  };
  const decision = (value: PackagePreview): PackageDecision => ({ previewId: value.previewId, previewHash: value.previewHash, revision: value.revision });
  const changeSelection = (candidate: PackageCandidate, checked: boolean) => {
    if (checked && selected.length >= 100) { setError('Select at most 100 candidate revisions.'); return; }
    setSelected(previous => checked ? [...previous, { candidateId: candidate.candidateId, revision: candidate.revision }]
      : previous.filter(item => item.candidateId !== candidate.candidateId));
    setPreview(null);
  };

  return <div className="space-y-6">
    <div className="flex flex-wrap gap-3">
      <Link className={secondaryButtonClass} to={api.packageImportHref()}>All packages</Link>
      <button type="button" disabled={busy} className={secondaryButtonClass} onClick={reload}>Refresh package</button>
    </div>
    <Status loading={remote.loading} error={remote.error} retry={remote.retry} />
    {notice && <p role="status" className={warningClass}>{notice}</p>}
    {remote.data && <>
      <PackageSummary item={remote.data} onSources={() => setView('sources')} />
      <details className={`${surfaceClass} rounded-xl px-5 py-3`}>
        <summary className="cursor-pointer text-sm font-medium">Processing details and retry</summary>
        <div className="mt-4 space-y-4">
          <PackageReceiptCard item={remote.data} />
          {remote.data.analysisProfileVersion !== undefined && <PackageEnrichment status={remote.data} disabled={disabled || publicationPending}
            onPendingChange={setEnrichmentPending} onChanged={() => { invalidate(); reload(); }} />}
      {['Failed', 'NeedsAttention'].includes(remote.data.processingState) && <button type="button" className={secondaryButtonClass} disabled={mutationDisabled}
        onClick={() => void run(async () => {
          retryKey.current ??= crypto.randomUUID();
          await api.retryPackage(packageId, retryKey.current);
          retryKey.current = null; invalidate(); reload();
        })}>Retry unfinished analysis</button>}
        </div>
      </details>
      <div role="group" aria-label="Package review views" className="flex flex-wrap gap-2 border-b border-slate-200 pb-3 dark:border-slate-700">
        <button type="button" aria-pressed={view === 'records'} className={view === 'records' ? buttonClass : secondaryButtonClass} onClick={() => setView('records')}>Extracted records</button>
        <button type="button" aria-pressed={view === 'sources'} className={view === 'sources' ? buttonClass : secondaryButtonClass} onClick={() => setView('sources')}>Source files</button>
        <span className="ml-auto self-center text-sm text-slate-500">{selected.length} selected for publication</span>
      </div>
    </>}
    <div className="grid min-w-0 items-start gap-6 lg:grid-cols-[minmax(0,1fr)_22rem]">
    <div className="min-w-0 space-y-6">
      {remote.data && <>
      <div hidden={view !== 'records'} className="space-y-5">
      <PackageCandidates packageId={packageId} revision={remote.data.revision + refresh} selected={selected} disabled={mutationDisabled}
        onSelect={changeSelection} onReview={setEditing}
        onRevisionChanged={() => { invalidate(); setNotice('Candidate revision changed. Review current records and generate a fresh preview.'); }} />
      {editing && (isClaimKind(editing.type)
        ? <ClaimReview key={`${editing.candidateId}:${editing.revision}`} candidate={editing} packageId={packageId}
            onCancel={() => setEditing(null)} onReload={() => { invalidate(); reload(); }} onSaved={() => { invalidate(); reload(); }} />
        : <CandidateReview key={`${editing.candidateId}:${editing.revision}`} candidate={editing} packageId={packageId}
            onCancel={() => setEditing(null)} onReload={() => { invalidate(); reload(); }} onSaved={() => { invalidate(); reload(); }} />)}
      </div>
      <div hidden={view !== 'sources'}>
      <PackageEntries packageId={packageId} revision={remote.data.revision + refresh} disabled={mutationDisabled}
        onChanged={() => { invalidate(); reload(); }} />
      </div>
      </>}
    </div>
    <aside aria-label="Approval and publication" className={`${surfaceClass} min-w-0 space-y-4 p-5 lg:sticky lg:top-4`}>
      <h2 className="text-lg font-semibold">Publish reviewed records</h2>
      <p className="text-sm text-slate-500">Review records → select components and capabilities → preview → approve → publish.</p>
      <details open={selected.length > 0 || !!preview || !!publication || !!error || publicationPending || !!review.error || !!publicationIntent.error || mismatchedReads}>
      <summary className="cursor-pointer text-sm font-medium">Publication controls{selected.length ? ` · ${selected.length} selected` : ''}</summary>
      <div className="mt-4 space-y-4">
      <section aria-label="Persisted review state">
        <Status loading={review.loading} error={review.error} retry={review.retry} />
        {publicationIntent.error && <p role="alert" className={errorClass}>{publicationIntent.error}</p>}
        {mismatchedReads && <p role="alert" className={warningClass}>Package changed while loading saved decisions. Refresh the package before making another decision.</p>}
      </section>
      <p className="text-sm text-gray-600">{selected.length} exact candidate revisions selected. The server checks coverage, evidence, review, duplicates and all dependencies.</p>
      {offeringId && <ImpactReviewSelection offeringId={offeringId} packageVersionId={packageVersionId}
        packageId={packageId} boundaryRevisionId={boundaryRevisionId} value={impactIds} disabled={mutationDisabled}
        onChange={value => { setImpactIds(value); setPreview(null); }} />}
      <button type="button" className={secondaryButtonClass} disabled={mutationDisabled || !selected.length || !!impactSelectionError(impactIds)} onClick={() => void run(async () => {
        if (!remote.data) throw new Error('Reload the package before generating a preview.');
        const ids = impactReviewIds(impactIds);
        const result = await api.previewPackage(packageId, { expectedRevision: remote.data.revision, candidates: selected,
          ...(ids.length ? { impactReviewIds: ids } : {}) });
        setPreview(result); setPreviewIsStale(false); setConflict(false);
      })}>Preview selected revisions</button>
      {preview && <div className="space-y-2 rounded border border-indigo-200 bg-indigo-50 p-4">
        <h3 className="font-semibold">Approval preview</h3>
        <p className="text-sm">Revision {preview.revision} · {preview.candidates.length} exact records · {preview.newComponents} new components · {preview.newCapabilities} new capabilities</p>
        <p className="text-sm">Decision state: {stateLabel(preview.state)}</p>
        <ul aria-label="Exact preview selection" className="max-h-48 space-y-1 overflow-y-auto text-xs text-gray-700">{preview.candidates.map(item =>
          <li key={item.candidateId} className="break-all"><code>{item.candidateId}</code> · Revision {item.revision}</li>)}</ul>
        <p className="break-all text-xs text-gray-600">Preview {preview.previewId} · Hash {preview.previewHash}</p>
        <AuthorizationContextSummary contextSnapshotHash={preview.contextSnapshotHash} impactReviewIds={preview.impactReviewIds} />
        {stalePreview && <p role="alert" className={warningClass}>This saved preview is stale. Review current candidates and generate a fresh preview before approval or publication.</p>}
        {preview.blockers.length > 0 ? <ul role="alert" className={errorClass}>{preview.blockers.map(blocker => <li key={blocker}>{blocker}</li>)}</ul>
          : !stalePreview && <p className="text-sm">{preview.state === 'Preview'
            ? 'Server preview returned no blockers. Approval is still required before publication.'
            : preview.state === 'Approved'
              ? 'Server approval is recorded for this exact preview. Publication is a separate action.'
              : preview.state === 'Published'
                ? 'The server recorded publication for this exact preview.'
                : 'This preview is not eligible for approval or publication. Refresh the package to review its current state.'}</p>}
      </div>}
      <div className="flex flex-wrap gap-3">
        <button type="button" className={buttonClass} disabled={mutationDisabled || !validPreview || validPreview.state !== 'Preview' || validPreview.blockers.length > 0}
          onClick={() => void run(async () => {
            if (!validPreview) throw new Error('Generate a current approval preview first.');
            const saved = await api.approvePackage(packageId, decision(validPreview)); setPreview(saved);
          })}>Approve exact preview</button>
        <button type="button" className={buttonClass} disabled={disabled || !validPreview || validPreview.state !== 'Approved' || validPreview.blockers.length > 0
          || (publicationPending && publicationIntent.pendingPreviewId !== validPreview.previewId)}
          onClick={() => void run(async () => {
            if (!validPreview) throw new Error('Explicit approval of the current preview is required.');
            publicationIntent.begin(validPreview.previewId);
            const result = await api.publishPackage(packageId, decision(validPreview), packagePublicationKey(validPreview));
            publicationIntent.clear(); setPublication(result); setPreview(null); setSelected([]); setRefresh(value => value + 1);
          })}>Publish approved set</button>
      </div>
      {busy && <p role="status">Saving package operation...</p>}
      {publicationPending && !busy && <p role="status" className={warningClass}>Publication outcome is unknown. Retry the same approved set or refresh to recover server status before changing the selection.</p>}
      {error && <p role="alert" className={errorClass}>{error}</p>}
      {conflict && <button type="button" className={secondaryButtonClass} onClick={() => { invalidate(); reload(); }}>Reload current package</button>}
      {publication && <section aria-label="Persisted publication outcome" className="space-y-2 rounded border border-emerald-200 bg-emerald-50 p-4">
        <h3 className="font-semibold">Publication recorded: {stateLabel(publication.publicationState)}</h3>
        <p className="text-sm">{publication.existing ? 'Recovered an existing publication outcome.' : 'The server persisted the publication outcome.'} Only the records below belong to this approved set.</p>
        <ul className="space-y-2 text-sm">{publication.records.map(record => <li key={record.candidateId}>
          {record.type}<code className="block break-all">{record.recordId}</code>{record.releaseId && <span className="block break-all text-xs">Release {record.releaseId}</span>}
        </li>)}</ul>
      </section>}
      </div></details>
    </aside>
    </div>
  </div>;
}
