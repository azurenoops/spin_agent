import { useEffect, useState, type ReactElement } from 'react';
import { useNavigate } from 'react-router-dom';
import {
  getCspOnboardingState,
  isUnavailable,
  postCspOnboardingClassification,
  postCspOnboardingIdentity,
  postCspOnboardingSubmit,
  postCspOnboardingSupport,
  type CspOnboardingStateDto,
  type CspOnboardingStep,
} from './api';
import IdentityStep from './steps/IdentityStep';
import SupportContactStep from './steps/SupportContactStep';
import ClassificationStep from './steps/ClassificationStep';
import AtoDocumentsStep from './steps/AtoDocumentsStep';
import ReviewStep from './steps/ReviewStep';

/**
 * `CspWizard` — Feature 048 / US7 / T167.
 *
 * 4-step React form (Identity → Support → Classification → Review) that
 * walks the hosting CSP through onboarding the deployment itself. Mounted
 * at `/onboarding/csp` by the dashboard router. Persists every step
 * server-side via `/api/csp/onboarding/*` so the wizard can be resumed
 * after a refresh / browser-restart.
 *
 * Self-hides in `SingleTenant` deployments (the API returns 404) and for
 * non-CSP-Admin callers (401/403). After successful submission the user
 * is redirected to `/` (portfolio home) — by that point the
 * `503 CSP_ONBOARDING_INCOMPLETE` gate has lifted.
 */
type WizardStep =
  | 'Identity'
  | 'SupportContact'
  | 'Classification'
  | 'AtoDocuments'
  | 'Review';

// Feature 048 / US9 / T212: `AtoDocuments` is a UI-only step inserted
// between server-side `Classification` and `Review`. The server's
// `CspOnboardingStep` enum (Identity/SupportContact/Classification/Review/Complete)
// remains the source of truth for resumption — when the server reports
// `Review`, the user has already navigated past `AtoDocuments` at least
// once, so we resume on `Review` rather than re-injecting the upload step.
const STEPS: WizardStep[] = [
  'Identity',
  'SupportContact',
  'Classification',
  'AtoDocuments',
  'Review',
];

const STEP_LABELS: Record<WizardStep, string> = {
  Identity: 'Identity',
  SupportContact: 'Support',
  Classification: 'Classification',
  AtoDocuments: 'ATO documents',
  Review: 'Review',
};

function nextWizardStep(current: WizardStep): WizardStep {
  const i = STEPS.indexOf(current);
  return STEPS[Math.min(i + 1, STEPS.length - 1)] ?? current;
}

function prevWizardStep(current: WizardStep): WizardStep {
  const i = STEPS.indexOf(current);
  return STEPS[Math.max(i - 1, 0)] ?? current;
}

function toWizardStep(s: CspOnboardingStep): WizardStep {
  switch (s) {
    case 'Identity':
      return 'Identity';
    case 'SupportContact':
      return 'SupportContact';
    case 'Classification':
      return 'Classification';
    case 'Review':
    case 'Complete':
    default:
      // Server says the user has progressed past Classification → resume
      // on Review (they have already had a chance to upload ATO docs in
      // a previous wizard pass). They can hit Back to revisit AtoDocuments.
      return 'Review';
  }
}

export default function CspWizard(): ReactElement {
  const navigate = useNavigate();
  const [state, setState] = useState<CspOnboardingStateDto | null>(null);
  const [unavailable, setUnavailable] = useState<string | null>(null);
  const [step, setStep] = useState<WizardStep>('Identity');
  const [saving, setSaving] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      const next = await getCspOnboardingState();
      if (cancelled) return;
      if (isUnavailable(next)) {
        setUnavailable(next.reason);
        return;
      }
      setState(next);
      // Honor server-side currentStep so the wizard resumes where the user
      // left off (FR-091 reentrancy contract).
      setStep(toWizardStep(next.currentStep));
      // If the CSP is already onboarded, kick the user back to home — this
      // route exists only for the unfinished singleton.
      if (next.onboardingState === 'Active') {
        navigate('/', { replace: true });
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [navigate]);

  function describeError(err: unknown): string {
    const e = err as { errorCode?: string; message?: string };
    if (e?.errorCode === 'CSP_ALREADY_ONBOARDED') {
      return 'CSP onboarding has already been finalized. Refresh to continue.';
    }
    if (e?.errorCode === 'VALIDATION_FAILED') return e.message ?? 'Please review the values above.';
    if (e?.errorCode === 'FORBIDDEN_NOT_CSP_ADMIN') {
      return 'Your account is not a CSP administrator. Contact your portal admin.';
    }
    return e?.message ?? 'Something went wrong. Please try again.';
  }

  async function handleSaveIdentity(payload: import('./api').IdentityRequest): Promise<void> {
    setSaving(true);
    setErrorMessage(null);
    try {
      const next = await postCspOnboardingIdentity(payload);
      setState(next);
      setStep(nextWizardStep(step));
    } catch (err) {
      setErrorMessage(describeError(err));
    } finally {
      setSaving(false);
    }
  }

  async function handleSaveSupport(payload: import('./api').SupportContactRequest): Promise<void> {
    setSaving(true);
    setErrorMessage(null);
    try {
      const next = await postCspOnboardingSupport(payload);
      setState(next);
      setStep(nextWizardStep(step));
    } catch (err) {
      setErrorMessage(describeError(err));
    } finally {
      setSaving(false);
    }
  }

  async function handleSaveClassification(
    payload: import('./api').ClassificationRequest,
  ): Promise<void> {
    setSaving(true);
    setErrorMessage(null);
    try {
      const next = await postCspOnboardingClassification(payload);
      setState(next);
      setStep(nextWizardStep(step));
    } catch (err) {
      setErrorMessage(describeError(err));
    } finally {
      setSaving(false);
    }
  }

  async function handleSubmit(): Promise<void> {
    setSaving(true);
    setErrorMessage(null);
    try {
      await postCspOnboardingSubmit();
      // Refresh state so the post-submission `Active` value is visible to
      // every other consumer (header logo, route guard) before we route
      // away from the wizard.
      const refreshed = await getCspOnboardingState();
      if (!isUnavailable(refreshed)) setState(refreshed);
      navigate('/', { replace: true });
    } catch (err) {
      setErrorMessage(describeError(err));
    } finally {
      setSaving(false);
    }
  }

  if (unavailable !== null) {
    // SingleTenant mode → wizard not applicable. Bounce home so the user
    // doesn't land on a dead-end screen.
    return (
      <div className="mx-auto max-w-xl p-6 text-center text-gray-600">
        <p className="text-sm">
          The CSP onboarding wizard is not available in this deployment
          (<span className="font-mono text-gray-700">{unavailable}</span>).
        </p>
        <button
          type="button"
          onClick={() => navigate('/', { replace: true })}
          className="mt-4 rounded-md bg-indigo-600 px-4 py-2 text-sm font-medium text-white shadow-sm hover:bg-indigo-700"
        >
          Return to dashboard
        </button>
      </div>
    );
  }

  if (state === null) {
    return (
      <div className="mx-auto max-w-xl p-6 text-center text-sm text-gray-500">
        Loading CSP onboarding state…
      </div>
    );
  }

  return (
    <div className="mx-auto max-w-2xl px-6 py-8">
      <header className="mb-6">
        <h1 className="text-2xl font-semibold text-gray-900">CSP onboarding</h1>
        <p className="mt-1 text-sm text-gray-600">
          One-time setup for this hosting deployment. After submission, you
          can pre-provision tenants and the dashboard becomes fully
          operational.
        </p>
      </header>

      {/* Progress indicator */}
      <ol className="mb-8 flex items-center justify-between gap-2 text-xs text-gray-500">
        {STEPS.map((s, idx) => {
          const stepIdx = STEPS.indexOf(step);
          const isActive = idx === stepIdx;
          const isComplete = idx < stepIdx;
          return (
            <li key={s} className="flex flex-1 items-center gap-2">
              <span
                aria-current={isActive ? 'step' : undefined}
                className={`flex h-6 w-6 flex-shrink-0 items-center justify-center rounded-full text-[11px] font-semibold ${
                  isComplete
                    ? 'bg-emerald-600 text-white'
                    : isActive
                      ? 'bg-indigo-600 text-white'
                      : 'bg-gray-200 text-gray-500'
                }`}
              >
                {isComplete ? '✓' : idx + 1}
              </span>
              <span className={isActive ? 'font-medium text-gray-900' : ''}>
                {STEP_LABELS[s]}
              </span>
              {idx < STEPS.length - 1 && <span className="flex-1 border-t border-dashed border-gray-200" />}
            </li>
          );
        })}
      </ol>

      <section className="rounded-lg border border-gray-200 bg-white p-6 shadow-sm">
        {step === 'Identity' && (
          <IdentityStep
            initial={{
              legalEntityName: state.identity?.legalEntityName ?? '',
              displayName: state.identity?.displayName ?? '',
              logoUrl: state.identity?.logoUrl ?? '',
            }}
            saving={saving}
            errorMessage={errorMessage}
            onSubmit={handleSaveIdentity}
          />
        )}
        {step === 'SupportContact' && (
          <SupportContactStep
            initial={{
              primarySupportEmail: state.supportContact?.primarySupportEmail ?? '',
              supportPhone: state.supportContact?.supportPhone ?? '',
            }}
            saving={saving}
            errorMessage={errorMessage}
            onSubmit={handleSaveSupport}
            onBack={() => setStep(prevWizardStep(step))}
          />
        )}
        {step === 'Classification' && (
          <ClassificationStep
            initial={{
              defaultClassificationFloor:
                state.classification?.defaultClassificationFloor ?? 'Unclassified',
            }}
            saving={saving}
            errorMessage={errorMessage}
            onSubmit={handleSaveClassification}
            onBack={() => setStep(prevWizardStep(step))}
          />
        )}
        {step === 'AtoDocuments' && (
          <AtoDocumentsStep
            saving={saving}
            errorMessage={errorMessage}
            onContinue={() => setStep(nextWizardStep(step))}
            onBack={() => setStep(prevWizardStep(step))}
          />
        )}
        {step === 'Review' && (
          <ReviewStep
            state={state}
            saving={saving}
            errorMessage={errorMessage}
            onSubmit={handleSubmit}
            onBack={() => setStep(prevWizardStep(step))}
          />
        )}
      </section>
    </div>
  );
}
