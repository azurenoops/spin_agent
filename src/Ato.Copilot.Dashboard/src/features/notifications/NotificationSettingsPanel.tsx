import { useState, useEffect } from 'react';
import apiClient from '../../api/client';

// ─── Types ────────────────────────────────────────────────────────────────────

interface NotificationPreferences {
  userId: string;
  emailEnabled: boolean;
  emailAddress: string | null;
  teamsEnabled: boolean;
  slackEnabled: boolean;
}

// ─── Component ────────────────────────────────────────────────────────────────

/**
 * UF-016 — Notification Settings Panel (spec-063 T-077 / #200).
 *
 * Embeddable panel for configuring per-user notification delivery channels
 * (Email, Teams, Slack). Saves to PUT /api/dashboard/notifications/preferences.
 */
export default function NotificationSettingsPanel() {
  const [prefs, setPrefs] = useState<NotificationPreferences>({
    userId: 'dashboard-user',
    emailEnabled: false,
    emailAddress: null,
    teamsEnabled: false,
    slackEnabled: false,
  });
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    apiClient
      .get<NotificationPreferences>('/notifications/preferences')
      .then((res) => setPrefs(res.data))
      .catch(() => {/* use defaults */})
      .finally(() => setLoading(false));
  }, []);

  const handleSave = async () => {
    setSaving(true);
    setError(null);
    try {
      await apiClient.put('/notifications/preferences', prefs);
      setSaved(true);
      setTimeout(() => setSaved(false), 2500);
    } catch {
      setError('Failed to save preferences. Please try again.');
    } finally {
      setSaving(false);
    }
  };

  if (loading)
    return <div className="py-4 text-sm text-gray-500">Loading notification preferences…</div>;

  return (
    <div className="space-y-5">
      <div>
        <h3 className="text-sm font-semibold text-gray-900">Notification Delivery</h3>
        <p className="mt-0.5 text-xs text-gray-500">
          Configure how you receive RMF event notifications (authorization decisions, POA&M due dates, SCAP imports).
        </p>
      </div>

      {/* Email */}
      <div className="space-y-2 rounded-lg border border-gray-200 p-4">
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-2">
            {/* Mail icon */}
            <svg className="h-4 w-4 text-gray-500" fill="none" viewBox="0 0 24 24" strokeWidth={1.5} stroke="currentColor">
              <path strokeLinecap="round" strokeLinejoin="round" d="M21.75 6.75v10.5a2.25 2.25 0 01-2.25 2.25h-15a2.25 2.25 0 01-2.25-2.25V6.75m19.5 0A2.25 2.25 0 0019.5 4.5h-15a2.25 2.25 0 00-2.25 2.25m19.5 0v.243a2.25 2.25 0 01-1.07 1.916l-7.5 4.615a2.25 2.25 0 01-2.36 0L3.32 8.91a2.25 2.25 0 01-1.07-1.916V6.75" />
            </svg>
            <span className="text-sm font-medium text-gray-900">Email</span>
          </div>
          <button
            type="button"
            role="switch"
            aria-checked={prefs.emailEnabled}
            onClick={() => setPrefs((p) => ({ ...p, emailEnabled: !p.emailEnabled }))}
            className={`relative inline-flex h-5 w-9 flex-shrink-0 cursor-pointer rounded-full border-2 border-transparent transition-colors ${
              prefs.emailEnabled ? 'bg-indigo-600' : 'bg-gray-200'
            }`}
          >
            <span
              className={`pointer-events-none inline-block h-4 w-4 transform rounded-full bg-white shadow transition-transform ${
                prefs.emailEnabled ? 'translate-x-4' : 'translate-x-0'
              }`}
            />
          </button>
        </div>
        {prefs.emailEnabled && (
          <input
            type="email"
            value={prefs.emailAddress ?? ''}
            onChange={(e) => setPrefs((p) => ({ ...p, emailAddress: e.target.value }))}
            placeholder="Your email address"
            className="w-full rounded-md border border-gray-300 px-3 py-1.5 text-sm focus:border-indigo-500 focus:ring-indigo-500"
          />
        )}
      </div>

      {/* Microsoft Teams */}
      <div className="flex items-center justify-between rounded-lg border border-gray-200 p-4">
        <div className="flex items-center gap-2">
          <svg className="h-4 w-4 text-indigo-600" viewBox="0 0 24 24" fill="currentColor">
            <path d="M19.5 8.5a3 3 0 100-6 3 3 0 000 6zm0 1.5a4.5 4.5 0 100-9 4.5 4.5 0 000 9z" />
            <path d="M12 2a5 5 0 100 10A5 5 0 0012 2zm0 1.5a3.5 3.5 0 110 7 3.5 3.5 0 010-7zM2 17c0-2.761 4.477-5 10-5 .578 0 1.145.028 1.697.082A6.5 6.5 0 0012 17.5V21.5C6.477 21.5 2 19.261 2 17z" />
          </svg>
          <span className="text-sm font-medium text-gray-900">Microsoft Teams</span>
        </div>
        <button
          type="button"
          role="switch"
          aria-checked={prefs.teamsEnabled}
          onClick={() => setPrefs((p) => ({ ...p, teamsEnabled: !p.teamsEnabled }))}
          className={`relative inline-flex h-5 w-9 flex-shrink-0 cursor-pointer rounded-full border-2 border-transparent transition-colors ${
            prefs.teamsEnabled ? 'bg-indigo-600' : 'bg-gray-200'
          }`}
        >
          <span
            className={`pointer-events-none inline-block h-4 w-4 transform rounded-full bg-white shadow transition-transform ${
              prefs.teamsEnabled ? 'translate-x-4' : 'translate-x-0'
            }`}
          />
        </button>
      </div>

      {/* Slack */}
      <div className="flex items-center justify-between rounded-lg border border-gray-200 p-4">
        <div className="flex items-center gap-2">
          <svg className="h-4 w-4 text-green-600" viewBox="0 0 24 24" fill="currentColor">
            <path d="M5.042 15.165a2.528 2.528 0 01-2.52 2.523A2.528 2.528 0 010 15.165a2.527 2.527 0 012.522-2.52h2.52v2.52zM6.313 15.165a2.527 2.527 0 012.521-2.52 2.527 2.527 0 012.521 2.52v6.313A2.528 2.528 0 018.834 24a2.528 2.528 0 01-2.521-2.522v-6.313zM8.834 5.042a2.528 2.528 0 01-2.521-2.52A2.528 2.528 0 018.834 0a2.528 2.528 0 012.521 2.522v2.52H8.834zM8.834 6.313a2.528 2.528 0 012.521 2.521 2.528 2.528 0 01-2.521 2.521H2.522A2.528 2.528 0 010 8.834a2.528 2.528 0 012.522-2.521h6.312zM18.956 8.834a2.528 2.528 0 012.522-2.521A2.528 2.528 0 0124 8.834a2.528 2.528 0 01-2.522 2.521h-2.522V8.834zM17.688 8.834a2.528 2.528 0 01-2.523 2.521 2.527 2.527 0 01-2.52-2.521V2.522A2.527 2.527 0 0115.165 0a2.528 2.528 0 012.523 2.522v6.312zM15.165 18.956a2.528 2.528 0 012.523 2.522A2.528 2.528 0 0115.165 24a2.527 2.527 0 01-2.52-2.522v-2.522h2.52zM15.165 17.688a2.527 2.527 0 01-2.52-2.523 2.526 2.526 0 012.52-2.52h6.313A2.527 2.527 0 0124 15.165a2.528 2.528 0 01-2.522 2.523h-6.313z" />
          </svg>
          <span className="text-sm font-medium text-gray-900">Slack</span>
        </div>
        <button
          type="button"
          role="switch"
          aria-checked={prefs.slackEnabled}
          onClick={() => setPrefs((p) => ({ ...p, slackEnabled: !p.slackEnabled }))}
          className={`relative inline-flex h-5 w-9 flex-shrink-0 cursor-pointer rounded-full border-2 border-transparent transition-colors ${
            prefs.slackEnabled ? 'bg-indigo-600' : 'bg-gray-200'
          }`}
        >
          <span
            className={`pointer-events-none inline-block h-4 w-4 transform rounded-full bg-white shadow transition-transform ${
              prefs.slackEnabled ? 'translate-x-4' : 'translate-x-0'
            }`}
          />
        </button>
      </div>

      {/* Save */}
      {error && <p className="text-sm text-red-600">{error}</p>}
      <div className="flex justify-end">
        <button
          type="button"
          onClick={() => void handleSave()}
          disabled={saving}
          className="rounded-md bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-700 disabled:opacity-50"
        >
          {saved ? '✓ Saved' : saving ? 'Saving…' : 'Save Preferences'}
        </button>
      </div>
    </div>
  );
}
