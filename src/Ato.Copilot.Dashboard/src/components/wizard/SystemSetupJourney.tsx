import { useEffect, useRef, useState } from 'react';
import SetupFrame, { SetupGuidance, SetupPanel } from '../../features/onboarding/shared/SetupFrame';
import { Link, useNavigate, useSearchParams } from '../../features/workspaces/workspaceNavigation';
import { workspaceErrorMessage } from '../../features/workspaces/api';
import { getAssessmentReadiness } from '../../api/assessments';
import SystemSourcePanel from '../../features/onboarding/SystemSourcePanel';
import {
  confirmSystemSetup, getSystemSetup, getSystemSetupContext, saveSystemSetup, systemSetupSteps,
  type SystemSetupContext, type SystemSetupDraft, type SystemSetupScreen, type SystemSetupView,
} from '../../api/systemSetup';

const initial: SystemSetupDraft = {
  name: '', acronym: '', missionPurpose: '', objective: 'initialAto', contact: null,
  sourceChoice: 'blank', hostingChoice: 'deferred', monitoringChoice: 'configureLater', lastScreen: 's-details',
};
const titles: Record<SystemSetupScreen, [string, string]> = {
  's-details': ['Start your system workspace', 'Capture the system identity and choose the documentation work you need to begin.'],
  's-team': ['Identify your system team', 'Review effective roles and identify an accountable preparation contact.'],
  's-sources': ['Bring existing documentation', 'Start with a blank draft or leave a clear source review task.'],
  's-hosting': ['Identify how the system is hosted', 'Use an exact allocated service, document organization-managed hosting, or defer.'],
  's-connect': ['Plan cloud monitoring', 'Configuration, access, scoped collection and rule evaluation are separate facts.'],
  's-review': ['Review system setup', 'Confirm the retained facts and the remaining documentation tasks.'],
  's-ready': ['Your system workspace is ready', 'The draft is ready for preparation, not authorization or eMASS submission.'],
};
const fieldClass = 'mt-1 w-full rounded-lg border border-slate-300 px-3 py-2 text-sm';

export default function SystemSetupJourney({ tenantId, resumeSystemId, onClose, onSystemId }: {
  tenantId: string; resumeSystemId?: string; onClose: () => void; onSystemId?: (id: string) => void;
}) {
  const navigate = useNavigate();
  const [query] = useSearchParams();
  const requestedScreen = query.get('step');
  const [context, setContext] = useState<SystemSetupContext | null>(null);
  const [view, setView] = useState<SystemSetupView>();
  const [draft, setDraft] = useState<SystemSetupDraft>(initial);
  const [screen, setScreen] = useState<SystemSetupScreen>('s-details');
  const [busy, setBusy] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [confirmed, setConfirmed] = useState(false);
  const [accessMessage, setAccessMessage] = useState('');
  const [sourcePending, setSourcePending] = useState(false);
  const alive = useRef(true);
  const pending = useRef<{ draft: SystemSetupDraft; key: string; current?: SystemSetupView; confirm?: boolean } | undefined>(undefined);
  useEffect(() => {
    alive.current = true;
    setLoading(true);
    setError(null);
    const load = resumeSystemId
      ? getSystemSetup(tenantId, resumeSystemId).then(saved => {
        if (!alive.current) return;
        setView(saved); setDraft(saved.draft);
        setScreen(requestedScreen && systemSetupSteps.some(step => step.id === requestedScreen)
          ? requestedScreen as SystemSetupScreen
          : saved.setupState === 'confirmed' ? 's-ready' : saved.draft.lastScreen);
        setContext({ organizationName: saved.organizationName, canCreate: false, contacts: saved.contacts });
      })
      : getSystemSetupContext(tenantId).then(next => { if (alive.current) setContext(next); });
    void load.catch(reason => { if (alive.current) setError(workspaceErrorMessage(reason)); })
      .finally(() => { if (alive.current) setLoading(false); });
    return () => { alive.current = false; };
  }, [tenantId, resumeSystemId, requestedScreen]);

  const canManage = view ? view.canManage : context?.canCreate === true;
  const update = <K extends keyof SystemSetupDraft>(field: K, value: SystemSetupDraft[K]) => {
    setDraft(previous => ({ ...previous, [field]: value }));
    setConfirmed(false);
  };
  async function save(next: SystemSetupScreen, exit = false) {
    if (busy || !canManage) return;
    if (sourcePending) { setError('Resolve the existing source request before leaving or saving another setup choice.'); return; }
    if (!draft.name.trim()) { setError('Enter a system name before saving this draft.'); return; }
    setError(null); setBusy(true);
    const intent = pending.current ?? { draft: { ...draft, lastScreen: next }, key: crypto.randomUUID(), current: view };
    pending.current = intent;
    try {
      const saved = intent.confirm && intent.current
        ? await confirmSystemSetup(tenantId, intent.current, intent.key)
        : await saveSystemSetup(tenantId, intent.draft, intent.key, intent.current);
      if (!alive.current) return;
      pending.current = undefined;
      setView(saved); setDraft(saved.draft); onSystemId?.(saved.systemId);
      setScreen(intent.confirm ? 's-ready' : intent.draft.lastScreen);
      if (exit) onClose();
    } catch (reason) {
      if (!alive.current) return;
      setError(workspaceErrorMessage(reason));
      const status = reason && typeof reason === 'object' && 'status' in reason ? reason.status : undefined;
      if (status === 400 || status === 409 || status === 428) pending.current = undefined;
    } finally { if (alive.current) setBusy(false); }
  }
  async function finish() {
    if (!view || !confirmed || busy || !canManage) return;
    pending.current ??= { draft, key: crypto.randomUUID(), current: view, confirm: true };
    await save('s-ready');
  }
  async function checkAccess() {
    if (!view || busy) return;
    setBusy(true); setError(null);
    try {
      const result = await getAssessmentReadiness(view.systemId);
      if (alive.current) setAccessMessage(result.message);
    } catch (reason) { if (alive.current) setError(workspaceErrorMessage(reason)); }
    finally { if (alive.current) setBusy(false); }
  }
  const index = systemSetupSteps.findIndex(step => step.id === screen);
  const systemPath = view ? `/systems/${encodeURIComponent(view.systemId)}` : '';
  const locked = busy || Boolean(pending.current) || sourcePending || !canManage;
  const primary = screen === 's-ready'
    ? { label: 'Open system work queue', onClick: () => { onClose(); navigate(systemPath); } }
    : screen === 's-review'
      ? { label: 'Confirm system setup', onClick: () => void finish(), disabled: !confirmed || !canManage }
      : { label: screen === 's-details' || screen === 's-team' ? 'Save & continue' : 'Save choice & continue',
        onClick: () => void save(systemSetupSteps[index + 1]?.id ?? screen), disabled: !canManage || loading };
  return <SetupFrame journey="System" title={titles[screen][0]} description={titles[screen][1]}
    currentStep={screen} steps={systemSetupSteps.map((step, i) => ({ ...step, disabled: i > index || Boolean(pending.current) }))}
    onStepChange={id => { if (!pending.current && !sourcePending) setScreen(id as SystemSetupScreen); }}
    onBack={index > 0 && screen !== 's-ready' ? () => { if (!pending.current && !sourcePending) setScreen(systemSetupSteps[index - 1]?.id ?? 's-details'); } : undefined}
    onSaveLater={screen !== 's-ready' ? () => void save(screen, true) : undefined}
    primaryAction={primary} busy={busy || loading || sourcePending} error={error}
    saveStatus={view ? `Saved draft · revision ${view.revision}` : 'Not saved yet'}
    footerHelp="Saving setup never grants roles, approves documents, advances RMF, or starts collection."
    guidance={<>
      <SetupGuidance title="Minimum setup">Keep a named system and an accountable contact. Detailed definition, categorization, boundaries and review remain Systems tasks.</SetupGuidance>
      <SetupGuidance title="No fabricated status">A source receipt is not an applied import. A hosting relationship is not inherited control acceptance. Monitoring is not an authorization decision.</SetupGuidance>
    </>}>
    {loading && <p role="status">Loading authorized setup context…</p>}
    {pending.current && !busy && <p className="rounded-lg bg-amber-50 p-3 text-sm">The last write has not been reconciled. Retry the same action; its saved request and key are retained.</p>}
    {!loading && screen === 's-details' && <>
      <SetupPanel title="System identity"><fieldset disabled={locked} className="space-y-4">
        <label className="block">System name<input className={fieldClass} value={draft.name} maxLength={200} onChange={e => update('name', e.target.value)} /></label>
        <div><span className="text-sm font-medium">Owning organization</span><p>{context?.organizationName}</p></div>
        <label className="block">Mission purpose<textarea className={fieldClass} value={draft.missionPurpose} maxLength={2000} onChange={e => update('missionPurpose', e.target.value)} /></label>
      </fieldset></SetupPanel>
      <SetupPanel title="What are you preparing?"><fieldset disabled={locked} className="space-y-3">
        {([['initialAto', 'Prepare an initial ATO package'], ['continuePackage', 'Continue an existing system package'], ['maintainSystem', 'Maintain an authorized system']] as const).map(([value, label]) =>
          <label key={value} className="flex gap-3 rounded-lg border border-purple-200 p-4"><input type="radio" name="objective" checked={draft.objective === value} onChange={() => update('objective', value)} />{label}</label>)}
      </fieldset></SetupPanel>
    </>}
    {screen === 's-team' && <>
      <SetupPanel title="Accountable system contact"><label className="block">System contact<select className={fieldClass} disabled={locked}
        value={draft.contact?.personId ?? ''} onChange={e => update('contact', e.target.value ? { personId: e.target.value, responsibility: 'preparationContact' } : null)}>
        <option value="">Assign a preparation contact</option>
        {(view?.contacts ?? context?.contacts ?? []).map(person => <option key={person.personId} value={person.personId}>{person.displayName}</option>)}
      </select></label><p className="mt-3 text-sm text-slate-600">Preparation responsibility only. Selecting a name does not grant a system role.</p></SetupPanel>
      <SetupPanel title="Effective system roles">{view?.effectiveTeam.length ? view.effectiveTeam.map(role =>
        <p key={`${role.role}:${role.displayName}`}>{role.role}: {role.displayName ?? 'Not assigned'} · {role.source}</p>)
        : <p>Review role assignments in Team. Missing roles remain preparation tasks.</p>}</SetupPanel>
    </>}
    {screen === 's-sources' && <SetupPanel title="Choose a starting point"><fieldset disabled={locked} className="space-y-3">
      {([['blank', 'Start with a blank system'], ['sspPdf', 'Review an existing SSP'], ['emass', 'Review a supported eMASS export'], ['deferred', 'Complete source review later']] as const).map(([value, label]) =>
        <label key={value} className="flex gap-3 rounded-lg border p-4"><input name="sources" type="radio" checked={draft.sourceChoice === value} onChange={() => update('sourceChoice', value)} />{label}</label>)}
    </fieldset>
      {view && (draft.sourceChoice === 'sspPdf' || draft.sourceChoice === 'emass') && <SystemSourcePanel
        key={`${view.systemId}:${draft.sourceChoice}`} tenantId={tenantId} systemId={view.systemId}
        kind={draft.sourceChoice === 'sspPdf' ? 'ssp-pdf' : 'emass'} sources={view.sources}
        disabled={!canManage}
        onPendingChange={setSourcePending} onChanged={async () => {
          const saved = await getSystemSetup(tenantId, view.systemId);
          if (alive.current) setView(saved);
        }} />}
      {draft.sourceChoice === 'deferred' && <p className="mt-4 text-sm">Source review remains an explicit task. No source was received or applied by this choice.</p>}
    </SetupPanel>}
    {screen === 's-hosting' && <SetupPanel title="Hosting relationship"><fieldset disabled={locked} className="space-y-3">
      {([['allocatedService', 'Use an allocated provider service'], ['organizationManaged', 'Use organization-managed hosting'], ['deferred', 'Complete hosting details later']] as const).map(([value, label]) =>
        <label key={value} className="flex gap-3 rounded-lg border p-4"><input name="hosting" type="radio" checked={draft.hostingChoice === value} onChange={() => update('hostingChoice', value)} />{label}</label>)}
    </fieldset><p className="mt-4 text-sm">This saves your choice, not an association. Confirm the exact available allocation in Environment & hosting after setup; capabilities and customer duties remain separate.</p></SetupPanel>}
    {screen === 's-connect' && <>
      <SetupPanel title="Monitoring connection"><fieldset disabled={locked} className="space-y-3">
        <label className="flex gap-3"><input type="radio" name="monitoring" checked={draft.monitoringChoice === 'configureLater'} onChange={() => update('monitoringChoice', 'configureLater')} />Configure after setup</label>
        <label className="flex gap-3"><input type="radio" name="monitoring" checked={draft.monitoringChoice === 'reviewAzureConnection'} onChange={() => update('monitoringChoice', 'reviewAzureConnection')} />Review an available Azure connection</label>
      </fieldset></SetupPanel>
      <SetupPanel title="Recorded connection facts"><dl className="space-y-2">{Object.entries(view?.monitoring ?? {}).map(([key, value]) =>
        <div key={key} className="flex justify-between gap-4"><dt>{key}</dt><dd>{value}</dd></div>)}</dl>
        <p className="mt-4 text-sm">System-resource-scoped collection and evaluation are not established by a subscription attachment or an access check.</p>
        {draft.monitoringChoice === 'reviewAzureConnection' && <button type="button" disabled={busy} onClick={() => void checkAccess()} className="mt-3 rounded border px-3 py-2">Check Azure assessment access</button>}
        {accessMessage && <p role="status" className="mt-3">{accessMessage} Collection remains unverified.</p>}
      </SetupPanel>
    </>}
    {screen === 's-review' && <SetupPanel title="Saved system summary"><dl className="space-y-3">
      <div><dt>System</dt><dd>{view?.displayName}</dd></div><div><dt>Organization</dt><dd>{context?.organizationName}</dd></div>
      <div><dt>Preparation objective</dt><dd>{view?.draft.objective}</dd></div>
      <div><dt>Accountable contact</dt><dd>{view?.contacts.find(p => p.personId === view.draft.contact?.personId)?.displayName ?? 'Not assigned — return to Team'}</dd></div>
      <div><dt>Source choice</dt><dd>{view?.draft.sourceChoice} · no source approval by setup</dd></div>
      <div><dt>Hosting choice</dt><dd>{view?.draft.hostingChoice} · detailed scope review remains separate</dd></div>
      <div><dt>Authorization standing</dt><dd>No authorization decision is created by setup.</dd></div>
    </dl><label className="mt-5 flex gap-3"><input type="checkbox" checked={confirmed} disabled={busy} onChange={e => setConfirmed(e.target.checked)} />Confirm the saved identity and setup choices shown.</label></SetupPanel>}
    {screen === 's-ready' && <>
      <SetupPanel title="Draft system ready for preparation"><p>{view?.displayName} has a retained setup record. Its package remains subject to documentation, review and authorization gates.</p></SetupPanel>
      <SetupPanel title="Next documentation tasks">{view?.tasks.map(task =>
        <div key={task.id} className="border-b py-3"><p className="font-medium">{task.label} · {task.state}</p><p className="text-sm">{task.contribution}</p>
          {task.canAct ? <Link className="text-purple-700 underline" to={task.link}>Open task</Link> : <p className="text-sm">Ask the assigned {task.ownerRole ?? 'responsible role'} to complete this task.</p>}</div>)}</SetupPanel>
      <SetupPanel title="Continue in Systems"><p className="mb-3 text-sm">No document is generated or approved by setup.</p><div className="flex flex-wrap gap-4">
        {view?.links?.documents && <Link to={view.links.documents}>Document previews</Link>}
        {view?.links?.hosting && <Link to={view.links.hosting}>Environment & hosting</Link>}
        {view?.links?.capabilities && <Link to={view.links.capabilities}>Capabilities & customer duties</Link>}
        {view?.links?.monitoring && <Link to={view.links.monitoring}>Continuous monitoring</Link>}
      </div></SetupPanel>
    </>}
  </SetupFrame>;
}
