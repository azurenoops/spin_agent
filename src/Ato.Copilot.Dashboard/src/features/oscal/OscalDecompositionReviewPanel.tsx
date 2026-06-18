/**
 * Feature 076 T013: OSCAL Decomposition Review Panel (side-by-side diff + approval workflow)
 */
import { useState, useEffect, useCallback } from 'react';
import axios from 'axios';

interface SuggestedParam { paramId: string; value: string; }

interface DecompositionFragment {
  fragmentId: string;
  statementId: string;
  componentUuid: string | null;
  description: string;
  suggestedParams: SuggestedParam[];
  confidenceScore: number;
}

interface DecompositionDraft {
  draftId: string;
  controlId: string;
  status: 'Pending' | 'Approved' | 'Discarded';
  generatedAt: string;
  generatedBy: string;
  fragments: DecompositionFragment[];
}

interface OscalDecompositionReviewPanelProps {
  systemId: string;
  controlId: string;
  narrative: string;
  onApproved: () => void;
  onDismiss: () => void;
}

function ConfidenceBadge({ score }: { score: number }) {
  const pct = Math.round(score * 100);
  const cls = score >= 0.8
    ? 'bg-green-100 text-green-800'
    : score >= 0.5
    ? 'bg-amber-100 text-amber-800'
    : 'bg-red-100 text-red-800';
  return (
    <span className={`inline-block rounded px-1.5 py-0.5 text-xs font-medium ${cls}`}>{pct}%</span>
  );
}

const Spinner = () => (
  <svg className="h-4 w-4 animate-spin" fill="none" viewBox="0 0 24 24">
    <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4"/>
    <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z"/>
  </svg>
);

export default function OscalDecompositionReviewPanel({
  systemId, controlId, narrative, onApproved, onDismiss,
}: OscalDecompositionReviewPanelProps) {
  const [draft, setDraft] = useState<DecompositionDraft | null>(null);
  const [editedDescriptions, setEditedDescriptions] = useState<Record<string, string>>({});
  const [editingId, setEditingId] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [decomposing, setDecomposing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [approving, setApproving] = useState(false);

  const baseUrl = `/api/systems/${systemId}/controls/${controlId}/oscal`;

  const loadDraft = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await axios.get<{ ok: boolean; data: DecompositionDraft }>(`${baseUrl}/decomposition/draft`);
      setDraft(res.data.data);
    } catch (err: unknown) {
      if ((err as { response?: { status?: number } })?.response?.status !== 404) {
        setError('Failed to load decomposition draft.');
      }
    } finally {
      setLoading(false);
    }
  }, [baseUrl]);

  useEffect(() => { void loadDraft(); }, [loadDraft]);

  const handleDecompose = async () => {
    setDecomposing(true);
    setError(null);
    try {
      const res = await axios.post<{ ok: boolean; data: DecompositionDraft }>(
        `${baseUrl}/decompose`,
        { narrative }
      );
      setDraft(res.data.data);
      setEditedDescriptions({});
    } catch {
      setError('AI decomposition failed. Please try again.');
    } finally {
      setDecomposing(false);
    }
  };

  const handleApprove = async () => {
    if (!draft) return;
    setApproving(true);
    setError(null);
    try {
      await axios.put(`${baseUrl}/decomposition/approve`);
      onApproved();
    } catch {
      setError('Approval failed. Please try again.');
      setApproving(false);
    }
  };

  const handleDiscard = async () => {
    try {
      await axios.delete(`${baseUrl}/decomposition/draft`);
      setDraft(null);
      onDismiss();
    } catch {
      setError('Failed to discard draft.');
    }
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 backdrop-blur-sm">
      <div className="w-full max-w-4xl rounded-xl bg-white shadow-2xl border border-gray-200 overflow-hidden flex flex-col max-h-[90vh]">

        {/* Header */}
        <div className="flex items-center justify-between px-6 py-4 bg-gray-50 border-b border-gray-200 flex-shrink-0">
          <div>
            <h2 className="text-base font-semibold text-gray-900">OSCAL Decomposition Review</h2>
            <p className="text-xs text-gray-500 mt-0.5">
              Control: <span className="font-mono font-medium">{controlId}</span>
            </p>
          </div>
          <button onClick={onDismiss} className="text-gray-400 hover:text-gray-600" aria-label="Close">
            <svg className="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" />
            </svg>
          </button>
        </div>

        {/* Body */}
        <div className="flex-1 overflow-y-auto">

          {/* Loading */}
          {loading && (
            <div className="flex items-center justify-center p-12">
              <svg className="h-8 w-8 animate-spin text-indigo-500" fill="none" viewBox="0 0 24 24">
                <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4"/>
                <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z"/>
              </svg>
            </div>
          )}

          {/* No draft state */}
          {!loading && !draft && (
            <div className="flex flex-col items-center justify-center p-12 gap-4">
              <p className="text-sm text-gray-600 text-center max-w-sm">
                No decomposition draft exists for{' '}
                <span className="font-mono font-medium">{controlId}</span>.
                Generate one using AI to segment the narrative into OSCAL statement fragments.
              </p>
              {error && (
                <div className="rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-700">{error}</div>
              )}
              <button
                onClick={() => void handleDecompose()}
                disabled={decomposing}
                className="px-5 py-2 text-sm font-medium text-white bg-indigo-600 rounded-lg hover:bg-indigo-700 disabled:opacity-50 flex items-center gap-2"
              >
                {decomposing && <Spinner />}
                {decomposing ? 'Decomposing...' : 'Generate Decomposition'}
              </button>
            </div>
          )}

          {/* Side-by-side diff */}
          {!loading && draft && (
            <div className="grid grid-cols-2 divide-x divide-gray-200">

              {/* Left: original narrative */}
              <div className="p-5">
                <h3 className="text-xs font-semibold text-gray-500 uppercase tracking-wide mb-3">
                  Original Narrative
                </h3>
                <div className="text-sm text-gray-700 whitespace-pre-wrap bg-gray-50 rounded-lg p-3 border border-gray-200">
                  {narrative || <span className="italic text-gray-400">No narrative stored</span>}
                </div>
              </div>

              {/* Right: OSCAL fragments */}
              <div className="p-5 space-y-3">
                <div className="flex items-center justify-between mb-1">
                  <h3 className="text-xs font-semibold text-gray-500 uppercase tracking-wide">
                    OSCAL Fragments ({draft.fragments.length})
                  </h3>
                  <span className="text-xs text-gray-400">Generated by {draft.generatedBy}</span>
                </div>

                {draft.fragments.map((frag) => {
                  const desc = editedDescriptions[frag.fragmentId] ?? frag.description;
                  const isEditing = editingId === frag.fragmentId;
                  return (
                    <div key={frag.fragmentId} className="rounded-lg border border-gray-200 bg-white p-3 space-y-2">
                      <div className="flex items-center gap-2">
                        <span className="font-mono text-xs font-semibold text-indigo-700 bg-indigo-50 px-1.5 py-0.5 rounded">
                          {frag.statementId}
                        </span>
                        <ConfidenceBadge score={frag.confidenceScore} />
                        {frag.componentUuid && (
                          <span className="text-xs text-gray-400 font-mono">
                            comp: {frag.componentUuid.slice(0, 8)}...
                          </span>
                        )}
                      </div>

                      {isEditing ? (
                        <div>
                          <textarea
                            className="w-full rounded border border-indigo-300 p-2 text-sm focus:outline-none focus:ring-1 focus:ring-indigo-500 resize-none"
                            rows={4}
                            defaultValue={desc}
                            autoFocus
                            onBlur={(e) => {
                              setEditedDescriptions((prev) => ({ ...prev, [frag.fragmentId]: e.target.value }));
                              setEditingId(null);
                            }}
                          />
                          <p className="text-xs text-gray-400 mt-1">Click outside to save</p>
                        </div>
                      ) : (
                        <p
                          className="text-sm text-gray-700 cursor-text rounded p-1 hover:bg-gray-50 border border-transparent hover:border-gray-200 transition-colors"
                          title="Click to edit"
                          onClick={() => setEditingId(frag.fragmentId)}
                        >
                          {desc}
                        </p>
                      )}

                      {frag.suggestedParams.length > 0 && (
                        <div className="flex flex-wrap gap-1">
                          {frag.suggestedParams.map((p) => (
                            <span key={p.paramId} className="text-xs bg-gray-100 text-gray-600 px-1.5 py-0.5 rounded font-mono">
                              {p.paramId}: {p.value}
                            </span>
                          ))}
                        </div>
                      )}
                    </div>
                  );
                })}

                {error && (
                  <div className="rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-700">{error}</div>
                )}
              </div>
            </div>
          )}
        </div>

        {/* Footer */}
        {!loading && draft && (
          <div className="flex justify-between px-6 py-3 bg-gray-50 border-t border-gray-200 flex-shrink-0">
            <button
              onClick={() => void handleDiscard()}
              className="px-4 py-2 text-sm font-medium text-red-700 bg-white border border-red-300 rounded-lg hover:bg-red-50"
            >
              Discard Draft
            </button>
            <button
              onClick={() => void handleApprove()}
              disabled={approving}
              className="px-4 py-2 text-sm font-medium text-white bg-indigo-600 rounded-lg hover:bg-indigo-700 disabled:opacity-50 flex items-center gap-2"
            >
              {approving && <Spinner />}
              {approving ? 'Approving...' : 'Approve & Apply'}
            </button>
          </div>
        )}
      </div>
    </div>
  );
}
