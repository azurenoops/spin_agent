import { useCallback, useEffect, useMemo, useState } from 'react';
import { rolesApi } from '../../api/roles';
import {
  RBAC_ASSIGNABLE_BY,
  RMF_ROLES,
  type ResolvedRoleAssignment,
  type RmfRole,
} from '../../types/roles';
import AssignRoleDialog from '../roles/AssignRoleDialog';

/**
 * @file Phase 4 / Feature 049 — Unified per-system role panel.
 *
 * Renders **exactly 7 rows** in the canonical {@link RMF_ROLES} order. Each row
 * surfaces the role's resolved state (`override` / `inherited` / `org-fallback`
 * / `legacy` / `not-assigned`) and offers the action affordances the FR-027
 * matrix permits for the calling user.
 *
 * Contract pins (see T034 + `contracts/frontend-types.md § 3`):
 *  - Inherited row → `<button data-action="override">Override</button>` (FR-010).
 *  - Override row  → `<button data-action="remove-override">Remove override</button>` (FR-011).
 *  - Org-fallback  → `<span data-badge="pending" title="…propagating…">Pending</span>`.
 *  - Legacy row    → `<span data-badge="legacy">Legacy</span>` (FR-024 read-side compat).
 *  - Not-assigned  → `<button data-action="assign">Assign</button>` gated by RBAC.
 *  - Every row has `data-testid="role-row-${role}"`.
 */

interface Props {
  registeredSystemId: string;
  /**
   * Caller's effective role used to gate write affordances. `null` disables all
   * write actions (server still enforces FR-027).
   */
  callerEffectiveRole: RmfRole | null;
}

const ROLE_LABELS: Record<RmfRole, string> = {
  AuthorizingOfficial: 'Authorizing Official',
  Issm: 'ISSM',
  Isso: 'ISSO',
  Sca: 'Security Control Assessor',
  SystemOwner: 'System Owner',
  MissionOwner: 'Mission Owner',
  Administrator: 'Administrator',
};

function roleBadgeColor(role: RmfRole): string {
  switch (role) {
    case 'AuthorizingOfficial': return 'bg-purple-100 text-purple-800';
    case 'Issm':                return 'bg-indigo-100 text-indigo-800';
    case 'Isso':                return 'bg-green-100 text-green-800';
    case 'Sca':                 return 'bg-amber-100 text-amber-800';
    case 'SystemOwner':         return 'bg-blue-100 text-blue-800';
    case 'MissionOwner':        return 'bg-teal-100 text-teal-800';
    case 'Administrator':       return 'bg-slate-200 text-slate-900';
  }
}

export default function RoleAssignmentPanel({ registeredSystemId, callerEffectiveRole }: Props) {
  const [rows, setRows] = useState<ResolvedRoleAssignment[]>([]);
  const [loading, setLoading] = useState(true);
  const [dialogRole, setDialogRole] = useState<RmfRole | null>(null);
  const [dialogMode, setDialogMode] = useState<'assign' | 'override'>('override');
  const [busyKey, setBusyKey] = useState<string | null>(null);
  const [errorMsg, setErrorMsg] = useState<string | null>(null);

  // ── Load + sort into canonical 7-role order ──────────────────────────────
  const load = useCallback(async () => {
    setLoading(true);
    try {
      const resp = await rolesApi.getSystemRoles(registeredSystemId);
      // Build a map and project in canonical order so the test can rely on
      // 7 stable testids even if the server permutes the array.
      const byRole = new Map(resp.roles.map((r) => [r.role, r]));
      const ordered: ResolvedRoleAssignment[] = RMF_ROLES.map(
        (role) => byRole.get(role) ?? { role, person: null, source: 'not-assigned' as const },
      );
      setRows(ordered);
    } finally {
      setLoading(false);
    }
  }, [registeredSystemId]);

  useEffect(() => { void load(); }, [load]);

  // ── RBAC affordance gate ─────────────────────────────────────────────────
  const assignable = useMemo<readonly RmfRole[]>(
    () => (callerEffectiveRole ? RBAC_ASSIGNABLE_BY[callerEffectiveRole] : []),
    [callerEffectiveRole],
  );

  const canAct = (role: RmfRole) => assignable.includes(role);

  // ── Write paths (Override + Remove-override + Assign) ────────────────────
  const handleOverride = (role: RmfRole) => {
    setErrorMsg(null);
    setDialogMode('override');
    setDialogRole(role);
  };

  const handleAssign = (role: RmfRole) => {
    setErrorMsg(null);
    setDialogMode('assign');
    setDialogRole(role);
  };

  const handleRemoveOverride = async (role: RmfRole, personId: string) => {
    setBusyKey(`${role}:remove`);
    setErrorMsg(null);
    try {
      const result = await rolesApi.removeSystemRole(registeredSystemId, role, personId);
      if (result.status === 'error') {
        setErrorMsg(result.error?.message ?? 'Remove failed.');
      } else {
        await load();
      }
    } finally {
      setBusyKey(null);
    }
  };

  const handleAssigned = async () => {
    setDialogRole(null);
    await load();
  };

  // ── Render ───────────────────────────────────────────────────────────────
  return (
    <>
      <div className="mb-6 rounded-lg border border-gray-200 bg-white p-4 shadow-sm">
        <div className="flex items-center justify-between mb-3">
          <h2 className="text-sm font-semibold text-gray-700">Team &amp; Roles</h2>
        </div>

        {errorMsg && (
          <div className="mb-3 rounded border border-red-200 bg-red-50 px-3 py-2 text-xs text-red-700">
            {errorMsg}
          </div>
        )}

        {loading ? (
          <div className="space-y-2">
            {[1, 2, 3, 4, 5, 6, 7].map((i) => (
              <div key={i} className="h-10 animate-pulse rounded bg-gray-100" />
            ))}
          </div>
        ) : (
          <div className="divide-y divide-gray-100">
            {rows.map((r) => (
              <div
                key={r.role}
                data-testid={`role-row-${r.role}`}
                className="flex items-center justify-between py-2.5"
              >
                {/* ── Identity column ─────────────────────────────────────── */}
                <div className="flex items-center gap-3 min-w-0">
                  <span className={`inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium ${roleBadgeColor(r.role)}`}>
                    {ROLE_LABELS[r.role]}
                  </span>
                  <span className="text-sm text-gray-900 truncate">
                    {r.person?.displayName ?? <span className="italic text-gray-400">unassigned</span>}
                  </span>

                  {/* Source-specific provenance badges */}
                  {r.source === 'org-fallback' && (
                    <span
                      data-badge="pending"
                      title="Inherited assignment is propagating from Organization to this system (fan-out worker)."
                      className="inline-flex items-center rounded-full bg-yellow-100 px-2 py-0.5 text-[10px] font-medium text-yellow-800"
                    >
                      Pending
                    </span>
                  )}
                  {r.source === 'inherited' && (
                    <span
                      data-badge="inherited"
                      title="Inherited from Organization assignment"
                      className="inline-flex items-center rounded-full bg-blue-50 px-2 py-0.5 text-[10px] font-medium text-blue-700"
                    >
                      Inherited
                    </span>
                  )}
                  {r.source === 'legacy' && (
                    <span
                      data-badge="legacy"
                      title="Pre-Feature-049 (legacy) per-system row — read-side compatibility."
                      className="inline-flex items-center rounded-full bg-amber-100 px-2 py-0.5 text-[10px] font-medium text-amber-800"
                    >
                      Legacy
                    </span>
                  )}
                </div>

                {/* ── Action column ───────────────────────────────────────── */}
                <div className="flex items-center gap-2">
                  {r.source === 'inherited' && canAct(r.role) && (
                    <button
                      type="button"
                      data-action="override"
                      onClick={() => handleOverride(r.role)}
                      className="rounded-md border border-indigo-200 bg-white px-2.5 py-1 text-xs font-medium text-indigo-700 hover:bg-indigo-50"
                    >
                      Override
                    </button>
                  )}
                  {r.source === 'override' && canAct(r.role) && r.person && (
                    <button
                      type="button"
                      data-action="remove-override"
                      disabled={busyKey === `${r.role}:remove`}
                      onClick={() => handleRemoveOverride(r.role, r.person!.id)}
                      className="rounded-md border border-red-200 bg-white px-2.5 py-1 text-xs font-medium text-red-700 hover:bg-red-50 disabled:opacity-50"
                    >
                      Remove override
                    </button>
                  )}
                  {r.source === 'not-assigned' && canAct(r.role) && (
                    <button
                      type="button"
                      data-action="assign"
                      onClick={() => handleAssign(r.role)}
                      className="rounded-md bg-indigo-600 px-2.5 py-1 text-xs font-medium text-white hover:bg-indigo-700"
                    >
                      Assign
                    </button>
                  )}
                </div>
              </div>
            ))}
          </div>
        )}
      </div>

      {dialogRole && (
        <AssignRoleDialog
          open
          onClose={() => setDialogRole(null)}
          scope={{ kind: 'system', registeredSystemId }}
          initialRole={dialogRole}
          lockRole
          mode={dialogMode}
          callerEffectiveRole={callerEffectiveRole}
          onAssigned={handleAssigned}
        />
      )}
    </>
  );
}
