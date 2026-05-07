export default function PoamSeverityHeatbar({ catI, catII, catIII }: { catI: number; catII: number; catIII: number }) {
  const total = catI + catII + catIII;
  if (total === 0) return null;
  return (
    <div className="rounded-xl border border-gray-200 bg-white p-4 shadow-sm">
      <p className="mb-2 text-xs font-medium uppercase tracking-wider text-gray-500">Severity Distribution</p>
      <div className="flex h-4 overflow-hidden rounded-full">
        {catI > 0 && <div className="bg-red-500" style={{ width: `${(catI / total) * 100}%` }} title={`CAT I: ${catI}`} />}
        {catII > 0 && <div className="bg-amber-400" style={{ width: `${(catII / total) * 100}%` }} title={`CAT II: ${catII}`} />}
        {catIII > 0 && <div className="bg-indigo-400" style={{ width: `${(catIII / total) * 100}%` }} title={`CAT III: ${catIII}`} />}
      </div>
      <div className="mt-2 flex gap-4 text-xs text-gray-500">
        <span className="flex items-center gap-1"><span className="inline-block h-2 w-2 rounded-full bg-red-500" /> CAT I: {catI}</span>
        <span className="flex items-center gap-1"><span className="inline-block h-2 w-2 rounded-full bg-amber-400" /> CAT II: {catII}</span>
        <span className="flex items-center gap-1"><span className="inline-block h-2 w-2 rounded-full bg-indigo-400" /> CAT III: {catIII}</span>
      </div>
    </div>
  );
}
