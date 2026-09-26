import AssessmentEnvironmentPanel from '../components/AssessmentEnvironmentPanel';
import { useSystemContext } from '../components/layout/SystemLayout';
import { Link } from '../features/workspaces/workspaceNavigation';

export default function AssessmentEnvironment() {
  const { detail } = useSystemContext();
  const systemPath = `/systems/${encodeURIComponent(detail.systemId)}`;

  return (
    <div className="space-y-6">
      <div>
        <Link to={`${systemPath}/assessments`} className="text-sm text-indigo-700 underline dark:text-indigo-200">Back to Assessments</Link>
        <h1 className="mt-3 text-2xl font-bold text-gray-900 dark:text-gray-100">Azure assessment configuration</h1>
        <p className="mt-2 text-sm text-gray-600 dark:text-gray-300">
          Configure Azure subscriptions used to assess {detail.name}. Describe where the system runs in{' '}
          <Link to={`${systemPath}/profile/EnvironmentAndDeployment`} className="text-indigo-700 underline dark:text-indigo-200">Environment</Link>.
        </p>
      </div>
      <AssessmentEnvironmentPanel systemId={detail.systemId} />
    </div>
  );
}
