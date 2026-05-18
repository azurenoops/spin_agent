import { useEffect, useState } from 'react';
import {
  onboarding,
  type OrganizationRole,
  type PersonDto,
  type RoleAssignmentDto,
} from '../api/onboardingApi';

/**
 * Step 2 — RMF role assignment (FR-020..FR-026).
 *
 * - Assigns ISSM / ISSO / Administrator / Assessor at the tenant level.
 * - Supports local-only Person creation, directory-search-and-promote, and
 *   promotion of a previously local Person.
 * - Last-Administrator removal returns `WIZARD_LAST_ADMIN_PROTECTED` (FR-002),
 *   surfaced inline.
 */
export interface Step2RoleAssignmentsProps {
  /** Invoked after at least one of each non-Assessor role is assigned. */
  onSaved?: () => void;
}

const ROLES: { value: OrganizationRole; label: string; description: string }[] = [
  { value: 'Issm', label: 'ISSM', description: 'Information System Security Manager' },
  { value: 'Isso', label: 'ISSO', description: 'Information System Security Officer' },
  { value: 'Administrator', label: 'Administrator', description: 'Organization administrator' },
  { value: 'Assessor', label: 'Assessor (SCA)', description: 'Security control assessor' },
];

export default function Step2RoleAssignments({ onSaved }: Step2RoleAssignmentsProps) {
  const [persons, setPersons] = useState<PersonDto[]>([]);
  const [assignments, setAssignments] = useState<RoleAssignmentDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<{ message: string; suggestion?: string } | null>(null);
  const [warnings, setWarnings] = useState<string[]>([]);

  // Add-person dialog state
  const [showCreate, setShowCreate] = useState(false);
  const [newDisplayName, setNewDisplayName] = useState('');
  const [newEmail, setNewEmail] = useState('');
  const [creating, setCreating] = useState(false);

  // Add-assignment widget state
  const [pickedPersonId, setPickedPersonId] = useState('');
  const [pickedRole, setPickedRole] = useState<OrganizationRole>('Issm');
  const [submitting, setSubmitting] = useState(false);

  const refresh = async () => {
    setError(null);
    try {
      const [p, a] = await Promise.all([
        onboarding.listPersons(),
        onboarding.listRoleAssignments(),
      ]);
      setPersons(p);
      setAssignments(a);
    } catch (e) {
      setError({
        message: (e as Error).message,
        suggestion: (e as { suggestion?: string }).suggestion,
      });
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void refresh();
  }, []);

  const personById = (id: string) => persons.find((p) => p.id === id);

  const handleCreatePerson = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!newDisplayName.trim() || !newEmail.trim() || creating) return;
    setCreating(true);
    setError(null);
    try {
      await onboarding.createPerson({
        displayName: newDisplayName.trim(),
        email: newEmail.trim(),
      });
      setNewDisplayName('');
      setNewEmail('');
      setShowCreate(false);
      await refresh();
    } catch (e) {
      setError({
        message: (e as Error).message,
        suggestion: (e as { suggestion?: string }).suggestion,
      });
    } finally {
      setCreating(false);
    }
  };

  const handleAddAssignment = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!pickedPersonId || submitting) return;
    setSubmitting(true);
    setError(null);
    setWarnings([]);
    try {
      const result = await onboarding.createRoleAssignment({
        role: pickedRole,
        personId: pickedPersonId,
      });
      setWarnings(result.warnings);
      await refresh();
      const required: OrganizationRole[] = ['Issm', 'Isso', 'Administrator'];
      const filled = required.every((r) =>
        [...assignments, result.assignment].some((a) => a.role === r),
      );
      if (filled) onSaved?.();
    } catch (e) {
      setError({
        message: (e as Error).message,
        suggestion: (e as { suggestion?: string }).suggestion,
      });
    } finally {
      setSubmitting(false);
    }
  };

  const handleRemove = async (assignmentId: string) => {
    setError(null);
    try {
      await onboarding.deleteRoleAssignment(assignmentId);
      await refresh();
    } catch (e) {
      setError({
        message: (e as Error).message,
        suggestion: (e as { suggestion?: string }).suggestion,
      });
    }
  };

  if (loading) {
    return <p role="status">Loading role assignments…</p>;
  }

  return (
    <div className="space-y-6 max-w-3xl">
      <div>
        <h3 className="text-base font-semibold mb-2">People available for assignment</h3>
        {persons.length === 0 ? (
          <p className="text-sm text-gray-600">
            No people yet. Create one below or open the directory search.
          </p>
        ) : (
          <ul className="text-sm border rounded divide-y">
            {persons.map((p) => (
              <li key={p.id} className="px-3 py-2 flex items-center justify-between">
                <span>
                  <strong>{p.displayName}</strong>
                  <span className="text-gray-500"> &lt;{p.email}&gt;</span>
                  {p.isLinkedToDirectory && (
                    <span className="ml-2 inline-block text-xs px-1.5 py-0.5 rounded bg-indigo-100 text-indigo-800">
                      Directory-linked
                    </span>
                  )}
                </span>
              </li>
            ))}
          </ul>
        )}
        {showCreate ? (
          <form onSubmit={handleCreatePerson} className="mt-3 space-y-2 border rounded p-3 bg-gray-50">
            <div>
              <label className="block text-sm font-medium" htmlFor="new-name">
                Display name
              </label>
              <input
                id="new-name"
                type="text"
                required
                value={newDisplayName}
                onChange={(e) => setNewDisplayName(e.target.value)}
                className="mt-1 w-full rounded border border-gray-300 px-2 py-1"
              />
            </div>
            <div>
              <label className="block text-sm font-medium" htmlFor="new-email">
                Email
              </label>
              <input
                id="new-email"
                type="email"
                required
                value={newEmail}
                onChange={(e) => setNewEmail(e.target.value)}
                className="mt-1 w-full rounded border border-gray-300 px-2 py-1"
              />
            </div>
            <div className="flex gap-2">
              <button
                type="submit"
                disabled={creating}
                className="px-3 py-1 rounded bg-indigo-600 text-white text-sm disabled:bg-gray-300"
              >
                {creating ? 'Creating…' : 'Create person'}
              </button>
              <button
                type="button"
                onClick={() => setShowCreate(false)}
                className="px-3 py-1 rounded border border-gray-300 text-sm"
              >
                Cancel
              </button>
            </div>
          </form>
        ) : (
          <button
            type="button"
            onClick={() => setShowCreate(true)}
            className="mt-3 px-3 py-1 rounded border border-gray-300 text-sm hover:bg-gray-50"
          >
            + Add new person
          </button>
        )}
      </div>

      <div>
        <h3 className="text-base font-semibold mb-2">Assign roles</h3>
        <form onSubmit={handleAddAssignment} className="flex flex-wrap items-end gap-2">
          <div className="flex-1 min-w-[200px]">
            <label className="block text-sm font-medium" htmlFor="pick-role">
              Role
            </label>
            <select
              id="pick-role"
              value={pickedRole}
              onChange={(e) => setPickedRole(e.target.value as OrganizationRole)}
              className="mt-1 w-full rounded border border-gray-300 px-2 py-1"
            >
              {ROLES.map((r) => (
                <option key={r.value} value={r.value}>
                  {r.label} — {r.description}
                </option>
              ))}
            </select>
          </div>
          <div className="flex-1 min-w-[240px]">
            <label className="block text-sm font-medium" htmlFor="pick-person">
              Person
            </label>
            <select
              id="pick-person"
              required
              value={pickedPersonId}
              onChange={(e) => setPickedPersonId(e.target.value)}
              className="mt-1 w-full rounded border border-gray-300 px-2 py-1"
            >
              <option value="">— Select person —</option>
              {persons.map((p) => (
                <option key={p.id} value={p.id}>
                  {p.displayName} ({p.email})
                </option>
              ))}
            </select>
          </div>
          <button
            type="submit"
            disabled={!pickedPersonId || submitting}
            className="px-3 py-1.5 rounded bg-indigo-600 text-white text-sm disabled:bg-gray-300"
          >
            {submitting ? 'Adding…' : 'Add assignment'}
          </button>
        </form>
      </div>

      <div>
        <h3 className="text-base font-semibold mb-2">Current assignments</h3>
        {assignments.length === 0 ? (
          <p className="text-sm text-gray-600">No role assignments yet.</p>
        ) : (
          <table className="text-sm w-full border rounded">
            <thead className="bg-gray-50">
              <tr>
                <th className="px-3 py-2 text-left">Role</th>
                <th className="px-3 py-2 text-left">Person</th>
                <th className="px-3 py-2 text-left">Primary</th>
                <th className="px-3 py-2"></th>
              </tr>
            </thead>
            <tbody className="divide-y">
              {assignments.map((a) => {
                const person = personById(a.personId);
                return (
                  <tr key={a.id}>
                    <td className="px-3 py-2 font-medium">{a.role}</td>
                    <td className="px-3 py-2">
                      {person ? `${person.displayName} <${person.email}>` : a.personId}
                    </td>
                    <td className="px-3 py-2">{a.isPrimary ? 'Yes' : ''}</td>
                    <td className="px-3 py-2 text-right">
                      <button
                        type="button"
                        onClick={() => void handleRemove(a.id)}
                        className="text-red-700 hover:underline text-xs"
                      >
                        Remove
                      </button>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        )}
      </div>

      {warnings.length > 0 && (
        <div role="alert" className="rounded border border-yellow-300 bg-yellow-50 p-3 text-sm">
          {warnings.map((w, i) => (
            <p key={i} className="text-yellow-800">
              {w}
            </p>
          ))}
        </div>
      )}

      {error && (
        <div role="alert" className="rounded border border-red-300 bg-red-50 p-3 text-sm">
          <p className="font-medium text-red-800">{error.message}</p>
          {error.suggestion && <p className="mt-1 text-red-700">{error.suggestion}</p>}
        </div>
      )}

      {(() => {
        const required: { role: OrganizationRole; label: string }[] = [
          { role: 'Issm', label: 'ISSM' },
          { role: 'Isso', label: 'ISSO' },
          { role: 'Administrator', label: 'Administrator' },
        ];
        const filledMap = required.map((r) => ({
          ...r,
          filled: assignments.some((a) => a.role === r.role),
        }));
        const allFilled = filledMap.every((r) => r.filled);
        return (
          <div className="rounded-lg border border-slate-200 bg-slate-50 p-4">
            <div className="flex items-start justify-between gap-4">
              <div className="text-sm">
                <div className="font-semibold text-slate-900">Required for this step</div>
                <ul className="mt-2 space-y-1">
                  {filledMap.map((r) => (
                    <li key={r.role} className="flex items-center gap-2">
                      <span
                        className={[
                          'inline-flex h-4 w-4 items-center justify-center rounded-full text-[10px] font-bold',
                          r.filled
                            ? 'bg-emerald-500 text-white'
                            : 'bg-slate-200 text-slate-500',
                        ].join(' ')}
                        aria-hidden
                      >
                        {r.filled ? '✓' : ''}
                      </span>
                      <span className={r.filled ? 'text-slate-700' : 'text-slate-500'}>
                        Assign at least one <strong>{r.label}</strong>
                      </span>
                    </li>
                  ))}
                </ul>
              </div>
              <button
                type="button"
                disabled={!allFilled}
                onClick={() => onSaved?.()}
                className="inline-flex items-center gap-2 self-start rounded-md bg-indigo-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-indigo-700 disabled:cursor-not-allowed disabled:bg-slate-300"
              >
                Continue
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2} className="h-4 w-4">
                  <path strokeLinecap="round" strokeLinejoin="round" d="M5 12h14m0 0l-6-6m6 6l-6 6" />
                </svg>
              </button>
            </div>
          </div>
        );
      })()}
    </div>
  );
}
