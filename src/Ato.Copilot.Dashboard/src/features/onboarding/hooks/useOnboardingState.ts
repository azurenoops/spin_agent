import { useCallback, useEffect, useState } from 'react';
import { onboarding } from '../api/onboardingApi';
import type { OnboardingStateDto, WizardStepName } from '../api/onboardingApi';

/**
 * `useOnboardingState` — fetches and exposes the wizard state for the current
 * tenant, with helpers to start the wizard and skip steps. Pure foundational
 * layer; per-step mutations live in step-specific hooks added under user stories.
 */
export function useOnboardingState() {
  const [state, setState] = useState<OnboardingStateDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<Error | null>(null);

  const refresh = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const next = await onboarding.getState();
      setState(next);
    } catch (e) {
      setError(e as Error);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  const start = useCallback(async () => {
    const next = await onboarding.start();
    setState(next);
    return next;
  }, []);

  const skip = useCallback(async (step: WizardStepName) => {
    await onboarding.skipStep(step);
    await refresh();
  }, [refresh]);

  return { state, loading, error, refresh, start, skip };
}
