import { useState } from 'react';
import { Link } from '../workspaces/workspaceNavigation';
import WorkspacePageHeader from '../../components/layout/WorkspacePageHeader';
import { useWorkspaceSession } from '../workspaces/WorkspaceBoundary';
import { EntraUserPicker } from '../workspace-operations/EntraUserPicker';
import { getDirectoryConnections, listOrganizations, type DirectoryUser } from '../workspace-operations/api';
import { inputClass, Pager, secondaryButtonClass, Status, useRemote } from '../workspace-operations/workspaceUi';
import SetupDialog from '../workspace-operations/SetupDialog';
import { getMembershipPersons, getWorkspaceMemberships } from '../workspaces/api';
import { ProviderBadge, ProviderFact, ProviderPanel } from './ProviderPresentation';
import { MutationForm } from './forms';
import { enrollOrganizationAdministrator } from './providerAdministrationApi';
import { authorizationHref, listOfferings } from './api';
import { getHostingScope } from './hostingApi';
import { readAllPages } from './providerReadModels';
import { allocationScopeName } from './ProviderAllocationForm';
import { getCspOnboardingState, isUnavailable } from '../csp-onboarding/api';

export function ProviderAdministration() {
  const session = useWorkspaceSession();
  if (session?.target.kind !== 'csp' || !session.workspace.roles?.includes('CSP.Admin'))
    return <p role="alert">CSP administrator authority is required for directory and organization access administration.</p>;
  return <ProviderAdministrationContent />;
}

function ProviderAdministrationContent() {
  const session = useWorkspaceSession();
  const [dialog, setDialog] = useState<'role' | 'directory' | 'azure' | 'reference' | 'profile' | null>(null);
  const [user, setUser] = useState<DirectoryUser | null>(null);
  const connections = useRemote(signal => getDirectoryConnections(signal), []);
  const identity = session?.identity;
  const roles = session?.workspace.roles ?? [];
  const configured = connections.data?.filter(item => item.configured).length ?? 0;
  const directoryState = connections.loading ? 'Checking' : connections.error ? 'Unavailable'
    : configured ? `${configured} configured` : 'Not configured';
  return <div className="provider-workspace">
    <div className="provider-page-head"><WorkspacePageHeader eyebrow="Provider operations" title="Provider administration"
      description="Manage the people and connections used to maintain provider records."
      actions={<button type="button" className="provider-primary" onClick={() => { setUser(null); setDialog('directory'); }}>Find user in Entra</button>} /></div>
    <div className="provider-grid"><div className="space-y-5">
      <ProviderPanel title="Provider team">
        <div className="provider-table-wrap"><table className="provider-table min-w-[560px]" aria-label="Provider team">
          <thead><tr><th>Person</th><th>Role</th><th>Access</th><th><span className="sr-only">Actions</span></th></tr></thead>
          <tbody><tr>
            <td>{identity?.displayName || 'Signed-in provider administrator'}<small>Current authenticated account</small></td>
            <td>{roles.map(role => role === 'CSP.Admin' ? 'Provider administrator' : role).join(', ')}</td>
            <td><ProviderBadge tone="success">Current session authorized</ProviderBadge></td>
            <td><button type="button" className="provider-secondary" onClick={() => setDialog('role')}>View role</button></td>
          </tr></tbody>
        </table></div>
        <p className="mt-3 text-xs text-slate-500">This API supplies the signed-in account, not a complete provider-team roster. Directory results and offering contacts are not role grants.</p>
      </ProviderPanel>
      <ProviderPanel title="Connections">
        <Status loading={connections.loading} error={connections.error} retry={connections.retry} />
        <div className="provider-checklist-row">
          <div className="min-w-0 flex-1"><h3>Microsoft Entra directory</h3><p>Search configured directories for an identity. Selection does not grant access.</p></div>
          <div className="flex flex-wrap items-center gap-3"><ProviderBadge tone={configured ? 'neutral' : 'attention'}>{directoryState}</ProviderBadge>
            <button type="button" className="provider-secondary" onClick={() => { setUser(null); setDialog('directory'); }}>Find user</button></div>
        </div>
        <div className="provider-checklist-row">
          <div className="min-w-0 flex-1"><h3>Azure subscriptions &amp; allocations</h3><p>Select an offering to manage registered subscription allocations and released hosting scopes. Allocation does not grant Azure access.</p></div>
          <button type="button" className="provider-secondary" onClick={() => setDialog('azure')}>Review scope configuration</button>
        </div>
        <div className="provider-checklist-row">
          <div className="min-w-0 flex-1"><h3>External service catalog</h3><p>Select the owning offering before configuring an upstream service reference.</p></div>
          <button type="button" className="provider-secondary" onClick={() => setDialog('reference')}>Configure reference</button>
        </div>
      </ProviderPanel>
      <details className="provider-record-details"><summary>Organization role administration</summary>
        <OrganizationAccessAdministration />
      </details>
    </div><aside className="provider-support">
      <ProviderPanel title="Identity is separate from permission"><p>Finding a user supplies an identity. An authorized administrator must explicitly grant the appropriate role; lookup does not grant CSP authority.</p></ProviderPanel>
      <ProviderPanel title="Separate workspaces"><p>Provider administration does not enroll organization members or assign mission RMF roles automatically.</p>
        <Link className="provider-text mt-3 block" to="/organizations">Manage organizations</Link>
        <button type="button" className="provider-text mt-3" onClick={() => setDialog('profile')}>Review provider profile</button>
      </ProviderPanel>
      <Link className="provider-text" to="/audit">Review audit history →</Link>
    </aside></div>
    {dialog && <SetupDialog title={{ role: 'Provider role details', directory: 'Find user in Microsoft Entra',
      azure: 'Review Azure scope configuration', reference: 'Configure service reference', profile: 'Provider profile' }[dialog]}
      busy={false} onClose={() => setDialog(null)} description="Provider-scoped administration. Viewing these records does not change membership or authorization.">
      {dialog === 'directory' && <>
        <EntraUserPicker purpose="lookup" onSelect={setUser} />
        {user && <p role="status" className="mt-4 rounded border border-indigo-200 p-3 text-sm">Selected identity: {user.displayName} · {user.email}. No membership or role has been granted.</p>}
      </>}
      {dialog === 'role' && <div className="space-y-4 text-sm">
        <p>This view explains the authenticated account's server-granted authority; it does not grant or revoke roles.</p>
        <dl className="grid gap-2"><dt>Account</dt><dd>{identity?.displayName ?? 'Unavailable'}</dd><dt>Provider roles</dt><dd>{roles.join(', ')}</dd>
          <dt>Provider workspace access</dt><dd>{session?.workspace.permissions.canAccessCsp ? 'Authorized' : 'Not authorized'}</dd>
          <dt>Organization membership administration</dt><dd>{session?.workspace.permissions.canManageMemberships ? 'Subject to target-specific server checks' : 'Not granted'}</dd>
        </dl><details><summary className="cursor-pointer">Identity details</summary><p className="mt-2 break-all">Directory: {identity?.directoryTenantId ?? 'Not returned'}<br />Object: {identity?.oid ?? 'Not returned'}</p></details>
        <p>Provider authority is managed through the deployment's identity configuration, not by editing a contact or choosing a directory search result.</p>
      </div>}
      {(dialog === 'azure' || dialog === 'reference') && <OfferingAdministrationTarget key={dialog} kind={dialog} />}
      {dialog === 'profile' && <ProviderProfile />}
    </SetupDialog>}
  </div>;
}

function OfferingAdministrationTarget({ kind }: { kind: 'azure' | 'reference' }) {
  const [selectedId, setSelectedId] = useState('');
  const offerings = useRemote(signal => readAllPages(page => listOfferings(page, '', signal), signal), []);
  const choices = offerings.data?.filter(item => kind !== 'azure' || item.environments.some(environment => environment === 'AzureCloud' || environment === 'AzureUSGovernment')) ?? [];
  const selected = choices.find(item => item.offeringId === selectedId);
  const scope = useRemote(signal => kind === 'azure' && selected?.currentHostingScopeRevisionId
    ? getHostingScope(selected.offeringId, selected.currentHostingScopeRevisionId, signal) : Promise.resolve(null),
  [kind, selected?.offeringId, selected?.currentHostingScopeRevisionId]);
  return <div className="space-y-4">
    <Status loading={offerings.loading} error={offerings.error} retry={offerings.retry} />
    <label className="block text-sm">Service offering<select aria-label="Service offering" className={`${inputClass} mt-1 w-full`} value={selectedId} onChange={event => setSelectedId(event.target.value)}>
      <option value="">Select owning offering</option>{choices.map(item => <option key={item.offeringId} value={item.offeringId}>{item.name}</option>)}
    </select></label>
    {offerings.data && !choices.length && <p className="text-sm">No eligible offering records are available.</p>}
    {kind === 'azure' && <>
      <p className="text-sm">These are recorded scope definitions, not a credential test or a live Azure connection-health result.</p>
      <Status loading={scope.loading} error={scope.error} retry={scope.retry} />
      {selected && !scope.loading && !scope.error && !scope.data && <p>No current hosting scope is recorded for this offering.</p>}
      {scope.data && <div className="space-y-3 text-sm"><p>Recorded scope revision {scope.data.snapshot.revision}</p>
        <ul className="list-disc pl-5">{scope.data.permittedScopes.map((item, index) => <li key={index}>{allocationScopeName(item)}</li>)}</ul>
        <details><summary>Exact recorded scope</summary><pre className="mt-2 max-h-64 overflow-auto whitespace-pre-wrap break-all text-xs">{JSON.stringify(scope.data.permittedScopes, null, 2)}</pre></details>
      </div>}
    </>}
    {kind === 'reference' && <p className="text-sm">Continue to the selected offering's actual reference editor. Saving and reviewing its source remains an explicit operation.</p>}
    {selected && <Link className={secondaryButtonClass} to={authorizationHref(selected.offeringId, kind === 'azure' ? 'inherited-coverage' : 'inherited-coverage?task=references')}>
      {kind === 'azure' ? 'Open service scope' : 'Open reference configuration'}</Link>}
  </div>;
}

function ProviderProfile() {
  const profile = useRemote(async () => {
    const result = await getCspOnboardingState();
    if (isUnavailable(result)) throw new Error(`Provider profile unavailable: ${result.reason}`);
    return result;
  }, []);
  return <div className="provider-workspace">
    <Status loading={profile.loading} error={profile.error} retry={profile.retry} />
    {profile.data && <dl className="space-y-3 text-sm">
      <ProviderFact label="Provider">{profile.data.identity?.displayName || 'Not recorded'}</ProviderFact>
      <ProviderFact label="Legal entity">{profile.data.identity?.legalEntityName || 'Not recorded'}</ProviderFact>
      <ProviderFact label="Support email">{profile.data.supportContact?.primarySupportEmail || 'Not recorded'}</ProviderFact>
      <ProviderFact label="Classification floor">{profile.data.classification?.defaultClassificationFloor ?? 'Not recorded'}</ProviderFact>
      <ProviderFact label="Onboarding state">{profile.data.onboardingState}</ProviderFact>
    </dl>}
    <p className="mt-4 text-sm">Profile and onboarding state do not establish live cloud connectivity or mission authorization.</p>
  </div>;
}

function OrganizationAccessAdministration() {
  const [page, setPage] = useState(1);
  const [tenantId, setTenantId] = useState('');
  const [pending, setPending] = useState(false);
  const organizations = useRemote(signal => listOrganizations({ page, pageSize: 25 }, signal), [page]);
  return <div className="provider-workspace space-y-5">
    <ProviderPanel title="Organization role administration">
      <p className="mb-4 text-sm">CSP administrators may explicitly enroll the first organization Administrator. Existing Administrator changes and RMF role assignments remain in the organization’s authorized role-management workflow.</p>
      <Status loading={organizations.loading} error={organizations.error} retry={organizations.retry} />
      {organizations.data && <fieldset disabled={pending}>
        <label className="grid gap-1 text-sm">Organization for access administration<select aria-label="Organization for access administration" className={inputClass} value={tenantId} onChange={event => setTenantId(event.target.value)}>
          <option value="">Select an organization</option>{organizations.data.items.map(item => <option key={item.id} value={item.id}>{item.displayName}</option>)}
        </select></label>
        <Pager {...organizations.data} onPage={next => { setTenantId(''); setPage(next); }} />
      </fieldset>}
      {tenantId && <OrganizationAdministratorEnrollment key={tenantId} tenantId={tenantId}
        organizationName={organizations.data?.items.find(item => item.id === tenantId)?.displayName ?? tenantId} onPendingChange={setPending} />}
    </ProviderPanel>
  </div>;
}

function OrganizationAdministratorEnrollment({ tenantId, organizationName, onPendingChange }: {
  tenantId: string; organizationName: string; onPendingChange: (pending: boolean) => void;
}) {
  const [page, setPage] = useState(1);
  const [personId, setPersonId] = useState('');
  const [confirmed, setConfirmed] = useState(false);
  const [pending, setPending] = useState(false);
  const [saved, setSaved] = useState('');
  const [open, setOpen] = useState(false);
  const persons = useRemote(() => getMembershipPersons(tenantId), [tenantId]);
  const memberships = useRemote(() => getWorkspaceMemberships(tenantId, page), [tenantId, page]);
  const active = memberships.data?.items.filter(item => item.revokedAt === null) ?? [];
  const eligible = persons.data?.filter(person => active.some(member => member.personId === person.id)) ?? [];
  const unavailable = persons.loading || memberships.loading || !!persons.error || !!memberships.error;
  return <div className="mt-4 space-y-4">
    <Status loading={persons.loading || memberships.loading} error={persons.error ?? memberships.error}
      retry={() => { persons.retry(); memberships.retry(); }} />
    {!unavailable && !eligible.length && <p>No active members on this page. Grant explicit membership through organization administration first.</p>}
    <button type="button" className="provider-secondary" disabled={unavailable || !!saved || !eligible.length || pending} onClick={() => {
      setPersonId(''); setConfirmed(false); setOpen(true);
    }}>Enroll initial Administrator</button>
    {open && <SetupDialog title="Enroll initial Administrator" description={`Organization: ${organizationName}`} busy={pending} onClose={() => setOpen(false)}>
      <Status loading={persons.loading || memberships.loading} error={persons.error ?? memberships.error}
        retry={() => { persons.retry(); memberships.retry(); }} />
      <MutationForm label="Enroll initial Administrator" disabled={unavailable || !!saved}
      submitDisabled={!confirmed || !eligible.some(person => person.id === personId)}
      onPendingChange={value => { setPending(value); onPendingChange(value); }}
      submit={async () => {
        const result = await enrollOrganizationAdministrator(tenantId, personId);
        setSaved(`Administrator role recorded: ${result.id}`);
      }} onSaved={() => { setOpen(false); setPending(false); onPendingChange(false); }}>
      <label className="grid gap-1 text-sm">Existing active member<select aria-label="Existing active member" className={inputClass} value={personId}
        onChange={event => { setPersonId(event.target.value); setConfirmed(false); }}>
        <option value="">Select an active member</option>{eligible.map(person => <option key={person.id} value={person.id}>{person.displayName}</option>)}
      </select></label>
      <label className="flex gap-2 text-sm"><input type="checkbox" checked={confirmed} onChange={event => setConfirmed(event.target.checked)} />
        I explicitly grant the organization Administrator role to this active member.</label>
      <p className="text-xs">This action does not grant CSP authority or issue a mission authorization. The server verifies membership and prevents replacing an existing Administrator through initial enrollment.</p>
    </MutationForm>
      <button type="button" className={`${secondaryButtonClass} mt-4`} disabled={pending} onClick={() => setOpen(false)}>Cancel</button>
    </SetupDialog>}
    {memberships.data && <fieldset disabled={pending}><Pager page={page} pageSize={50} total={memberships.data.total}
      onPage={next => { setPersonId(''); setConfirmed(false); setPage(next); }} /></fieldset>}
    {saved && <p role="status">{saved}</p>}
  </div>;
}
