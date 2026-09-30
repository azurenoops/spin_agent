import { useState, useEffect } from 'react';
import { Link, useParams } from '../features/workspaces/workspaceNavigation';
import RoleAssignmentPanel from '../components/cards/RoleAssignmentPanel';
import { useSystemContext } from '../components/layout/SystemLayout';
import { rolesApi } from '../api/roles';
import type { RmfRole } from '../types/roles';
import { SystemTaskColumns, SystemTaskHeading, SystemTaskSupport, systemPanel } from '../features/systems/SystemTaskPresentation';

/**
 * Epic #121 / Task #147 — Roles Management first-class page (RMF Prepare phase).
 *
 * Promotes the existing `RoleAssignmentPanel` from `SystemDetail.tsx` into a
 * dedicated routed page at `/systems/:id/roles`.
 *
 * Mirrors the same `callerEffectiveRole` fetch pattern used in `SystemDetail.tsx`
 * (Feature 049 / T040a). The panel handles its own data fetching internally;
 * this page only provides the caller-role context the panel needs for RBAC gating.
 */
export default function RolesManagementPage() {
  const { id: systemId = '' } = useParams<{ id: string }>();
  const { detail } = useSystemContext();
  const [callerEffectiveRole, setCallerEffectiveRole] = useState<RmfRole | null>(null);
  const [accessError, setAccessError] = useState<string | null>(null);
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    if (!systemId) return;
    let current = true;
    setAccessError(null);
    setCallerEffectiveRole(null);
    rolesApi
      .getEffectiveRole()
      .then(r => { if (current) setCallerEffectiveRole(r.effectiveRole ?? null); })
      .catch(reason => {
        if (current) setAccessError(reason instanceof Error ? reason.message : 'Assignment access is unavailable.');
      });
    return () => { current = false; };
  }, [systemId, attempt]);

  return (
    <div className="space-y-6">
      <SystemTaskHeading title="Team & permissions"
        description={`See effective system roles and whether they come from the organization or a system assignment for ${detail?.name ?? systemId}.`} />
      <SystemTaskColumns support={<>
        <SystemTaskSupport title="Contributes to"><p>SSP · Responsible personnel / Review routing</p></SystemTaskSupport>
        <SystemTaskSupport title="Effective assignments">
          <p>Organization assignments and explicit system overrides are shown separately. Assign only roles permitted by the current server policy.</p>
          <p>Being able to view a system does not grant approval or authorization-decision authority.</p>
        </SystemTaskSupport>
        <SystemTaskSupport title="Next in Systems">
          <Link className="block text-indigo-700 underline dark:text-indigo-300" to={`/systems/${systemId}/profile/UsersAndAccess`}>Document user populations</Link>
          <Link className="block text-indigo-700 underline dark:text-indigo-300" to={`/systems/${systemId}/inheritance/subscriptions`}>Review control responsibilities</Link>
        </SystemTaskSupport>
      </>}>
      {accessError && <div className="rounded-lg border border-amber-200 bg-amber-50 p-4 text-sm text-amber-900">
        <p role="alert">{accessError}</p>
        <button type="button" className="mt-3 rounded border px-3 py-2" onClick={() => setAttempt(value => value + 1)}>Retry assignment access</button>
      </div>}
      <div className={systemPanel}>
        <RoleAssignmentPanel
          registeredSystemId={systemId}
          callerEffectiveRole={callerEffectiveRole}
        />
      </div>
      </SystemTaskColumns>
    </div>
  );
}
