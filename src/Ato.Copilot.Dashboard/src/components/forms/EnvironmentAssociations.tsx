import { useEffect, useRef, useState } from 'react';
import { Database, Info } from 'lucide-react';
import { Link } from '../../features/workspaces/workspaceNavigation';
import { useWorkspaceSession } from '../../features/workspaces/WorkspaceBoundary';
import { associateProviderRelationship, listAllProviderRelationships } from '../../features/provider-relationships/api';
import type { ProviderRelationship } from '../../features/provider-relationships/types';
import { ScopeDetails } from '../../features/provider-relationships/MissionTaskPresentation';
import { listSystemCapabilities } from '../../features/workspace-operations/system-capabilities/systemCapabilityApi';
import { buttonClass, secondaryButtonClass, Status, useRemote } from '../../features/workspace-operations/workspaceUi';
import SetupDialog from '../../features/workspace-operations/SetupDialog';
import ProviderScopeReview from '../../features/provider-relationships/ProviderScopeReview';

interface Props {
  systemId: string;
  hostingModel: string;
  description: string;
  readOnly: boolean;
  busy?: boolean;
  onHostingStatusChange?: (status: string) => void;
  onPrefill: (values: Record<string, string>) => void;
}

export default function EnvironmentAssociations(props: Props) {
  const session = useWorkspaceSession();
  const permitted = session?.workspace.kind === 'organization' && !!session.workspace.tenantId && session.workspace.mode === 'ordinary'
    && session.systemAccess?.systemId.toLowerCase() === props.systemId.toLowerCase()
    && session.systemAccess.permissions.canRead === true;
  useEffect(() => {
    if (!permitted) props.onHostingStatusChange?.('Hosting association · Workspace access required');
  }, [permitted, props.onHostingStatusChange]);
  if (!permitted || session?.workspace.kind !== 'organization' || !session.workspace.tenantId) {
    return <section aria-label="Provider hosting">
      <h2 className="font-semibold">Provider hosting</h2>
      <p className="mt-2 text-sm">Open this system in its authorized organization workspace to view hosting and security capabilities.</p>
    </section>;
  }
  return <Associations key={`${session.workspace.tenantId}:${props.systemId}`} {...props} tenantId={session.workspace.tenantId} />;
}

function Associations({ systemId, tenantId, hostingModel, description, readOnly, busy = false, onPrefill, onHostingStatusChange }: Props & { tenantId: string }) {
  const hosting = useRemote(signal => listAllProviderRelationships(systemId, signal), [systemId]);
  const capabilities = useRemote(signal => listSystemCapabilities(tenantId, systemId,
    { scope: 'applied', grouping: 'capability', page: 1, pageSize: 10 }, signal), [tenantId, systemId]);
  const [selected, setSelected] = useState<ProviderRelationship | null>(null);
  const [drawerOpen, setDrawerOpen] = useState(false);
  const drawerContent = useRef<HTMLDivElement>(null);
  const [prefill, setPrefill] = useState<ProviderRelationship | null>(null);
  const [confirmed, setConfirmed] = useState(false);
  const [intent, setIntent] = useState<{ item: ProviderRelationship; key: string } | null>(null);
  const [associationConfirmed, setAssociationConfirmed] = useState(false);
  const [associating, setAssociating] = useState(false);
  const [associationError, setAssociationError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [reviewing, setReviewing] = useState<ProviderRelationship | null>(null);
  const [reviewBusy, setReviewBusy] = useState(false);
  const writing = useRef(false);
  const mounted = useRef(true);
  useEffect(() => {
    mounted.current = true;
    return () => { mounted.current = false; };
  }, []);
  useEffect(() => {
    if (drawerOpen) drawerContent.current?.focus();
  }, [drawerOpen, selected, intent, prefill, reviewing]);
  const closeDrawer = () => {
    if (writing.current || busy || reviewBusy) return;
    setDrawerOpen(false); setSelected(null); setIntent(null); setPrefill(null); setReviewing(null);
  };
  const base = `/systems/${encodeURIComponent(systemId)}`;
  const associated = hosting.data?.filter(item => !!item.relationshipId);
  const available = hosting.data?.filter(item => !item.relationshipId);
  useEffect(() => {
    const records = hosting.data?.filter(item => !!item.relationshipId) ?? [];
    const pending = records.filter(item => item.reviewRequired || item.state === 'Undetermined').length;
    onHostingStatusChange?.(hosting.loading ? 'Hosting association · Loading scopes'
      : hosting.error ? 'Hosting association · Unavailable'
        : !records.length ? 'Hosting association · Not associated'
          : pending ? `Hosting association · ${pending} pending review`
            : `Hosting association · ${records.length} recorded`);
  }, [hosting.data, hosting.loading, hosting.error, onHostingStatusChange]);
  const proposedDescription = prefill
    ? `${prefill.offeringName} - ${prefill.hostingScopeName ?? 'Allocated hosting scope'}${prefill.providerName ? `, provided by ${prefill.providerName}` : ''}.`
    : '';
  const openAssociation = (item: ProviderRelationship) => {
    setIntent({ item, key: crypto.randomUUID() }); setAssociationConfirmed(false);
    setAssociationError(null); setNotice(null);
  };
  const associate = async () => {
    if (!intent || !associationConfirmed || writing.current || busy) return;
    writing.current = true; setAssociating(true); setAssociationError(null);
    try {
      const current = (await listAllProviderRelationships(systemId)).find(item => item.assignmentId === intent.item.assignmentId);
      if (!current || current.systemId !== systemId || current.assignmentRevision !== intent.item.assignmentRevision) {
        throw new Error('This allocation changed. Cancel, refresh scopes, and review the current allocation before associating.');
      }
      if (!current.canAssociate && !current.relationshipId) {
        throw new Error('Association is no longer permitted for this allocation and your current system assignment.');
      }
      if (!mounted.current) return;
      await associateProviderRelationship(systemId, {
        assignmentId: intent.item.assignmentId, expectedAssignmentRevision: intent.item.assignmentRevision,
      }, intent.key);
      if (!mounted.current) return;
      setIntent(null);
      setDrawerOpen(false);
      setNotice('Provider scope associated with this system. Control inheritance is reviewed separately.');
      hosting.retry();
    } catch (error) {
      if (mounted.current) setAssociationError(error instanceof Error ? error.message : 'The scope association could not be confirmed. Retry the same operation.');
    } finally {
      writing.current = false;
      if (mounted.current) setAssociating(false);
    }
  };
  const showEmpty = !hosting.loading && !hosting.error && associated?.length === 0;
  return <section aria-labelledby="environment-associations"
    className="min-w-0 space-y-4 rounded-[10px] border border-slate-200 bg-white p-[22px] dark:border-gray-700 dark:bg-gray-900">
    <header className="flex items-start gap-3">
      <Database aria-hidden="true" size={20} className="mt-0.5 shrink-0 text-violet-600 dark:text-violet-400" />
      <div><h2 id="environment-associations" className="text-lg font-semibold">Provider hosting</h2>
        <p className="mt-1 text-sm text-slate-500 dark:text-gray-400">Associate a provider scope to record where this system runs.</p></div>
    </header>
    <Status loading={hosting.loading} error={hosting.error} retry={hosting.retry} />
    {notice && <p role="status" className="rounded border border-emerald-200 bg-emerald-50 p-3 text-sm text-emerald-900">{notice}</p>}
    {!!associated?.length && <div className="relative overflow-x-auto">
      <table aria-label="Associated provider scope" className="w-full text-left text-xs">
        <thead className="border-b border-slate-200 text-[10px] uppercase tracking-wide text-slate-500 dark:text-gray-300">
          <tr>{['Provider / offering', 'Scopes', 'Relationship state', 'Action'].map(label =>
            <th key={label} scope="col" className="px-2.5 py-3 font-semibold">{label}</th>)}</tr>
        </thead>
        <tbody className="divide-y divide-slate-200 dark:divide-gray-700">{associated.map(item =>
          <tr key={item.assignmentId}>
            <td className="break-words px-2.5 py-4 font-semibold">{providerLabel(item)}</td>
            <td className="px-2.5 py-4">{scopeCount(item)}</td>
            <td className="px-2.5 py-4"><span className="block">{relationshipState(item)}</span>
              {item.reviewRequired || item.state === 'Undetermined'
                ? <span className="text-xs font-medium text-amber-800 dark:text-amber-300">Review required</span>
                : <span className="text-xs font-medium text-emerald-700 dark:text-emerald-300">Reviewed</span>}</td>
            <td className="px-2.5 py-4"><button type="button" disabled={busy || associating}
              className="rounded border border-slate-200 px-2 py-1 text-[11px] text-indigo-700 disabled:opacity-50 dark:text-indigo-300"
              onClick={() => { setSelected(item); setDrawerOpen(true); }}>Open</button></td>
          </tr>)}</tbody>
      </table>
    </div>}
    <div role={showEmpty ? 'group' : undefined} aria-label={showEmpty ? 'Available provider hosting' : undefined}
      className={showEmpty ? 'flex flex-wrap items-center justify-between gap-4 rounded-lg border border-slate-200 bg-[#f4f3fa] px-4 py-4 dark:border-gray-700 dark:bg-gray-800' : undefined}>
      {showEmpty && <div className="flex min-w-0 flex-1 basis-52 items-start gap-3">
        <Info aria-hidden="true" size={18} className="mt-0.5 shrink-0 text-violet-600 dark:text-violet-400" />
        <div><p className="text-sm font-medium">No provider scope associated</p>
          <p className="mt-1 text-xs text-slate-500 dark:text-gray-400">
            {available?.length ?? 0} {available?.length === 1 ? 'scope is' : 'scopes are'} available for this system.
          </p></div>
      </div>}
      <button type="button" disabled={busy || associating} className={`${secondaryButtonClass} shrink-0`}
        onClick={() => setDrawerOpen(true)}>Choose provider hosting</button>
    </div>
    <p className="text-xs text-slate-500 dark:text-gray-400">Hosting records where this system runs. Control inheritance is reviewed separately.</p>
    {drawerOpen && <SetupDialog placement="right"
      title={reviewing ? 'Review provider relationship' : intent ? 'Associate CSP scope' : prefill ? 'Copy hosting description to draft' : selected ? 'Provider scope details' : 'Choose provider hosting'}
      description={intent ? 'Confirm this existing provider allocation. No capability is adopted by this action.'
        : prefill ? 'Copy text only. This does not record a provider-relationship review or change the table’s review status.'
          : reviewing ? 'Prepare and explicitly record the selected scope determination using the server review workflow.'
          : 'Inspect provider scopes allocated to this system. Selecting or opening a scope makes no changes.'}
      busy={associating || busy || reviewBusy} onClose={closeDrawer}>
      <div ref={drawerContent} tabIndex={-1} className="space-y-4 text-sm outline-none">
      {!selected && !intent && !prefill && !reviewing && <>
        <button type="button" disabled={hosting.loading || busy || associating} onClick={hosting.retry}
          className="text-xs text-indigo-700 underline disabled:opacity-50 dark:text-indigo-300">Refresh scopes</button>
        <Status loading={hosting.loading} error={hosting.error} retry={hosting.retry} />
        {!hosting.loading && !hosting.error && available && <section aria-label="Available CSP scopes">
          <h3 className="font-semibold">Available CSP scopes ({available.length})</h3>
          <p className="mt-2 text-xs text-slate-500">Allocated by the provider to this system; not yet associated.</p>
          {!available.length && <p className="mt-3">No unassociated provider scopes are available. Ask the provider to allocate an offering scope to this system.</p>}
          <ul className="mt-2 divide-y divide-slate-200 dark:divide-gray-700">{available.map(item =>
            <li key={item.assignmentId} className="space-y-3 py-4">
              <h4 className="break-words font-semibold">{providerLabel(item)}</h4>
              <p className="text-xs">{scopeCount(item)} · Not associated</p>
              <ProviderDetails item={item} />
              <div className="flex flex-wrap gap-2">
                <button type="button" disabled={busy} className={secondaryButtonClass} onClick={() => setSelected(item)}>Open</button>
                {item.canAssociate && <button type="button" disabled={busy} className={buttonClass}
                  onClick={() => openAssociation(item)}>Associate CSP scope</button>}
              </div>
              {!item.canAssociate && <p className="text-xs text-slate-500">Association is not currently permitted for this allocation and your system assignment. Ask the provider to verify scope currency and your ISSM to verify your role.</p>}
            </li>)}</ul>
        </section>}
        <details className="text-xs text-slate-500">
          <summary className="cursor-pointer font-medium">When and who associates a scope?</summary>
          <p className="mt-2">After registration and the CSP allocation, associate hosting during System definition, before using provider coverage in capability adoption and the SSP.
            The assigned Mission Owner normally does this; the assigned System Owner, ISSM or ISSO may also associate when permitted by the server.</p>
          <p className="mt-2">Next, review the authorization relationship, adopt applicable capabilities, and confirm customer responsibilities.
            Association alone grants neither an ATO nor control inheritance. Changing the deployment description does not change associations.</p>
        </details>
        <Link className="inline-block underline" to={`${base}/security-capabilities`}>Review applied capabilities →</Link>
        <Status loading={capabilities.loading} error={capabilities.error} retry={capabilities.retry} />
        {capabilities.data && <details>
          <summary className="cursor-pointer">Applied capabilities ({capabilities.data.total})</summary>
          <div className="space-y-2 pt-2">
            {capabilities.data.total === 0 && <p>No security capabilities are applied to this system yet. Organization capabilities do not require a provider.</p>}
            <ul className="space-y-2">{capabilities.data.items.map(item =>
              <li key={`${item.source}:${item.recordId}`}><Link className="underline" to={`${base}/security-capabilities/${item.source}/${encodeURIComponent(item.recordId)}`}>
                {item.name}</Link><p className="text-xs">{item.sourceName} &middot; {item.reviewRequiredCount} controls need responsibility review</p></li>)}</ul>
            {capabilities.data.total > capabilities.data.items.length && <p>Showing {capabilities.data.items.length} of {capabilities.data.total} applied capabilities.</p>}
            <Link className="inline-block underline" to={`${base}/security-capabilities`}>View all applied capabilities</Link>
            {!readOnly && capabilities.data.permissions.canManage && <Link className="block underline" to={`${base}/security-capabilities/add`}>Add from library</Link>}
          </div>
        </details>}
      </>}
      {selected && <>
        <h3 className="font-semibold">{providerLabel(selected)}</h3>
        <p>{scopeCount(selected)}</p>
        <p>{selected.relationshipId ? relationshipState(selected) : 'Not associated'}{selected.relationshipId && selected.reviewRequired ? ' · Review required' : ''}</p>
        <ProviderDetails item={selected} />
        <p>A provider association does not authorize this mission system or establish live collection.</p>
        {selected.relationshipId && <>
          <p className="rounded bg-indigo-50 p-3 text-indigo-900 dark:bg-indigo-950 dark:text-indigo-200">
            Copying the description below does not change review status. Record a relationship determination separately.
          </p>
          {selected.canReviewRelationship === true && selected.state !== 'ExplicitlyCoveredByRecordedScope'
            ? <button type="button" className={buttonClass} disabled={busy}
              onClick={() => { setReviewing(selected); setSelected(null); }}>Review provider relationship</button>
            : <p className="text-xs text-slate-500">{selected.state === 'ExplicitlyCoveredByRecordedScope'
              ? 'This recorded covered-scope relationship requires an assigned AO and exact authorization evidence to change. It cannot be downgraded through a description edit.'
              : 'Relationship review is not currently permitted for your system assignment or this allocation. Verify your role and the current provider scope.'}</p>}
        </>}
        <p>Select this allocation again in the hosting task; this link does not preselect it. Capability adoption and duty confirmation are separate actions.</p>
        <Link className="inline-block underline" to={`${base}/profile/EnvironmentAndDeployment/hosting`}>Open hosting task</Link>
        {!readOnly && <p>Save your environment draft before leaving this page.</p>}
        {!readOnly && selected.relationshipId && <button type="button" disabled={busy} className={`block ${secondaryButtonClass}`} onClick={() => {
          setPrefill(selected); setSelected(null); setConfirmed(false);
        }}>Copy hosting description to draft</button>}
        {!selected.relationshipId && selected.canAssociate && <button type="button" disabled={busy} className={buttonClass}
          onClick={() => { openAssociation(selected); setSelected(null); }}>Associate CSP scope</button>}
        {!selected.relationshipId && !selected.canAssociate && <p>Association is not currently permitted for this allocation and your system assignment.</p>}
        <button type="button" disabled={busy} className={secondaryButtonClass} onClick={() => setSelected(null)}>Back to scopes</button>
      </>}
      {prefill && <>
        <div><h3 className="font-semibold">Current description</h3><p className="whitespace-pre-wrap">{description || 'Not recorded'}</p></div>
        <div><h3 className="font-semibold">Proposed description</h3><p>{proposedDescription}</p></div>
        <p>Hosting model: {hostingModel === 'Hybrid' ? 'Hybrid (retained)' : 'CSP-hosted'}. Provider: {prefill.providerName ?? 'Not recorded'}.</p>
        <p>Network, recovery, availability and operating details remain unchanged. No cloud access, capability subscription or duty acceptance is created. The table will remain review required until a relationship review is recorded.</p>
        <label className="flex items-start gap-2"><input type="checkbox" disabled={busy || readOnly} checked={confirmed} onChange={event => setConfirmed(event.target.checked)} />
          I want to copy this text into my draft. This does not record a relationship review.</label>
        <button type="button" className={buttonClass} disabled={!confirmed || readOnly || busy} onClick={() => {
          if (!confirmed || readOnly || busy) return;
          onPrefill({ hostingModel: hostingModel === 'Hybrid' ? 'Hybrid' : 'CSP-hosted', additionalDetails: proposedDescription });
          closeDrawer();
        }}>Use in draft</button>
        <p>Use Save Draft on the environment form to persist these changes.</p>
        <button type="button" disabled={busy} className={secondaryButtonClass} onClick={() => {
          setSelected(prefill); setPrefill(null);
        }}>Cancel</button>
      </>}
      {intent && <>
        <h3 className="font-semibold">{providerLabel(intent.item)}</h3>
        <p>System: {intent.item.systemName ?? 'Selected system'}</p>
        <p>{scopeCount(intent.item)}</p>
        <ProviderDetails item={intent.item} />
        <p>This saves the hosting association immediately. It does not save your environment-description draft, review provider coverage, accept duties, provision cloud resources or grant authorization.</p>
        {associationError && <p role="alert" className="rounded border border-amber-300 bg-amber-50 p-3 text-amber-900">{associationError}</p>}
        <label className="flex items-start gap-2"><input type="checkbox" disabled={associating || busy} checked={associationConfirmed}
          onChange={event => setAssociationConfirmed(event.target.checked)} />I reviewed this allocation and want to associate it with this system.</label>
        <div className="flex flex-wrap justify-end gap-3">
          <button type="button" disabled={associating || busy} className={secondaryButtonClass} onClick={() => setIntent(null)}>Cancel</button>
          <button type="button" disabled={associating || busy || !associationConfirmed} className={buttonClass} onClick={() => void associate()}>
            {associating ? 'Associating…' : 'Confirm association'}
          </button>
        </div>
      </>}
      {reviewing && <ProviderScopeReview systemId={systemId} item={reviewing} onBusyChange={setReviewBusy}
        onCancel={() => { setSelected(reviewing); setReviewing(null); hosting.retry(); }}
        onRecorded={result => {
          setReviewBusy(false); setReviewing(null); setSelected(null); setDrawerOpen(false);
          setNotice(result.reviewRequired || result.state === 'Undetermined'
            ? 'Relationship review recorded as unresolved. Review is still required.'
            : 'Provider relationship review recorded. The saved scope is reviewed; this is not a mission authorization.');
          hosting.retry();
        }} />}
      </div>
    </SetupDialog>}
  </section>;
}

function providerLabel(item: ProviderRelationship) {
  return `${item.providerName ?? 'Provider not recorded'} · ${item.offeringName ?? 'Offering name unavailable'}`;
}

function ProviderDetails({ item }: { item: ProviderRelationship }) {
  return <details className="text-xs">
    <summary className="cursor-pointer font-medium">Details</summary>
    <div className="space-y-3 pt-3">
      <dl className="grid gap-x-4 gap-y-2 break-all sm:grid-cols-[auto_1fr]">
        {[
          ['Allocated scope', item.hostingScopeName], ['System ID', item.systemId], ['Offering ID', item.offeringId],
          ['Allocation ID', item.assignmentId], ['Relationship ID', item.relationshipId], ['Recorded state', item.state],
          ['Authorization revision ID', item.authorizationRevisionId], ['Boundary revision ID', item.boundaryRevisionId],
          ['Reviewed by', item.reviewedBy], ['Reviewed at', item.reviewedAt],
        ].map(([label, value]) => <div key={label} className="contents"><dt className="font-medium">{label}</dt><dd>{value ?? 'Not recorded'}</dd></div>)}
      </dl>
      <p>Relationship revision: {item.revision}</p>
      <p>Allocation revision: {item.assignmentRevision}</p>
      <ScopeDetails scopes={item.assignedScopes} />
    </div>
  </details>;
}

function relationshipState(item: ProviderRelationship) {
  return {
    Undetermined: 'Not reviewed',
    SeparateBoundaryConsumer: 'Separate boundary consumer',
    ExplicitlyCoveredByRecordedScope: 'Covered by recorded scope',
  }[item.state] ?? item.state;
}

function scopeCount(item: ProviderRelationship) {
  const services = item.assignedScopes.filter(scope => scope.kind === 'Service').length;
  const resources = item.assignedScopes.length - services;
  return [
    resources ? `${resources} resource scope${resources === 1 ? '' : 's'}` : '',
    services ? `${services} service scope${services === 1 ? '' : 's'}` : '',
  ].filter(Boolean).join(' · ') || '0 recorded scopes';
}
