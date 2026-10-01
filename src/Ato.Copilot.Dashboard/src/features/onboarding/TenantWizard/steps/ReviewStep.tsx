import { tenantWizard } from '../api';
import type { StepProps } from './types';
import { useTenantDraftRevision } from '../draftContext';

/**
 * Step 7 — Final review + submit. Calls
 * <c>POST /api/onboarding/tenant/submit</c>; the server validates that
 * all six prior steps were recorded and that the seed Organization
 * exists, then transitions the tenant to <c>Active</c>.
 *
 * The wizard shell auto-redirects to <c>/</c> once the returned
 * <c>onboardingState</c> equals <c>Active</c>.
 */
export default function ReviewStep({ busy, beforeSubmit, onAdvance, onError, blocked }: StepProps & { blocked?: boolean }) {
  const revision = useTenantDraftRevision();
  const submit = async () => {
    beforeSubmit();
    try {
      const next = await tenantWizard.submitFinal(revision);
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
          The server validates applied fields before marking this tenant
          <strong> Active </strong>
          and unlock the rest of the application.
        </p>
      </header>
      <p className="text-sm">Saved drafts are not submitted facts. Apply changes in their steps before activation. This does not issue a system authorization or assign system roles.</p>
      {blocked && <p role="status">Apply your changed steps and complete the required fields before activation.</p>}
      <button
        type="button"
        onClick={submit}
        disabled={busy || blocked}
        className="rounded bg-green-600 px-4 py-2 font-medium text-white disabled:opacity-50"
      >
        {busy ? 'Submitting…' : 'Activate tenant'}
      </button>
    </div>
  );
}
