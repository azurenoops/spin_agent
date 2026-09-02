import { useNavigate } from 'react-router-dom';
import type { ReactElement } from 'react';
import type { PortfolioSystemSummary } from '../../types/dashboard';
import AtoCountdown from './AtoCountdown';

interface SystemSummaryRowProps {
  system: PortfolioSystemSummary;
  onEdit?: (system: PortfolioSystemSummary) => void;
  onDelete?: (system: PortfolioSystemSummary) => void;
}

const rmfBadgeColor: Record<string, string> = {
  Prepare: 'bg-gray-100 text-gray-700',
  Categorize: 'bg-indigo-100 text-indigo-700',
  Select: 'bg-indigo-100 text-indigo-700',
  Implement: 'bg-purple-100 text-purple-700',
  Assess: 'bg-amber-100 text-amber-700',
  Authorize: 'bg-green-100 text-green-700',
  Monitor: 'bg-teal-100 text-teal-700',
};

// fix/433: Systems in early RMF phases (Prepare, Categorize) legitimately lack
// a baseline, boundary, and role assignments. The amber "Setup Incomplete" badge
// is misleading — these systems ARE correctly set up for their phase. We use a
// phase-aware label: Prepare/Categorize → "Phase Setup" (informational, gray),
// later phases → "Setup Incomplete" (amber, calls for action).
function setupBadge(phase: string, isSetupComplete: boolean): ReactElement | null {
  if (isSetupComplete) return null;
  const earlyPhase = phase === 'Prepare' || phase === 'Categorize';
  if (earlyPhase) {
    return (
      <span className="ml-2 inline-flex rounded-full bg-gray-100 px-2 py-0.5 text-xs font-medium text-gray-500">
        In Setup
      </span>
    );
  }
  return (
    <span className="ml-2 inline-flex rounded-full bg-amber-100 px-2 py-0.5 text-xs font-medium text-amber-800">
      Setup Incomplete
    </span>
  );
}

export default function SystemSummaryRow({ system, onEdit, onDelete }: SystemSummaryRowProps) {
  const navigate = useNavigate();

  // fix/433: ImpactLevel === 'Unknown' means no baseline configured. Render
  // 'Not Configured' to avoid confusion with a genuine data-retrieval failure.
  const displayImpactLevel =
    system.impactLevel === 'Unknown' ? 'Not Configured' : system.impactLevel;

  return (
    <tr
      className="cursor-pointer border-b border-gray-100 hover:bg-gray-50"
      onClick={() => navigate(`/systems/${system.systemId}`)}
    >
      <td className="py-3 pl-4 pr-3">
        <span className="font-medium text-gray-900">{system.name}</span>
        {setupBadge(system.currentRmfPhase, system.isSetupComplete)}
      </td>
      <td className="px-3 py-3 text-sm text-gray-500">{displayImpactLevel}</td>
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
        <div className="flex items-center justify-end gap-1">
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
          {onDelete && (
            <button
              type="button"
              onClick={(e) => { e.stopPropagation(); onDelete(system); }}
              className="rounded p-1 text-gray-400 hover:bg-red-50 hover:text-red-600"
              title="Delete system"
            >
              <svg className="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
                <path strokeLinecap="round" strokeLinejoin="round" d="m14.74 9-.346 9m-4.788 0L9.26 9m9.968-3.21c.342.052.682.107 1.022.166m-1.022-.165L18.16 19.673a2.25 2.25 0 0 1-2.244 2.077H8.084a2.25 2.25 0 0 1-2.244-2.077L4.772 5.79m14.456 0a48.108 48.108 0 0 0-3.478-.397m-12 .562c.34-.059.68-.114 1.022-.165m0 0a48.11 48.11 0 0 1 3.478-.397m7.5 0v-.916c0-1.18-.91-2.164-2.09-2.201a51.964 51.964 0 0 0-3.32 0c-1.18.037-2.09 1.022-2.09 2.201v.916m7.5 0a48.667 48.667 0 0 0-7.5 0" />
              </svg>
            </button>
          )}
        </div>
      </td>
    </tr>
  );
}
