import { useState, useEffect, useCallback } from 'react';
import { getTicketingConfig, configureTicketing } from '../../api/poam';
import type { ConfigureTicketingRequest } from '../../types/poam';

interface TicketingConfigProps {
  systemId: string;
}

const defaultFieldMapping: Record<string, string> = {
  weakness: 'summary',
  catSeverity: 'priority',
  poc: 'assignee',
  scheduledCompletionDate: 'duedate',
};

export default function TicketingConfig({ systemId }: TicketingConfigProps) {
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState(false);
  const [configured, setConfigured] = useState(false);

  const [provider, setProvider] = useState<'jira' | 'servicenow'>('jira');
  const [baseUrl, setBaseUrl] = useState('');
  const [projectKeyOrTableName, setProjectKeyOrTableName] = useState('');
  const [issueType, setIssueType] = useState('');
  const [authToken, setAuthToken] = useState('');
  const [syncEnabled, setSyncEnabled] = useState(false);
  const [fieldMapping, setFieldMapping] = useState<Record<string, string>>(defaultFieldMapping);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    getTicketingConfig(systemId)
      .then(config => {
        if (cancelled) return;
        if (config && config.provider) {
          setConfigured(true);
          setProvider(config.provider as 'jira' | 'servicenow');
          setBaseUrl((config.baseUrl as string) ?? '');
          setProjectKeyOrTableName((config.projectKeyOrTableName as string) ?? '');
          setIssueType((config.issueType as string) ?? '');
          setSyncEnabled(!!config.syncEnabled);
          if (config.fieldMapping) setFieldMapping(config.fieldMapping as Record<string, string>);
        }
      })
      .catch(() => { /* no config yet */ })
      .finally(() => { if (!cancelled) setLoading(false); });
    return () => { cancelled = true; };
  }, [systemId]);

  const handleSave = useCallback(async () => {
    setError(null);
    setSuccess(false);
    setSaving(true);
    try {
      const request: ConfigureTicketingRequest = {
        provider,
        baseUrl: baseUrl.trim(),
        projectKeyOrTableName: projectKeyOrTableName.trim(),
        issueType: issueType.trim() || undefined,
        authToken: authToken.trim(),
        fieldMapping,
        syncEnabled,
      };
      await configureTicketing(systemId, request);
      setConfigured(true);
      setSuccess(true);
      setAuthToken('');
      setTimeout(() => setSuccess(false), 3000);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Failed to save configuration');
    } finally {
      setSaving(false);
    }
  }, [systemId, provider, baseUrl, projectKeyOrTableName, issueType, authToken, fieldMapping, syncEnabled]);

  const updateMapping = useCallback((poamField: string, externalField: string) => {
    setFieldMapping(prev => ({ ...prev, [poamField]: externalField }));
  }, []);

  if (loading) {
    return <div className="py-8 text-center text-gray-400">Loading ticketing configuration...</div>;
  }

  return (
    <div className="space-y-6">
      {/* Status Banner */}
      {configured && (
        <div className="flex items-center gap-2 rounded-lg border border-green-200 bg-green-50 px-4 py-3 text-sm text-green-700">
          <svg className="h-5 w-5" fill="none" viewBox="0 0 24 24" strokeWidth={1.5} stroke="currentColor">
            <path strokeLinecap="round" strokeLinejoin="round" d="M9 12.75L11.25 15 15 9.75M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
          </svg>
          Ticketing integration configured ({provider === 'jira' ? 'Jira' : 'ServiceNow'})
          {syncEnabled && ' — Auto-sync enabled'}
        </div>
      )}

      {error && (
        <div className="rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
          {error}
        </div>
      )}

      {success && (
        <div className="rounded-lg border border-green-200 bg-green-50 px-4 py-3 text-sm text-green-700">
          Configuration saved successfully!
        </div>
      )}

      {/* Provider Selection */}
      <fieldset>
        <legend className="mb-2 text-sm font-semibold text-gray-700">Provider</legend>
        <div className="flex gap-4">
          {(['jira', 'servicenow'] as const).map(p => (
            <label key={p} className="flex items-center gap-2 cursor-pointer">
              <input
                type="radio"
                name="provider"
                value={p}
                checked={provider === p}
                onChange={() => setProvider(p)}
                className="h-4 w-4 text-indigo-600 focus:ring-indigo-500"
              />
              <span className="text-sm text-gray-700">{p === 'jira' ? 'Jira' : 'ServiceNow'}</span>
            </label>
          ))}
        </div>
      </fieldset>

      {/* Connection Details */}
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
        <div>
          <label htmlFor="ticketing-url" className="mb-1 block text-sm font-medium text-gray-700">
            Base URL
          </label>
          <input
            id="ticketing-url"
            type="url"
            value={baseUrl}
            onChange={e => setBaseUrl(e.target.value)}
            placeholder={provider === 'jira' ? 'https://myorg.atlassian.net' : 'https://myorg.service-now.com'}
            className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
          />
        </div>
        <div>
          <label htmlFor="ticketing-project" className="mb-1 block text-sm font-medium text-gray-700">
            {provider === 'jira' ? 'Project Key' : 'Table Name'}
          </label>
          <input
            id="ticketing-project"
            type="text"
            value={projectKeyOrTableName}
            onChange={e => setProjectKeyOrTableName(e.target.value)}
            placeholder={provider === 'jira' ? 'POAM' : 'incident'}
            className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
          />
        </div>
        {provider === 'jira' && (
          <div>
            <label htmlFor="ticketing-issue-type" className="mb-1 block text-sm font-medium text-gray-700">
              Issue Type
            </label>
            <input
              id="ticketing-issue-type"
              type="text"
              value={issueType}
              onChange={e => setIssueType(e.target.value)}
              placeholder="Task"
              className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
            />
          </div>
        )}
        <div>
          <label htmlFor="ticketing-token" className="mb-1 block text-sm font-medium text-gray-700">
            {configured ? 'Auth Token (leave blank to keep existing)' : 'Auth Token'}
          </label>
          <input
            id="ticketing-token"
            type="password"
            value={authToken}
            onChange={e => setAuthToken(e.target.value)}
            placeholder={configured ? '••••••••' : 'API token or PAT'}
            className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
          />
        </div>
      </div>

      {/* Auto-Sync Toggle */}
      <div className="flex items-center gap-3">
        <button
          type="button"
          role="switch"
          aria-checked={syncEnabled}
          onClick={() => setSyncEnabled(s => !s)}
          className={`relative inline-flex h-6 w-11 shrink-0 cursor-pointer rounded-full border-2 border-transparent transition-colors ${
            syncEnabled ? 'bg-indigo-600' : 'bg-gray-200'
          }`}
        >
          <span
            className={`pointer-events-none inline-block h-5 w-5 rounded-full bg-white shadow ring-0 transition-transform ${
              syncEnabled ? 'translate-x-5' : 'translate-x-0'
            }`}
          />
        </button>
        <span className="text-sm text-gray-700">
          Enable automatic bidirectional sync
        </span>
      </div>

      {/* Field Mapping */}
      <fieldset>
        <legend className="mb-2 text-sm font-semibold text-gray-700">Field Mapping</legend>
        <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
          {Object.entries(fieldMapping).map(([poamField, extField]) => (
            <div key={poamField} className="flex items-center gap-2">
              <span className="w-40 truncate text-sm text-gray-500">{poamField}</span>
              <span className="text-gray-400">→</span>
              <input
                type="text"
                value={extField}
                onChange={e => updateMapping(poamField, e.target.value)}
                className="flex-1 rounded border border-gray-300 px-2 py-1 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
              />
            </div>
          ))}
        </div>
      </fieldset>

      {/* Save Button */}
      <div className="flex justify-end">
        <button
          type="button"
          onClick={handleSave}
          disabled={saving || !baseUrl.trim() || !projectKeyOrTableName.trim() || (!configured && !authToken.trim())}
          className="inline-flex items-center gap-2 rounded-lg bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-700 disabled:cursor-not-allowed disabled:opacity-50"
        >
          {saving ? 'Saving...' : configured ? 'Update Configuration' : 'Save Configuration'}
        </button>
      </div>
    </div>
  );
}
