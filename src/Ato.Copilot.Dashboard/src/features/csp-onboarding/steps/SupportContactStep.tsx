import { useState, type FormEvent } from 'react';
import type { SupportContactRequest } from '../api';

interface SupportContactStepProps {
  initial: { primarySupportEmail?: string | null; supportPhone?: string | null };
  saving: boolean;
  errorMessage: string | null;
  onSubmit: (payload: SupportContactRequest) => void;
  onBack: () => void;
}

const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

/**
 * Step 2 — captures the CSP's primary support contact (email + optional
 * phone). Email is shown to tenants when their environment generates a
 * service-impacting alert (e.g. circuit-breaker open, JIT timeout).
 */
export default function SupportContactStep({
  initial,
  saving,
  errorMessage,
  onSubmit,
  onBack,
}: SupportContactStepProps) {
  const [email, setEmail] = useState(initial.primarySupportEmail ?? '');
  const [phone, setPhone] = useState(initial.supportPhone ?? '');

  const trimmedEmail = email.trim();
  const trimmedPhone = phone.trim();

  const emailValid = EMAIL_RE.test(trimmedEmail);
  const formValid = emailValid;

  function handleSubmit(e: FormEvent<HTMLFormElement>): void {
    e.preventDefault();
    if (!formValid || saving) return;
    onSubmit({
      primarySupportEmail: trimmedEmail,
      supportPhone: trimmedPhone.length > 0 ? trimmedPhone : null,
    });
  }

  return (
    <form className="space-y-4" onSubmit={handleSubmit} noValidate>
      <div>
        <h2 className="text-lg font-semibold text-gray-900">Step 2 — Support contact</h2>
        <p className="mt-1 text-sm text-gray-600">
          Where should tenants reach you for service-impacting issues?
        </p>
      </div>

      <div>
        <label htmlFor="csp-support-email" className="block text-sm font-medium text-gray-700">
          Primary support email
          <span aria-hidden className="ml-0.5 text-red-500">
            *
          </span>
        </label>
        <input
          id="csp-support-email"
          type="email"
          required
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2 text-sm shadow-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
          placeholder="ato-support@contoso.example.us"
          disabled={saving}
        />
        {!emailValid && trimmedEmail.length > 0 && (
          <p className="mt-1 text-xs text-red-600">Enter a valid email address.</p>
        )}
      </div>

      <div>
        <label htmlFor="csp-support-phone" className="block text-sm font-medium text-gray-700">
          Support phone <span className="text-gray-400">(optional)</span>
        </label>
        <input
          id="csp-support-phone"
          type="tel"
          value={phone}
          onChange={(e) => setPhone(e.target.value)}
          className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2 text-sm shadow-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
          placeholder="+1 (555) 123-4567"
          disabled={saving}
        />
      </div>

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
          disabled={!formValid || saving}
          className="rounded-md bg-indigo-600 px-4 py-2 text-sm font-medium text-white shadow-sm hover:bg-indigo-700 focus:outline-none focus:ring-2 focus:ring-indigo-500 disabled:cursor-not-allowed disabled:bg-indigo-300"
        >
          {saving ? 'Saving…' : 'Save & continue'}
        </button>
      </div>
    </form>
  );
}
