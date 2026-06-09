import { useMemo } from 'react';
import { useMsal } from '@azure/msal-react';
import { useSearchParams } from 'react-router-dom';
import { useLoginConfig } from './LoginConfigContext';
import { errorCopy } from './errorCopy';
import { DEFAULT_API_SCOPES } from './msalInstance';
import type { ErrorClass } from './types';

/**
 * Feature 051 T083 [US4] — `/login/error` route. Renders class-specific
 * copy from `errorCopy.ts` plus the correlation id (so the user can
 * cite it when filing a ticket) and a support email link drawn from
 * `LoginConfig.branding.supportEmail`.
 *
 * For `ConditionalAccessBlock` errors, FR-015 mandates surfacing the
 * Entra-provided remediation URL when present — we accept it as a
 * `?remediationUrl=` query param and render it as the primary CTA.
 *
 * Fix #363 (CRIT/SECURITY): `remediationUrl` is restricted to `https://`
 * only. This route is public (no `RequireAuth`), so an unvalidated `href`
 * allows `javascript:` XSS via a crafted link sent to any user.
 */
export default function LoginErrorPage() {
  const [searchParams] = useSearchParams();
  const { instance } = useMsal();
  const login = useLoginConfig();

  const rawClass = searchParams.get('errorClass');
  const correlationId = searchParams.get('correlationId') ?? '';
  // Security fix #363: only allow https:// remediationUrls.
  // javascript:, data:, http:, and any other scheme are silently discarded.
  const rawRemediationUrl = searchParams.get('remediationUrl');
  const remediationUrl = rawRemediationUrl?.startsWith('https://') ? rawRemediationUrl : null;

  const recognized = useMemo<ErrorClass | null>(() => {
    if (rawClass && rawClass in errorCopy) {
      return rawClass as ErrorClass;
    }
    return null;
  }, [rawClass]);

  const copy = recognized
    ? errorCopy[recognized]
    : {
        title: 'Sign-in failed',
        body: 'We could not complete sign-in.',
        suggestion: 'Try again, or contact your administrator if the issue persists.',
      };

  const handleTryAgain = () => {
    void instance.loginRedirect({
      scopes: DEFAULT_API_SCOPES,
      state: '/',
    });
  };

  const supportEmail = login.branding.supportEmail;

  return (
    <div className="min-h-screen flex items-center justify-center bg-gray-50 px-4">
      <div className="max-w-md w-full bg-white shadow-lg rounded-lg p-8">
        <div className="flex items-center justify-center w-12 h-12 mx-auto rounded-full bg-red-100 mb-4">
          <svg
            className="w-6 h-6 text-red-600"
            xmlns="http://www.w3.org/2000/svg"
            fill="none"
            viewBox="0 0 24 24"
            stroke="currentColor"
            aria-hidden="true"
          >
            <path
              strokeLinecap="round"
              strokeLinejoin="round"
              strokeWidth={2}
              d="M12 9v2m0 4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z"
            />
          </svg>
        </div>

        <h1 className="text-xl font-semibold text-gray-900 text-center mb-3">
          {copy.title}
        </h1>

        <p className="text-sm text-gray-700 text-center mb-2">{copy.body}</p>
        <p className="text-sm text-gray-600 text-center mb-6">{copy.suggestion}</p>

        {/* Conditional Access remediation URL (FR-015) */}
        {recognized === 'ConditionalAccessBlock' && remediationUrl && (
          <div className="mb-6 text-center">
            <a
              href={remediationUrl}
              className="inline-block w-full px-4 py-3 rounded-md bg-amber-600 text-white font-medium text-center hover:bg-amber-700 focus:outline-none focus:ring-2 focus:ring-amber-500 focus:ring-offset-2"
              rel="noopener noreferrer"
            >
              Open remediation page to resolve
            </a>
          </div>
        )}

        <div className="space-y-3">
          <button
            type="button"
            onClick={handleTryAgain}
            className="w-full px-4 py-3 rounded-md bg-blue-600 text-white font-medium hover:bg-blue-700 focus:outline-none focus:ring-2 focus:ring-blue-500 focus:ring-offset-2"
          >
            Try again
          </button>
        </div>

        {/* Correlation id + support contact (FR-016) */}
        <div className="mt-8 pt-6 border-t border-gray-200 text-center space-y-2">
          {correlationId && (
            <p className="text-xs text-gray-500">
              Correlation ID:{' '}
              <code className="font-mono text-gray-700">{correlationId}</code>
            </p>
          )}
          {supportEmail ? (
            <p className="text-xs text-gray-500">
              Need help? Contact{' '}
              <a
                href={`mailto:${supportEmail}?subject=${encodeURIComponent(
                  `ATO Copilot sign-in error (${rawClass ?? 'unknown'})`,
                )}&body=${encodeURIComponent(
                  `Correlation ID: ${correlationId}\nError class: ${rawClass ?? 'unknown'}`,
                )}`}
                className="text-blue-600 hover:underline"
              >
                {supportEmail}
              </a>
            </p>
          ) : (
            <p className="text-xs text-gray-500">
              Need help? Contact your administrator or support team.
            </p>
          )}
        </div>
      </div>
    </div>
  );
}
