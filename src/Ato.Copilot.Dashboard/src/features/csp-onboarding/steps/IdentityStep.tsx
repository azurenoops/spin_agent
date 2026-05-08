import { useState, type FormEvent } from 'react';
import type { IdentityRequest } from '../api';

interface IdentityStepProps {
  initial: { legalEntityName?: string | null; displayName?: string | null; logoUrl?: string | null };
  saving: boolean;
  errorMessage: string | null;
  onSubmit: (payload: IdentityRequest) => void;
}

/**
 * Step 1 of the CSP onboarding wizard. Captures the hosting CSP's legal
 * entity name (e.g. "Contoso Federal Cloud, LLC"), short display name
 * (shown in the dashboard header), and an optional logo URL.
 *
 * Validation mirrors `IdentityRequest` from the OpenAPI contract:
 *   - legalEntityName: required, 2–256 chars
 *   - displayName: required, 2–64 chars
 *   - logoUrl: optional URI
 */
export default function IdentityStep({
  initial,
  saving,
  errorMessage,
  onSubmit,
}: IdentityStepProps) {
  const [legalEntityName, setLegalEntityName] = useState(initial.legalEntityName ?? '');
  const [displayName, setDisplayName] = useState(initial.displayName ?? '');
  const [logoUrl, setLogoUrl] = useState(initial.logoUrl ?? '');

  const trimmedLegal = legalEntityName.trim();
  const trimmedDisplay = displayName.trim();
  const trimmedLogo = logoUrl.trim();

  const legalValid = trimmedLegal.length >= 2 && trimmedLegal.length <= 256;
  const displayValid = trimmedDisplay.length >= 2 && trimmedDisplay.length <= 64;

  function isValidUrl(value: string): boolean {
    if (value.length === 0) return true;
    try {
      // eslint-disable-next-line no-new
      new URL(value);
      return true;
    } catch {
      return false;
    }
  }

  const logoValid = isValidUrl(trimmedLogo);
  const formValid = legalValid && displayValid && logoValid;

  function handleSubmit(e: FormEvent<HTMLFormElement>): void {
    e.preventDefault();
    if (!formValid || saving) return;
    onSubmit({
      legalEntityName: trimmedLegal,
      displayName: trimmedDisplay,
      logoUrl: trimmedLogo.length > 0 ? trimmedLogo : null,
    });
  }

  return (
    <form className="space-y-4" onSubmit={handleSubmit} noValidate>
      <div>
        <h2 className="text-lg font-semibold text-gray-900">Step 1 — CSP identity</h2>
        <p className="mt-1 text-sm text-gray-600">
          Tell us who you are. The legal entity name appears on every audit-log
          export and SSP cover page; the display name is shown in the
          dashboard header.
        </p>
      </div>

      <div>
        <label htmlFor="csp-legal-entity" className="block text-sm font-medium text-gray-700">
          Legal entity name
          <span aria-hidden className="ml-0.5 text-red-500">
            *
          </span>
        </label>
        <input
          id="csp-legal-entity"
          type="text"
          required
          minLength={2}
          maxLength={256}
          value={legalEntityName}
          onChange={(e) => setLegalEntityName(e.target.value)}
          className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2 text-sm shadow-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
          placeholder="Contoso Federal Cloud, LLC"
          disabled={saving}
        />
        {!legalValid && trimmedLegal.length > 0 && (
          <p className="mt-1 text-xs text-red-600">Must be 2–256 characters.</p>
        )}
      </div>

      <div>
        <label htmlFor="csp-display-name" className="block text-sm font-medium text-gray-700">
          Display name
          <span aria-hidden className="ml-0.5 text-red-500">
            *
          </span>
        </label>
        <input
          id="csp-display-name"
          type="text"
          required
          minLength={2}
          maxLength={64}
          value={displayName}
          onChange={(e) => setDisplayName(e.target.value)}
          className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2 text-sm shadow-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
          placeholder="Contoso Cloud"
          disabled={saving}
        />
        {!displayValid && trimmedDisplay.length > 0 && (
          <p className="mt-1 text-xs text-red-600">Must be 2–64 characters.</p>
        )}
      </div>

      <div>
        <label htmlFor="csp-logo-url" className="block text-sm font-medium text-gray-700">
          Logo URL <span className="text-gray-400">(optional)</span>
        </label>
        <input
          id="csp-logo-url"
          type="url"
          maxLength={2048}
          value={logoUrl}
          onChange={(e) => setLogoUrl(e.target.value)}
          className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2 text-sm shadow-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
          placeholder="https://contoso.example.us/logo.png"
          disabled={saving}
        />
        {!logoValid && (
          <p className="mt-1 text-xs text-red-600">Must be a valid URL.</p>
        )}
        {logoValid && trimmedLogo.length > 0 && (
          <div className="mt-2 flex items-center gap-3 rounded-md border border-gray-200 bg-gray-50 px-3 py-2">
            <span className="text-xs text-gray-500">Preview:</span>
            <img
              src={trimmedLogo}
              alt="CSP logo preview"
              className="h-8 w-auto object-contain"
              onError={(e) => {
                (e.currentTarget as HTMLImageElement).style.display = 'none';
              }}
            />
          </div>
        )}
      </div>

      {errorMessage && (
        <div role="alert" className="rounded-md border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">
          {errorMessage}
        </div>
      )}

      <div className="flex justify-end pt-2">
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
