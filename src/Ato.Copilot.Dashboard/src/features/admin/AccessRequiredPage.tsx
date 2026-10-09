import { useEffect, useState } from 'react';
import { Link, useLocation } from 'react-router-dom';
import { useEffectiveAccess } from './access';
import * as providerAccessApi from '../csp-onboarding/providerAccessApi';

export default function AccessRequiredPage() {
  const { access } = useEffectiveAccess();
  const location = useLocation();
  const [request, setRequest] = useState<providerAccessApi.ProviderAccessRequestView | null>(null);
  const [justification, setJustification] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const hasSystemWorkspace = access?.destinations.some((destination) =>
    destination.workspace === 'System') ?? false;

  useEffect(() => {
    providerAccessApi.getCurrentAccessRequest()
      .then(setRequest)
      .catch(reason => setError(reason instanceof Error ? reason.message : 'Access request status is unavailable.'));
  }, []);

  const submit = async () => {
    if (!justification.trim()) {
      setError('Explain the access needed before submitting.');
      return;
    }
    setBusy(true);
    setError(null);
    try {
      setRequest(await providerAccessApi.createAccessRequest(justification.trim()));
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Access request could not be submitted.');
    } finally {
      setBusy(false);
    }
  };

  return (
    <main className="flex min-h-screen items-center justify-center bg-slate-50 px-6">
      <section className="w-full max-w-lg rounded-xl border border-slate-200 bg-white p-8 shadow-sm">
        <div className="mb-5 flex h-12 w-12 items-center justify-center rounded-full bg-amber-100 text-amber-700" aria-hidden="true">
          <span className="text-xl font-bold">!</span>
        </div>
        <h1 className="text-2xl font-semibold text-slate-950">Access required</h1>
        <p className="mt-3 text-sm leading-6 text-slate-600">
          Your authenticated account does not currently have permission for this destination.
          No restricted data was loaded.
        </p>
        {location.state && (
          <p className="mt-2 text-xs text-slate-500">
            Ask an authorized administrator to verify your active membership or assignment.
          </p>
        )}
        {error && <div role="alert" className="mt-5 rounded-lg border border-red-200 bg-red-50 p-4 text-sm text-red-800">{error}</div>}
        {request ? <div role="status" className="mt-6 rounded-lg border border-indigo-200 bg-indigo-50 p-4 text-sm">
          <strong>Provider access request: {request.status}</strong>
          <p className="mt-2">{request.justification}</p>
          {request.decisionReason && <p className="mt-2">Decision reason: {request.decisionReason}</p>}
        </div> : <section className="mt-6 rounded-lg border border-slate-200 p-4">
          <h2 className="font-semibold">Request provider access</h2>
          <label className="mt-4 grid gap-2 text-sm font-medium">Reason for access
            <textarea value={justification} onChange={event => setJustification(event.target.value)}
              className="min-h-28 rounded-lg border border-slate-300 p-3" />
          </label>
          <button type="button" disabled={busy} onClick={() => void submit()}
            className="mt-4 rounded-md bg-indigo-600 px-4 py-2 text-sm font-semibold text-white disabled:opacity-50">
            {busy ? 'Submitting…' : 'Submit access request'}
          </button>
        </section>}
        <div className="mt-6 flex gap-3">
          {hasSystemWorkspace && (
            <Link
              to="/workspace"
              className="rounded-md bg-indigo-600 px-4 py-2 text-sm font-semibold text-white hover:bg-indigo-700"
            >
              Choose workspace
            </Link>
          )}
          <Link
            to="/"
            className="rounded-md border border-slate-300 px-4 py-2 text-sm font-semibold text-slate-700 hover:bg-slate-50"
          >
            Return home
          </Link>
        </div>
      </section>
    </main>
  );
}
