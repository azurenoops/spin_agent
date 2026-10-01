import { useEffect, type ReactNode } from 'react';
import { Navigate, useLocation } from '../workspaces/workspaceNavigation';
import { useEffectiveAccess } from './access';

export interface AdminAccessGateProps {
  action?: string;
  children: ReactNode;
}

export default function AdminAccessGate({ action, children }: AdminAccessGateProps) {
  const location = useLocation();
  const {
    access,
    selectedDestination,
    isLoading,
    error,
    selectDestination,
    reload,
  } = useEffectiveAccess();

  const permitsAction = (destination: NonNullable<typeof selectedDestination>) =>
    destination.workspace === 'Administration'
      && (!action || destination.actions.includes(action));
  const authorizedDestination =
    (selectedDestination && permitsAction(selectedDestination) ? selectedDestination : null)
    ?? access?.destinations.find((destination) =>
      destination.scopeId === access.subject.tenantId && permitsAction(destination))
    ?? access?.destinations.find(permitsAction)
    ?? null;

  useEffect(() => {
    if (authorizedDestination && selectedDestination?.id !== authorizedDestination.id) {
      void selectDestination(authorizedDestination.id);
    }
  }, [authorizedDestination, selectedDestination?.id, selectDestination]);

  if (isLoading) {
    return (
      <div className="flex min-h-screen items-center justify-center bg-slate-50" role="status">
        <div className="text-center">
          <div className="mx-auto h-8 w-8 animate-spin rounded-full border-2 border-indigo-200 border-t-indigo-600" />
          <p className="mt-3 text-sm text-slate-600">Verifying administrative access…</p>
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="flex min-h-screen items-center justify-center bg-slate-50 px-6">
        <div className="max-w-md rounded-xl border border-red-200 bg-white p-6 text-center shadow-sm">
          <h1 className="text-xl font-semibold text-slate-900">Access could not be verified</h1>
          <p className="mt-2 text-sm text-slate-600">
            Restricted content was not loaded. Retry after checking your connection.
          </p>
          <button
            type="button"
            onClick={reload}
            className="mt-5 rounded-md bg-indigo-600 px-4 py-2 text-sm font-semibold text-white hover:bg-indigo-700"
          >
            Retry
          </button>
        </div>
      </div>
    );
  }

  if (!authorizedDestination) {
    return (
      <Navigate
        to="/access-required"
        replace
        state={{ from: location.pathname, reason: 'administrative-access-required' }}
      />
    );
  }

  return <>{children}</>;
}
