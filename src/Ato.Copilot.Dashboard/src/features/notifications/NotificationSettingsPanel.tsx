import { useState, useEffect, useRef } from 'react';
import apiClient from '../../api/client';
import { useMe } from '../auth/useMe';

interface NotificationPreferences {
  poamOverdueAlerts: boolean;
  atoExpirationAlerts: boolean;
  complianceDriftAlerts: boolean;
  alertDaysBefore: number;
}

function isPreferences(value: unknown): value is NotificationPreferences {
  if (!value || typeof value !== 'object') return false;
  return 'poamOverdueAlerts' in value && typeof value.poamOverdueAlerts === 'boolean'
    && 'atoExpirationAlerts' in value && typeof value.atoExpirationAlerts === 'boolean'
    && 'complianceDriftAlerts' in value && typeof value.complianceDriftAlerts === 'boolean'
    && 'alertDaysBefore' in value && typeof value.alertDaysBefore === 'number'
    && Number.isInteger(value.alertDaysBefore) && value.alertDaysBefore >= 0;
}

export default function NotificationSettingsPanel() {
  const { data: identity, isLoading, error, refetch } = useMe();
  if (isLoading) return <p role="status">Resolving notification preferences access...</p>;
  if (error || !identity) return (
    <div role="alert">Notification preferences require an authenticated workspace.
      <button type="button" onClick={refetch} className="ml-2 underline">Retry</button>
    </div>
  );
  const key = JSON.stringify([
    identity.directoryTenantId, identity.oid, identity.workspace?.kind, identity.workspace?.tenantId,
    identity.workspace?.mode, identity.effectiveTenant?.id,
  ]);
  return <PreferencesForm key={key} />;
}

function PreferencesForm() {
  const [prefs, setPrefs] = useState<NotificationPreferences | null>(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [revision, setRevision] = useState(0);
  const request = useRef<AbortController | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    request.current = controller;
    setLoading(true);
    setError(null);
    void apiClient.get<unknown>('/notifications/preferences', { signal: controller.signal })
      .then(response => {
        if (controller.signal.aborted) return;
        if (!isPreferences(response.data)) throw new Error('Unexpected notification preferences response.');
        setPrefs(response.data);
      })
      .catch(() => {
        if (!controller.signal.aborted) setError('Unable to load notification preferences. Please retry.');
      })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [revision]);

  const handleSave = async () => {
    const controller = request.current;
    if (!prefs || !controller || controller.signal.aborted) return;
    setSaving(true);
    setSaved(false);
    setError(null);
    try {
      const response = await apiClient.put<unknown>('/notifications/preferences', {
        poamOverdueAlerts: prefs.poamOverdueAlerts,
        atoExpirationAlerts: prefs.atoExpirationAlerts,
        complianceDriftAlerts: prefs.complianceDriftAlerts,
        alertDaysBefore: prefs.alertDaysBefore,
      }, { signal: controller.signal });
      if (controller.signal.aborted) return;
      if (!isPreferences(response.data)) throw new Error('Unexpected notification preferences response.');
      setPrefs(response.data);
      setSaved(true);
    } catch {
      if (!controller.signal.aborted) setError('Failed to save preferences. Please try again.');
    } finally {
      if (!controller.signal.aborted) setSaving(false);
    }
  };

  if (loading) return <p role="status" className="py-4 text-sm text-gray-500">Loading notification preferences...</p>;
  if (!prefs) return (
    <div role="alert" className="text-sm text-red-700">{error}
      <button type="button" onClick={() => setRevision(value => value + 1)} className="ml-2 underline">Retry</button>
    </div>
  );
  const alerts = [
    ['poamOverdueAlerts', 'POA&M overdue alerts'],
    ['atoExpirationAlerts', 'ATO expiration alerts'],
    ['complianceDriftAlerts', 'Compliance drift alerts'],
  ] as const;
  return (
    <form className="space-y-5" onSubmit={event => { event.preventDefault(); void handleSave(); }}>
      <div>
        <h3 className="text-sm font-semibold text-gray-900">Notification preferences</h3>
        <p className="mt-1 text-xs text-gray-500">Preferences apply to your identity in this organization.</p>
      </div>
      <fieldset disabled={saving} className="space-y-4">
        {alerts.map(([field, label]) => (
          <label key={field} className="flex items-center gap-3 rounded-lg border border-gray-200 p-4 text-sm">
            <input type="checkbox" checked={prefs[field]} onChange={event => {
              setPrefs({ ...prefs, [field]: event.target.checked }); setSaved(false);
            }} />
            {label}
          </label>
        ))}
        <label className="block text-sm">
          Warning days before expiration
          <input type="number" min={0} step={1} required value={prefs.alertDaysBefore}
            onChange={event => { setPrefs({ ...prefs, alertDaysBefore: Number(event.target.value) }); setSaved(false); }}
            className="mt-1 block rounded border border-gray-300 px-3 py-2" />
        </label>
        <button type="submit" className="rounded bg-indigo-600 px-4 py-2 text-sm text-white">
          {saving ? 'Saving...' : 'Save preferences'}
        </button>
      </fieldset>
      {error && <p role="alert" className="text-sm text-red-700">{error}</p>}
      {saved && <p role="status" className="text-sm text-green-700">Preferences saved.</p>}
    </form>
  );
}
