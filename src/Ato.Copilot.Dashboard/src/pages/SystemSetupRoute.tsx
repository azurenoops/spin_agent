import SystemSetupJourney from '../components/wizard/SystemSetupJourney';
import { useNavigate, useParams, useSearchParams, useWorkspaceTarget } from '../features/workspaces/workspaceNavigation';
import SystemSourceReview from '../features/onboarding/SystemSourceReview';

export default function SystemSetupRoute() {
  const workspace = useWorkspaceTarget();
  const { id, systemId } = useParams<{ id?: string; systemId?: string }>();
  const navigate = useNavigate();
  const [query] = useSearchParams();
  if (workspace?.kind !== 'organization' || workspace.mode === 'support') {
    return <p role="alert" className="p-6">Choose an authorized organization workspace to prepare a system.</p>;
  }
  const sourceKind = query.get('source');
  const receipt = query.get('receipt');
  if ((sourceKind === 'emass' || sourceKind === 'ssp-pdf') && receipt && (systemId ?? id)) {
    return <SystemSourceReview key={`${workspace.tenantId}:${systemId ?? id}:${sourceKind}:${receipt}`}
      tenantId={workspace.tenantId} systemId={(systemId ?? id)!} kind={sourceKind} receiptId={receipt} />;
  }
  return <SystemSetupJourney key={`${workspace.tenantId}:${systemId ?? id ?? 'new'}`}
    tenantId={workspace.tenantId} resumeSystemId={systemId ?? id}
    onClose={() => navigate('/systems')} />;
}
