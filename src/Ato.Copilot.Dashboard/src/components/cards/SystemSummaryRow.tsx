import { useNavigate } from 'react-router-dom';
import type { PortfolioSystemSummary } from '../../types/dashboard';
import AtoCountdown from './AtoCountdown';

interface SystemSummaryRowProps {
  system: PortfolioSystemSummary;
}

const rmfBadgeColor: Record<string, string> = {
  Prepare: 'bg-gray-100 text-gray-700',
  Categorize: 'bg-blue-100 text-blue-700',
  Select: 'bg-indigo-100 text-indigo-700',
  Implement: 'bg-purple-100 text-purple-700',
  Assess: 'bg-amber-100 text-amber-700',
  Authorize: 'bg-green-100 text-green-700',
  Monitor: 'bg-teal-100 text-teal-700',
};

export default function SystemSummaryRow({ system }: SystemSummaryRowProps) {
  const navigate = useNavigate();

  return (
    <tr
      className="cursor-pointer border-b border-gray-100 hover:bg-gray-50"
      onClick={() => navigate(`/systems/${system.systemId}`)}
    >
      <td className="py-3 pl-4 pr-3">
        <span className="font-medium text-gray-900">{system.name}</span>
      </td>
      <td className="px-3 py-3 text-sm text-gray-500">{system.impactLevel}</td>
      <td className="px-3 py-3">
        <span
          className={`inline-flex rounded-full px-2 py-0.5 text-xs font-medium ${rmfBadgeColor[system.currentRmfPhase] ?? 'bg-gray-100 text-gray-700'}`}
        >
          {system.currentRmfPhase}
        </span>
      </td>
      <td className="px-3 py-3 text-sm">
        <span className="font-semibold">{system.complianceScore.toFixed(1)}%</span>
        {system.complianceScoreDelta !== 0 && (
          <span
            className={`ml-1 text-xs ${system.complianceScoreDelta > 0 ? 'text-green-600' : 'text-red-600'}`}
          >
            {system.complianceScoreDelta > 0 ? '+' : ''}
            {system.complianceScoreDelta.toFixed(1)}
          </span>
        )}
      </td>
      <td className="px-3 py-3">
        <AtoCountdown
          daysRemaining={system.atoDaysRemaining}
          severity={system.atoSeverity}
        />
      </td>
      <td className="px-3 py-3 text-sm text-gray-500">
        {system.openPoamCount}
        {system.overduePoamCount > 0 && (
          <span className="ml-1 text-xs text-red-600">({system.overduePoamCount} overdue)</span>
        )}
      </td>
    </tr>
  );
}
