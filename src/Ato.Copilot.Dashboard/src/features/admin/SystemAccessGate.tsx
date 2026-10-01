import { type ReactNode } from 'react';
import { Navigate, useLocation, useParams } from 'react-router-dom';
import { useEffectiveAccess } from './access';

export default function SystemAccessGate({ children }: { children: ReactNode }) {
  const { id } = useParams<{ id: string }>();
  const location = useLocation();
  const { access, isLoading, error, reload } = useEffectiveAccess();

  if (isLoading) {
    return <div className="p-10 text-center text-sm text-slate-600" role="status">Verifying system access…</div>;
  }
  if (error) {
    return (
      <div className="p-10 text-center">
        <p className="text-sm text-red-700">System access could not be verified.</p>
        <button type="button" onClick={reload} className="mt-4 rounded bg-indigo-600 px-4 py-2 text-sm text-white">Retry</button>
      </div>
    );
  }

  const permitted = access?.destinations.some((destination) =>
    destination.workspace === 'System' && destination.scopeId === id) ?? false;
  if (!permitted) {
    return <Navigate to="/access-required" replace state={{ from: location.pathname }} />;
  }
  return <>{children}</>;
}

export function AnySystemAccessGate({ children }: { children: ReactNode }) {
  const location = useLocation();
  const { access, isLoading, error, reload } = useEffectiveAccess();
  if (isLoading) return <div className="p-10 text-center text-sm text-slate-600" role="status">Verifying system access…</div>;
  if (error) {
    return <div className="p-10 text-center"><button type="button" onClick={reload}>Retry access check</button></div>;
  }
  if (!access?.destinations.some((destination) => destination.workspace === 'System')) {
    return <Navigate to="/access-required" replace state={{ from: location.pathname }} />;
  }
  return <>{children}</>;
}
