import { useNavigate } from 'react-router-dom';
import { useEffectiveAccess } from './access';

export default function WorkspaceChooserPage() {
  const { access, isLoading, error, selectDestination, reload } = useEffectiveAccess();
  const navigate = useNavigate();

  if (isLoading) {
    return <div className="p-10 text-center text-sm text-slate-600" role="status">Loading authorized workspaces…</div>;
  }
  if (error) {
    return (
      <div className="p-10 text-center">
        <p className="text-sm text-red-700">Workspaces could not be verified.</p>
        <button type="button" onClick={reload} className="mt-4 rounded bg-indigo-600 px-4 py-2 text-sm text-white">
          Retry
        </button>
      </div>
    );
  }
  if (!access || access.destinations.length === 0) {
    return <AccessRequiredPage />;
  }

  const choose = async (destinationId: string) => {
    const destination = access.destinations.find((item) => item.id === destinationId);
    if (!destination || !await selectDestination(destinationId)) return;
    navigate(destination.workspace === 'Administration'
      ? '/administration'
      : `/systems/${destination.scopeId}`);
  };

  return (
    <main className="min-h-screen bg-slate-50 px-6 py-12">
      <section className="mx-auto max-w-5xl">
        <p className="text-sm font-semibold uppercase tracking-wide text-indigo-600">SPIN</p>
        <h1 className="mt-2 text-3xl font-semibold text-slate-950">Choose a workspace</h1>
        <p className="mt-2 text-slate-600">
          Each workspace uses only the permissions and scope verified by the server.
        </p>
        <div className="mt-8 grid gap-4 md:grid-cols-2">
          {access.destinations.map((destination) => (
            <button
              key={destination.id}
              type="button"
              onClick={() => { void choose(destination.id); }}
              className="rounded-xl border border-slate-200 bg-white p-6 text-left shadow-sm transition hover:border-indigo-300 hover:shadow-md focus:outline-none focus:ring-2 focus:ring-indigo-500"
            >
              <span className="text-xs font-semibold uppercase tracking-wide text-indigo-600">
                {destination.workspace}
              </span>
              <h2 className="mt-2 text-lg font-semibold text-slate-900">{destination.displayName}</h2>
              <p className="mt-1 text-sm text-slate-500">{destination.scopeKind} scope</p>
              <div className="mt-4 flex flex-wrap gap-2">
                {destination.badges.map((badge) => (
                  <span key={`${badge.source}:${badge.label}`} className="rounded-full bg-slate-100 px-2.5 py-1 text-xs text-slate-700">
                    {badge.label}
                  </span>
                ))}
              </div>
            </button>
          ))}
        </div>
      </section>
    </main>
  );
}

import AccessRequiredPage from './AccessRequiredPage';
