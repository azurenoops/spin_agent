import { useSystemContext } from '../components/layout/SystemLayout';
import SystemReadinessOverview from '../features/systems/SystemReadinessOverview';

export default function SystemDetail() {
  const { detail } = useSystemContext();

  return <SystemReadinessOverview key={detail.systemId} systemId={detail.systemId}
    systemName={detail.name} currentPhase={detail.currentRmfPhase} />;
}
