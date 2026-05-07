import type { RoadmapPhase } from '../../types/dashboard';

interface Props {
  phases: RoadmapPhase[];
  totalWeeks?: number;
}

const statusColors: Record<string, string> = {
  Complete: 'bg-green-500',
  InProgress: 'bg-indigo-500',
  NotStarted: 'bg-gray-300',
};

const statusBadgeColors: Record<string, string> = {
  Complete: 'bg-green-100 text-green-800',
  InProgress: 'bg-indigo-100 text-indigo-800',
  NotStarted: 'bg-gray-100 text-gray-600',
};

export default function RoadmapTimeline({ phases, totalWeeks }: Props) {
  const maxWeek = totalWeeks ?? Math.max(...phases.map((p) => p.targetEndWeek ?? 0), 12);

  return (
    <div className="space-y-3">
      {/* Week axis labels */}
      <div className="flex items-center text-xs text-gray-400">
        <div className="w-40 shrink-0" />
        <div className="flex flex-1 justify-between">
          {Array.from({ length: Math.min(maxWeek, 20) }, (_, i) => (
            <span key={i}>Wk {i + 1}</span>
          ))}
        </div>
      </div>

      {/* Phase bars */}
      {phases.map((phase) => {
        const start = phase.targetStartWeek ?? 1;
        const end = phase.targetEndWeek ?? start + 1;
        const leftPct = ((start - 1) / maxWeek) * 100;
        const widthPct = ((end - start + 1) / maxWeek) * 100;
        const progress =
          phase.totalItemCount > 0
            ? (phase.completedItemCount / phase.totalItemCount) * 100
            : 0;

        return (
          <div key={phase.phaseId} className="flex items-center gap-3">
            {/* Phase name */}
            <div className="w-40 shrink-0 text-right">
              <p className="truncate text-sm font-medium text-gray-700">{phase.name}</p>
              <p className="text-xs text-gray-500">{phase.estimatedEffortDays}d effort</p>
            </div>

            {/* Bar track */}
            <div className="relative flex-1 h-8 rounded bg-gray-100">
              {/* Phase bar */}
              <div
                className={`absolute top-0 h-full rounded ${statusColors[phase.status] ?? 'bg-gray-300'} opacity-30`}
                style={{ left: `${leftPct}%`, width: `${widthPct}%` }}
              />
              {/* Progress fill */}
              <div
                className={`absolute top-0 h-full rounded ${statusColors[phase.status] ?? 'bg-gray-300'}`}
                style={{
                  left: `${leftPct}%`,
                  width: `${(widthPct * progress) / 100}%`,
                }}
              />
              {/* Label on bar */}
              <div
                className="absolute top-1 flex items-center gap-1 text-xs font-medium text-white"
                style={{ left: `${leftPct + 1}%` }}
              >
                <span>
                  {phase.completedItemCount}/{phase.totalItemCount}
                </span>
              </div>
            </div>

            {/* Status badge */}
            <span
              className={`shrink-0 rounded-full px-2 py-0.5 text-xs font-medium ${statusBadgeColors[phase.status] ?? 'bg-gray-100 text-gray-600'}`}
            >
              {phase.status}
            </span>
          </div>
        );
      })}
    </div>
  );
}
