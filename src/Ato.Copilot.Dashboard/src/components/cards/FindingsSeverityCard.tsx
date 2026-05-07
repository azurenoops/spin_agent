import HelpTooltip from '../help/HelpTooltip';

interface FindingsSeverityCardProps {
  catI: number;
  catII: number;
  catIII: number;
}

export default function FindingsSeverityCard({ catI, catII, catIII }: FindingsSeverityCardProps) {
  const total = catI + catII + catIII;
  if (total === 0) {
    return (
      <div className="rounded-lg border border-gray-200 bg-white p-4 shadow-sm">
        <div className="flex items-center">
          <p className="text-sm font-medium text-gray-500">Findings</p>
          <HelpTooltip helpKey="findings" />
        </div>
        <p className="mt-1 text-2xl font-bold text-green-600">0</p>
        <p className="text-xs text-gray-400">No open findings</p>
      </div>
    );
  }

  return (
    <div className="rounded-lg border border-gray-200 bg-white p-4 shadow-sm">
      <div className="flex items-center">
        <p className="text-sm font-medium text-gray-500">Findings</p>
        <HelpTooltip helpKey="findings" />
      </div>
      <p className="mt-1 text-2xl font-bold">{total}</p>
      {/* Stacked bar */}
      <div className="mt-2 flex h-3 overflow-hidden rounded-full">
        {catI > 0 && (
          <div
            className="bg-red-500"
            style={{ width: `${(catI / total) * 100}%` }}
            title={`CAT I: ${catI}`}
          />
        )}
        {catII > 0 && (
          <div
            className="bg-yellow-500"
            style={{ width: `${(catII / total) * 100}%` }}
            title={`CAT II: ${catII}`}
          />
        )}
        {catIII > 0 && (
          <div
            className="bg-indigo-400"
            style={{ width: `${(catIII / total) * 100}%` }}
            title={`CAT III: ${catIII}`}
          />
        )}
      </div>
      <div className="mt-1 flex gap-3 text-xs text-gray-500">
        <span className="flex items-center gap-1">
          <span className="h-2 w-2 rounded-full bg-red-500" /> I: {catI}
        </span>
        <span className="flex items-center gap-1">
          <span className="h-2 w-2 rounded-full bg-yellow-500" /> II: {catII}
        </span>
        <span className="flex items-center gap-1">
          <span className="h-2 w-2 rounded-full bg-indigo-400" /> III: {catIII}
        </span>
      </div>
    </div>
  );
}
