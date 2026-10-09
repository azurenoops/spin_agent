import { useEffect, useState } from 'react';
import { useNavigate, useParams } from '../workspaces/workspaceNavigation';
import * as api from './providerAccessApi';

export default function ProviderInvitationPage() {
  const { invitationId = '' } = useParams();
  const navigate = useNavigate();
  const [invitation, setInvitation] = useState<api.ProviderInvitationView | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    const controller = new AbortController();
    api.getInvitation(invitationId)
      .then(value => { if (!controller.signal.aborted) setInvitation(value); })
      .catch(reason => { if (!controller.signal.aborted) setError(reason instanceof Error ? reason.message : 'Invitation is unavailable.'); });
    return () => controller.abort();
  }, [invitationId]);

  const accept = async () => {
    setBusy(true);
    setError(null);
    try {
      const result = await api.acceptInvitation(invitationId);
      navigate(result.destination, { replace: true });
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Invitation acceptance failed.');
    } finally {
      setBusy(false);
    }
  };

  return <main className="mx-auto max-w-3xl px-5 py-12">
    <p className="text-xs font-semibold uppercase tracking-widest text-indigo-700">Provider invitation</p>
    <h1 className="mt-3 text-3xl font-semibold">Confirm your invitation</h1>
    <p className="mt-3 text-slate-600">Confirm identity, membership, scope, and requested roles. Provider registration will not be repeated.</p>
    {error && <div role="alert" className="mt-6 rounded-lg border border-red-200 bg-red-50 p-4 text-red-800">{error}</div>}
    {!invitation && !error && <p role="status" className="mt-6">Loading invitation…</p>}
    {invitation && <section className="mt-8 rounded-xl border border-slate-200 bg-white p-6">
      <dl className="grid gap-x-6 gap-y-4 text-sm sm:grid-cols-2">
        <dt className="text-slate-500">Authenticated identity</dt><dd>{invitation.authenticatedIdentity}</dd>
        <dt className="text-slate-500">Invitation target</dt><dd>{invitation.targetIdentity}</dd>
        <dt className="text-slate-500">Provider</dt><dd>{invitation.providerName}</dd>
        <dt className="text-slate-500">Offering</dt><dd>{invitation.offeringName || 'Provider-wide scope'}</dd>
        <dt className="text-slate-500">Requested roles</dt><dd>{invitation.requestedRoles.join(', ') || 'Membership only'}</dd>
        <dt className="text-slate-500">Requested scope</dt><dd>{invitation.requestedScope}</dd>
      </dl>
      <p className="mt-6 rounded-lg bg-slate-50 p-4 text-sm text-slate-600">
        Invitation, directory identity, membership, and role grants remain separate audited records. Recording a contact does not grant access.
      </p>
      <button type="button" disabled={busy || invitation.status !== 'Pending'} onClick={() => void accept()}
        className="mt-6 rounded-lg bg-indigo-600 px-5 py-3 text-sm font-semibold text-white disabled:opacity-50">
        {busy ? 'Confirming…' : 'Confirm membership and assigned access'}
      </button>
    </section>}
  </main>;
}
