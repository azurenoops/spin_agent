import { useState } from 'react';
import { tenantWizard } from '../api';
import type { StepProps } from './types';

/**
 * Step 4 — Authorizing Official (AO). Captures name + email of the
 * official who will sign ATOs for systems within this tenant. Persists
 * via <c>POST /api/onboarding/tenant/ao</c>.
 */
export default function AoStep({ busy, beforeSubmit, onAdvance, onError }: StepProps) {
  const [name, setName] = useState('');
  const [email, setEmail] = useState('');

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    beforeSubmit();
    try {
      const next = await tenantWizard.submitAo({
        authorizingOfficialName: name.trim(),
        authorizingOfficialEmail: email.trim(),
      });
      onAdvance(next);
    } catch (err) {
      onError((err as Error).message);
    }
  };

  const valid = name.trim() && /\S+@\S+\.\S+/.test(email.trim());

  return (
    <form onSubmit={submit} className="space-y-4">
      <header>
        <h2 className="text-lg font-semibold">Authorizing Official</h2>
        <p className="text-sm text-gray-600">
          The official accountable for ATO decisions across this tenant.
        </p>
      </header>
      <Field label="Full name" required>
        <input
          required
          value={name}
          onChange={(e) => setName(e.target.value)}
          className="w-full rounded border border-gray-300 px-3 py-2"
        />
      </Field>
      <Field label="Government email" required>
        <input
          required
          type="email"
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          className="w-full rounded border border-gray-300 px-3 py-2"
        />
      </Field>
      <button
        type="submit"
        disabled={busy || !valid}
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
