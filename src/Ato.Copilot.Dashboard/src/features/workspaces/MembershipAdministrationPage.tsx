import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react';
import PageLayout from '../../components/layout/PageLayout';
import {
  createMembershipPerson,
  getMembershipPersons,
  getWorkspaceMemberships,
  grantWorkspaceMembership,
  revokeWorkspaceMembership,
  workspaceErrorMessage,
} from './api';
import type {
  GrantWorkspaceMembershipRequest,
  MembershipPerson,
  WorkspaceMembership,
  WorkspacePage,
} from './types';

interface MembershipAdministrationPageProps {
  tenantId: string;
  tenantName: string;
  canManageMemberships: boolean;
  /** Selected record, supplied after the server confirms revocation. */
  onRevoked?: (membership: WorkspaceMembership) => void;
}

const inputClass = 'mt-1 block w-full rounded-md border border-gray-300 bg-white px-3 py-2 text-sm text-gray-900';
const buttonClass = 'rounded-md bg-indigo-700 px-3 py-2 text-sm font-medium text-white hover:bg-indigo-800 disabled:cursor-not-allowed disabled:opacity-50';
const secondaryClass = 'rounded-md border border-gray-300 bg-white px-3 py-2 text-sm text-gray-700 hover:bg-gray-50 disabled:cursor-not-allowed disabled:opacity-50';
const panelClass = 'space-y-4 rounded-lg border border-gray-200 bg-white p-5';
const errorClass = 'rounded-md border border-red-200 bg-red-50 p-3 text-sm text-red-800';
const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const emptyGuid = '00000000-0000-0000-0000-000000000000';

function isForbidden(error: unknown): boolean {
  if (!error || typeof error !== 'object' || !('response' in error)) return false;
  const response = error.response;
  return !!response && typeof response === 'object' && 'status' in response && response.status === 403;
}

export default function MembershipAdministrationPage(props: MembershipAdministrationPageProps) {
  return (
    <PageLayout title="Membership administration">
      <div className="mx-auto w-full max-w-6xl space-y-6 p-6">
        <h2 className="text-2xl font-semibold text-gray-900">{props.tenantName} — Membership administration</h2>
        {props.canManageMemberships !== true ? (
          <p role="alert" className={errorClass}>Access denied. Membership administration requires server-authorized administrator access to this organization.</p>
        ) : (
          // Remount before rendering a new tenant label; old async work cannot populate its state.
          <TenantMembershipAdministration key={props.tenantId} tenantId={props.tenantId} tenantName={props.tenantName}
            onRevoked={props.onRevoked} />
        )}
      </div>
    </PageLayout>
  );
}

function useMembershipAdministration(tenantId: string, onRevoked: MembershipAdministrationPageProps['onRevoked']) {
  const mounted = useRef(false);
  const mutating = useRef(false);
  const blocked = useRef(false);
  const onRevokedRef = useRef(onRevoked);
  const [accessError, setAccessError] = useState('');
  const [busy, setBusy] = useState(false);
  const [success, setSuccess] = useState('');
  const [mutationError, setMutationError] = useState('');
  const [people, setPeople] = useState<MembershipPerson[]>([]);
  const [personNames, setPersonNames] = useState<Record<string, string>>({});
  const [peopleLoading, setPeopleLoading] = useState(true);
  const [peopleError, setPeopleError] = useState('');
  const [peopleRequest, setPeopleRequest] = useState({ query: '', revision: 0 });
  const [memberships, setMemberships] = useState<WorkspacePage<WorkspaceMembership> | null>(null);
  const [membershipsError, setMembershipsError] = useState('');
  const [membershipRequest, setMembershipRequest] = useState({ page: 1, revision: 0 });

  useEffect(() => {
    mounted.current = true;
    return () => { mounted.current = false; };
  }, []);

  useEffect(() => {
    onRevokedRef.current = onRevoked;
  }, [onRevoked]);

  const reportError = useCallback((reason: unknown) => {
    const message = workspaceErrorMessage(reason);
    if (isForbidden(reason)) {
      blocked.current = true;
      setAccessError(`Access denied. ${message}`);
    }
    return message;
  }, []);

  useEffect(() => {
    let current = true;
    setPeopleLoading(true);
    setPeopleError('');
    setPeople([]);
    getMembershipPersons(tenantId, peopleRequest.query).then(result => {
      if (!current) return;
      setPeople(result);
      setPersonNames(names => ({ ...names, ...Object.fromEntries(result.map(person => [person.id, person.displayName])) }));
    }).catch(reason => {
      if (current) setPeopleError(reportError(reason));
    }).finally(() => {
      if (current) setPeopleLoading(false);
    });
    return () => { current = false; };
  }, [tenantId, peopleRequest, reportError]);

  useEffect(() => {
    let current = true;
    setMemberships(null);
    setMembershipsError('');
    getWorkspaceMemberships(tenantId, membershipRequest.page).then(result => {
      if (current) setMemberships(result);
    }).catch(reason => {
      if (current) setMembershipsError(reportError(reason));
    });
    return () => { current = false; };
  }, [tenantId, membershipRequest, reportError]);

  function clearMessages() {
    setSuccess('');
    setMutationError('');
  }

  function refreshMemberships(page = membershipRequest.page) {
    setMemberships(null);
    setMembershipsError('');
    setMembershipRequest(previous => ({ page, revision: previous.revision + 1 }));
  }

  async function mutate<T>(action: () => Promise<T>, message: string, onSuccess: (result: T) => void): Promise<boolean> {
    if (!mounted.current || mutating.current || blocked.current) return false;
    mutating.current = true;
    setBusy(true);
    clearMessages();
    try {
      const result = await action();
      if (!mounted.current || blocked.current) return false;
      onSuccess(result);
      setSuccess(message);
      return true;
    } catch (reason) {
      if (mounted.current && !blocked.current) setMutationError(reportError(reason));
      return false;
    } finally {
      mutating.current = false;
      if (mounted.current) setBusy(false);
    }
  }

  const createContact = (request: Omit<MembershipPerson, 'id'>) => mutate(
    () => createMembershipPerson(tenantId, request),
    'Contact created. No membership or RMF role was granted.',
    result => {
      setPeople(current => [...current.filter(person => person.id !== result.id), result]);
      setPersonNames(current => ({ ...current, [result.id]: result.displayName }));
    },
  );
  const grant = (request: GrantWorkspaceMembershipRequest) => mutate(
    () => grantWorkspaceMembership(tenantId, request),
    'Membership granted. No RMF role was assigned.',
    () => refreshMemberships(1),
  );
  const revoke = (membership: WorkspaceMembership) => mutate(
    () => revokeWorkspaceMembership(tenantId, membership.id),
    'Membership revoked.',
    () => {
      refreshMemberships();
      onRevokedRef.current?.(membership);
    },
  );
  const searchPeople = (query: string) => setPeopleRequest(current => ({ query: query.trim(), revision: current.revision + 1 }));

  return {
    accessError, busy, success, mutationError, clearMessages, people, personNames, peopleLoading, peopleError,
    searchPeople, retryPeople: () => searchPeople(peopleRequest.query),
    memberships, membershipsError, page: membershipRequest.page, refreshMemberships, createContact, grant, revoke,
  };
}

function TenantMembershipAdministration({ tenantId, tenantName, onRevoked }: Omit<MembershipAdministrationPageProps, 'canManageMemberships'>) {
  const state = useMembershipAdministration(tenantId, onRevoked);
  if (state.accessError) return <p role="alert" className={errorClass}>{state.accessError}</p>;
  return (
    <>
      <div className="space-y-2 text-sm text-gray-700">
        <p>Ordinary workspace membership is explicit and revocable. Granting membership does not assign any RMF role.</p>
        <p>Email is contact information only, not a login identity. Bind a verified directory tenant ID and object ID to an existing organization-local Person.</p>
        <p>Only CSP administrators or already-authorized administrators of this organization may manage memberships. Creating a contact does not authorize a first administrator.</p>
      </div>
      {state.success && <p role="status" className="rounded-md border border-green-200 bg-green-50 p-3 text-sm text-green-800">{state.success}</p>}
      {state.mutationError && <p role="alert" className={errorClass}>{state.mutationError}</p>}
      <div className="grid gap-6 lg:grid-cols-2">
        <ContactForm disabled={state.busy || state.peopleLoading} onCreate={state.createContact} onStart={state.clearMessages} />
        <section className={panelClass} aria-labelledby="grant-membership-heading">
          <h3 id="grant-membership-heading" className="text-lg font-semibold text-gray-900">Grant ordinary membership</h3>
          <p className="text-sm text-gray-600">Use IDs verified in the trusted identity directory. The directory tenant ID is not this organization&apos;s internal ID.</p>
          <PeopleSearch disabled={state.busy || state.peopleLoading} onSearch={state.searchPeople} />
          {state.peopleLoading ? <p role="status" className="text-sm text-gray-600">Loading organization-local people…</p> : state.peopleError ? (
            <div className="space-y-2">
              <p role="alert" className={errorClass}>{state.peopleError}</p>
              <button type="button" className={secondaryClass} disabled={state.busy} onClick={state.retryPeople}>Retry people</button>
            </div>
          ) : state.people.length === 0 ? <p className="text-sm text-gray-600">No matching organization-local people. Search again or create a contact separately.</p> : null}
          <GrantForm people={state.people} disabled={state.busy || state.peopleLoading || !!state.peopleError}
            onGrant={state.grant} onStart={state.clearMessages} />
        </section>
      </div>
      <section className={panelClass} aria-labelledby="memberships-heading">
        <h3 id="memberships-heading" className="text-lg font-semibold text-gray-900">Memberships</h3>
        {state.membershipsError ? (
          <div className="space-y-2">
            <p role="alert" className={errorClass}>{state.membershipsError}</p>
            <button type="button" className={secondaryClass} disabled={state.busy} onClick={() => state.refreshMemberships()}>Retry memberships</button>
          </div>
        ) : !state.memberships ? <p role="status" className="text-sm text-gray-600">Loading memberships…</p> : (
          <>
            {state.memberships.items.length === 0 ? <p className="text-sm text-gray-600">No memberships found.</p> : (
              <MembershipTable memberships={state.memberships.items} personNames={state.personNames}
                busy={state.busy} tenantName={tenantName} onRevoke={state.revoke} />
            )}
            <nav aria-label="Membership pagination" className="flex flex-wrap items-center gap-3 text-sm text-gray-700">
              <button type="button" className={secondaryClass} aria-label="Previous memberships page"
                disabled={state.busy || state.page <= 1} onClick={() => state.refreshMemberships(state.page - 1)}>Previous</button>
              <span>Page {state.page} of {Math.max(1, Math.ceil(state.memberships.total / 50))} · {state.memberships.total} memberships</span>
              <button type="button" className={secondaryClass} aria-label="Next memberships page"
                disabled={state.busy || state.page * 50 >= state.memberships.total} onClick={() => state.refreshMemberships(state.page + 1)}>Next</button>
            </nav>
          </>
        )}
      </section>
    </>
  );
}

function ContactForm({ disabled, onCreate, onStart }: {
  disabled: boolean;
  onCreate: (request: Omit<MembershipPerson, 'id'>) => Promise<boolean>;
  onStart: () => void;
}) {
  const [displayName, setDisplayName] = useState('');
  const [email, setEmail] = useState('');
  const [error, setError] = useState('');
  async function submit(event: FormEvent) {
    event.preventDefault();
    if (disabled) return;
    onStart();
    const name = displayName.trim();
    const contactEmail = email.trim();
    if (!name) { setError('Display name is required.'); return; }
    if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(contactEmail)) { setError('Enter a valid contact email.'); return; }
    setError('');
    if (await onCreate({ displayName: name, email: contactEmail })) {
      setDisplayName('');
      setEmail('');
    }
  }
  return (
    <section className={panelClass} aria-labelledby="create-contact-heading">
      <h3 id="create-contact-heading" className="text-lg font-semibold text-gray-900">Create organization-local contact</h3>
      <p className="text-sm text-gray-600">This creates a Person record only. Use the separate grant form to authorize ordinary membership.</p>
      <form noValidate onSubmit={submit} className="space-y-4" aria-labelledby="create-contact-heading">
        <fieldset disabled={disabled} className="space-y-4">
          <div>
            <label htmlFor="membership-contact-name" className="text-sm font-medium text-gray-700">Display name</label>
            <input id="membership-contact-name" className={inputClass} value={displayName} required
              autoComplete="off" onChange={event => setDisplayName(event.target.value)} />
          </div>
          <div>
            <label htmlFor="membership-contact-email" className="text-sm font-medium text-gray-700">Contact email</label>
            <input id="membership-contact-email" type="email" className={inputClass} value={email} required
              autoComplete="off" onChange={event => setEmail(event.target.value)} />
          </div>
          {error && <p role="alert" className={errorClass}>{error}</p>}
          <button type="submit" className={buttonClass}>Create contact</button>
        </fieldset>
      </form>
    </section>
  );
}

function PeopleSearch({ disabled, onSearch }: { disabled: boolean; onSearch: (query: string) => void }) {
  const [query, setQuery] = useState('');
  return (
    <form className="space-y-2" role="search" aria-label="Organization-local people"
      onSubmit={event => { event.preventDefault(); if (!disabled) onSearch(query); }}>
      <label htmlFor="membership-people-search" className="text-sm font-medium text-gray-700">Search organization-local people</label>
      <input id="membership-people-search" type="search" className={inputClass} value={query} disabled={disabled}
        onChange={event => setQuery(event.target.value)} />
      <button type="submit" className={secondaryClass} disabled={disabled}>Search people</button>
    </form>
  );
}

function GrantForm({ people, disabled, onGrant, onStart }: {
  people: MembershipPerson[];
  disabled: boolean;
  onGrant: (request: GrantWorkspaceMembershipRequest) => Promise<boolean>;
  onStart: () => void;
}) {
  const [directoryTenantId, setDirectoryTenantId] = useState('');
  const [objectId, setObjectId] = useState('');
  const [personId, setPersonId] = useState('');
  const [error, setError] = useState('');
  useEffect(() => {
    if (!people.some(person => person.id === personId)) setPersonId('');
  }, [people, personId]);

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (disabled || people.length === 0) return;
    onStart();
    const directory = directoryTenantId.trim();
    const object = objectId.trim();
    if (!guid.test(directory) || directory === emptyGuid) { setError('Directory tenant ID must be a non-empty GUID.'); return; }
    if (!guid.test(object) || object === emptyGuid) { setError('Object ID must be a non-empty GUID.'); return; }
    if (!people.some(person => person.id === personId)) { setError('Select an organization-local Person.'); return; }
    setError('');
    if (await onGrant({ directoryTenantId: directory, objectId: object, personId })) {
      setDirectoryTenantId('');
      setObjectId('');
      setPersonId('');
    }
  }
  return (
    <form noValidate className="space-y-4" onSubmit={submit} aria-label="Grant ordinary membership">
      <fieldset disabled={disabled} className="space-y-4">
        <div>
          <label htmlFor="membership-directory-id" className="text-sm font-medium text-gray-700">Directory tenant ID</label>
          <input id="membership-directory-id" className={inputClass} value={directoryTenantId} required
            autoComplete="off" spellCheck={false} aria-describedby="membership-guid-help"
            onChange={event => setDirectoryTenantId(event.target.value)} />
        </div>
        <div>
          <label htmlFor="membership-object-id" className="text-sm font-medium text-gray-700">Object ID</label>
          <input id="membership-object-id" className={inputClass} value={objectId} required
            autoComplete="off" spellCheck={false} aria-describedby="membership-guid-help"
            onChange={event => setObjectId(event.target.value)} />
        </div>
        <p id="membership-guid-help" className="text-sm text-gray-600">Both IDs must be non-empty GUIDs in xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx format. The server verifies authorization.</p>
        <div>
          <label htmlFor="membership-person-id" className="text-sm font-medium text-gray-700">Organization-local Person</label>
          <select id="membership-person-id" className={inputClass} value={personId} required onChange={event => setPersonId(event.target.value)}>
            <option value="">Select a Person</option>
            {people.map(person => <option key={person.id} value={person.id}>{person.displayName} ({person.email}) — {person.id}</option>)}
          </select>
        </div>
        {error && <p role="alert" className={errorClass}>{error}</p>}
        <button type="submit" className={buttonClass} disabled={people.length === 0}>Grant membership</button>
      </fieldset>
    </form>
  );
}

function MembershipTable({ memberships, personNames, busy, tenantName, onRevoke }: {
  memberships: WorkspaceMembership[];
  personNames: Record<string, string>;
  busy: boolean;
  tenantName: string;
  onRevoke: (membership: WorkspaceMembership) => Promise<boolean>;
}) {
  return (
    <div className="overflow-x-auto">
      <table className="w-full text-left text-sm text-gray-700">
        <caption className="sr-only">Ordinary memberships for {tenantName}</caption>
        <thead className="border-b border-gray-200 bg-gray-50">
          <tr>
            {['Person association', 'Directory identity', 'State', 'Granted', 'Revoked', 'Actions'].map(label => (
              <th key={label} scope="col" className="px-3 py-2 font-medium">{label}</th>
            ))}
          </tr>
        </thead>
        <tbody>
          {memberships.map(membership => (
            <MembershipRow key={membership.id} membership={membership} personName={personNames[membership.personId]}
              busy={busy} tenantName={tenantName} onRevoke={onRevoke} />
          ))}
        </tbody>
      </table>
    </div>
  );
}

function MembershipRow({ membership, personName, busy, tenantName, onRevoke }: {
  membership: WorkspaceMembership;
  personName?: string;
  busy: boolean;
  tenantName: string;
  onRevoke: (membership: WorkspaceMembership) => Promise<boolean>;
}) {
  const [confirming, setConfirming] = useState(false);
  const revokeButton = useRef<HTMLButtonElement>(null);
  const revoked = membership.revokedAt !== null;
  const label = personName ?? membership.personId;
  return (
    <tr className="border-b border-gray-100 align-top">
      <td className="px-3 py-3">
        <p className="font-medium text-gray-900">{personName ?? 'Person not in loaded contacts'}</p>
        <p className="break-all text-xs text-gray-600">{membership.personId}</p>
      </td>
      <td className="space-y-1 px-3 py-3">
        <p className="break-all"><span className="font-medium">Directory: </span>{membership.directoryTenantId}</p>
        <p className="break-all"><span className="font-medium">Object: </span>{membership.objectId}</p>
      </td>
      <td className="px-3 py-3"><span className={revoked ? 'text-gray-600' : 'text-green-800'}>{revoked ? 'Revoked' : 'Active'}</span></td>
      <td className="px-3 py-3"><time dateTime={membership.grantedAt}>{membership.grantedAt}</time></td>
      <td className="px-3 py-3">{membership.revokedAt ? <time dateTime={membership.revokedAt}>{membership.revokedAt}</time> : 'Not revoked'}</td>
      <td className="space-y-3 px-3 py-3">
        {!revoked && (
          <>
            <button ref={revokeButton} type="button" className={secondaryClass} disabled={busy}
              aria-label={`Revoke membership for ${label} (${membership.objectId})`}
              aria-expanded={confirming} onClick={() => setConfirming(true)}>Revoke</button>
            {confirming && (
              <div role="group" aria-label={`Confirm membership revocation for ${label}`} className="min-w-56 space-y-3 rounded-md border border-amber-300 bg-amber-50 p-3">
                <p>Revoke ordinary membership for {label} in {tenantName}? RMF role administration is separate from ordinary membership.</p>
                <button autoFocus type="button" className={buttonClass} disabled={busy} onClick={async () => {
                  if (await onRevoke(membership)) setConfirming(false);
                }}>Confirm revocation</button>
                <button type="button" className={secondaryClass} disabled={busy} onClick={() => {
                  setConfirming(false);
                  revokeButton.current?.focus();
                }}>Cancel revocation</button>
              </div>
            )}
          </>
        )}
      </td>
    </tr>
  );
}
