import { useEffect, useState } from 'react';
import { onboarding, type OnboardingStateDto, type WizardStepName } from './api/onboardingApi';
import OnboardingWizardModal from './OnboardingWizardModal';

/**
 * `OnboardingGate` — global app-level gate that auto-opens the onboarding
 * wizard modal whenever the active tenant has not yet completed the two
 * mandatory bootstrap steps (Organization Context + Roles).
 *
 * The gate fetches the wizard state once per app load and inspects the
 * `steps[]` array to determine whether either of `OrganizationContext` or
 * `Roles` has yet to be marked completed. If either is missing, the
 * wizard modal is rendered as an overlay covering the underlying app and
 * cannot be dismissed.
 *
 * Once both steps are recorded, the gate becomes inert (returns `null`).
 * The full wizard remains available via the `/onboarding` route for the
 * remaining (skippable) steps and admin re-runs.
 *
 * Wizard auth-forbidden envelopes (`WIZARD_AUTH_FORBIDDEN`) are silently
 * ignored here — the gate only forces the wizard for users that have
 * permission to use it.
 */
const REQUIRED_STEPS: WizardStepName[] = ['OrganizationContext', 'Roles'];

export default function OnboardingGate() {
  const [state, setState] = useState<OnboardingStateDto | null>(null);
  const [checked, setChecked] = useState(false);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const next = await onboarding.getState();
        if (!cancelled) setState(next);
      } catch {
        // Auth-forbidden / network errors → leave state null; gate stays closed.
      } finally {
        if (!cancelled) setChecked(true);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  if (!checked || !state) return null;

  // Only steps explicitly marked `Completed` count toward the required-step
  // gate. Skipped entries (e.g. someone hit the eMASS skip endpoint) must not
  // satisfy `OrganizationContext` / `Roles`, both of which are non-skippable.
  const completedNames = new Set(
    state.steps.filter((s) => s.status === 'Completed').map((s) => s.step),
  );
  const missingRequired = REQUIRED_STEPS.some((step) => !completedNames.has(step));

  if (!missingRequired) return null;

  // Force-open the modal until both required steps are complete.
  // The modal itself will refresh state on save, and once both required
  // steps record completion the gate auto-dismisses.
  return (
    <OnboardingWizardModal
      initialState={state}
      onStateChange={setState}
      forced
    />
  );
}
