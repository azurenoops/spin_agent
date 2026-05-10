import { useEffect, useState } from 'react';
import {
  getCspOnboardingAtosState,
  type AtoStepState,
  type CspOnboardingStateDto,
} from '../api';

interface ReviewStepProps {
  state: CspOnboardingStateDto;
  saving: boolean;
  errorMessage: string | null;
  onSubmit: () => void;
  onBack: () => void;
}

/**
 * Step 5 — Review + Submit. Displays everything captured in steps 1–4
 * and submits once the operator confirms.
 *
 * After successful submission `OnboardingState` flips to `Active`, the
 * `503 CSP_ONBOARDING_INCOMPLETE` middleware gate lifts, and tenant
 * pre-provisioning becomes available.
 *
 * Feature 048 / US9 / T213: Surfaces the running ATO upload tally
 * (`componentsExtracted`, `capabilitiesMapped`, `capabilitiesNeedsReview`)
 * and an FR-102 banner when the AI capability mapper was unavailable
 * during upload.
 */
export default function ReviewStep({
  state,
  saving,
  errorMessage,
  onSubmit,
  onBack,
}: ReviewStepProps) {
  const id = state.identity ?? {};
  const sup = state.supportContact ?? {};
  const cls = state.classification ?? {};

  const [atoState, setAtoState] = useState<AtoStepState | null>(null);

  useEffect(() => {
    let cancelled = false;
    void (async () => {
      try {
        const next = await getCspOnboardingAtosState();
        if (!cancelled) setAtoState(next);
      } catch {
        // Best-effort — Review still renders without the tally row.
        if (!cancelled) setAtoState(null);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  return (
    <div className="space-y-4">
      <div>
        <h2 className="text-lg font-semibold text-gray-900">Step 5 — Review &amp; submit</h2>
        <p className="mt-1 text-sm text-gray-600">
          Confirm the values below. After submitting, the deployment will
          accept tenant pre-provisioning. Use the back button to make
          changes — this is your last chance before the audit log is written.
        </p>
      </div>

      <dl className="divide-y divide-gray-200 rounded-md border border-gray-200 bg-white text-sm">
        <div className="grid grid-cols-3 gap-4 px-4 py-3">
          <dt className="font-medium text-gray-700">Legal entity</dt>
          <dd className="col-span-2 text-gray-900">{id.legalEntityName ?? <em className="text-gray-400">(missing)</em>}</dd>
        </div>
        <div className="grid grid-cols-3 gap-4 px-4 py-3">
          <dt className="font-medium text-gray-700">Display name</dt>
          <dd className="col-span-2 text-gray-900">{id.displayName ?? <em className="text-gray-400">(missing)</em>}</dd>
        </div>
        {id.logoUrl && (
          <div className="grid grid-cols-3 gap-4 px-4 py-3">
            <dt className="font-medium text-gray-700">Logo</dt>
            <dd className="col-span-2">
              <img src={id.logoUrl} alt={`${id.displayName ?? 'CSP'} logo`} className="h-10 w-auto object-contain" />
            </dd>
          </div>
        )}
        <div className="grid grid-cols-3 gap-4 px-4 py-3">
          <dt className="font-medium text-gray-700">Support email</dt>
          <dd className="col-span-2 text-gray-900">{sup.primarySupportEmail ?? <em className="text-gray-400">(missing)</em>}</dd>
        </div>
        {sup.supportPhone && (
          <div className="grid grid-cols-3 gap-4 px-4 py-3">
            <dt className="font-medium text-gray-700">Support phone</dt>
            <dd className="col-span-2 text-gray-900">{sup.supportPhone}</dd>
          </div>
        )}
        <div className="grid grid-cols-3 gap-4 px-4 py-3">
          <dt className="font-medium text-gray-700">Classification floor</dt>
          <dd className="col-span-2 text-gray-900">{cls.defaultClassificationFloor ?? <em className="text-gray-400">(missing)</em>}</dd>
        </div>
        {atoState !== null && (
          <div className="grid grid-cols-3 gap-4 px-4 py-3" data-testid="review-ato-tally">
            <dt className="font-medium text-gray-700">ATO documents</dt>
            <dd className="col-span-2 text-gray-900">
              {atoState.documentsUploaded === 0 ? (
                <em className="text-gray-400">No ATO documents uploaded (optional).</em>
              ) : (
                <>
                  {atoState.documentsUploaded} document
                  {atoState.documentsUploaded === 1 ? '' : 's'} →{' '}
                  {atoState.componentsExtracted} component
                  {atoState.componentsExtracted === 1 ? '' : 's'} extracted,{' '}
                  {atoState.capabilitiesMapped} capabilit
                  {atoState.capabilitiesMapped === 1 ? 'y' : 'ies'} auto-mapped
                  {atoState.capabilitiesNeedsReview > 0 && (
                    <>, {atoState.capabilitiesNeedsReview} need
                    {atoState.capabilitiesNeedsReview === 1 ? 's' : ''} review</>
                  )}
                  .
                </>
              )}
            </dd>
          </div>
        )}
      </dl>

      {atoState && !atoState.aiMappingAvailable && atoState.documentsUploaded > 0 && (
        <div
          role="alert"
          className="rounded-md border border-amber-300 bg-amber-50 px-3 py-2 text-sm text-amber-800"
          data-testid="review-ai-mapping-unavailable-banner"
        >
          <p className="font-medium">AI capability mapping was unavailable during upload.</p>
          <p className="mt-1">
            Components were imported but capabilities were not auto-mapped
            {atoState.aiMappingFailureReason ? ` (${atoState.aiMappingFailureReason})` : ''}.
            You can re-run mapping after onboarding from the{' '}
            <em>Inherited Components</em> page.
          </p>
        </div>
      )}

      {errorMessage && (
        <div role="alert" className="rounded-md border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">
          {errorMessage}
        </div>
      )}

      <div className="flex justify-between pt-2">
        <button
          type="button"
          onClick={onBack}
          disabled={saving}
          className="rounded-md border border-gray-300 bg-white px-4 py-2 text-sm font-medium text-gray-700 shadow-sm hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-indigo-500 disabled:cursor-not-allowed disabled:opacity-50"
        >
          Back
        </button>
        <button
          type="button"
          onClick={onSubmit}
          disabled={saving}
          className="rounded-md bg-emerald-600 px-4 py-2 text-sm font-medium text-white shadow-sm hover:bg-emerald-700 focus:outline-none focus:ring-2 focus:ring-emerald-500 disabled:cursor-not-allowed disabled:bg-emerald-300"
        >
          {saving ? 'Submitting…' : 'Submit & finalize onboarding'}
        </button>
      </div>
    </div>
  );
}
