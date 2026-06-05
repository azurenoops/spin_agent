import { useCallback, useEffect, useState } from 'react';
import { onboarding } from '../../features/onboarding/api/onboardingApi';

/**
 * Epic #208 / Task #250 — Standalone org settings page at /settings/org.
 *
 * Provides a persistent management surface for reviewing and editing the
 * organization profile submitted during the TenantWizard. This is NOT a
 * re-entry point into the wizard — it is a separate page for ongoing
 * management.
 *
 * Loads via onboarding.getOrganizationContext() and saves via
 * onboarding.upsertOrganizationContext().
 */

interface OrgContextFields {
  organizationName: string;
  branch: number;
  classificationPosture: number;
}

export default function OrgSettingsPage() {
  const [fields, setFields] = useState<OrgContextFields>({
    organizationName: '',
    branch: 0,
    classificationPosture: 0,
  });
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);

  const loadOrg = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const ctx = await onboarding.getOrganizationContext();
      if (ctx) {
        setFields({
          organizationName: (ctx as unknown as { organizationName?: string }).organizationName ?? '',
          branch: (ctx as unknown as { branch?: number }).branch ?? 0,
          classificationPosture: (ctx as unknown as { classificationPosture?: number }).classificationPosture ?? 0,
        });
      }
    } catch {
      setError('Failed to load organization settings.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { void loadOrg(); }, [loadOrg]);

  const handleSave = async (e: React.FormEvent) => {
    e.preventDefault();
    setSaving(true);
    setError(null);
    setSuccess(null);
    try {
      await onboarding.upsertOrganizationContext({
        organizationName: fields.organizationName,
        branch: fields.branch,
        classificationPosture: fields.classificationPosture,
      } as Parameters<typeof onboarding.upsertOrganizationContext>[0]);
      setSuccess('Organization settings saved.');
    } catch {
      setError('Failed to save organization settings. Please try again.');
    } finally {
      setSaving(false);
    }
  };

  if (loading) {
    return (
      <div className="flex items-center justify-center py-20">
        <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-indigo-600" />
      </div>
    );
  }

  return (
    <div className="mx-auto max-w-2xl space-y-6 p-6">
      <div>
        <h1 className="text-2xl font-semibold text-gray-900">Organization Settings</h1>
        <p className="mt-1 text-sm text-gray-500">
          Review and update your organization profile. Changes take effect immediately.
        </p>
      </div>

      {success && (
        <div className="rounded-md bg-green-50 p-4 text-sm text-green-800">{success}</div>
      )}
      {error && (
        <div className="rounded-md bg-red-50 p-4 text-sm text-red-800">{error}</div>
      )}

      <form
        onSubmit={(e) => { void handleSave(e); }}
        className="rounded-lg border border-gray-200 bg-white p-6 shadow-sm space-y-4"
      >
        <div>
          <label htmlFor="org-name" className="block text-sm font-medium text-gray-700">
            Organization Name <span className="text-red-500">*</span>
          </label>
          <input
            id="org-name"
            type="text"
            required
            value={fields.organizationName}
            onChange={(e) => setFields((f) => ({ ...f, organizationName: e.target.value }))}
            className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm"
          />
        </div>

        <div>
          <label htmlFor="classification" className="block text-sm font-medium text-gray-700">
            Classification Posture
          </label>
          <select
            id="classification"
            value={fields.classificationPosture}
            onChange={(e) => setFields((f) => ({ ...f, classificationPosture: Number(e.target.value) }))}
            className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm"
          >
            <option value={0}>Unclassified</option>
            <option value={1}>CUI</option>
            <option value={2}>Secret</option>
            <option value={3}>Top Secret</option>
          </select>
        </div>

        <div className="flex justify-end pt-2">
          <button
            type="submit"
            disabled={saving || !fields.organizationName.trim()}
            className="inline-flex items-center rounded-md bg-indigo-600 px-4 py-2 text-sm font-medium text-white shadow-sm hover:bg-indigo-700 disabled:opacity-50"
          >
            {saving ? 'Saving…' : 'Save Changes'}
          </button>
        </div>
      </form>
    </div>
  );
}
