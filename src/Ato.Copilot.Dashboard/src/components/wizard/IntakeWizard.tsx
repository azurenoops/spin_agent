import SystemSetupJourney from './SystemSetupJourney';
import { Link } from '../../features/workspaces/workspaceNavigation';

export default function IntakeWizard({ setupTenantId, setupSystemId, onClose }: {
  setupTenantId?: string; setupSystemId?: string; onClose: () => void;
}) {
  if (!setupTenantId) return (
    <section className="p-6">
      <p role="alert">Choose an authorized organization workspace to prepare a system.</p>
      <Link className="mt-3 inline-block text-purple-700 underline" to="/setup">Choose a setup path</Link>
      <button type="button" className="ml-4 rounded border px-3 py-2" onClick={onClose}>Close setup</button>
    </section>
  );
  return <SystemSetupJourney key={`${setupTenantId}:${setupSystemId ?? 'new'}`}
    tenantId={setupTenantId} resumeSystemId={setupSystemId} onClose={onClose} />;
}
