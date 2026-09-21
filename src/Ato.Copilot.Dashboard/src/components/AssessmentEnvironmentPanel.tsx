import { useCallback, useEffect, useRef, useState } from 'react';
import { Link } from '../features/workspaces/workspaceNavigation';
import {
  detachAssessmentEnvironment, getAssessmentEnvironment, saveAssessmentEnvironment,
  type AssessmentEnvironment,
} from '../api/assessments';
import { useAssessmentReadiness } from '../hooks/useAssessmentReadiness';
import { assessmentError, type AssessmentError } from '../utils/assessmentErrors';

export default function AssessmentEnvironmentPanel({ systemId }: { systemId: string }) {
  return <EnvironmentAttachment key={systemId} systemId={systemId} />;
}

function EnvironmentAttachment({ systemId }: { systemId: string }) {
  const [configuration, setConfiguration] = useState<AssessmentEnvironment | null>(null);
  const [selected, setSelected] = useState<string[]>([]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<AssessmentError | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const generation = useRef(0);
  const readiness = useAssessmentReadiness(systemId);

  const load = useCallback(async () => {
    const request = ++generation.current;
    setLoading(true);
    setError(null);
    try {
      const result = await getAssessmentEnvironment(systemId);
      if (request !== generation.current) return;
      if (result.systemId !== systemId) throw new Error('Unable to load the environment for this system.');
      setConfiguration(result);
      setSelected(result.subscriptionIds);
    } catch (err: unknown) {
      if (request !== generation.current) return;
      setConfiguration(null);
      setError(assessmentError(err, 'Unable to load Azure assessment environment.'));
    } finally {
      if (request === generation.current) setLoading(false);
    }
  }, [systemId]);

  useEffect(() => {
    void load();
    return () => { generation.current += 1; };
  }, [load]);

  const supportedCloud = configuration?.deploymentCloud === 'Commercial' || configuration?.deploymentCloud === 'Government';
  const eligible = (subscriptionId: string) => configuration?.availableSubscriptions.some(
    (subscription) => subscription.subscriptionId === subscriptionId
      && subscription.isAvailable
      && subscription.cloudEnvironment === configuration.deploymentCloud,
  ) === true;
  const invalidBindings = configuration?.subscriptionIds.filter((id) => !eligible(id)) ?? [];
  const cloudMismatch = !!configuration?.cloudEnvironment
    && configuration.cloudEnvironment !== configuration.deploymentCloud;
  const attached = !!configuration?.cloudEnvironment || !!configuration?.subscriptionIds.length;
  const canSave = !!configuration && supportedCloud && !cloudMismatch
    && selected.length > 0 && selected.every(eligible) && !saving;

  const save = async () => {
    if (!configuration || !canSave || saving) return;
    const request = ++generation.current;
    setSaving(true);
    setError(null);
    setNotice(null);
    try {
      const result = await saveAssessmentEnvironment(systemId, {
        cloudEnvironment: configuration.deploymentCloud, subscriptionIds: selected,
      });
      if (request !== generation.current) return;
      setConfiguration(result);
      setSelected(result.subscriptionIds);
      setNotice('Environment attachment saved. Connectivity is checked separately below.');
      await readiness.refresh();
    } catch (err: unknown) {
      if (request === generation.current) setError(assessmentError(err, 'Unable to save Azure assessment environment.'));
    } finally {
      if (request === generation.current) setSaving(false);
    }
  };

  const detach = async () => {
    if (!configuration || !attached || saving || !window.confirm('Detach this Azure assessment environment? Historical assessments will be preserved.')) return;
    const request = ++generation.current;
    setSaving(true);
    setError(null);
    setNotice(null);
    try {
      await detachAssessmentEnvironment(systemId);
      if (request !== generation.current) return;
      setConfiguration({ ...configuration, cloudEnvironment: null, subscriptionIds: [] });
      setSelected([]);
      setNotice('Environment detached. Historical assessments are unchanged.');
      await readiness.refresh();
    } catch (err: unknown) {
      if (request === generation.current) setError(assessmentError(err, 'Unable to detach Azure assessment environment.'));
    } finally {
      if (request === generation.current) setSaving(false);
    }
  };

  const readinessMessage = readiness.loading
    ? 'Checking Azure assessment readiness…'
    : readiness.error?.message ?? readiness.result?.message;
  const readinessSuggestion = readiness.error?.suggestion ?? readiness.result?.suggestion;

  return (
    <section id="azure-assessment-environment" aria-labelledby="azure-assessment-environment-heading" className="scroll-mt-6 rounded-xl border border-gray-200 bg-white p-6 space-y-4">
      <h2 id="azure-assessment-environment-heading" className="text-lg font-semibold text-gray-900">Azure assessment environment</h2>
      <p className="text-sm text-gray-600">
        Attach real Azure subscriptions for Run Assessment. This configuration is separate from the descriptive Mission Profile and its review status.
        Saving an attachment does not verify Azure connectivity. No credentials are collected here.
      </p>
      {loading && <p role="status">Loading Azure assessment environment…</p>}
      {error && (
        <div role="alert" className="rounded-md bg-red-50 p-3 text-sm text-red-700">
          <p>{error.message}</p>
          {error.suggestion && <p className="mt-1">{error.suggestion}</p>}
          {!configuration && <button type="button" onClick={() => void load()} className="mt-2 underline">Retry</button>}
        </div>
      )}
      {notice && <p role="status" className="text-sm text-gray-700">{notice}</p>}
      {!loading && configuration && (
        <>
          <p className="text-sm">Deployment cloud: <strong>{configuration.deploymentCloud}</strong>. Only matching-cloud subscriptions are eligible.</p>
          {(!supportedCloud || cloudMismatch || invalidBindings.length > 0) && (
            <div role="alert" className="rounded-md bg-amber-50 p-3 text-sm text-amber-800">
              <p>Invalid or mismatched attachment. Review the retained bindings below. Detach the environment to replace incompatible legacy configuration.</p>
              {configuration.cloudEnvironment && <p>Attached cloud: {configuration.cloudEnvironment}</p>}
              {invalidBindings.filter((id) => !configuration.availableSubscriptions.some((subscription) => subscription.subscriptionId === id))
                .map((id) => <p key={id}>{id} — not registered or unavailable</p>)}
            </div>
          )}
          {configuration.availableSubscriptions.length === 0 && <p className="text-sm text-gray-600">No organization subscriptions are registered.</p>}
          <fieldset disabled={saving} className="space-y-2">
            <legend className="font-medium text-sm mb-2">Organization subscriptions</legend>
            {configuration.availableSubscriptions.map((subscription) => (
              <label key={subscription.subscriptionId} className="flex items-start gap-2 rounded border border-gray-200 p-3 text-sm">
                <input
                  type="checkbox"
                  checked={selected.includes(subscription.subscriptionId)}
                  disabled={!supportedCloud || !eligible(subscription.subscriptionId)}
                  onChange={(event) => setSelected((previous) => event.target.checked
                    ? [...previous, subscription.subscriptionId]
                    : previous.filter((id) => id !== subscription.subscriptionId))}
                  className="mt-1"
                />
                <span>
                  <span className="block font-medium">{subscription.displayName}</span>
                  <span className="block font-mono text-xs">{subscription.subscriptionId}</span>
                  <span className="block text-gray-500">{subscription.cloudEnvironment}{!eligible(subscription.subscriptionId) ? ' — unavailable in this deployment' : ''}</span>
                </span>
              </label>
            ))}
          </fieldset>
          <div className="flex flex-wrap items-center gap-3 text-sm">
            <button type="button" disabled={!canSave} onClick={() => void save()} className="rounded bg-indigo-600 px-3 py-2 text-white disabled:opacity-50">
              Save environment
            </button>
            {attached && <button type="button" disabled={saving} onClick={() => void detach()} className="rounded border border-gray-300 px-3 py-2 disabled:opacity-50">Detach environment</button>}
            {saving && <span role="status">Updating environment…</span>}
          </div>
          <div aria-live="polite" className="rounded bg-gray-50 p-3 text-sm">
            <p>{readinessMessage}</p>
            {readinessSuggestion && <p className="mt-1">{readinessSuggestion}</p>}
            <button type="button" disabled={readiness.loading || saving} onClick={() => void readiness.refresh()} className="mt-2 underline disabled:opacity-50">Retry readiness check</button>
          </div>
        </>
      )}
      <Link to="/settings/azure-subscriptions" className="inline-block text-sm text-indigo-700 underline">Manage organization subscriptions</Link>
    </section>
  );
}
