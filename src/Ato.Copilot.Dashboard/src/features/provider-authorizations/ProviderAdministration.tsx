import { useState } from 'react';
import { useWorkspaceSession } from '../workspaces/WorkspaceBoundary';
import { EntraUserPicker } from '../workspace-operations/EntraUserPicker';
import { listOrganizations, type DirectoryUser } from '../workspace-operations/api';
import { inputClass, Pager, Status, useRemote } from '../workspace-operations/workspaceUi';
import { getMembershipPersons, getWorkspaceMemberships } from '../workspaces/api';
import { ProviderPanel } from './ProviderPresentation';
import { MutationForm } from './forms';
import { enrollOrganizationAdministrator } from './providerAdministrationApi';

export function ProviderAdministration() {
  const session = useWorkspaceSession();
  if (session?.target.kind !== 'csp' || !session.workspace.roles?.includes('CSP.Admin'))
    return <p role="alert">CSP administrator authority is required for directory and organization access administration.</p>;
  return <ProviderAdministratorOperations />;
}

function ProviderAdministratorOperations() {
  const [page, setPage] = useState(1);
  const [tenantId, setTenantId] = useState('');
  const [user, setUser] = useState<DirectoryUser | null>(null);
  const [pending, setPending] = useState(false);
  const organizations = useRemote(signal => listOrganizations({ page, pageSize: 25 }, signal), [page]);
  return <div className="provider-workspace space-y-5">
    <ProviderPanel title="People and directory connections">
      <p className="mb-4 text-sm">Only configured directories are queried. Selecting a directory identity does not create a membership, grant a role, or connect a cloud resource.</p>
      <EntraUserPicker onSelect={setUser} />
      {user && <p role="status" className="mt-4 break-words text-sm">Selected directory identity: {user.displayName} · {user.email}. No access has been granted.</p>}
    </ProviderPanel>
    <ProviderPanel title="Organization role administration">
      <p className="mb-4 text-sm">CSP administrators may explicitly enroll the first organization Administrator. Existing Administrator changes and RMF role assignments remain in the organization’s authorized role-management workflow.</p>
      <Status loading={organizations.loading} error={organizations.error} retry={organizations.retry} />
      {organizations.data && <fieldset disabled={pending}>
        <label className="grid gap-1 text-sm">Organization for access administration<select className={inputClass} value={tenantId} onChange={event => setTenantId(event.target.value)}>
          <option value="">Select an organization</option>{organizations.data.items.map(item => <option key={item.id} value={item.id}>{item.displayName}</option>)}
        </select></label>
        <Pager {...organizations.data} onPage={next => { setTenantId(''); setPage(next); }} />
      </fieldset>}
      {tenantId && <OrganizationAdministratorEnrollment key={tenantId} tenantId={tenantId} onPendingChange={setPending} />}
    </ProviderPanel>
  </div>;
}

function OrganizationAdministratorEnrollment({ tenantId, onPendingChange }: { tenantId: string; onPendingChange: (pending: boolean) => void }) {
  const [page, setPage] = useState(1);
  const [personId, setPersonId] = useState('');
  const [confirmed, setConfirmed] = useState(false);
  const [pending, setPending] = useState(false);
  const [saved, setSaved] = useState('');
  const persons = useRemote(() => getMembershipPersons(tenantId), [tenantId]);
  const memberships = useRemote(() => getWorkspaceMemberships(tenantId, page), [tenantId, page]);
  const active = memberships.data?.items.filter(item => item.revokedAt === null) ?? [];
  const eligible = persons.data?.filter(person => active.some(member => member.personId === person.id)) ?? [];
  const unavailable = persons.loading || memberships.loading || !!persons.error || !!memberships.error;
  return <div className="mt-4 space-y-4">
    <Status loading={persons.loading || memberships.loading} error={persons.error ?? memberships.error}
      retry={() => { persons.retry(); memberships.retry(); }} />
    {!unavailable && !eligible.length && <p>No active members on this page. Grant explicit membership through organization administration first.</p>}
    <MutationForm label="Enroll initial Administrator" disabled={unavailable || !!saved}
      submitDisabled={!confirmed || !eligible.some(person => person.id === personId)}
      onPendingChange={value => { setPending(value); onPendingChange(value); }}
      submit={async () => {
        const result = await enrollOrganizationAdministrator(tenantId, personId);
        setSaved(`Administrator role recorded: ${result.id}`);
      }} onSaved={() => undefined}>
      <label className="grid gap-1 text-sm">Existing active member<select className={inputClass} value={personId}
        onChange={event => { setPersonId(event.target.value); setConfirmed(false); }}>
        <option value="">Select an active member</option>{eligible.map(person => <option key={person.id} value={person.id}>{person.displayName}</option>)}
      </select></label>
      <label className="flex gap-2 text-sm"><input type="checkbox" checked={confirmed} onChange={event => setConfirmed(event.target.checked)} />
        I explicitly grant the organization Administrator role to this active member.</label>
      <p className="text-xs">This action does not grant CSP authority or issue a mission authorization. The server verifies membership and prevents replacing an existing Administrator through initial enrollment.</p>
    </MutationForm>
    {memberships.data && <fieldset disabled={pending}><Pager page={page} pageSize={50} total={memberships.data.total}
      onPage={next => { setPersonId(''); setConfirmed(false); setPage(next); }} /></fieldset>}
    {saved && <p role="status">{saved}</p>}
  </div>;
}
