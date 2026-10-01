import { Link, useLocation } from 'react-router-dom';
import { useEffectiveAccess } from './access';

export default function AccessRequiredPage() {
  const { access } = useEffectiveAccess();
  const location = useLocation();
  const hasSystemWorkspace = access?.destinations.some((destination) =>
    destination.workspace === 'System') ?? false;

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
