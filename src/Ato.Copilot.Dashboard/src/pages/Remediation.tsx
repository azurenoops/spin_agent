import { useSystemContext } from '../components/layout/SystemLayout';
import FindingsWorkspace from '../features/remediation-workspace/FindingsWorkspace';

export default function Remediation() {
  const { detail } = useSystemContext();
  return <FindingsWorkspace systemId={detail.systemId} />;
}
