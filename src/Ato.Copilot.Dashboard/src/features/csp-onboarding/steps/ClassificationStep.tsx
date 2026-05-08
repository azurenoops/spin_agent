import { useState, type FormEvent } from 'react';
import type { ClassificationFloor, ClassificationRequest } from '../api';

interface ClassificationStepProps {
  initial: { defaultClassificationFloor?: ClassificationFloor | null };
  saving: boolean;
  errorMessage: string | null;
  onSubmit: (payload: ClassificationRequest) => void;
  onBack: () => void;
}

const FLOORS: Array<{
  value: ClassificationFloor;
  label: string;
  description: string;
}> = [
  {
    value: 'Unclassified',
    label: 'Unclassified',
    description: 'Lowest floor — accepts any tenant classification, including Public.',
  },
  {
    value: 'CUI',
    label: 'CUI',
    description: 'Controlled Unclassified Information. Tenants below this floor will be rejected.',
  },
  {
    value: 'Secret',
    label: 'Secret',
    description: 'Highest available floor. Only Secret-and-above tenants may onboard.',
  },
];

/**
 * Step 3 — captures the default classification floor for tenants on this
 * deployment. New tenants whose declared classification is below the floor
 * are rejected at onboarding time. Floor cannot be raised retroactively
 * without an audit-logged migration (FR-091).
 */
export default function ClassificationStep({
  initial,
  saving,
  errorMessage,
  onSubmit,
  onBack,
}: ClassificationStepProps) {
  const [floor, setFloor] = useState<ClassificationFloor>(
    initial.defaultClassificationFloor ?? 'Unclassified',
  );

  function handleSubmit(e: FormEvent<HTMLFormElement>): void {
    e.preventDefault();
    if (saving) return;
    onSubmit({ defaultClassificationFloor: floor });
  }

  return (
    <form className="space-y-4" onSubmit={handleSubmit} noValidate>
      <div>
        <h2 className="text-lg font-semibold text-gray-900">Step 3 — Classification floor</h2>
        <p className="mt-1 text-sm text-gray-600">
          Set the default minimum classification level for tenants in this
          deployment. Tenants below the floor cannot onboard.
        </p>
      </div>

      <fieldset className="space-y-3">
        <legend className="sr-only">Default classification floor</legend>
        {FLOORS.map((opt) => {
          const selected = floor === opt.value;
          return (
            <label
              key={opt.value}
              className={`flex cursor-pointer items-start gap-3 rounded-md border px-4 py-3 transition-colors ${
                selected
                  ? 'border-indigo-500 bg-indigo-50 ring-1 ring-indigo-500'
                  : 'border-gray-200 bg-white hover:border-gray-300'
              }`}
            >
              <input
                type="radio"
                name="csp-classification-floor"
                value={opt.value}
                checked={selected}
                onChange={() => setFloor(opt.value)}
                disabled={saving}
                className="mt-1 h-4 w-4 border-gray-300 text-indigo-600 focus:ring-indigo-500"
              />
              <div>
                <span className="block text-sm font-medium text-gray-900">{opt.label}</span>
                <span className="mt-0.5 block text-xs text-gray-600">{opt.description}</span>
              </div>
            </label>
          );
        })}
      </fieldset>

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
          type="submit"
          disabled={saving}
          className="rounded-md bg-indigo-600 px-4 py-2 text-sm font-medium text-white shadow-sm hover:bg-indigo-700 focus:outline-none focus:ring-2 focus:ring-indigo-500 disabled:cursor-not-allowed disabled:bg-indigo-300"
        >
          {saving ? 'Saving…' : 'Save & continue'}
        </button>
      </div>
    </form>
  );
}
