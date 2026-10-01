import { Navigate } from 'react-router-dom';
import PortfolioRoute from '../../pages/PortfolioRoute';
import { useEffectiveAccess } from './access';

export default function HomeResolver() {
  const { access, isLoading, error, reload } = useEffectiveAccess();

  if (isLoading) {
    return <div className="p-10 text-center text-sm text-slate-600" role="status">Resolving your authorized workspace…</div>;
  }
  if (error) {
    return (
      <div className="p-10 text-center">
        <p className="text-sm text-red-700">Your effective access could not be verified.</p>
        <button type="button" onClick={reload} className="mt-4 rounded bg-indigo-600 px-4 py-2 text-sm text-white">Retry</button>
      </div>
    );
  }

  const destinations = access?.destinations ?? [];
  if (destinations.length === 0) return <Navigate to="/access-required" replace />;
  if (destinations.length > 1) return <Navigate to="/workspace" replace />;

  const destination = destinations[0]!;
  if (destination.workspace === 'Administration') {
    return <Navigate to="/administration" replace />;
  }

  // Preserve the existing mission experience for organization-wide RMF users.
  // A system-specific assignment is sent directly to that system so the
  // portfolio cannot request unrelated tenant-wide data.
  const hasOrganizationScopedRmfRole = destination.badges.some((badge) =>
    badge.source === 'OrganizationRoleAssignment');
  return hasOrganizationScopedRmfRole
    ? <PortfolioRoute />
    : <Navigate to={`/systems/${destination.scopeId}`} replace />;
}
