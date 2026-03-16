import { useState, useCallback, useEffect } from 'react';
import { fetchRoles, assignRole, deleteRole } from '../../api/roles';
import type { RoleAssignment } from '../../api/roles';

const ROLE_OPTIONS = [
  { value: 'AuthorizingOfficial', label: 'Authorizing Official (AO)' },
  { value: 'Issm', label: 'ISSM' },
  { value: 'Isso', label: 'ISSO' },
  { value: 'Sca', label: 'SCA' },
  { value: 'SystemOwner', label: 'System Owner' },
];

function roleBadgeColor(role: string): string {
  switch (role) {
    case 'AuthorizingOfficial': return 'bg-purple-100 text-purple-800';
    case 'Issm': return 'bg-blue-100 text-blue-800';
    case 'Isso': return 'bg-green-100 text-green-800';
    case 'Sca': return 'bg-amber-100 text-amber-800';
    case 'SystemOwner': return 'bg-indigo-100 text-indigo-800';
    default: return 'bg-gray-100 text-gray-800';
  }
}

function roleLabel(role: string): string {
  return ROLE_OPTIONS.find((r) => r.value === role)?.label ?? role;
}

interface Props {
  systemId: string;
}

export default function RoleAssignmentPanel({ systemId }: Props) {
  const [roles, setRoles] = useState<RoleAssignment[]>([]);
  const [loading, setLoading] = useState(true);
  const [dialogOpen, setDialogOpen] = useState(false);
  const [formRole, setFormRole] = useState(ROLE_OPTIONS[0]!.value);
  const [formName, setFormName] = useState('');
  const [saving, setSaving] = useState(false);
  const [deletingId, setDeletingId] = useState<string | null>(null);

  const load = useCallback(async () => {
    try {
      const data = await fetchRoles(systemId);
      setRoles(data);
    } finally {
      setLoading(false);
    }
  }, [systemId]);

  useEffect(() => { load(); }, [load]);

  const handleAssign = async () => {
    if (!formName.trim()) return;
    setSaving(true);
    try {
      await assignRole(systemId, { role: formRole, userDisplayName: formName.trim() });
      setFormName('');
      setFormRole(ROLE_OPTIONS[0]!.value);
      setDialogOpen(false);
      await load();
    } finally {
      setSaving(false);
    }
  };

  const handleDelete = async (roleId: string) => {
    setDeletingId(roleId);
    try {
      await deleteRole(systemId, roleId);
      await load();
    } finally {
      setDeletingId(null);
    }
  };

  return (
    <>
      <div className="mb-6 rounded-lg border border-gray-200 bg-white p-4 shadow-sm">
        <div className="flex items-center justify-between mb-3">
          <h2 className="text-sm font-semibold text-gray-700">Team & Roles</h2>
          <button
            type="button"
            onClick={() => setDialogOpen(true)}
            className="rounded-md bg-blue-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-blue-700 transition-colors"
          >
            + Assign Role
          </button>
        </div>

        {loading ? (
          <div className="space-y-2">
            {[1, 2].map((i) => (
              <div key={i} className="h-10 animate-pulse rounded bg-gray-100" />
            ))}
          </div>
        ) : roles.length === 0 ? (
          <p className="text-sm text-gray-500">No roles assigned yet.</p>
        ) : (
          <div className="divide-y divide-gray-100">
            {roles.map((r) => (
              <div key={r.id} className="flex items-center justify-between py-2.5">
                <div className="flex items-center gap-3 min-w-0">
                  <span className={`inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium ${roleBadgeColor(r.role)}`}>
                    {roleLabel(r.role)}
                  </span>
                  <span className="text-sm text-gray-900 truncate">{r.userDisplayName ?? r.userId}</span>
                </div>
                <button
                  type="button"
                  onClick={() => handleDelete(r.id)}
                  disabled={deletingId === r.id}
                  className="text-gray-400 hover:text-red-600 transition-colors disabled:opacity-50"
                  title="Remove assignment"
                >
                  <svg className="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
                    <path strokeLinecap="round" strokeLinejoin="round" d="M14.74 9l-.346 9m-4.788 0L9.26 9m9.968-3.21c.342.052.682.107 1.022.166m-1.022-.165L18.16 19.673a2.25 2.25 0 01-2.244 2.077H8.084a2.25 2.25 0 01-2.244-2.077L4.772 5.79m14.456 0a48.108 48.108 0 00-3.478-.397m-12 .562c.34-.059.68-.114 1.022-.165m0 0a48.11 48.11 0 013.478-.397m7.5 0v-.916c0-1.18-.91-2.164-2.09-2.201a51.964 51.964 0 00-3.32 0c-1.18.037-2.09 1.022-2.09 2.201v.916m7.5 0a48.667 48.667 0 00-7.5 0" />
                  </svg>
                </button>
              </div>
            ))}
          </div>
        )}
      </div>

      {/* Assign Role Dialog */}
      {dialogOpen && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40">
          <div className="w-full max-w-md rounded-xl bg-white p-6 shadow-xl">
            <h3 className="text-lg font-semibold text-gray-900 mb-4">Assign RMF Role</h3>
            <div className="space-y-4">
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">Role</label>
                <select
                  value={formRole}
                  onChange={(e) => setFormRole(e.target.value)}
                  className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm focus:border-blue-500 focus:ring-1 focus:ring-blue-500"
                >
                  {ROLE_OPTIONS.map((o) => (
                    <option key={o.value} value={o.value}>{o.label}</option>
                  ))}
                </select>
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">Display Name</label>
                <input
                  type="text"
                  value={formName}
                  onChange={(e) => setFormName(e.target.value)}
                  placeholder="e.g. Jane Smith"
                  className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm focus:border-blue-500 focus:ring-1 focus:ring-blue-500"
                />
              </div>
            </div>
            <div className="mt-6 flex justify-end gap-3">
              <button
                type="button"
                onClick={() => setDialogOpen(false)}
                className="rounded-lg border border-gray-300 px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50"
              >
                Cancel
              </button>
              <button
                type="button"
                onClick={handleAssign}
                disabled={!formName.trim() || saving}
                className="rounded-lg bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50"
              >
                {saving ? 'Assigning…' : 'Assign'}
              </button>
            </div>
          </div>
        </div>
      )}
    </>
  );
}
