import { useEffect, useRef, useState } from 'react';
import { PackageUpload } from './PackageUpload';
import { PackageReceiptCard } from './PackageReceipts';
import { preparePackageUpload } from './uploadIdentity';
import { PackageImportError } from './request';
import { Pager, Status, useRemote } from '../workspace-operations/workspaceUi';
import * as setup from '../csp-onboarding/providerSetupApi';
import type { UploadIntent, UploadIntentInput } from '../csp-onboarding/providerSetupApi';
import type { PackageStatus } from './types';
import { uploadPackage } from '../provider-authorizations/api';
import type { PackageReceipt } from '../provider-authorizations/types';
import { useSearchParams } from '../workspaces/workspaceNavigation';

interface Props {
  packageName?: string;
  offeringHintId?: string;
  context?: NonNullable<UploadIntentInput['context']>;
  disabled?: boolean;
  showReceipt?: boolean;
  onPendingChange?: (pending: boolean) => void;
  onReceived: (receipt: PackageStatus, association?: PackageReceipt) => void;
}

export function ProviderSourceUpload({ packageName, offeringHintId, context, disabled, showReceipt = true, onPendingChange, onReceived }: Props) {
  const [search, setSearch] = useSearchParams();
  const retainedId = search.get('uploadIntentId');
  const policy = useRemote(() => setup.getHandlingPolicy(), []);
  const [page, setPage] = useState(1);
  const pending = useRemote(() => setup.listPortalIntents(page, offeringHintId), [page, offeringHintId]);
  const [classification, setClassification] = useState('');
  const [synthetic, setSynthetic] = useState(false);
  const [markings, setMarkings] = useState<string[]>([]);
  const [intent, setIntent] = useState<UploadIntent | null>(null);
  const preparation = useRef<UploadIntentInput | null>(null);
  const [checking, setChecking] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [receipt, setReceipt] = useState<PackageStatus | null>(null);
  const [definiteRejection, setDefiniteRejection] = useState(false);
  const remember = (id: string | null) => {
    const next = new URLSearchParams(search);
    if (id) next.set('uploadIntentId', id); else next.delete('uploadIntentId');
    setSearch(next, { replace: true });
  };
  useEffect(() => {
    if (!retainedId) { setChecking(false); return; }
    let cancelled = false;
    setChecking(true);
    setup.getUploadIntent(retainedId).then(value => { if (!cancelled) setIntent(value); })
      .catch(reason => { if (!cancelled) setError(reason instanceof Error ? reason.message : 'Retained source request is unavailable.'); })
      .finally(() => { if (!cancelled) setChecking(false); });
    return () => { cancelled = true; };
  }, [retainedId]);
  const check = async (selected: UploadIntent) => {
    setChecking(true); setError(null);
    try {
      const current = await setup.getUploadIntent(selected.intentId);
      setIntent(current);
      if (current.receipt) {
        setReceipt(current.receipt); setIntent(null); preparation.current = null;
        onPendingChange?.(false); remember(null); onReceived(current.receipt); pending.retry();
      } else {
        onPendingChange?.(true);
        setError('No receipt is observed yet. Reselect the same files and retry this exact request; no new key will be created.');
      }
    } catch (reason) { setError(reason instanceof Error ? reason.message : 'Receipt status is unavailable.'); }
    finally { setChecking(false); }
  };
  return <section aria-label="Policy-enforced provider source intake" className="space-y-4">
    <Status loading={policy.loading || pending.loading} error={policy.error || pending.error}
      retry={() => { policy.retry(); pending.retry(); }} />
    {policy.data && !policy.data.uploadsPermitted && <p role="alert" className="rounded border border-amber-300 bg-amber-50 p-3 text-amber-950">
      Approved deployment handling permission is unavailable. No source bytes can be uploaded.</p>}
    {pending.data && pending.data.items.length > 0 && <div className="space-y-2 rounded border p-4">
      <h3 className="font-semibold">Unfinished source requests</h3>
      {pending.data.items.map(item => <div key={item.intentId} className="flex flex-wrap justify-between gap-2">
        <span>{item.input.packageName} · Receipt not confirmed</span>
        <button type="button" disabled={checking} className="text-indigo-700 underline" onClick={() => void check(item)}>Recover {item.input.packageName}</button>
      </div>)}
      {pending.data.total > pending.data.pageSize && <Pager {...pending.data} onPage={setPage} />}
    </div>}
    {intent && <div role="status" className="rounded border border-amber-300 p-3">
      <p>Retained request: {intent.input.packageName}. Reselect the exact original files after a browser restart.</p>
      <ul>{intent.input.files.map(file => <li key={file.ordinal}>{file.fileName} · {file.byteLength} bytes</li>)}</ul>
      <button type="button" className="mt-2 text-indigo-700 underline" disabled={checking} onClick={() => void check(intent)}>Check retained receipt</button>
    </div>}
    {error && <p role="alert">{error}</p>}
    {definiteRejection && <div className="space-y-2 rounded border p-3">
      <p>The server rejected this submission before confirming receipt. Its request remains retained.</p>
      <button type="button" className="text-indigo-700 underline" onClick={() => {
        setIntent(null); preparation.current = null; setDefiniteRejection(false); onPendingChange?.(false); remember(null);
      }}>Prepare a corrected request</button>
    </div>}
    {policy.data && <fieldset disabled={!!intent || !!preparation.current} className="space-y-2">
      <label className="block text-sm">Declared source classification<select className="ml-3 rounded border p-2"
        value={classification} onChange={event => setClassification(event.target.value)}>
        <option value="">Choose permitted content</option>{policy.data.allowedClassifications.map(value => <option key={value}>{value}</option>)}
      </select></label>
      {policy.data.syntheticOnly && <label className="flex gap-2 text-sm"><input type="checkbox" checked={synthetic} onChange={event => setSynthetic(event.target.checked)} />
        These files contain only synthetic data.</label>}
      {policy.data.allowedMarkings.map(marking => <label key={marking} className="flex gap-2 text-sm">
        <input type="checkbox" checked={markings.includes(marking)} onChange={event => setMarkings(previous => event.target.checked
          ? [...previous, marking] : previous.filter(value => value !== marking))} />{marking}</label>)}
    </fieldset>}
    <PackageUpload disabled={disabled || checking || policy.loading || pending.loading || !!policy.error || !!pending.error
      || !policy.data?.uploadsPermitted || !intent && !preparation.current && (!classification || policy.data.syntheticOnly && !synthetic)}
      onPendingChange={value => onPendingChange?.(value || !!intent || !!preparation.current)}
      upload={async files => {
        if (!policy.data?.uploadsPermitted) throw new Error('Approved handling permission is unavailable.');
        const prepared = await preparePackageUpload(files);
        let selected = intent;
        if (!selected) {
          preparation.current ??= { intentId: crypto.randomUUID(), schemaVersion: 1, packageName: packageName?.trim()
            || (files.length === 1 ? files[0]!.name : `Source package (${files.length} files)`),
            entryPoint: 'ActivePortal', associationMode: context ? 'ExactBoundary' : 'Unassociated',
            offeringHintId: offeringHintId ?? null, context: context ?? null, files: prepared.manifest,
            handlingPolicyVersion: policy.data.version!,
            declaredContent: { classification, markings, containsOnlySyntheticData: synthetic } };
          try { selected = await setup.prepareUpload(0, preparation.current); }
          catch (reason) {
            if (reason instanceof PackageImportError && [400, 401, 403, 409, 422].includes(reason.status ?? 0)) preparation.current = null;
            pending.retry();
            throw reason;
          }
          setIntent(selected);
          remember(selected.intentId);
        }
        if (JSON.stringify(prepared.manifest) !== JSON.stringify(selected.input.files))
          throw new Error('The selected bytes do not match the retained request manifest. Reselect the original files.');
        const recovered = await setup.getUploadIntent(selected.intentId);
        if (recovered.receipt) {
          setReceipt(recovered.receipt); setIntent(null); preparation.current = null;
          remember(null); onReceived(recovered.receipt); pending.retry(); return;
        }
        let received: PackageStatus;
        let associated: PackageReceipt | undefined;
        try {
          const exact = selected.input.context;
          if (exact) {
            associated = await uploadPackage(exact.offeringId, {
              name: selected.input.packageName, boundaryRevisionId: exact.boundaryRevisionId,
              expectedOfferingRevision: exact.expectedOfferingRevision,
              ...(exact.seriesId ? { seriesId: exact.seriesId } : {}),
              ...(exact.previousVersionId ? { previousVersionId: exact.previousVersionId } : {}),
            }, prepared.files, selected.intentId, selected.intentId);
            received = associated.package;
          } else received = await setup.uploadSource(selected, prepared.files, true);
        } catch (reason) {
          if (reason instanceof PackageImportError && [400, 413, 422].includes(reason.status ?? 0)) {
            const checked = await setup.getUploadIntent(selected.intentId);
            if (!checked.receipt) setDefiniteRejection(true);
          }
          throw reason;
        }
        const confirmed = await setup.getUploadIntent(selected.intentId);
        if (!confirmed.receipt || confirmed.receipt.packageId !== received.packageId)
          throw new Error('Receipt remains uncertain. Check the exact retained request before retrying.');
        setReceipt(confirmed.receipt); setIntent(null); preparation.current = null;
        remember(null); onReceived(confirmed.receipt, associated); pending.retry();
      }} />
    {receipt && showReceipt && <PackageReceiptCard item={receipt} showLink />}
    <p className="text-xs text-slate-500">Declaration is not classification detection. Receipt, source review and publication remain separate.</p>
  </section>;
}
