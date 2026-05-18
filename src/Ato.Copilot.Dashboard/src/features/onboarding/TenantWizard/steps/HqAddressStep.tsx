import { useState } from 'react';
import { tenantWizard } from '../api';
import type { StepProps } from './types';

/**
 * Step 2 — Headquarters address. All six required fields enforce FR-001
 * coverage (line1, city, state/province, postal, country). Persists via
 * <c>POST /api/onboarding/tenant/hq-address</c>.
 */
export default function HqAddressStep({ busy, beforeSubmit, onAdvance, onError }: StepProps) {
  const [line1, setLine1] = useState('');
  const [line2, setLine2] = useState('');
  const [city, setCity] = useState('');
  const [region, setRegion] = useState('');
  const [postal, setPostal] = useState('');
  const [country, setCountry] = useState('US');

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    beforeSubmit();
    try {
      const next = await tenantWizard.submitHqAddress({
        hqAddressLine1: line1.trim(),
        hqAddressLine2: line2.trim() || null,
        hqCity: city.trim(),
        hqStateOrProvince: region.trim(),
        hqPostalCode: postal.trim(),
        hqCountry: country.trim(),
      });
      onAdvance(next);
    } catch (err) {
      onError((err as Error).message);
    }
  };

  const valid =
    line1.trim() && city.trim() && region.trim() && postal.trim() && country.trim();

  return (
    <form onSubmit={submit} className="space-y-4">
      <header>
        <h2 className="text-lg font-semibold">Headquarters address</h2>
        <p className="text-sm text-gray-600">
          Required for the SSP cover sheet and AO contact metadata.
        </p>
      </header>
      <Field label="Address line 1" required>
        <input
          required
          value={line1}
          onChange={(e) => setLine1(e.target.value)}
          className="w-full rounded border border-gray-300 px-3 py-2"
        />
      </Field>
      <Field label="Address line 2">
        <input
          value={line2}
          onChange={(e) => setLine2(e.target.value)}
          className="w-full rounded border border-gray-300 px-3 py-2"
        />
      </Field>
      <div className="grid grid-cols-2 gap-3">
        <Field label="City" required>
          <input
            required
            value={city}
            onChange={(e) => setCity(e.target.value)}
            className="w-full rounded border border-gray-300 px-3 py-2"
          />
        </Field>
        <Field label="State / Province" required>
          <input
            required
            value={region}
            onChange={(e) => setRegion(e.target.value)}
            className="w-full rounded border border-gray-300 px-3 py-2"
          />
        </Field>
        <Field label="Postal code" required>
          <input
            required
            value={postal}
            onChange={(e) => setPostal(e.target.value)}
            className="w-full rounded border border-gray-300 px-3 py-2"
          />
        </Field>
        <Field label="Country" required>
          <input
            required
            value={country}
            onChange={(e) => setCountry(e.target.value)}
            className="w-full rounded border border-gray-300 px-3 py-2"
          />
        </Field>
      </div>
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
