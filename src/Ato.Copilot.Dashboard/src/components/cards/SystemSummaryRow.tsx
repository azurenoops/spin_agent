import { useNavigate } from 'react-router-dom';
import type { PortfolioSystemSummary } from '../../types/dashboard';
import AtoCountdown from './AtoCountdown';

interface SystemSummaryRowProps {
  system: PortfolioSystemSummary;
  onEdit?: (system: PortfolioSystemSummary) => void;
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

export default function SystemSummaryRow({ system, onEdit }: SystemSummaryRowProps) {
  const navigate = useNavigate();

  return (
    <tr
      className="cursor-pointer border-b border-gray-100 hover:bg-gray-50"
      onClick={() => navigate(`/systems/${system.systemId}`)}
    >
      <td className="py-3 pl-4 pr-3">
        <span className="font-medium text-gray-900">{system.name}</span>
        {system.isSetupComplete === false && (
          <span className="ml-2 inline-flex rounded-full bg-amber-100 px-2 py-0.5 text-xs font-medium text-amber-800">
            Setup Incomplete
          </span>
        )}
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
      <td className="px-3 py-3 text-right">
        <button
          type="button"
          onClick={(e) => { e.stopPropagation(); onEdit?.(system); }}
          className="rounded p-1 text-gray-400 hover:bg-gray-100 hover:text-gray-600"
          title="Edit system"
        >
          <svg className="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
            <path strokeLinecap="round" strokeLinejoin="round" d="m16.862 4.487 1.687-1.688a1.875 1.875 0 1 1 2.652 2.652L10.582 16.07a4.5 4.5 0 0 1-1.897 1.13L6 18l.8-2.685a4.5 4.5 0 0 1 1.13-1.897l8.932-8.931Zm0 0L19.5 7.125M18 14v4.75A2.25 2.25 0 0 1 15.75 21H5.25A2.25 2.25 0 0 1 3 18.75V8.25A2.25 2.25 0 0 1 5.25 6H10" />
          </svg>
        </button>
      </td>
    </tr>
  );
}
