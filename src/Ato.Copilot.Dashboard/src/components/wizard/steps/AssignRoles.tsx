import { useState, useEffect } from 'react';
// Issue #366 — migrate from deprecated fetchRoles/assignRole to rolesApi
// (FR-008 / FR-010 unified role endpoints, Feature 049).
import { rolesApi } from '../../../api/roles';
import type { ResolvedRoleAssignment, RmfRole } from '../../../types/roles';
import apiClient from '../../../api/client';

const RMF_ROLES = [
  'AuthorizingOfficial',
  'Issm',
  'Isso',
  'Sca',
  'SystemOwner',
] as const;

const ROLE_LABELS: Record<string, string> = {
  AuthorizingOfficial: 'Authorizing Official (AO)',
  Issm: 'Information System Security Manager (ISSM)',
  Isso: 'Information System Security Officer (ISSO)',
  Sca: 'Security Control Assessor (SCA)',
  SystemOwner: 'System Owner',
};

interface PersonOption {
  id: string;
  name: string;
  personName: string;
}

interface AssignRolesProps {
  systemId: string;
  onNext: () => void;
  onErrors: (errors: Record<string, string[]>) => void;
}

export default function AssignRoles({ systemId, onNext, onErrors }: AssignRolesProps) {
  const [assignments, setAssignments] = useState<ResolvedRoleAssignment[]>([]);
  const [persons, setPersons] = useState<PersonOption[]>([]);
  const [saving, setSaving] = useState<string | null>(null);

  useEffect(() => {
    rolesApi.getSystemRoles(systemId)
      .then((res) => setAssignments(res.roles.filter((r) => r.source !== 'not-assigned')))
      .catch(() => {});
    apiClient.get('/components', { params: { type: 'Person', pageSize: 200 } })
      .then((res) => setPersons(res.data.items ?? []))
      .catch(() => {});
  }, [systemId]);

  const getAssignment = (role: string) => assignments.find((a) => a.role === role);

  const handleAssign = async (role: string, person: PersonOption) => {
    setSaving(role);
    try {
      const result = await rolesApi.assignSystemRole(systemId, {
        role: role as RmfRole,
        personId: person.id,
      });
      if (result.status === 'error') {
        onErrors({ [role]: [result.error?.message ?? 'Failed to assign role'] });
        return;
      }
      const updated = await rolesApi.getSystemRoles(systemId);
      setAssignments(updated.roles.filter((r) => r.source !== 'not-assigned'));
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to assign role';
      onErrors({ [role]: [msg] });
    } finally {
      setSaving(null);
    }
  };

  return (
    <div>
      <h2 className="text-xl font-semibold text-gray-900 mb-1">Step 5: Assign RMF Roles</h2>
      <p className="text-sm text-gray-500 mb-6">Assign personnel to standard RMF roles from existing Person components.</p>

      <div className="space-y-4">
        {RMF_ROLES.map((role) => {
          const current = getAssignment(role);
          return (
            <div key={role} className="flex items-center gap-4 rounded-md border border-gray-200 px-4 py-3">
              <div className="flex-1">
                <div className="text-sm font-medium text-gray-900">{ROLE_LABELS[role]}</div>
                {current ? (
                  <div className="text-xs text-green-600 mt-0.5">
                    Assigned to: {current.person?.displayName ?? '—'}
                  </div>
                ) : (
                  <div className="text-xs text-gray-400 mt-0.5">Unassigned</div>
                )}
              </div>
              <select
                value={current?.person?.id ?? ''}
                onChange={(e) => {
                  const person = persons.find((p) => p.id === e.target.value);
                  if (person) handleAssign(role, person);
                }}
                disabled={saving === role}
                className="rounded-md border border-gray-300 px-3 py-1.5 text-sm min-w-[200px]"
              >
                <option value="">Select person...</option>
                {persons.map((p) => (
                  <option key={p.id} value={p.id}>
                    {p.personName || p.name}
                  </option>
                ))}
              </select>
            </div>
          );
        })}
      </div>

      <div className="mt-6 flex justify-end">
        <button onClick={onNext} className="rounded-md bg-indigo-600 px-6 py-2 text-sm font-medium text-white hover:bg-indigo-700">
          Next
        </button>
      </div>
    </div>
  );
}
