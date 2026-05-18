import { useEffect, useMemo, useState, type ReactNode } from 'react';
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

interface StepDef {
  name: TenantWizardStep;
  number: number;
  title: string;
  description: string;
  icon: ReactNode;
}

const ORDERED_STEPS: StepDef[] = [
  {
    name: 'Tenant.LegalEntity',
    number: 1,
    title: 'Legal entity',
    description: 'Identify the chartered organization that owns this tenant.',
    icon: (
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.8} className="h-5 w-5">
        <path strokeLinecap="round" strokeLinejoin="round" d="M3 21h18M5 21V7l7-4 7 4v14M9 21V11h6v10M9 7h.01M12 7h.01M15 7h.01" />
      </svg>
    ),
  },
  {
    name: 'Tenant.HqAddress',
    number: 2,
    title: 'Headquarters address',
    description: 'Where the organization is officially located.',
    icon: (
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.8} className="h-5 w-5">
        <path strokeLinecap="round" strokeLinejoin="round" d="M12 21s-7-6.5-7-12a7 7 0 1114 0c0 5.5-7 12-7 12z" />
        <circle cx="12" cy="9" r="2.5" stroke="currentColor" strokeWidth={1.8} fill="none" />
      </svg>
    ),
  },
  {
    name: 'Tenant.Classification',
    number: 3,
    title: 'Default classification',
    description: 'Highest classification level this organization processes.',
    icon: (
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.8} className="h-5 w-5">
        <path strokeLinecap="round" strokeLinejoin="round" d="M12 3l8 4v5c0 5-3.5 8.5-8 9-4.5-.5-8-4-8-9V7l8-4z" />
        <path strokeLinecap="round" strokeLinejoin="round" d="M9 12l2 2 4-4" />
      </svg>
    ),
  },
  {
    name: 'Tenant.Ao',
    number: 4,
    title: 'Authorizing Official',
    description: 'Senior official who issues authorizations to operate.',
    icon: (
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.8} className="h-5 w-5">
        <path strokeLinecap="round" strokeLinejoin="round" d="M16 7a4 4 0 11-8 0 4 4 0 018 0z" />
        <path strokeLinecap="round" strokeLinejoin="round" d="M4 21v-1a6 6 0 0112 0v1M18 14l1.5 1.5M18 14l-1.5 1.5M21 17l-3 3" />
      </svg>
    ),
  },
  {
    name: 'Tenant.PrimaryPoc',
    number: 5,
    title: 'Primary POC',
    description: 'Day-to-day point of contact for this organization.',
    icon: (
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.8} className="h-5 w-5">
        <path strokeLinecap="round" strokeLinejoin="round" d="M22 16.92v3a2 2 0 01-2.18 2A19.86 19.86 0 012 4.18 2 2 0 014 2h3a2 2 0 012 1.72c.12.84.33 1.66.62 2.45a2 2 0 01-.45 2.11L7.91 9.91a16 16 0 006.18 6.18l1.63-1.26a2 2 0 012.11-.45c.79.29 1.61.5 2.45.62A2 2 0 0122 16.92z" />
      </svg>
    ),
  },
  {
    name: 'Org.Profile',
    number: 6,
    title: 'First organization',
    description: 'Seed the first organization that will own systems.',
    icon: (
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.8} className="h-5 w-5">
        <path strokeLinecap="round" strokeLinejoin="round" d="M17 20h5v-2a4 4 0 00-3-3.87M9 20H4v-2a4 4 0 013-3.87m4-2a4 4 0 100-8 4 4 0 000 8zm6-2a3 3 0 100-6 3 3 0 000 6z" />
      </svg>
    ),
  },
  {
    name: 'Submitted',
    number: 7,
    title: 'Review & submit',
    description: 'Activate the tenant and unlock the dashboard.',
    icon: (
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.8} className="h-5 w-5">
        <path strokeLinecap="round" strokeLinejoin="round" d="M9 12l2 2 4-4M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
      </svg>
    ),
  },
];

/**
 * Feature 048 / US4 — Tenant onboarding wizard.
 *
 * Renders the seven-step tenant-bootstrap flow inside a polished full-screen
 * modal chrome that matches the Feature 047 organization-onboarding wizard
 * (`OnboardingWizardModal`): gradient header with progress indicator, sidebar
 * with step icons + status pills, and a refined card-style content area.
 *
 * Navigation model:
 *  - `currentStep` is held locally (initialized from the server's
 *    `progress.currentStep`). Sidebar clicks freely jump between steps —
 *    this lets a CSP-Admin revisit or edit any step after the tenant has
 *    transitioned to `Active`.
 *  - Each step's `onAdvance(next)` returns the latest server progress and
 *    automatically advances to the next step in order.
 *  - The wizard does NOT auto-redirect when the tenant is `Active`. Instead,
 *    a Close (X) button appears in the header so the admin can exit on demand.
 */
export default function TenantWizard() {
  const [progress, setProgress] = useState<TenantOnboardingProgress | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [currentStep, setCurrentStep] = useState<TenantWizardStep>('Tenant.LegalEntity');
  const [activatedThisSession, setActivatedThisSession] = useState(false);
  // Tracks whether the tenant was already `Active` on initial load. If yes,
  // we keep the wizard open for free navigation (the admin returned to edit).
  // If no, the first transition to `Active` (i.e. the user clicked
  // "Activate tenant" on the Review step) auto-navigates home — matching the
  // Feature 047 org wizard's behavior on final completion.
  const [initiallyActive, setInitiallyActive] = useState<boolean | null>(null);
  const navigate = useNavigate();

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const state = await tenantWizard.getState();
        if (!cancelled) {
          setProgress(state);
          setCurrentStep(state.currentStep);
          setInitiallyActive(state.onboardingState === 'Active');
        }
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

  // Auto-navigate home the first time the wizard transitions to Active
  // during *this* session. Re-entries (already-Active on load) stay open.
  useEffect(() => {
    if (!activatedThisSession) return;
    if (initiallyActive) return;
    navigate('/', { replace: true });
  }, [activatedThisSession, initiallyActive, navigate]);

  const completedSet = useMemo(
    () => new Set(progress?.completedSteps ?? []),
    [progress?.completedSteps],
  );
  const completedCount = progress?.completedSteps.length ?? 0;
  const totalSteps = ORDERED_STEPS.length;
  const percent = Math.round((completedCount / totalSteps) * 100);
  const isActive = progress?.onboardingState === 'Active';
  const canDismiss = isActive;

  const onAdvance = (next: TenantOnboardingProgress) => {
    setProgress(next);
    setBusy(false);
    setError(null);
    // Detect Review-step success: the tenant flipped to Active on this call.
    if (next.onboardingState === 'Active' && progress?.onboardingState !== 'Active') {
      setActivatedThisSession(true);
      return;
    }
    const idx = ORDERED_STEPS.findIndex((s) => s.name === currentStep);
    const nextStep = ORDERED_STEPS[idx + 1];
    if (nextStep) setCurrentStep(nextStep.name);
  };
  const onError = (message: string) => {
    setError(message);
    setBusy(false);
  };
  const beforeSubmit = () => {
    setError(null);
    setBusy(true);
  };

  const stepDef = ORDERED_STEPS.find((s) => s.name === currentStep);

  return (
    <div
      className="fixed inset-0 z-50 flex flex-col bg-slate-50"
      role="dialog"
      aria-modal="true"
      aria-labelledby="tenant-wizard-title"
    >
      {/* Gradient header */}
      <div className="relative bg-gradient-to-r from-indigo-700 via-indigo-700 to-sky-600 text-white">
        <div
          className="absolute inset-0 opacity-20"
          style={{
            backgroundImage:
              'radial-gradient(circle at 20% 30%, rgba(255,255,255,.4) 0, transparent 40%), radial-gradient(circle at 80% 70%, rgba(255,255,255,.25) 0, transparent 45%)',
          }}
          aria-hidden
        />
        <div className="relative mx-auto flex max-w-7xl items-center justify-between px-6 py-5">
          <div className="flex items-center gap-3">
            <div className="flex h-10 w-10 items-center justify-center rounded-lg bg-white/15 ring-1 ring-white/30 backdrop-blur">
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.8} className="h-6 w-6">
                <path strokeLinecap="round" strokeLinejoin="round" d="M12 2l9 5-9 5-9-5 9-5zM3 12l9 5 9-5M3 17l9 5 9-5" />
              </svg>
            </div>
            <div>
              <h1 id="tenant-wizard-title" className="text-xl font-semibold tracking-tight">
                SPIN Agent — Organization Onboarding
              </h1>
              <p className="text-sm text-white/80">
                Capture the headquarters, classification, and authorizing officials so this organization can issue ATOs.
              </p>
            </div>
          </div>
          <div className="flex items-center gap-4">
            <div className="text-right">
              <div className="text-xs uppercase tracking-wide text-white/70">Progress</div>
              <div className="text-lg font-semibold">
                {completedCount} <span className="text-white/70">/ {totalSteps}</span>
              </div>
            </div>
            {canDismiss && (
              <button
                type="button"
                onClick={() => navigate('/', { replace: true })}
                className="rounded-md p-1.5 text-white/80 hover:bg-white/10 hover:text-white"
                aria-label="Close wizard"
              >
                <svg className="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                  <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" />
                </svg>
              </button>
            )}
          </div>
        </div>
        <div className="relative h-1 bg-white/20">
          <div
            className="h-1 bg-white transition-[width] duration-300"
            style={{ width: `${percent}%` }}
            aria-hidden
          />
        </div>
      </div>

      {/* Body: sidebar + content */}
      <div className="flex flex-1 overflow-hidden">
        {/* Sidebar */}
        <aside className="hidden w-80 flex-shrink-0 overflow-y-auto border-r border-slate-200 bg-white px-3 py-4 md:block">
          <ol className="space-y-1">
            {ORDERED_STEPS.map((step) => {
              const isDone = completedSet.has(step.name);
              const isActiveStep = step.name === currentStep;
              return (
                <li key={step.name}>
                  <button
                    type="button"
                    onClick={() => setCurrentStep(step.name)}
                    className={[
                      'group flex w-full items-start gap-3 rounded-lg border px-3 py-2.5 text-left transition-all',
                      isActiveStep
                        ? 'border-indigo-300 bg-indigo-50 ring-1 ring-indigo-200'
                        : 'border-transparent hover:border-slate-200 hover:bg-slate-50',
                    ].join(' ')}
                    aria-current={isActiveStep ? 'step' : undefined}
                  >
                    <div
                      className={[
                        'flex h-9 w-9 flex-shrink-0 items-center justify-center rounded-full text-sm font-semibold ring-1',
                        isDone
                          ? 'bg-emerald-500 text-white ring-emerald-500'
                          : isActiveStep
                            ? 'bg-indigo-600 text-white ring-indigo-600'
                            : 'bg-slate-100 text-slate-600 ring-slate-200',
                      ].join(' ')}
                    >
                      {isDone ? (
                        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={3} className="h-4 w-4">
                          <path strokeLinecap="round" strokeLinejoin="round" d="M5 12l5 5L20 7" />
                        </svg>
                      ) : (
                        step.icon
                      )}
                    </div>
                    <div className="min-w-0 flex-1">
                      <div className="flex items-center gap-2">
                        <span
                          className={`text-xs font-semibold uppercase tracking-wide ${
                            isActiveStep ? 'text-indigo-700' : 'text-slate-400'
                          }`}
                        >
                          Step {step.number}
                        </span>
                        {step.name !== 'Submitted' && (
                          <span className="rounded-full bg-rose-100 px-1.5 py-0.5 text-[10px] font-medium uppercase tracking-wide text-rose-700">
                            Required
                          </span>
                        )}
                      </div>
                      <div className={`mt-0.5 text-sm ${isActiveStep ? 'font-semibold text-slate-900' : 'text-slate-700'}`}>
                        {step.title}
                      </div>
                      {isDone && <div className="mt-0.5 text-xs text-emerald-600">Complete</div>}
                    </div>
                  </button>
                </li>
              );
            })}
          </ol>

          {isActive && (
            <div className="mt-4 rounded-lg border border-emerald-200 bg-emerald-50 p-3 text-xs text-emerald-900">
              <div className="font-semibold">Tenant active</div>
              <p className="mt-1 leading-relaxed">
                Onboarding is complete. You can revisit any step to refine values, or close the wizard to return to the dashboard.
              </p>
            </div>
          )}
        </aside>

        {/* Content */}
        <main className="flex-1 overflow-y-auto">
          <div className="mx-auto max-w-3xl px-6 py-8">
            {error && (
              <div
                className="mb-4 rounded-md border border-rose-200 bg-rose-50 p-3 text-sm text-rose-800"
                role="alert"
              >
                {error}
              </div>
            )}

            {!progress ? (
              <div className="rounded-xl border border-slate-200 bg-white p-8 text-sm text-slate-500 shadow-sm">
                Loading…
              </div>
            ) : (
              <div className="overflow-hidden rounded-xl border border-slate-200 bg-white shadow-sm">
                <div className="flex items-start justify-between border-b border-slate-100 px-6 py-5">
                  <div>
                    <p className="text-xs font-semibold uppercase tracking-wide text-indigo-600">
                      Step {stepDef?.number ?? '?'} of {totalSteps}
                    </p>
                    <h2 className="mt-1 text-2xl font-semibold tracking-tight text-slate-900">
                      {stepDef?.title}
                    </h2>
                    <p className="mt-1 text-sm text-slate-500">{stepDef?.description}</p>
                  </div>
                </div>
                <div className="px-6 py-6">
                  {currentStep === 'Tenant.LegalEntity' && (
                    <LegalEntityStep
                      busy={busy}
                      beforeSubmit={beforeSubmit}
                      onAdvance={onAdvance}
                      onError={onError}
                    />
                  )}
                  {currentStep === 'Tenant.HqAddress' && (
                    <HqAddressStep
                      busy={busy}
                      beforeSubmit={beforeSubmit}
                      onAdvance={onAdvance}
                      onError={onError}
                    />
                  )}
                  {currentStep === 'Tenant.Classification' && (
                    <ClassificationStep
                      busy={busy}
                      beforeSubmit={beforeSubmit}
                      onAdvance={onAdvance}
                      onError={onError}
                    />
                  )}
                  {currentStep === 'Tenant.Ao' && (
                    <AoStep
                      busy={busy}
                      beforeSubmit={beforeSubmit}
                      onAdvance={onAdvance}
                      onError={onError}
                    />
                  )}
                  {currentStep === 'Tenant.PrimaryPoc' && (
                    <PrimaryPocStep
                      busy={busy}
                      beforeSubmit={beforeSubmit}
                      onAdvance={onAdvance}
                      onError={onError}
                    />
                  )}
                  {currentStep === 'Org.Profile' && (
                    <OrgProfileStep
                      busy={busy}
                      beforeSubmit={beforeSubmit}
                      onAdvance={onAdvance}
                      onError={onError}
                    />
                  )}
                  {currentStep === 'Submitted' && (
                    <ReviewStep
                      busy={busy}
                      beforeSubmit={beforeSubmit}
                      onAdvance={onAdvance}
                      onError={onError}
                    />
                  )}
                </div>
              </div>
            )}
          </div>
        </main>
      </div>
    </div>
  );
}
