import { useState } from 'react';
import { generateAndApprovePia, type GenerateApprovePiaResponse } from '../../api/systemDetail';

// ─── Props ────────────────────────────────────────────────────────────────────

interface PiaPanelProps {
  systemId: string;
  /** Must be true (PTA determination complete) before generating a PIA. */
  ptaCompleted: boolean;
}

// ─── PIA Panel ────────────────────────────────────────────────────────────────

/**
 * Privacy Impact Assessment (PIA) generation panel.
 * Gated on `ptaCompleted` — a PIA cannot be generated until the PTA has been
 * completed.
 *
 * Calls POST /api/dashboard/systems/{id}/generate-approve-pia
 *
 * Closes #254.
 */
export default function PiaPanel({ systemId, ptaCompleted }: PiaPanelProps) {
  const [pia, setPia] = useState<GenerateApprovePiaResponse | null>(null);
  const [generating, setGenerating] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleGenerate = async () => {
    setGenerating(true);
    setError(null);
    try {
      const result = await generateAndApprovePia(systemId);
      setPia(result);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'PIA generation failed');
    } finally {
      setGenerating(false);
    }
  };

  const formatDate = (iso: string | null | undefined): string => {
    if (!iso) return '—';
    try {
      return new Date(iso).toLocaleDateString(undefined, {
        year: 'numeric',
        month: 'short',
        day: 'numeric',
      });
    } catch {
      return iso;
    }
  };

  const statusColor = (status: string): string => {
    switch (status.toLowerCase()) {
      case 'approved': return 'bg-green-100 text-green-700';
      case 'pending':  return 'bg-amber-100 text-amber-700';
      case 'expired':  return 'bg-red-100 text-red-700';
      default:         return 'bg-gray-100 text-gray-600';
    }
  };

  return (
    <div className="rounded-xl border border-gray-200 bg-white p-5 space-y-4">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-base font-semibold text-gray-900">Privacy Impact Assessment (PIA)</h2>
          <p className="text-sm text-gray-500 mt-0.5">
            Generate and approve a PIA after the PTA is complete.
          </p>
        </div>
        {pia && (
          <span className={`rounded-full px-2.5 py-0.5 text-xs font-medium ${statusColor(pia.status)}`}>
            {pia.status}
          </span>
        )}
      </div>

      {/* Existing PIA data */}
      {pia && (
        <div className="rounded-lg border border-gray-100 bg-gray-50 divide-y divide-gray-100 text-sm">
          <div className="flex justify-between px-4 py-2.5">
            <span className="text-gray-500">PIA ID</span>
            <span className="font-mono text-xs text-gray-700">{pia.piaId}</span>
          </div>
          <div className="flex justify-between px-4 py-2.5">
            <span className="text-gray-500">Status</span>
            <span className={`rounded-full px-2.5 py-0.5 text-xs font-medium ${statusColor(pia.status)}`}>
              {pia.status}
            </span>
          </div>
          <div className="flex justify-between px-4 py-2.5">
            <span className="text-gray-500">Expiration</span>
            <span className="text-gray-700">{formatDate(pia.expirationDate)}</span>
          </div>
        </div>
      )}

      {/* Error */}
      {error && (
        <div className="rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">
          {error}
        </div>
      )}

      {/* Gate: PTA must be completed */}
      {!ptaCompleted && (
        <div className="rounded-lg border border-amber-200 bg-amber-50 px-3 py-2 text-sm text-amber-700">
          Complete the Privacy Threshold Analysis (PTA) before generating a PIA.
        </div>
      )}

      {/* Generate button */}
      <div className="flex justify-end">
        <button
          type="button"
          onClick={handleGenerate}
          disabled={!ptaCompleted || generating}
          className="inline-flex items-center gap-2 rounded-lg bg-indigo-600 px-4 py-2 text-sm font-semibold text-white hover:bg-indigo-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
          title={!ptaCompleted ? 'Complete PTA first' : undefined}
        >
          {generating ? (
            <>
              <svg className="h-4 w-4 animate-spin" fill="none" viewBox="0 0 24 24">
                <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
              </svg>
              Generating PIA…
            </>
          ) : pia ? (
            'Regenerate PIA'
          ) : (
            'Generate PIA'
          )}
        </button>
      </div>
    </div>
  );
}
