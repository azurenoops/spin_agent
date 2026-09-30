import { useSystemContext } from '../components/layout/SystemLayout';
import { Link } from '../features/workspaces/workspaceNavigation';
import { useAssessmentReadiness } from '../hooks/useAssessmentReadiness';
import { useSystemMutationPermission } from '../components/permissions/useSystemMutationPermission';

export default function AssessmentEnvironment() {
  const { detail } = useSystemContext();
  const systemPath = `/systems/${encodeURIComponent(detail.systemId)}`;
  const canRun = useSystemMutationPermission(detail.systemId, 'canRunAssessments');
  const readiness = useAssessmentReadiness(detail.systemId, canRun);

  return (
    <div className="space-y-6">
      <div>
        <Link to={`${systemPath}/assessments`} className="text-sm text-indigo-700 underline dark:text-indigo-200">Back to Assessments</Link>
        <h1 className="mt-3 text-2xl font-bold text-gray-900 dark:text-gray-100">Azure assessment configuration</h1>
        <p className="mt-2 text-sm text-gray-600 dark:text-gray-300">
          Assessments reuse the environment attached to {detail.name}. Manage subscriptions and resource scope in{' '}
          <Link to={`${systemPath}/profile/EnvironmentAndDeployment`} className="text-indigo-700 underline dark:text-indigo-200">Environment</Link>.
        </p>
      </div>
      <section className="rounded-lg border border-slate-200 bg-white p-5 dark:border-slate-700 dark:bg-slate-900">
        <h2 className="font-semibold">System subscriptions</h2>
        <p className="mt-2 text-sm text-slate-500">There is no separate assessment subscription selection. Subscription entitlement, reviewed resource scope, collector access and assessment execution permissions are checked independently. A provider relationship is not required.</p>
        <Link className="mt-3 inline-block text-indigo-700 underline" to={`${systemPath}/profile/EnvironmentAndDeployment`}>Manage system subscriptions</Link>
        <button type="button" disabled={!canRun || readiness.loading} className="ml-4 rounded border px-3 py-2 text-sm disabled:opacity-50"
          onClick={() => void readiness.refresh()}>Check assessment prerequisites</button>
        {!canRun && <p className="mt-2 text-xs text-slate-500">Your current server-granted permissions do not permit assessment execution.</p>}
        {readiness.error && <p role="alert" className="mt-3 text-sm text-red-700">{readiness.error.message}</p>}
        {readiness.result && <p role="status" className="mt-3 text-sm">{readiness.result.message} {readiness.result.suggestion}</p>}
      </section>
    </div>
  );
}
