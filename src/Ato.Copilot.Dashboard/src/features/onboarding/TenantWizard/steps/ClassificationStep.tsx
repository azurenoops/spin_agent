import { useState } from 'react';
import { tenantWizard, type ClassificationLevel } from '../api';
import type { StepProps } from './types';

const LEVELS: { value: ClassificationLevel; label: string; help: string }[] = [
  {
    value: 'Unclassified',
    label: 'Unclassified',
    help: 'Public or non-sensitive data. Default.',
  },
  {
    value: 'CUI',
    label: 'CUI',
    help: 'Controlled Unclassified Information.',
  },
  {
    value: 'Secret',
    label: 'Secret',
    help: 'Tenants on classified networks (SIPRNet, etc.).',
  },
];

/**
 * Step 3 — Default classification level. Used to gate document marking
 * banners + AI prompts. Persists via <c>POST /api/onboarding/tenant/classification</c>.
 */
export default function ClassificationStep({
  busy,
  beforeSubmit,
  onAdvance,
  onError,
}: StepProps) {
  const [level, setLevel] = useState<ClassificationLevel>('Unclassified');

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    beforeSubmit();
    try {
      const next = await tenantWizard.submitClassification({
        defaultClassificationLevel: level,
      });
      onAdvance(next);
    } catch (err) {
      onError((err as Error).message);
    }
  };

  return (
    <form onSubmit={submit} className="space-y-4">
      <header>
        <h2 className="text-lg font-semibold">Default classification level</h2>
        <p className="text-sm text-gray-600">
          Sets the maximum data sensitivity this tenant may handle by default.
          Individual systems can still be marked higher.
        </p>
      </header>
      <fieldset className="space-y-2">
        {LEVELS.map((opt) => (
          <label
            key={opt.value}
            className={`flex cursor-pointer items-start gap-3 rounded border px-3 py-2 ${
              level === opt.value ? 'border-blue-400 bg-blue-50' : 'border-gray-200'
            }`}
          >
            <input
              type="radio"
              name="classification"
              value={opt.value}
              checked={level === opt.value}
              onChange={() => setLevel(opt.value)}
              className="mt-1"
            />
            <span>
              <span className="block font-medium text-gray-800">{opt.label}</span>
              <span className="block text-xs text-gray-600">{opt.help}</span>
            </span>
          </label>
        ))}
      </fieldset>
      <button
        type="submit"
        disabled={busy}
        className="rounded bg-blue-600 px-4 py-2 font-medium text-white disabled:opacity-50"
      >
        {busy ? 'Saving…' : 'Next'}
      </button>
    </form>
  );
}
