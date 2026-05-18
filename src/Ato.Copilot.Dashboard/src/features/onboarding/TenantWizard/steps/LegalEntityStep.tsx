import { useState } from 'react';
import { tenantWizard } from '../api';
import type { StepProps } from './types';

/**
 * Step 1 — Capture the tenant's legal entity name + (optional) DoD
 * component code and time zone. Maps to FR-001 and persists via
 * <c>POST /api/onboarding/tenant/legal-entity</c>.
 */
export default function LegalEntityStep({ busy, beforeSubmit, onAdvance, onError }: StepProps) {
  const [legalEntityName, setLegalEntityName] = useState('');
  const [doDComponent, setDoDComponent] = useState('');
  const [timeZone, setTimeZone] = useState('');

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    beforeSubmit();
    try {
      const next = await tenantWizard.submitLegalEntity({
        legalEntityName: legalEntityName.trim(),
        doDComponent: doDComponent.trim() || null,
        timeZone: timeZone.trim() || null,
      });
      onAdvance(next);
    } catch (err) {
      onError((err as Error).message);
    }
  };

  return (
    <form onSubmit={submit} className="space-y-4">
      <header>
        <h2 className="text-lg font-semibold">Legal entity</h2>
        <p className="text-sm text-gray-600">
          Identify the chartered organization that will own this tenant.
        </p>
      </header>
      <Field label="Legal entity name" required>
        <input
          required
          value={legalEntityName}
          onChange={(e) => setLegalEntityName(e.target.value)}
          className="w-full rounded border border-gray-300 px-3 py-2"
        />
      </Field>
      <Field label="DoD component (optional)">
        <input
          value={doDComponent}
          onChange={(e) => setDoDComponent(e.target.value)}
          placeholder="e.g. ARMY, NAVY, AF"
          className="w-full rounded border border-gray-300 px-3 py-2"
        />
      </Field>
      <Field label="Time zone (optional)">
        <input
          value={timeZone}
          onChange={(e) => setTimeZone(e.target.value)}
          placeholder="e.g. America/New_York"
          className="w-full rounded border border-gray-300 px-3 py-2"
        />
      </Field>
      <button
        type="submit"
        disabled={busy || !legalEntityName.trim()}
        className="rounded bg-blue-600 px-4 py-2 font-medium text-white disabled:opacity-50"
      >
        {busy ? 'Saving…' : 'Next'}
      </button>
    </form>
  );
}

function Field({
  label,
  required,
  children,
}: {
  label: string;
  required?: boolean;
  children: React.ReactNode;
}) {
  return (
    <label className="block text-sm">
      <span className="mb-1 block font-medium text-gray-700">
        {label}
        {required && <span className="text-red-600"> *</span>}
      </span>
      {children}
    </label>
  );
}
