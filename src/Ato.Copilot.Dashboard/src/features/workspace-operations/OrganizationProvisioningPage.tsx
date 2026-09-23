import { useEffect, useRef, useState } from 'react';
import { CheckCircle2, Circle, AlertTriangle } from 'lucide-react';
import PageHero from '../../components/layout/PageHero';
import PageLayout from '../../components/layout/PageLayout';
import { Link, useLocation, useNavigate } from '../workspaces/workspaceNavigation';
import { useWorkspaceSession } from '../workspaces/WorkspaceBoundary';
import * as api from './api';
import type { InitialAdministrator, ProvisioningResult } from './types';
import { buttonClass, errorClass, message, secondaryButtonClass, Status, useRemote, warningClass } from './workspaceUi';
import {
  AdministratorInputs, administratorIntent, emptyAdministrator, enrollmentComplete,
  SetupInfo, setupCard, SetupSteps, validateAdministrator, type FieldErrors,
} from './OrganizationSetupPresentation';

export default function OrganizationProvisioningPage({ tenantId }: { tenantId: string }) {
  const location = useLocation();
  const navigate = useNavigate();
  const canManageMembers = useWorkspaceSession()?.workspace.permissions.canManageMemberships === true;
  const requestedKey = new URLSearchParams(location.search).get('key');
  const organization = useRemote(signal => api.getOrganization(tenantId, signal), [tenantId]);
  const [result, setResult] = useState<ProvisioningResult | null>(null);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [fields, setFields] = useState(emptyAdministrator);
  const [newPerson, setNewPerson] = useState(true);
  const [editing, setEditing] = useState(false);
  const [errors, setErrors] = useState<FieldErrors>({});
  const key = useRef(requestedKey);
  const controller = useRef<AbortController | null>(null);
  const pending = useRef(false);
  const alive = useRef(true);
  const generation = useRef(0);
  const autoStarted = useRef(false);
  const localKey = useRef<string | null>(null);
  const [reload, setReload] = useState(0);
  const complete = !!result && enrollmentComplete(result);
  const hasIdentity = !!result?.initialAdministrator;
  const active = busy && !!result;
  const title = complete ? 'Organization setup complete'
    : result?.lastError ? 'Organization created · Enrollment needs attention'
    : active ? 'Organization created · Enrollment in progress'
    : 'Organization created · Enrollment pending';
  useEffect(() => { alive.current = true; return () => { alive.current = false; controller.current?.abort(); }; }, []);

  const adopt = (value: ProvisioningResult) => {
    if (value.tenantId !== tenantId) throw new Error('The setup response does not belong to this organization.');
    setResult(value);
    const identity = value.initialAdministrator;
    if (identity) {
      setFields({ directoryTenantId: identity.directoryTenantId, objectId: identity.objectId,
        personId: identity.personId ?? '', displayName: identity.newPerson?.displayName ?? '', email: identity.newPerson?.email ?? '' });
      setNewPerson(!!identity.newPerson);
    }
    setEditing(false);
    key.current = value.idempotencyKey || key.current;
  };
  useEffect(() => {
    if (localKey.current === requestedKey && pending.current) return;
    localKey.current = null;
    const current = ++generation.current;
    const read = new AbortController();
    controller.current?.abort(); controller.current = read; pending.current = false;
    setResult(null); setFields(emptyAdministrator); setError(null); setLoadError(null); setErrors({});
    setLoading(true); setBusy(false); key.current = requestedKey;
    const load = requestedKey ? api.getOrganizationProvisioning(tenantId, requestedKey, read.signal).catch(reason => {
      if (reason instanceof api.WorkspaceOperationError && reason.status === 404 && reason.code === 'PROVISIONING_NOT_FOUND') return null;
      throw reason;
    })
      : api.getCurrentOrganizationProvisioning(tenantId, read.signal);
    load.then(value => {
      if (read.signal.aborted || current !== generation.current) return;
      if (value) {
        adopt(value);
        if (!requestedKey && value.idempotencyKey) navigate(`/organizations/${tenantId}/provisioning?key=${encodeURIComponent(value.idempotencyKey)}`, { replace: true, state: location.state });
      }
    }).catch(reason => {
      if (!read.signal.aborted && current === generation.current) setLoadError(message(reason));
    }).finally(() => {
      if (!read.signal.aborted && current === generation.current) setLoading(false);
    });
    return () => read.abort();
    // The server response is scoped to these scalar route/reload identities.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tenantId, requestedKey, reload]);

  const resume = async (identity: InitialAdministrator) => {
    if (!result || pending.current || loading || loadError || organization.error) return;
    pending.current = true; setBusy(true); setError(null); setErrors({});
    const current = generation.current;
    const action = new AbortController();
    controller.current?.abort(); controller.current = action;
    try {
      const value = await api.resumeOrganizationProvisioning(tenantId, result.operationId, identity, action.signal);
      if (alive.current && !action.signal.aborted && current === generation.current) adopt(value);
    } catch (reason) {
      if (!alive.current || action.signal.aborted || current !== generation.current) return;
      setError(message(reason));
      try {
        const saved = key.current ? await api.getOrganizationProvisioning(tenantId, key.current, action.signal)
          : await api.getCurrentOrganizationProvisioning(tenantId, action.signal);
        if (alive.current && !action.signal.aborted && current === generation.current && saved) adopt(saved);
      } catch (reloadReason) {
        if (alive.current && !action.signal.aborted && current === generation.current) setLoadError(`Cannot confirm saved setup outcomes: ${message(reloadReason)}`);
      }
    } finally {
      if (alive.current && !action.signal.aborted && current === generation.current) { pending.current = false; setBusy(false); }
    }
  };
  useEffect(() => {
    if (autoStarted.current || !result?.initialAdministrator || loading || organization.loading || organization.error || complete) return;
    if (!(location.state as { autoResume?: boolean } | null)?.autoResume) return;
    autoStarted.current = true;
    navigate({ pathname: location.pathname, search: location.search }, { replace: true, state: null });
    void resume(result.initialAdministrator);
    // Automatic continuation is limited to this confirmed navigation, never a refresh.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [result, loading, organization.loading, organization.error, complete]);

  const begin = async () => {
    if (pending.current || loading || loadError || organization.error) return;
    pending.current = true; setBusy(true); setError(null);
    const current = generation.current;
    const action = new AbortController(); controller.current = action;
    key.current ??= crypto.randomUUID(); localKey.current = key.current;
    navigate(`/organizations/${tenantId}/provisioning?key=${encodeURIComponent(key.current)}`, { replace: true });
    try {
      const value = await api.beginOrganizationProvisioning(tenantId, key.current, action.signal);
      if (alive.current && !action.signal.aborted && current === generation.current) adopt(value);
    } catch (reason) {
      if (alive.current && !action.signal.aborted && current === generation.current) {
        setLoadError(message(reason));
        setError('The enrollment start outcome is uncertain. Reload saved status before retrying.');
      }
    } finally {
      if (alive.current && !action.signal.aborted && current === generation.current) { pending.current = false; setBusy(false); }
    }
  };
  const submitIdentity = () => {
    const validation = validateAdministrator(fields, newPerson);
    setErrors(validation);
    if (!Object.keys(validation).length) void resume(administratorIntent(fields, newPerson));
  };
  const retryLabel = result?.membershipState === 'Completed' ? 'Retry administrator enrollment'
    : result?.lastError ? 'Retry incomplete enrollment' : 'Continue enrollment';
  const allowed = !busy && !loading && !loadError && !organization.loading && !!organization.data && !organization.error;
  return <PageLayout title="Organization enrollment">
    <PageHero eyebrow="Provider administration" title={organization.data?.displayName ?? 'Organization enrollment'}
      description="Create the organization once, then resume administrator and membership enrollment independently."
      actions={<SetupSteps step={4} />} />
    <div className="space-y-5 text-slate-800 dark:text-gray-100">
      <Status loading={loading || organization.loading} error={loadError ?? organization.error}
        retry={() => { organization.retry(); setReload(value => value + 1); }} />
      {error && <p role="alert" className={errorClass}>{error}</p>}
      {!loading && !loadError && !organization.error && !result && <section className={setupCard}>
        <h2 className="mb-3 text-xl font-semibold">Enrollment not started</h2>
        <p className="mb-4 text-sm">The organization already exists. Start a resumable enrollment record; this does not create another organization.</p>
        <button className={buttonClass} disabled={!allowed} onClick={() => void begin()}>Start enrollment</button>
      </section>}
      {result && <section className={`${setupCard} space-y-5`} aria-busy={busy}>
        <h2 className="text-xl font-semibold">{title}</h2>
        {result.lastError && <p role="alert" className={warningClass}>{result.lastError}</p>}
        <div className="grid gap-3 md:grid-cols-2">
          <Stage label="Organization" state={result.tenantState} detail="Organization record" />
          {result.personState && result.personState !== 'NotRequested' && <Stage label="Person" state={result.personState} detail="Organization-local administrator record" />}
          <Stage label="Membership" state={result.membershipState} detail={active && result.membershipState !== 'Completed' ? 'Enrollment request is running; awaiting saved outcome.' : 'Explicit organization access'} />
          <Stage label="Administrator" state={result.administratorState} detail="Organization Administrator role; requires active membership" />
        </div>
        <SetupInfo>Completed work is preserved. Resume applies only unfinished work. You can return from Organizations at any time.</SetupInfo>
        {!complete && <div className="grid items-start gap-5 lg:grid-cols-2">
          <section className="space-y-4">
            {(!hasIdentity || editing) ? <form noValidate onSubmit={event => { event.preventDefault(); submitIdentity(); }}>
              <fieldset disabled={!allowed} className="space-y-4">
                <legend className="mb-4 font-semibold">Initial administrator</legend>
                <AdministratorInputs fields={fields} newPerson={newPerson} onNewPerson={value => { setNewPerson(value); setErrors({}); }}
                  errors={errors} onChange={(field, value) => {
                    setFields(current => ({ ...current, [field]: value }));
                    setErrors(current => { const next = { ...current }; delete next[field]; return next; });
                  }} />
                {Object.keys(errors).length > 0 && <p role="alert" className={errorClass}>Correct the highlighted identifiers or Person details.</p>}
                <button type="submit" className={buttonClass}>Resume incomplete enrollment</button>
              </fieldset>
            </form> : <>
              <h3 className="font-semibold">Saved administrator selection</h3>
              <dl className="space-y-2 break-all text-sm">
                <div><dt>Directory tenant ID</dt><dd>{result.initialAdministrator?.directoryTenantId}</dd></div>
                <div><dt>User object ID</dt><dd>{result.initialAdministrator?.objectId}</dd></div>
                <div><dt>Person record ID</dt><dd>{result.initialAdministrator?.personId ?? 'Pending local Person creation'}</dd></div>
              </dl>
              <p className="text-xs text-slate-500">Identifiers are not directory-verified by this flow.</p>
              <button className={buttonClass} disabled={!allowed} onClick={() => result.initialAdministrator && void resume(result.initialAdministrator)}>{busy ? 'Enrollment in progress...' : retryLabel}</button>
              {result.canEditAdministrator && <button className={`${secondaryButtonClass} ml-2`} disabled={!allowed} onClick={() => setEditing(true)}>Correct administrator details</button>}
            </>}
          </section>
          <aside className="space-y-4">
            <SetupInfo>Organization Administrator is separate from system and RMF roles. Enrollment does not grant the CSP operator access to customer systems.</SetupInfo>
            <p className="text-sm">If a directory or Person identifier is rejected, correct it only while the saved setup permits editing. Completed bindings must be repaired through membership administration.</p>
            {canManageMembers && <Link className={`${secondaryButtonClass} inline-flex`} to={`/organizations/${tenantId}/memberships`}>Manage organization members</Link>}
          </aside>
        </div>}
        {complete && <SetupInfo>Required setup stages are saved. Organization users can now continue through their own authorized onboarding workflows. System roles, provider subscriptions and ATO decisions remain separate.</SetupInfo>}
        <div className="flex flex-wrap gap-3">
          <Link className={buttonClass} to={`/organizations/${tenantId}`}>View organization</Link>
          <Link className={secondaryButtonClass} to="/organizations">{complete ? 'Back to organizations' : 'Finish later'}</Link>
          <button className={secondaryButtonClass} disabled={busy} onClick={() => setReload(value => value + 1)}>Refresh setup status</button>
        </div>
      </section>}
    </div>
  </PageLayout>;
}

function Stage({ label, state, detail }: { label: string; state: string; detail: string }) {
  const Icon = state === 'Completed' ? CheckCircle2 : state === 'Failed' ? AlertTriangle : Circle;
  return <div className="flex items-start gap-3 rounded-lg border border-slate-200 p-4 dark:border-gray-700">
    <Icon className={`mt-0.5 shrink-0 ${state === 'Completed' ? 'text-emerald-600' : state === 'Failed' ? 'text-amber-600' : 'text-slate-400'}`} size={22} aria-hidden="true" />
    <div><p className="text-sm font-semibold">{label}: {state}</p><p className="mt-1 text-xs text-slate-500">{detail}</p></div>
  </div>;
}
