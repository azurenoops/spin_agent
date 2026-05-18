import { tenantWizard } from '../api';
import type { StepProps } from './types';

/**
 * Step 7 — Final review + submit. Calls
 * <c>POST /api/onboarding/tenant/submit</c>; the server validates that
 * all six prior steps were recorded and that the seed Organization
 * exists, then transitions the tenant to <c>Active</c>.
 *
 * The wizard shell auto-redirects to <c>/</c> once the returned
 * <c>onboardingState</c> equals <c>Active</c>.
 */
export default function ReviewStep({ busy, beforeSubmit, onAdvance, onError }: StepProps) {
  const submit = async () => {
    beforeSubmit();
    try {
      const next = await tenantWizard.submitFinal();
      onAdvance(next);
    } catch (err) {
      onError((err as Error).message);
    }
  };

  return (
    <div className="space-y-4">
      <header>
        <h2 className="text-lg font-semibold">Ready to activate</h2>
        <p className="text-sm text-gray-600">
          All required fields are captured. Submitting will mark this tenant
          <strong> Active </strong>
          and unlock the rest of the application.
        </p>
      </header>
      <ul className="list-disc space-y-1 pl-5 text-sm text-gray-700">
        <li>Legal entity captured</li>
        <li>Headquarters address captured</li>
        <li>Default classification recorded</li>
        <li>Authorizing Official recorded</li>
        <li>Primary POC recorded</li>
        <li>First organization seeded</li>
      </ul>
      <button
        type="button"
        onClick={submit}
        disabled={busy}
        className="rounded bg-green-600 px-4 py-2 font-medium text-white disabled:opacity-50"
      >
        {busy ? 'Submitting…' : 'Activate tenant'}
      </button>
    </div>
  );
}
