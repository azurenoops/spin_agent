import { useEffect, useState } from 'react';
import { onboarding, type OnboardingStateDto, type WizardStepName } from './api/onboardingApi';
import OnboardingWizardModal from './OnboardingWizardModal';
import {
  getCspOnboardingState,
  isUnavailable as isCspOnboardingUnavailable,
} from '../csp-onboarding/api';
import { readImpersonation } from '../tenancy/api';

/**
 * `OnboardingGate` — global app-level gate that auto-opens the Feature 047
 * organization-context wizard whenever the *active org* has not yet
 * completed the two mandatory bootstrap steps (Organization Context + Roles).
 *
 * Role-aware short-circuit (Feature 048 follow-up): this wizard is the
 * "Org-Admin / Mission-Owner" onboarding surface — it captures the details
 * of an actual hosted organization. It must NOT auto-open for a CSP-Admin
 * who is operating at the deployment scope (i.e. not impersonating any
 * tenant), because there is no org for them to onboard there — their
 * landing page is the CSP Portfolio. The gate therefore:
 *
 *   1. Probes `GET /api/csp/onboarding/state`. A successful response means
 *      the caller is a CSP-Admin in a `MultiTenant` deployment.
 *   2. Checks the local impersonation mirror (`readImpersonation()`). If
 *      the CSP-Admin is currently impersonating a real org, the gate stays
 *      armed — that org may still need the bootstrap wizard. If they are
 *      NOT impersonating, the gate is suppressed.
 *
 * For everyone else (Org-Admin in a `MultiTenant` deployment, any role in
 * a `SingleTenant` deployment), the CSP probe returns an unavailable
 * sentinel and the gate uses its original behavior — open the wizard when
 * `OrganizationContext` or `Roles` is missing.
 *
 * Once both required steps are recorded, the gate becomes inert. The full
 * wizard remains available via the `/onboarding` route for the remaining
 * skippable steps and for admin re-runs.
 *
 * Wizard auth-forbidden envelopes (`WIZARD_AUTH_FORBIDDEN`) are silently
 * ignored — the gate only forces the wizard for users that have permission
 * to use it.
 */
const REQUIRED_STEPS: WizardStepName[] = ['OrganizationContext', 'Roles'];

export default function OnboardingGate() {
  const [state, setState] = useState<OnboardingStateDto | null>(null);
  const [checked, setChecked] = useState(false);
  const [suppressedForCspAdmin, setSuppressedForCspAdmin] = useState(false);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      // Role-aware short-circuit. We probe the CSP onboarding endpoint
      // FIRST so we can suppress the wizard for a CSP-Admin at deployment
      // scope without first fetching the org-onboarding state (which would
      // return an org-shaped payload pointing at a vestige tenant).
      try {
        const cspState = await getCspOnboardingState();
        if (cancelled) return;
        if (!isCspOnboardingUnavailable(cspState)) {
          // Caller is a CSP-Admin in a MultiTenant deployment. Only force
          // the org-context wizard if they are actively impersonating a
          // hosted tenant; otherwise their home is the CSP Portfolio.
          const impersonation = readImpersonation();
          if (impersonation === null) {
            setSuppressedForCspAdmin(true);
            setChecked(true);
            return;
          }
        }
      } catch {
        // Probe failure is non-fatal — fall through to the original
        // org-scoped behavior. The user may be an Org-Admin or the CSP
        // endpoint may be unreachable; either way the gate's existing
        // `WIZARD_AUTH_FORBIDDEN` handling below is the right default.
      }

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

  if (!checked) return null;
  if (suppressedForCspAdmin) return null;
  if (!state) return null;

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
