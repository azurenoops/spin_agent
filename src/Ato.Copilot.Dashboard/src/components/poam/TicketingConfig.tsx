import { useEffect, useRef, useState } from 'react';
import { getTicketingConfig, configureTicketing } from '../../api/poam';
import { useSystemMutationPermission } from '../permissions/useSystemMutationPermission';

interface TicketingConfigProps { systemId: string }

export default function TicketingConfig({ systemId }: TicketingConfigProps) {
  const canConfigure = useSystemMutationPermission(systemId, 'canManageSystem');
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState(false);
  const [configured, setConfigured] = useState(false);
  const [provider, setProvider] = useState<'jira' | 'servicenow'>('jira');
  const [baseUrl, setBaseUrl] = useState('');
  const [projectKey, setProjectKey] = useState('');
  const [secretName, setSecretName] = useState('');
  const [syncEnabled, setSyncEnabled] = useState(false);
  const generation = useRef(0);

  useEffect(() => {
    let cancelled = false;
    ++generation.current;
    setLoading(true); setError(null); setSuccess(false); setConfigured(false);
    setBaseUrl(''); setProjectKey(''); setSecretName(''); setSyncEnabled(false);
    getTicketingConfig(systemId).then(config => {
      if (cancelled) return;
      setConfigured(config.configured === true);
      setProvider(String(config.provider).toLowerCase() === 'servicenow' ? 'servicenow' : 'jira');
      setBaseUrl(typeof config.baseUrl === 'string' ? config.baseUrl : '');
      setProjectKey(typeof config.projectKey === 'string' ? config.projectKey : '');
      setSyncEnabled(config.syncEnabled === true);
    }).catch(() => {
      if (!cancelled) setError('Could not load ticket configuration. Verify your access and try again.');
    }).finally(() => { if (!cancelled) setLoading(false); });
    return () => { cancelled = true; ++generation.current; };
  }, [systemId]);

  async function save() {
    if (!canConfigure) return;
    const current = generation.current;
    setSaving(true); setError(null); setSuccess(false);
    try {
      await configureTicketing(systemId, {
        provider, baseUrl: baseUrl.trim(), projectKey: projectKey.trim(),
        apiKeySecretName: secretName.trim(), syncEnabled,
      });
      if (current === generation.current) { setConfigured(true); setSuccess(true); setSecretName(''); }
    } catch (reason) {
      const detail = reason && typeof reason === 'object' && 'error' in reason && typeof reason.error === 'string' ? reason.error : null;
      if (current === generation.current) setError(detail ?? 'Could not save ticket configuration.');
    } finally { if (current === generation.current) setSaving(false); }
  }

  if (loading) return <p role="status">Loading ticketing configuration…</p>;
  const input = 'w-full rounded-lg border border-gray-300 px-3 py-2 text-sm disabled:bg-gray-100';
  return <div className="space-y-4">
    <div>
      <h3 className="font-semibold">System ticket connector</h3>
      <p className="text-sm text-gray-600">Configure Jira or ServiceNow for explicit ticket creation and manual read-only snapshots. No scheduled sync, incoming webhooks, or bidirectional updates.</p>
    </div>
    {configured && <p className="text-sm text-green-700">Connector configured. {syncEnabled ? 'Manual operations enabled.' : 'Manual operations disabled.'}</p>}
    {error && <p role="alert" className="text-sm text-red-700">{error}</p>}
    {!canConfigure && <p role="alert" className="text-sm text-amber-800">System management permission is required to change this connector.</p>}
    {success && <p role="status" className="text-sm text-green-700">Configuration saved.</p>}
    <fieldset disabled={!canConfigure || saving} className="space-y-3">
      <label className="block text-sm">Provider
        <select value={provider} onChange={event => setProvider(event.target.value as 'jira' | 'servicenow')} className={input}>
          <option value="jira">Jira</option><option value="servicenow">ServiceNow</option>
        </select>
      </label>
      <label className="block text-sm">HTTPS base URL
        <input type="url" value={baseUrl} onChange={event => setBaseUrl(event.target.value)} placeholder="https://tickets.example.com" className={input} />
      </label>
      <label className="block text-sm">{provider === 'jira' ? 'Project key' : 'Table name'}
        <input value={projectKey} onChange={event => setProjectKey(event.target.value)} className={input} />
      </label>
      <label className="block text-sm">Server credential reference {configured ? '(blank keeps existing)' : ''}
        <input value={secretName} onChange={event => setSecretName(event.target.value)} autoComplete="off" className={input} />
      </label>
      <p className="text-xs text-gray-500">Enter an administrator-provisioned reference, never an API token or password. Credentials are resolved only on the server; the destination host must be allowlisted.</p>
      <label className="flex items-center gap-2 text-sm">
        <input type="checkbox" checked={syncEnabled} onChange={event => setSyncEnabled(event.target.checked)} />Enable manual ticket operations
      </label>
      <button type="button" disabled={saving || !baseUrl.trim() || !projectKey.trim() || (!configured && !secretName.trim())}
        onClick={() => void save()} className="rounded-lg bg-indigo-600 px-4 py-2 text-sm text-white disabled:opacity-50">
        {saving ? 'Saving…' : 'Save connector'}
      </button>
    </fieldset>
    <p className="text-xs text-gray-500">Existing POA&amp;M-owned ticket references are legacy records and remain separate from task-owned links.</p>
  </div>;
}
