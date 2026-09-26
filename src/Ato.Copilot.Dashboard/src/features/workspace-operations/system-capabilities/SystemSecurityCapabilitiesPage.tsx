import { Link, Navigate, useLocation, useParams } from '../../workspaces/workspaceNavigation';
import { useWorkspaceSession } from '../../workspaces/WorkspaceBoundary';
import { useSystemContext } from '../../../components/layout/SystemLayout';
import { errorClass } from '../workspaceUi';
import { SetupWizard } from '../WorkspaceOperationsPage';
import SystemCapabilityList from './SystemCapabilityList';
import SystemCapabilityDetail from './SystemCapabilityDetail';
import SystemCapabilitySetup from './SystemCapabilitySetup';

export default function SystemSecurityCapabilitiesPage() {
  const { id, '*': path } = useParams<{ id: string; '*': string }>();
  const session = useWorkspaceSession();
  const { detail } = useSystemContext();
  const location = useLocation();
  if (!id || session?.target.kind !== 'organization'
    || session.systemAccess?.systemId !== id || session.systemAccess.permissions.canRead !== true
    || detail.systemId !== id) {
    return <p role="alert" className={errorClass}>The selected organization and system could not be verified. Choose the system again.</p>;
  }
  const tenantId = session.target.tenantId;
  const base = `/systems/${encodeURIComponent(id)}/security-capabilities`;
  const segments = (path ?? '').split('/').filter(Boolean);
  const query = new URLSearchParams(location.search);
  const legacySetup = segments[0] === 'setup' || query.get('dialog') === 'capability';
  if (legacySetup && (query.has('operation') || query.has('key'))) {
    return <><SystemCapabilityList tenantId={tenantId} systemId={id} systemName={detail.name} />
      <SetupWizard tenantId={tenantId} routeSystemId={id} /></>;
  }
  if (legacySetup) {
    query.delete('dialog');
    query.delete('step');
    if (query.has('setupSource')) query.set('source', query.get('setupSource')!);
    const record = query.get('setupRecord') ?? query.get('record');
    if (record) query.set('recordId', record);
    return <Navigate replace to={{ pathname: `${base}/add`, search: query.size ? `?${query}` : '', hash: location.hash }} />;
  }
  if (segments.length === 0) return <SystemCapabilityList key={`${tenantId}:${id}`} tenantId={tenantId} systemId={id} systemName={detail.name} />;
  if (segments.length === 1 && segments[0] === 'add') {
    return <SystemCapabilitySetup key={`${tenantId}:${id}`} tenantId={tenantId} systemId={id} />;
  }
  const source = segments[0];
  const recordId = segments[1];
  if (segments.length === 2 && (source === 'local' || source === 'provider') && recordId) {
    if (query.get('recordType') === 'component') {
      query.set('view', 'component');
      query.set('componentId', recordId);
      query.set('componentSource', source);
      query.delete('recordType');
      return <Navigate replace to={{ pathname: base, search: `?${query}`, hash: location.hash }} />;
    }
    return <SystemCapabilityDetail key={`${tenantId}:${id}:${source}:${recordId}`}
      tenantId={tenantId} systemId={id} source={source} recordId={recordId} />;
  }
  return <div role="alert" className={errorClass}><p>Invalid system capability link.</p>
    <Link className="mt-2 inline-block underline" to={base}>Return to this system&apos;s capabilities</Link></div>;
}
