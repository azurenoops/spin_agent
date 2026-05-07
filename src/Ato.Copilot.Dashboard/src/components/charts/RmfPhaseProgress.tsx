import type { RmfPhaseProgress } from '../../types/dashboard';

interface RmfPhaseProgressProps {
  phases: RmfPhaseProgress[];
}

const circleColors: Record<string, string> = {
  complete: 'bg-green-500 text-white',
  current: 'bg-indigo-500 text-white',
  upcoming: 'bg-gray-200 text-gray-500',
};

export default function RmfPhaseProgressComponent({ phases }: RmfPhaseProgressProps) {
  return (
    <div>
      <div className="relative flex items-start justify-between">
        {/* Background connector line */}
        <div
          className="absolute top-4 bg-gray-200"
          style={{ left: `calc(${100 / phases.length / 2}%)`, right: `calc(${100 / phases.length / 2}%)`, height: '2px' }}
        />
        {/* Completed connector line overlay */}
        {(() => {
          const lastCompleteIdx = phases.reduce((acc, p, i) => (p.status === 'complete' ? i : acc), -1);
          if (lastCompleteIdx < 0) return null;
          const stepWidth = 100 / phases.length;
          const startPct = stepWidth / 2;
          const endPct = stepWidth * lastCompleteIdx + stepWidth / 2;
          if (endPct <= startPct) return null;
          return (
            <div
              className="absolute top-4 bg-green-500"
              style={{ left: `calc(${startPct}%)`, width: `calc(${endPct - startPct}%)`, height: '2px' }}
            />
          );
        })()}

        {phases.map((phase) => (
          <div
            key={phase.phase}
            className="relative z-10 flex flex-col items-center"
            style={{ width: `${100 / phases.length}%` }}
          >
            <div
              className={`flex h-8 w-8 items-center justify-center rounded-full text-xs font-bold ${circleColors[phase.status]}`}
            >
              {phase.ordinal}
            </div>
            <span className="mt-1 text-[11px] text-gray-600 text-center">{phase.phase}</span>
            {phase.status === 'current' && (
              <span className="text-[10px] font-medium text-indigo-600">
                {phase.completionPercent.toFixed(0)}%
              </span>
            )}
          </div>
        ))}
      </div>
    </div>
  );
}
