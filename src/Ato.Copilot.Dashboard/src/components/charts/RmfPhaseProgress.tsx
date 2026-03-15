import type { RmfPhaseProgress } from '../../types/dashboard';

interface RmfPhaseProgressProps {
  phases: RmfPhaseProgress[];
}

const phaseColors: Record<string, string> = {
  complete: 'bg-green-500',
  current: 'bg-blue-500',
  upcoming: 'bg-gray-200',
};

export default function RmfPhaseProgressComponent({ phases }: RmfPhaseProgressProps) {
  return (
    <div className="flex items-center gap-1">
      {phases.map((phase, i) => (
        <div key={phase.phase} className="flex flex-1 flex-col items-center">
          {/* Connector */}
          <div className="flex w-full items-center">
            {i > 0 && (
              <div
                className={`h-0.5 flex-1 ${phase.status === 'complete' || phases[i - 1]?.status === 'complete' ? 'bg-green-500' : 'bg-gray-200'}`}
              />
            )}
            <div
              className={`h-8 w-8 rounded-full ${phaseColors[phase.status]} flex items-center justify-center text-xs font-bold ${phase.status === 'upcoming' ? 'text-gray-500' : 'text-white'}`}
            >
              {phase.ordinal + 1}
            </div>
            {i < phases.length - 1 && (
              <div
                className={`h-0.5 flex-1 ${phase.status === 'complete' ? 'bg-green-500' : 'bg-gray-200'}`}
              />
            )}
          </div>
          <span className="mt-1 text-[10px] text-gray-600">{phase.phase}</span>
          {phase.status === 'current' && (
            <span className="text-[10px] font-medium text-blue-600">
              {phase.completionPercent.toFixed(0)}%
            </span>
          )}
        </div>
      ))}
    </div>
  );
}
