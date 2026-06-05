import { useState, useEffect } from 'react';
import { useParams } from 'react-router-dom';
import RoleAssignmentPanel from '../components/cards/RoleAssignmentPanel';
import { useSystemContext } from '../components/layout/SystemLayout';
import { rolesApi } from '../api/roles';
import type { RmfRole } from '../types/roles';

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

  useEffect(() => {
    if (!systemId) return;
    rolesApi
      .getEffectiveRole()
      .then((r) => setCallerEffectiveRole(r.effectiveRole ?? null))
      .catch(() => setCallerEffectiveRole(null));
  }, [systemId]);

  return (
    <div className="space-y-6 p-6">
      {/* Page header */}
      <div>
        <h1 className="text-2xl font-semibold text-gray-900">Roles &amp; Permissions</h1>
        <p className="mt-1 text-sm text-gray-500">
          RMF Prepare — Assign ISSO, ISSM, AO, SCA, Mission Owner, and other roles for{' '}
          <span className="font-medium">{detail?.name ?? systemId}</span>.
        </p>
      </div>

      {/* Role assignment panel — Feature 049 canonical 7-row panel */}
      <div className="rounded-lg border border-gray-200 bg-white p-6 shadow-sm">
        <RoleAssignmentPanel
          registeredSystemId={systemId}
          callerEffectiveRole={callerEffectiveRole}
        />
      </div>
    </div>
  );
}
