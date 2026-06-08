import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { onboarding } from '../features/onboarding/api/onboardingApi';
import type { AzureSubscriptionRegistrationDto } from '../features/onboarding/api/onboardingApi';

/**
 * Epic #215 / Task T001 — Admin Azure subscription settings page at /admin/azure-settings.
 * Also served at the legacy /settings/azure-subscriptions alias (Task #293).
 *
 * Provides a persistent management surface for reviewing, registering, and removing
 * Azure subscription registrations. Guarded by OnboardingAdministratorRequirement on the
 * backend; frontend shows an inline 403 banner if the caller lacks admin rights (T003).
 *
 * Uses onboarding.listAzureRegistrations, onboarding.putAzureRegistrations, and
 * onboarding.removeAzureRegistration.
 */
export default function AzureSettingsPage() {
  const [registrations, setRegistrations] = useState<AzureSubscriptionRegistrationDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [accessDenied, setAccessDenied] = useState(false);
  const [success, setSuccess] = useState<string | null>(null);

  const [newSubId, setNewSubId] = useState('');
  const [registering, setRegistering] = useState(false);

  const [deleteTarget, setDeleteTarget] = useState<AzureSubscriptionRegistrationDto | null>(null);
  const [deleting, setDeleting] = useState(false);

  async function loadRegistrations() {
    setLoading(true);
    setError(null);
    setAccessDenied(false);
    try {
      const regs = await onboarding.listAzureRegistrations();
      setRegistrations(regs);
    } catch (e: unknown) {
      // T003: 401/403 → inline access-denied gate (backend is guarded by OnboardingAdministratorRequirement)
      const status = (e as { response?: { status?: number } }).response?.status;
      if (status === 401 || status === 403) {
        setAccessDenied(true);
      } else {
        const err = e as { message?: string };
        setError(err.message ?? 'Failed to load Azure registrations.');
      }
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void loadRegistrations();
  }, []);

  async function handleRegister(e: React.FormEvent) {
    e.preventDefault();
    const trimmed = newSubId.trim();
    if (!trimmed) return;
    setRegistering(true);
    setError(null);
    setSuccess(null);
    try {
      const updated = await onboarding.putAzureRegistrations([trimmed]);
      setRegistrations(updated);
      setNewSubId('');
      setSuccess(`Subscription ${trimmed} registered successfully.`);
    } catch (e: unknown) {
      const err = e as { message?: string };
      setError(err.message ?? 'Failed to register subscription.');
    } finally {
      setRegistering(false);
    }
  }

  async function handleDelete() {
    if (!deleteTarget) return;
    setDeleting(true);
    setError(null);
    setSuccess(null);
    try {
      await onboarding.removeAzureRegistration(deleteTarget.id);
      setRegistrations((prev) => prev.filter((r) => r.id !== deleteTarget.id));
      setSuccess(`Subscription ${deleteTarget.subscriptionId} removed.`);
      setDeleteTarget(null);
    } catch (e: unknown) {
      const err = e as { message?: string };
      setError(err.message ?? 'Failed to remove subscription.');
    } finally {
      setDeleting(false);
    }
  }

  if (loading) {
    return (
      <div className="flex items-center justify-center py-20">
        <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-indigo-600" />
      </div>
    );
  }

  // T003: Admin role gate — backend returns 403 for non-OnboardingAdministrator callers
  if (accessDenied) {
    return (
      <div className="mx-auto max-w-3xl p-6">
        <div className="rounded-lg border border-red-200 bg-red-50 p-6 text-center">
          <h2 className="text-lg font-semibold text-red-800 mb-2">Access Denied</h2>
          <p className="text-sm text-red-700">
            This page requires the <strong>Onboarding Administrator</strong> role.
            Contact your system administrator to request access.
          </p>
          <Link to="/" className="mt-4 inline-block text-sm text-indigo-600 hover:underline">
            ← Return to dashboard
          </Link>
        </div>
      </div>
    );
  }

  return (
    <div className="mx-auto max-w-3xl space-y-6 p-6">
      <div className="flex items-start gap-4">
        <div className="flex-1">
          <h1 className="text-2xl font-semibold text-gray-900">Azure Subscription Settings</h1>
          <p className="mt-1 text-sm text-gray-500">
            Manage the Azure subscriptions registered for resource discovery and scope.
          </p>
        </div>
        <Link
          to="/admin/imported-documents"
          className="shrink-0 text-sm text-indigo-600 hover:text-indigo-800"
        >
          ← Back
        </Link>
      </div>

      {success && (
        <div className="rounded-md bg-green-50 p-4 text-sm text-green-800">{success}</div>
      )}
      {error && (
        <div className="rounded-md bg-red-50 p-4 text-sm text-red-800">{error}</div>
      )}

      {/* Register form */}
      <div className="rounded-lg border border-gray-200 bg-white p-6 shadow-sm">
        <h2 className="text-base font-medium text-gray-900 mb-3">Register a Subscription</h2>
        <form
          onSubmit={(e) => { void handleRegister(e); }}
          className="flex flex-col sm:flex-row gap-3 sm:items-end"
        >
          <div className="flex-1">
            <label htmlFor="sub-id" className="block text-sm font-medium text-gray-700 mb-1">
              Subscription ID
            </label>
            <input
              id="sub-id"
              type="text"
              value={newSubId}
              onChange={(e) => setNewSubId(e.target.value)}
              placeholder="xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
              className="block w-full rounded-md border border-gray-300 px-3 py-2 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm"
            />
          </div>
          <button
            type="submit"
            disabled={registering || !newSubId.trim()}
            className="inline-flex items-center rounded-md bg-indigo-600 px-4 py-2 text-sm font-medium text-white shadow-sm hover:bg-indigo-700 disabled:opacity-50"
          >
            {registering ? 'Registering…' : 'Register'}
          </button>
        </form>
      </div>

      {/* Registrations table */}
      <div className="rounded-lg border border-gray-200 bg-white shadow-sm overflow-hidden">
        <div className="px-6 py-4 border-b border-gray-200">
          <h2 className="text-base font-medium text-gray-900">Registered Subscriptions</h2>
        </div>
        {registrations.length === 0 ? (
          <p className="px-6 py-8 text-sm text-gray-500 text-center">
            No subscriptions registered yet. Use the form above to add one.
          </p>
        ) : (
          <table className="min-w-full divide-y divide-gray-200 text-sm">
            <thead className="bg-gray-50">
              <tr>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Subscription ID
                </th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Display Name
                </th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Status
                </th>
                <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Actions
                </th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-100 bg-white">
              {registrations.map((reg) => (
                <tr key={reg.id}>
                  <td className="px-6 py-4 font-mono text-xs text-gray-700">{reg.subscriptionId}</td>
                  <td className="px-6 py-4 text-gray-900">
                    {reg.displayName || <span className="text-gray-400 italic">—</span>}
                  </td>
                  <td className="px-6 py-4">
                    <span
                      className={`inline-flex rounded-full px-2 py-0.5 text-xs font-medium ${
                        reg.status === 'Selected'
                          ? 'bg-green-100 text-green-800'
                          : 'bg-amber-100 text-amber-800'
                      }`}
                    >
                      {reg.status}
                    </span>
                  </td>
                  <td className="px-6 py-4 text-right">
                    <button
                      type="button"
                      onClick={() => setDeleteTarget(reg)}
                      className="text-sm text-red-600 hover:text-red-800 font-medium"
                    >
                      Delete
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      {/* Delete confirmation dialog */}
      {deleteTarget && (
        <div className="fixed inset-0 bg-black/40 flex items-center justify-center z-50">
          <div className="bg-white rounded-lg shadow-xl p-6 w-full max-w-sm">
            <h3 className="font-semibold text-gray-900 mb-2">Remove Subscription?</h3>
            <p className="text-sm text-gray-600 mb-1">
              Are you sure you want to remove this subscription registration?
            </p>
            <p className="text-xs font-mono text-gray-700 bg-gray-50 rounded px-2 py-1 mb-4">
              {deleteTarget.subscriptionId}
            </p>
            <div className="flex justify-end gap-3">
              <button
                type="button"
                onClick={() => setDeleteTarget(null)}
                disabled={deleting}
                className="text-sm text-gray-600 hover:text-gray-800 px-3 py-1.5"
              >
                Cancel
              </button>
              <button
                type="button"
                onClick={() => { void handleDelete(); }}
                disabled={deleting}
                className="text-sm bg-red-600 text-white rounded px-3 py-1.5 hover:bg-red-700 disabled:opacity-50"
              >
                {deleting ? 'Removing…' : 'Remove'}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
