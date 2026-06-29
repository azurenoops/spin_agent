import { useEffect, useMemo, useState } from 'react';
import { rolesApi } from '../../api/roles';
import { onboarding, type PersonDto } from '../../features/onboarding/api/onboardingApi';
import {
  RBAC_ASSIGNABLE_BY,
  RMF_ROLES,
  type AssignmentResult,
  type RmfRole,
  type SoDWarning,
} from '../../types/roles';

/**
 * @file Feature 049 / Phase 4 — Shared dialog for the unified role write paths.
 *
 * Mounted in three places (FR-014, per `contracts/frontend-types.md § 2`):
 *  1. Mission Owner banner (`scope.kind === 'organization'`, `lockRole=true`).
 *  2. {@link RoleAssignmentPanel} (`scope.kind === 'system'`).
 *  3. Wizard Step 2 — `scope.kind === 'organization'` with `bootstrap=true`.
 *
 * The dialog displays SoD warnings inline (FR-026) and surfaces server-side
 * RBAC denials (FR-027) without retrying.
 */

export type AssignRoleScope =
  | { kind: 'organization' }
  | { kind: 'system'; registeredSystemId: string };

export interface AssignRoleDialogProps {
  open: boolean;
  onClose: () => void;
  scope: AssignRoleScope;
  /** Pre-selects a role; combined with `lockRole` to pin the choice. */
  initialRole?: RmfRole;
  /** Disables the role dropdown — used by the Mission Owner banner CTA. */
  lockRole?: boolean;
  /**
   * Caller's effective role, used to filter the role dropdown via
   * {@link RBAC_ASSIGNABLE_BY}. `null` means "no effective role" — only the
   * Wizard bootstrap path may proceed in that case (FR-029).
   */
  callerEffectiveRole: RmfRole | null;
  /**
   * Wizard bootstrap flag — propagated into the Org-role POST body. The server
   * ignores it when ≥1 active Organization role exists for the tenant.
   */
  bootstrap?: boolean;
  onAssigned: (result: AssignmentResult) => void;
}

/** Choices visible in the role dropdown — gated by the FR-027 matrix. */
function allowedRoles(callerEffectiveRole: RmfRole | null, lockRole: boolean, initialRole?: RmfRole): readonly RmfRole[] {
  // When locked, only the initial role is selectable (used by banner / wizard).
  if (lockRole && initialRole) return [initialRole];
  // No caller role → only the bootstrap path is usable; treat as "all 7" so the
  // wizard can pick Administrator. Server still validates.
  if (callerEffectiveRole === null) return RMF_ROLES;
  return RBAC_ASSIGNABLE_BY[callerEffectiveRole];
}

export default function AssignRoleDialog(props: AssignRoleDialogProps) {
  const {
    open, onClose, scope, initialRole, lockRole = false,
    callerEffectiveRole, bootstrap = false, onAssigned,
  } = props;

  const roleOptions = useMemo(
    () => allowedRoles(callerEffectiveRole, lockRole, initialRole),
    [callerEffectiveRole, lockRole, initialRole],
  );

  const [role, setRole] = useState<RmfRole>(initialRole ?? roleOptions[0] ?? 'Issm');
  const [personId, setPersonId] = useState('');
  const [persons, setPersons] = useState<PersonDto[]>([]);
  const [saving, setSaving] = useState(false);
  const [warnings, setWarnings] = useState<SoDWarning[] | null>(null);
  const [errorMsg, setErrorMsg] = useState<string | null>(null);

  // Fix #563: load Person records so users can pick by name instead of typing a GUID
  useEffect(() => {
    if (!open) return;
    onboarding.listPersons()
      .then((data) => setPersons(data))
      .catch(() => { /* non-fatal — falls back to manual GUID input */ });
  }, [open]);

  if (!open) return null;

  const canSubmit = personId.trim().length > 0 && !saving;

  const handleSubmit = async () => {
    setSaving(true);
    setWarnings(null);
    setErrorMsg(null);
    let result: AssignmentResult;
    if (scope.kind === 'organization') {
      result = await rolesApi.assignOrgRole({ role, personId: personId.trim(), bootstrap });
    } else {
      result = await rolesApi.assignSystemRole(scope.registeredSystemId, {
        role,
        personId: personId.trim(),
      });
    }
    setSaving(false);

    if (result.status === 'success') {
      if (result.warnings && result.warnings.length > 0) {
        // Surface the SoD warning inline; the write still succeeded.
        setWarnings(result.warnings);
      }
      onAssigned(result);
      // If there's a warning, keep the dialog open so the user can read it.
      if (!result.warnings || result.warnings.length === 0) {
        onClose();
      }
      return;
    }

    // Server-side error path — surface inline without retrying.
    setErrorMsg(result.error?.message ?? 'Assignment failed.');
    onAssigned(result);
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40" role="dialog" aria-modal>
      <div className="w-full max-w-md rounded-xl bg-white p-6 shadow-xl">
        <h3 className="text-lg font-semibold text-gray-900 mb-4">
          {scope.kind === 'organization' ? 'Assign Organization Role' : 'Assign Per-System Role Override'}
        </h3>

        <div className="space-y-4">
          <div>
            <label htmlFor="ard-role" className="block text-sm font-medium text-gray-700 mb-1">
              Role
            </label>
            <select
              id="ard-role"
              value={role}
              disabled={lockRole}
              onChange={(e) => setRole(e.target.value as RmfRole)}
              className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500 disabled:bg-gray-50 disabled:text-gray-500"
            >
              {roleOptions.map((r) => (
                <option key={r} value={r}>{r}</option>
              ))}
            </select>
          </div>

          <div>
            <label htmlFor="ard-person" className="block text-sm font-medium text-gray-700 mb-1">
              Person
            </label>
            {/* Fix #563: show person picker dropdown when Person records exist */}
            {persons.length > 0 ? (
              <select
                id="ard-person"
                value={personId}
                onChange={(e) => {
                  setPersonId(e.target.value);
                }}
                className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500"
              >
                <option value="">Select a person…</option>
                {persons.map((p) => (
                  <option key={p.id} value={p.id}>{p.displayName}</option>
                ))}
              </select>
            ) : (
              <input
                id="ard-person"
                type="text"
                value={personId}
                onChange={(e) => setPersonId(e.target.value)}
                placeholder="GUID of the Person record"
                className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500"
              />
            )}
            {persons.length === 0 && (
              <p className="mt-1 text-xs text-amber-700">
                No Person records found. Add personnel in the Onboarding wizard first, or enter a Person GUID manually.
              </p>
            )}
          </div>

          {warnings && warnings.length > 0 && (
            <div role="alert" className="rounded-md border border-amber-300 bg-amber-50 px-3 py-2 text-sm text-amber-900">
              <div className="font-semibold mb-1">Separation-of-duties warning</div>
              {warnings.map((w, i) => (
                <div key={i} className="space-y-1">
                  <div>{w.message}</div>
                  <div className="text-xs text-amber-700">{w.dodiReference}</div>
                  <div className="text-xs text-amber-700">{w.suggestedAction}</div>
                </div>
              ))}
            </div>
          )}

          {errorMsg && (
            <div role="alert" className="rounded-md border border-red-300 bg-red-50 px-3 py-2 text-sm text-red-900">
              {errorMsg}
            </div>
          )}
        </div>

        <div className="mt-6 flex justify-end gap-3">
          <button
            type="button"
            onClick={onClose}
            className="rounded-lg border border-gray-300 px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50"
          >
            Close
          </button>
          <button
            type="button"
            onClick={handleSubmit}
            disabled={!canSubmit}
            className="rounded-lg bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-700 disabled:opacity-50"
          >
            {saving ? 'Assigning…' : 'Assign'}
          </button>
        </div>
      </div>
    </div>
  );
}
