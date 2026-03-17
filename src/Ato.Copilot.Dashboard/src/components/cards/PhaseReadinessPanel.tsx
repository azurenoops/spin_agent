import { useState, useEffect, useCallback } from 'react';
import {
  getPhaseReadiness,
  advanceRmfStep,
} from '../../api/systemDetail';
import type {
  PhaseReadinessResponse,
  GateResult,
  AdvanceRmfStepResponse,
} from '../../api/systemDetail';
import GateActionDialog from './GateActionDialog';
import type { GateAction } from './GateActionDialog';

interface PhaseReadinessPanelProps {
  systemId: string;
  onAdvanced: () => void;
}

export default function PhaseReadinessPanel({ systemId, onAdvanced }: PhaseReadinessPanelProps) {
  const [readiness, setReadiness] = useState<PhaseReadinessResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [advancing, setAdvancing] = useState(false);
  const [advanceResult, setAdvanceResult] = useState<AdvanceRmfStepResponse | null>(null);
  const [activeAction, setActiveAction] = useState<GateAction | null>(null);

  const fetchReadiness = useCallback(async () => {
    try {
      setLoading(true);
      const data = await getPhaseReadiness(systemId);
      setReadiness(data);
      setError(null);
    } catch {
      setError('Failed to load phase readiness');
    } finally {
      setLoading(false);
    }
  }, [systemId]);

  useEffect(() => {
    void fetchReadiness();
  }, [fetchReadiness]);

  const passedGates = readiness?.gateResults.filter((g) => g.passed) ?? [];
  const failedGates = readiness?.gateResults.filter((g) => !g.passed) ?? [];
  const totalGates = readiness?.gateResults.length ?? 0;
  const progressPct = totalGates > 0 ? (passedGates.length / totalGates) * 100 : 0;
  const allPassed = failedGates.length === 0;

  const handleAdvance = async (force = false) => {
    if (!readiness?.nextPhase) return;
    setAdvancing(true);
    setAdvanceResult(null);
    try {
      const res = await advanceRmfStep(systemId, readiness.nextPhase, force);
      setAdvanceResult(res);
      if (res.success) {
        onAdvanced();
        void fetchReadiness();
      }
    } catch (err) {
      if (err && typeof err === 'object' && 'gateResults' in err) {
        setAdvanceResult(err as AdvanceRmfStepResponse);
      } else {
        setError('Failed to advance phase');
      }
    } finally {
      setAdvancing(false);
    }
  };

  const handleActionSuccess = () => {
    setActiveAction(null);
    onAdvanced();
    void fetchReadiness();
  };

  // Determine which quick actions to show for each failed gate
  const getGateAction = (gate: GateResult): { label: string; action: GateAction }[] | null => {
    const name = gate.gateName.toLowerCase();
    const msg = gate.message.toLowerCase();
    if (name.includes('privacy') && msg.includes('pia'))
      return [{ label: 'Approve PIA', action: 'pia' }];
    if (name.includes('privacy'))
      return [{ label: 'Create PTA', action: 'pta' }];
    if (name.includes('interconnection'))
      return [
        { label: 'Add', action: 'interconnection' },
        { label: 'None', action: 'certify-none' },
      ];
    if (name.includes('categorization') || name.includes('information type'))
      return [{ label: 'Set', action: 'categorization' }];
    if (name.includes('baseline'))
      return [{ label: 'Select', action: 'baseline' }];
    return null;
  };

  if (loading) {
    return (
      <div className="rounded-lg border border-gray-200 bg-white p-5 shadow-sm animate-pulse">
        <div className="h-5 w-48 bg-gray-200 rounded mb-4" />
        <div className="h-2 bg-gray-200 rounded mb-4" />
        <div className="space-y-3">
          <div className="h-4 bg-gray-200 rounded w-3/4" />
          <div className="h-4 bg-gray-200 rounded w-2/3" />
        </div>
      </div>
    );
  }

  if (error && !readiness) {
    return (
      <div className="rounded-lg border border-red-200 bg-red-50 p-4">
        <p className="text-sm text-red-700">{error}</p>
      </div>
    );
  }

  if (!readiness?.nextPhase) {
    return null; // At final phase — nothing to show
  }

  return (
    <div className="rounded-lg border border-gray-200 bg-white shadow-sm">
      {/* Header */}
      <div className="px-5 py-4 border-b border-gray-100">
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-2">
            <span className="text-base">📋</span>
            <h3 className="text-sm font-semibold text-gray-900">
              Phase Readiness: {readiness.currentPhase} → {readiness.nextPhase}
            </h3>
          </div>
          {allPassed && (
            <span className="inline-flex items-center gap-1 rounded-full bg-green-100 px-2 py-0.5 text-xs font-medium text-green-700">
              ✓ Ready
            </span>
          )}
        </div>

        {/* Progress bar */}
        <div className="mt-3 flex items-center gap-3">
          <div className="flex-1 h-2 bg-gray-100 rounded-full overflow-hidden">
            <div
              className={`h-full rounded-full transition-all duration-500 ${allPassed ? 'bg-green-500' : 'bg-blue-500'}`}
              style={{ width: `${progressPct}%` }}
            />
          </div>
          <span className="text-xs font-medium text-gray-500 tabular-nums">
            {passedGates.length}/{totalGates}
          </span>
        </div>
      </div>

      {/* Gate Checklist */}
      <div className="divide-y divide-gray-50">
        {readiness.gateResults.map((gate) => {
          const actions = gate.passed ? null : getGateAction(gate);
          return (
            <div key={gate.gateName} className="px-5 py-3 flex items-start gap-3">
              {/* Status icon */}
              <div className="mt-0.5 flex-shrink-0">
                {gate.passed ? (
                  <span className="flex h-5 w-5 items-center justify-center rounded-full bg-green-100 text-green-600">
                    <svg className="h-3 w-3" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={3}>
                      <path strokeLinecap="round" strokeLinejoin="round" d="M5 13l4 4L19 7" />
                    </svg>
                  </span>
                ) : (
                  <span className="flex h-5 w-5 items-center justify-center rounded-full bg-red-100 text-red-600">
                    <svg className="h-3 w-3" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={3}>
                      <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" />
                    </svg>
                  </span>
                )}
              </div>

              {/* Gate info */}
              <div className="flex-1 min-w-0">
                <p className={`text-sm font-medium ${gate.passed ? 'text-gray-900' : 'text-red-800'}`}>
                  {gate.gateName}
                </p>
                {!gate.passed && (
                  <p className="text-xs text-gray-500 mt-0.5">{gate.message}</p>
                )}
              </div>

              {/* Action buttons for failed gates */}
              {actions && (
                <div className="flex gap-1.5 flex-shrink-0">
                  {actions.map((a) => (
                    <button
                      key={a.action}
                      onClick={() => setActiveAction(a.action)}
                      className="inline-flex items-center rounded-md border border-blue-300 bg-blue-50 px-2 py-1 text-xs font-medium text-blue-700 hover:bg-blue-100 transition-colors"
                    >
                      {a.label} →
                    </button>
                  ))}
                </div>
              )}
            </div>
          );
        })}
      </div>

      {/* Gate Action Dialog */}
      {activeAction && (
        <GateActionDialog
          action={activeAction}
          systemId={systemId}
          onClose={() => setActiveAction(null)}
          onSuccess={handleActionSuccess}
        />
      )}

      {/* Advance button */}
      <div className="px-5 py-4 border-t border-gray-100">
        {advanceResult && !advanceResult.success && (
          <div className="mb-3 rounded-md border border-red-200 bg-red-50 p-3">
            <p className="text-xs text-red-700">{advanceResult.error ?? 'Gate checks failed'}</p>
          </div>
        )}

        <div className="flex items-center justify-between">
          <p className="text-xs text-gray-500">
            {allPassed
              ? 'All prerequisites met — ready to advance.'
              : `Complete ${failedGates.length} remaining prerequisite${failedGates.length === 1 ? '' : 's'} to enable advancement.`}
          </p>
          <div className="flex gap-2">
            {!allPassed && (
              <button
                onClick={() => void handleAdvance(true)}
                disabled={advancing}
                className="rounded-md border border-amber-300 bg-amber-50 px-3 py-1.5 text-xs font-medium text-amber-800 hover:bg-amber-100 disabled:opacity-50 transition-colors"
              >
                {advancing ? 'Forcing...' : 'Force Advance'}
              </button>
            )}
            <button
              onClick={() => void handleAdvance(false)}
              disabled={advancing || !allPassed}
              className={`inline-flex items-center gap-1.5 rounded-md px-4 py-1.5 text-xs font-medium transition-colors ${
                allPassed
                  ? 'bg-green-600 text-white hover:bg-green-700'
                  : 'bg-gray-100 text-gray-400 cursor-not-allowed'
              } disabled:opacity-50`}
            >
              <svg className="h-3.5 w-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M13 7l5 5m0 0l-5 5m5-5H6" />
              </svg>
              Advance to {readiness.nextPhase}
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}
