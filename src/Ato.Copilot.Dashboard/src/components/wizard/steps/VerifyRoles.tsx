import { useState, useEffect } from 'react';
import { fetchRoles } from '../../../api/roles';
import type { RoleAssignment } from '../../../api/roles';

const ROLE_LABELS: Record<string, string> = {
  AuthorizingOfficial: 'Authorizing Official (AO)',
  Issm: 'Information System Security Manager (ISSM)',
  Isso: 'Information System Security Officer (ISSO)',
  Sca: 'Security Control Assessor (SCA)',
  SystemOwner: 'System Owner',
};

interface VerifyRolesProps {
  systemId: string;
  onNext: () => void;
}

export default function VerifyRoles({ systemId, onNext }: VerifyRolesProps) {
  const [assignments, setAssignments] = useState<RoleAssignment[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    fetchRoles(systemId)
      .then(setAssignments)
      .catch(() => {})
      .finally(() => setLoading(false));
  }, [systemId]);

  return (
    <div>
      <h2 className="text-xl font-semibold text-gray-900 mb-1">Step 6: Verify Roles</h2>
      <p className="text-sm text-gray-500 mb-6">Review role assignments before proceeding.</p>

      {loading ? (
        <p className="text-sm text-gray-500">Loading role assignments...</p>
      ) : assignments.length === 0 ? (
        <div className="rounded-md border border-amber-200 bg-amber-50 p-4 text-sm text-amber-700">
          No roles assigned. You can go back to Step 5 to assign roles, or continue.
        </div>
      ) : (
        <div className="overflow-hidden rounded-md border border-gray-200">
          <table className="min-w-full divide-y divide-gray-200">
            <thead className="bg-gray-50">
              <tr>
                <th className="px-4 py-3 text-left text-xs font-medium uppercase text-gray-500">Role</th>
                <th className="px-4 py-3 text-left text-xs font-medium uppercase text-gray-500">Assigned Person</th>
                <th className="px-4 py-3 text-left text-xs font-medium uppercase text-gray-500">Assignment Date</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-100">
              {assignments.map((a) => (
                <tr key={a.id}>
                  <td className="px-4 py-3 text-sm font-medium text-gray-900">
                    {ROLE_LABELS[a.role] ?? a.role}
                  </td>
                  <td className="px-4 py-3 text-sm text-gray-600">
                    {a.userDisplayName ?? a.userId}
                  </td>
                  <td className="px-4 py-3 text-sm text-gray-500">
                    {new Date(a.assignedAt).toLocaleDateString()}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      <div className="mt-6 flex justify-end">
        <button onClick={onNext} className="rounded-md bg-indigo-600 px-6 py-2 text-sm font-medium text-white hover:bg-indigo-700">
          Next
        </button>
      </div>
    </div>
  );
}
