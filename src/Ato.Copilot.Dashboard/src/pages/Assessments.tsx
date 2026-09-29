import { useSystemContext } from '../components/layout/SystemLayout';
import AssessmentWorkflow from '../features/assessment-workspace/AssessmentWorkflow';

export default function Assessments() {
  const { detail } = useSystemContext();
  return <AssessmentWorkflow key={detail.systemId} systemId={detail.systemId} />;
}
