import { useState } from 'react';
import type { BulkCreateFromFindingsRequest, BulkCreateResponse } from '../../types/poam';

interface FindingItem {
  id: string;
  controlId: string;
  title: string;
  severity: string;
  hasActivePoam: boolean;
}

interface PostImportPoamPromptProps {
  findings: FindingItem[];
  systemId: string;
  onBulkCreate: (request: BulkCreateFromFindingsRequest) => Promise<BulkCreateResponse>;
  onClose: () => void;
}

export default function PostImportPoamPrompt({ findings, systemId: _systemId, onBulkCreate, onClose }: PostImportPoamPromptProps) {
  const [selected, setSelected] = useState<Set<string>>(
    new Set(findings.filter(f => !f.hasActivePoam).map(f => f.id)),
  );
  const [loading, setLoading] = useState(false);
  const [result, setResult] = useState<BulkCreateResponse | null>(null);

  const eligible = findings.filter(f => !f.hasActivePoam);
  const grayed = findings.filter(f => f.hasActivePoam);

  const toggleAll = () => {
    if (selected.size === eligible.length) {
      setSelected(new Set());
    } else {
      setSelected(new Set(eligible.map(f => f.id)));
    }
  };

  const toggle = (id: string) => {
    const next = new Set(selected);
    if (next.has(id)) next.delete(id);
    else next.add(id);
    setSelected(next);
  };

  const handleCreate = async () => {
    if (selected.size === 0) return;
    setLoading(true);
    try {
      const res = await onBulkCreate({ findingIds: Array.from(selected) });
      setResult(res);
    } catch {
      // handled by parent
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/30" onClick={onClose}>
      <div className="w-full max-w-xl rounded-xl bg-white p-6 shadow-xl" onClick={e => e.stopPropagation()}>
        <h2 className="mb-1 text-lg font-bold text-gray-900">Create POA&amp;M Items from Findings</h2>
        <p className="mb-4 text-sm text-gray-500">
          {eligible.length} finding(s) without active POA&amp;M items detected.
          {grayed.length > 0 && ` ${grayed.length} finding(s) already have active POA&Ms.`}
        </p>

        {result ? (
          <div className="space-y-3">
            <div className="rounded-lg bg-green-50 p-4">
              <p className="font-medium text-green-800">
                {result.created} POA&amp;M item(s) created, {result.skippedDuplicates} duplicate(s) skipped
              </p>
            </div>
            <div className="flex justify-end">
              <button onClick={onClose} className="rounded-lg bg-gray-100 px-4 py-2 text-sm hover:bg-gray-200">
                Close
              </button>
            </div>
          </div>
        ) : (
          <>
            <div className="max-h-64 overflow-y-auto rounded-lg border">
              {/* Select all header */}
              <div className="sticky top-0 flex items-center gap-3 border-b bg-gray-50 px-3 py-2">
                <input
                  type="checkbox"
                  checked={selected.size === eligible.length && eligible.length > 0}
                  onChange={toggleAll}
                  className="h-4 w-4 rounded border-gray-300"
                />
                <span className="text-xs font-medium text-gray-600">Select All ({selected.size}/{eligible.length})</span>
              </div>

              {/* Eligible findings */}
              {eligible.map(f => (
                <label key={f.id} className="flex items-center gap-3 px-3 py-2 hover:bg-gray-50">
                  <input
                    type="checkbox"
                    checked={selected.has(f.id)}
                    onChange={() => toggle(f.id)}
                    className="h-4 w-4 rounded border-gray-300"
                  />
                  <div className="min-w-0 flex-1">
                    <p className="truncate text-sm text-gray-900">{f.title}</p>
                    <p className="text-xs text-gray-500">{f.controlId} &middot; {f.severity}</p>
                  </div>
                </label>
              ))}

              {/* Grayed-out findings with active POA&Ms */}
              {grayed.map(f => (
                <div key={f.id} className="flex items-center gap-3 px-3 py-2 opacity-40">
                  <input type="checkbox" disabled checked className="h-4 w-4 rounded border-gray-300" />
                  <div className="min-w-0 flex-1">
                    <p className="truncate text-sm text-gray-500">{f.title}</p>
                    <p className="text-xs text-gray-400">{f.controlId} &middot; Active POA&amp;M exists</p>
                  </div>
                </div>
              ))}
            </div>

            <div className="mt-4 flex justify-end gap-2">
              <button onClick={onClose} className="rounded-lg bg-gray-100 px-4 py-2 text-sm hover:bg-gray-200">
                Skip
              </button>
              <button
                onClick={handleCreate}
                disabled={loading || selected.size === 0}
                className="rounded-lg bg-indigo-600 px-4 py-2 text-sm text-white hover:bg-indigo-700 disabled:opacity-50"
              >
                {loading ? 'Creating...' : `Create ${selected.size} POA&M Item(s)`}
              </button>
            </div>
          </>
        )}
      </div>
    </div>
  );
}
