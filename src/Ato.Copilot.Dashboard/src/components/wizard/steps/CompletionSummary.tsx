import { useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { advanceRmfStep, getPhaseReadiness } from '../../../api/systemDetail';
import type { PhaseReadinessResponse } from '../../../api/systemDetail';

interface CompletionSummaryProps {
  systemId: string;
  systemName: string;
  completedSteps: boolean[];
  onClose: () => void;
}

const STEP_LABELS = [
  'System Registration',
  'Security Capabilities',
  'System Components',
  'Authorization Boundaries',
  'Assign RMF Roles',
  'Verify Roles',
  'Categorization & Baseline',
  'Privacy Analysis',
];

export default function CompletionSummary({ systemId, systemName, completedSteps, onClose }: CompletionSummaryProps) {
  const navigate = useNavigate();
  const [phaseStatus, setPhaseStatus] = useState<'checking' | 'advanced' | 'not-ready' | 'error'>('checking');
  const [advancedTo, setAdvancedTo] = useState<string | null>(null);
  const [readiness, setReadiness] = useState<PhaseReadinessResponse | null>(null);

  // Auto-advance the RMF phase when the wizard completes
  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const ready = await getPhaseReadiness(systemId);
        if (cancelled) return;
        setReadiness(ready);

        if (ready.ready && ready.nextPhase) {
          const result = await advanceRmfStep(systemId, ready.nextPhase);
          if (cancelled) return;
          if (result.success) {
            setPhaseStatus('advanced');
            setAdvancedTo(result.newStep);
          } else {
            setPhaseStatus('not-ready');
          }
        } else {
          setPhaseStatus('not-ready');
        }
      } catch {
        if (!cancelled) setPhaseStatus('error');
      }
    })();
    return () => { cancelled = true; };
  }, [systemId]);

  const handleGoToSystem = () => {
    onClose();
    navigate(`/systems/${systemId}`);
  };

  return (
    <div className="flex flex-col items-center py-12 px-6 text-center">
      <div className="mb-6 flex h-16 w-16 items-center justify-center rounded-full bg-green-100">
        <svg className="h-8 w-8 text-green-600" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
          <path strokeLinecap="round" strokeLinejoin="round" d="M5 13l4 4L19 7" />
        </svg>
      </div>

      <h2 className="text-2xl font-bold text-gray-900 mb-2">System Setup Complete</h2>
      <p className="text-gray-600 mb-4">
        <span className="font-semibold">{systemName}</span> has been registered and configured.
      </p>

      {/* Phase advancement status */}
      {phaseStatus === 'checking' && (
        <div className="mb-6 w-full max-w-md rounded-md border border-indigo-200 bg-indigo-50 p-3 text-sm text-indigo-700">
          Checking phase readiness and attempting to advance...
        </div>
      )}
      {phaseStatus === 'advanced' && (
        <div className="mb-6 w-full max-w-md rounded-md border border-green-200 bg-green-50 p-3 text-sm text-green-700">
          <strong>Phase advanced</strong> to <span className="font-semibold">{advancedTo}</span> automatically.
        </div>
      )}
      {phaseStatus === 'not-ready' && readiness && !readiness.ready && (
        <div className="mb-6 w-full max-w-md rounded-md border border-amber-200 bg-amber-50 p-3 text-sm text-amber-700">
          <strong>Phase not advanced.</strong> Some prerequisites remain:
          <ul className="mt-1 list-disc pl-4">
            {readiness.gateResults.filter((g) => !g.passed).map((g) => (
              <li key={g.gateName}>{g.gateName}: {g.message}</li>
            ))}
          </ul>
        </div>
      )}

      <div className="w-full max-w-md mb-8">
        <h3 className="text-sm font-medium text-gray-500 uppercase tracking-wider mb-3">Completed Steps</h3>
        <ul className="space-y-2">
          {STEP_LABELS.map((label, index) => (
            <li key={label} className="flex items-center gap-3 text-sm">
              {completedSteps[index] ? (
                <span className="flex h-5 w-5 items-center justify-center rounded-full bg-green-100 text-green-700">
                  <svg className="h-3 w-3" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={3}>
                    <path strokeLinecap="round" strokeLinejoin="round" d="M5 13l4 4L19 7" />
                  </svg>
                </span>
              ) : (
                <span className="flex h-5 w-5 items-center justify-center rounded-full bg-gray-100 text-gray-400">
                  <svg className="h-3 w-3" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                    <path strokeLinecap="round" strokeLinejoin="round" d="M20 12H4" />
                  </svg>
                </span>
              )}
              <span className={completedSteps[index] ? 'text-gray-900' : 'text-gray-400'}>
                {label}
              </span>
            </li>
          ))}
        </ul>
      </div>

      <button
        onClick={handleGoToSystem}
        className="rounded-md bg-indigo-600 px-6 py-2.5 text-sm font-medium text-white hover:bg-indigo-700 transition-colors"
      >
        Go to System
      </button>
    </div>
  );
}
