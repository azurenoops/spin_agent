import { useState } from 'react';
import { tenantWizard } from '../api';
import type { StepProps } from './types';

/**
 * Step 6 — Seed the tenant's first <c>Organization</c>. Required before
 * the tenant can be marked Active. Persists via
 * <c>POST /api/onboarding/tenant/org-profile</c>.
 */
export default function OrgProfileStep({
  busy,
  beforeSubmit,
  onAdvance,
  onError,
}: StepProps) {
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    beforeSubmit();
    try {
      const next = await tenantWizard.submitOrgProfile({
        name: name.trim(),
        description: description.trim() || null,
      });
      onAdvance(next);
    } catch (err) {
      onError((err as Error).message);
    }
  };

  return (
    <form onSubmit={submit} className="space-y-4">
      <header>
        <h2 className="text-lg font-semibold">First organization</h2>
        <p className="text-sm text-gray-600">
          Create the first organization within this tenant. You can add more
          organizations after onboarding completes.
        </p>
      </header>
      <Field label="Organization name" required>
        <input
          required
          value={name}
          onChange={(e) => setName(e.target.value)}
          className="w-full rounded border border-gray-300 px-3 py-2"
        />
      </Field>
      <Field label="Description (optional)">
        <textarea
          value={description}
          onChange={(e) => setDescription(e.target.value)}
          rows={3}
          className="w-full rounded border border-gray-300 px-3 py-2"
        />
      </Field>
      <button
        type="submit"
        disabled={busy || !name.trim()}
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
