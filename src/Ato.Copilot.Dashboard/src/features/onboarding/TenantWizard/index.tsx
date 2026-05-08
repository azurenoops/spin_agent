import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import {
  tenantWizard,
  type TenantOnboardingProgress,
  type TenantWizardStep,
} from './api';
import LegalEntityStep from './steps/LegalEntityStep';
import HqAddressStep from './steps/HqAddressStep';
import ClassificationStep from './steps/ClassificationStep';
import AoStep from './steps/AoStep';
import PrimaryPocStep from './steps/PrimaryPocStep';
import OrgProfileStep from './steps/OrgProfileStep';
import ReviewStep from './steps/ReviewStep';

const STEP_ORDER: TenantWizardStep[] = [
  'Tenant.LegalEntity',
  'Tenant.HqAddress',
  'Tenant.Classification',
  'Tenant.Ao',
  'Tenant.PrimaryPoc',
  'Org.Profile',
  'Submitted',
];

const STEP_LABELS: Record<TenantWizardStep, string> = {
  'Tenant.LegalEntity': 'Legal entity',
  'Tenant.HqAddress': 'Headquarters address',
  'Tenant.Classification': 'Default classification',
  'Tenant.Ao': 'Authorizing Official',
  'Tenant.PrimaryPoc': 'Primary POC',
  'Org.Profile': 'First organization',
  Submitted: 'Review & submit',
};

/**
 * Feature 048 / US4 — Tenant onboarding wizard shell.
 *
 * Renders one of seven step components based on the
 * `currentStep` returned by the backend. Each step submits its slice via
 * the {@link tenantWizard} API client; the resulting `TenantOnboardingProgress`
 * is hoisted here to drive the progress rail and step transitions. The
 * wizard auto-redirects to `/` once the tenant transitions to `Active`.
 */
export default function TenantWizard() {
  const [progress, setProgress] = useState<TenantOnboardingProgress | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const navigate = useNavigate();

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const state = await tenantWizard.getState();
        if (!cancelled) setProgress(state);
      } catch (err) {
        if (!cancelled) {
          setError((err as Error).message);
        }
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    if (progress?.onboardingState === 'Active') {
      navigate('/', { replace: true });
    }
  }, [progress?.onboardingState, navigate]);

  if (error) {
    return (
      <div className="mx-auto max-w-3xl p-8">
        <h1 className="text-2xl font-semibold text-red-700">Wizard error</h1>
        <p className="mt-2 text-sm text-gray-700">{error}</p>
      </div>
    );
  }

  if (!progress) {
    return (
      <div className="mx-auto max-w-3xl p-8 text-sm text-gray-500">Loading…</div>
    );
  }

  const onAdvance = (next: TenantOnboardingProgress) => {
    setProgress(next);
    setBusy(false);
  };
  const onError = (message: string) => {
    setError(message);
    setBusy(false);
  };
  const beforeSubmit = () => setBusy(true);

  return (
    <div className="mx-auto max-w-3xl p-6">
      <h1 className="mb-4 text-2xl font-semibold">Tenant onboarding</h1>
      <ProgressRail current={progress.currentStep} completed={progress.completedSteps} />

      <section className="mt-6 rounded border border-gray-200 bg-white p-6 shadow-sm">
        {progress.currentStep === 'Tenant.LegalEntity' && (
          <LegalEntityStep
            busy={busy}
            beforeSubmit={beforeSubmit}
            onAdvance={onAdvance}
            onError={onError}
          />
        )}
        {progress.currentStep === 'Tenant.HqAddress' && (
          <HqAddressStep
            busy={busy}
            beforeSubmit={beforeSubmit}
            onAdvance={onAdvance}
            onError={onError}
          />
        )}
        {progress.currentStep === 'Tenant.Classification' && (
          <ClassificationStep
            busy={busy}
            beforeSubmit={beforeSubmit}
            onAdvance={onAdvance}
            onError={onError}
          />
        )}
        {progress.currentStep === 'Tenant.Ao' && (
          <AoStep
            busy={busy}
            beforeSubmit={beforeSubmit}
            onAdvance={onAdvance}
            onError={onError}
          />
        )}
        {progress.currentStep === 'Tenant.PrimaryPoc' && (
          <PrimaryPocStep
            busy={busy}
            beforeSubmit={beforeSubmit}
            onAdvance={onAdvance}
            onError={onError}
          />
        )}
        {progress.currentStep === 'Org.Profile' && (
          <OrgProfileStep
            busy={busy}
            beforeSubmit={beforeSubmit}
            onAdvance={onAdvance}
            onError={onError}
          />
        )}
        {progress.currentStep === 'Submitted' && (
          <ReviewStep
            busy={busy}
            beforeSubmit={beforeSubmit}
            onAdvance={onAdvance}
            onError={onError}
          />
        )}
      </section>
    </div>
  );
}

function ProgressRail({
  current,
  completed,
}: {
  current: TenantWizardStep;
  completed: TenantWizardStep[];
}) {
  const completedSet = new Set(completed);
  return (
    <ol className="flex flex-wrap gap-2 text-xs">
      {STEP_ORDER.map((step, idx) => {
        const isComplete = completedSet.has(step);
        const isCurrent = step === current;
        const cls = isComplete
          ? 'bg-green-50 text-green-800 border-green-300'
          : isCurrent
            ? 'bg-blue-50 text-blue-800 border-blue-300'
            : 'bg-gray-50 text-gray-500 border-gray-200';
        return (
          <li key={step} className={`rounded border px-3 py-1 ${cls}`}>
            <span className="font-mono">{idx + 1}.</span> {STEP_LABELS[step]}
          </li>
        );
      })}
    </ol>
  );
}
