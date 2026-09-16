import { useState } from 'react';
import type { ConflictStatus, EmassConflict } from '../api/emass-status';

type Resolution = Exclude<ConflictStatus, 'Unresolved'>;

interface Props {
  conflicts: EmassConflict[];
  onResolve: (conflictId: string, resolution: Resolution) => Promise<void>;
}

export default function EmassConflictList({ conflicts, onResolve }: Props) {
  const [resolving, setResolving] = useState<string | null>(null);

  async function resolve(conflictId: string, resolution: Resolution) {
    setResolving(conflictId);
    try {
      await onResolve(conflictId, resolution);
    } finally {
      setResolving(null);
    }
  }

  async function acceptAll() {
    if (!window.confirm(`Accept eMASS values for all ${conflicts.length} unresolved conflicts?`)) return;
    setResolving('all');
    try {
      await Promise.all(conflicts.map((conflict) => onResolve(conflict.id, 'AcceptEmass')));
    } finally {
      setResolving(null);
    }
  }

  if (conflicts.length === 0) {
    return <p className="px-5 py-8 text-center text-sm text-gray-500">No unresolved conflicts.</p>;
  }

  return (
    <div className="overflow-hidden">
      <div className="flex items-center justify-between border-b border-gray-200 bg-gray-50 px-5 py-3">
        <p className="text-sm text-gray-600">{conflicts.length} unresolved</p>
        <button
          type="button"
          onClick={acceptAll}
          disabled={resolving !== null}
          className="rounded-md border border-gray-300 bg-white px-3 py-1.5 text-xs font-medium text-gray-700 hover:bg-gray-100 disabled:opacity-50"
        >
          Accept all eMASS
        </button>
      </div>
      <div className="overflow-x-auto">
        <table className="min-w-full divide-y divide-gray-200 text-left text-sm">
          <thead className="bg-white text-xs uppercase text-gray-500">
            <tr>
              <th className="px-5 py-3 font-medium">Record / field</th>
              <th className="px-5 py-3 font-medium">SPIN value</th>
              <th className="px-5 py-3 font-medium">eMASS value</th>
              <th className="px-5 py-3 text-right font-medium">Resolution</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-gray-100 bg-white">
            {conflicts.map((conflict) => (
              <tr key={conflict.id}>
                <td className="px-5 py-4 align-top">
                  <p className="font-medium text-gray-900">{conflict.entityId ?? conflict.entityType}</p>
                  <p className="mt-1 text-xs text-gray-500">{conflict.fieldName}</p>
                </td>
                <td className="max-w-xs whitespace-pre-wrap px-5 py-4 align-top text-gray-700">{conflict.spinValue ?? 'Empty'}</td>
                <td className="max-w-xs whitespace-pre-wrap px-5 py-4 align-top text-gray-700">{conflict.emassValue ?? 'Empty'}</td>
                <td className="px-5 py-4 align-top">
                  <div className="flex justify-end gap-2">
                    <button type="button" onClick={() => resolve(conflict.id, 'KeepSpin')} disabled={resolving !== null} className="rounded-md border border-gray-300 px-2.5 py-1.5 text-xs font-medium text-gray-700 hover:bg-gray-50 disabled:opacity-50">Keep SPIN</button>
                    <button type="button" onClick={() => resolve(conflict.id, 'AcceptEmass')} disabled={resolving !== null} className="rounded-md bg-indigo-600 px-2.5 py-1.5 text-xs font-medium text-white hover:bg-indigo-700 disabled:opacity-50">Accept eMASS</button>
                    <button type="button" onClick={() => resolve(conflict.id, 'Deferred')} disabled={resolving !== null} className="rounded-md px-2.5 py-1.5 text-xs font-medium text-gray-500 hover:bg-gray-100 disabled:opacity-50">Defer</button>
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}