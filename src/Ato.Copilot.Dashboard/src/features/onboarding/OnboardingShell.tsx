import { useEffect, useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { onboarding, type OnboardingStateDto } from './api/onboardingApi';
import OnboardingWizardModal from './OnboardingWizardModal';

/**
 * `OnboardingShell` — route entry point for the onboarding wizard
 * (`/onboarding`). Loads the current wizard state and delegates rendering
 * to {@link OnboardingWizardModal}, which presents the wizard as an in-app
 * modal experience consistent with the Register-System wizard.
 *
 * The modal here is closeable (since the user explicitly navigated to
 * `/onboarding`); the {@link import('./OnboardingGate').default} component
 * is responsible for forcing it open when bootstrap onboarding has not yet
 * been completed.
 */
export default function OnboardingShell() {
  const [state, setState] = useState<OnboardingStateDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<Error | null>(null);
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const adminRerun = searchParams.get('stepNav') === 'admin';

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const next = await onboarding.getState();
        if (!cancelled) setState(next);
      } catch (e) {
        if (!cancelled) setError(e as Error);
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  if (loading) {
    return <p role="status" className="p-6 text-sm text-slate-500">Loading onboarding state…</p>;
  }
  if (error) {
    const ec = (error as { errorCode?: string }).errorCode;
    if (ec === 'WIZARD_AUTH_FORBIDDEN') {
      return (
        <div role="alert" className="mx-auto max-w-2xl p-6">
          <h2 className="text-lg font-semibold text-slate-900">Onboarding wizard</h2>
          <p className="mt-2 text-sm text-slate-700">You do not have permission to use the onboarding wizard.</p>
          <p className="mt-1 text-xs text-slate-500">
            Sign in with an account that holds the Administrator role for your tenant.
          </p>
        </div>
      );
    }
    return (
      <div role="alert" className="mx-auto max-w-2xl p-6 text-sm text-rose-700">
        Failed to load onboarding state: {error.message}
      </div>
    );
  }
  if (!state) return null;

  if (state.status === 'Completed' && !adminRerun) {
    navigate('/', { replace: true });
    return null;
  }

  return (
    <OnboardingWizardModal
      initialState={state}
      onStateChange={setState}
      forced={false}
      onClose={() => navigate('/', { replace: true })}
    />
  );
}

