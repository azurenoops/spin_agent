import { useEffect, useState } from 'react';
import {
  getCategorizationHistory,
  type CategorizationHistoryEntry,
} from '../../api/systemDetail';

interface CategorizationHistoryPanelProps {
  systemId: string;
}

export default function CategorizationHistoryPanel({ systemId }: CategorizationHistoryPanelProps) {
  const [entries, setEntries] = useState<CategorizationHistoryEntry[]>([]);
  const [loading, setLoading] = useState(true);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    let active = true;
    void getCategorizationHistory(systemId)
      .then((history) => {
        if (active) setEntries(history);
      })
      .catch(() => {
        if (active) setFailed(true);
      })
      .finally(() => {
        if (active) setLoading(false);
      });
    return () => { active = false; };
  }, [systemId]);

  return (
    <section className="mb-6 border-y border-gray-200 bg-white py-4" aria-labelledby="categorization-history-heading">
      <h2 id="categorization-history-heading" className="text-sm font-semibold text-gray-700">
        Categorization History
      </h2>
      {loading && <p className="mt-3 text-sm text-gray-500">Loading categorization history...</p>}
      {!loading && failed && (
        <p className="mt-3 text-sm text-red-700">Categorization history is unavailable.</p>
      )}
      {!loading && !failed && entries.length === 0 && (
        <p className="mt-3 text-sm text-gray-500">No categorization decisions recorded.</p>
      )}
      {entries.length > 0 && (
        <ol className="mt-3 divide-y divide-gray-100">
          {entries.map((entry) => (
            <li key={entry.id} className="grid gap-2 py-3 md:grid-cols-[7rem_10rem_1fr]">
              <div>
                <span className="text-sm font-semibold text-gray-900">Version {entry.version}</span>
                {entry.isCurrent && (
                  <span className="ml-2 text-xs font-medium text-emerald-700">Current</span>
                )}
              </div>
              <div className="text-sm text-gray-700">
                <div>{entry.newOverallImpact}</div>
                <div className="text-xs text-gray-500">
                  C:{entry.newConfidentialityImpact} I:{entry.newIntegrityImpact} A:{entry.newAvailabilityImpact}
                </div>
              </div>
              <div className="min-w-0 text-sm text-gray-700">
                <div>{entry.justification || 'No rationale provided'}</div>
                <div className="mt-1 text-xs text-gray-500">
                  {entry.changedBy} | {new Date(entry.changedAt).toLocaleString()}
                </div>
              </div>
            </li>
          ))}
        </ol>
      )}
    </section>
  );
}