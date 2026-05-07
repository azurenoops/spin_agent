import type { ReactNode } from 'react';
import { useOrganizationContext } from '../../hooks/useOrganizationContext';

interface PageHeroProps {
  /** Section eyebrow above the title (e.g. "Portfolio"). */
  eyebrow?: string;
  title: string;
  description?: string;
  /** Right-side actions (buttons, filters, etc.). */
  actions?: ReactNode;
  /**
   * When true, renders the org/sub-org name as a chip next to the title.
   * Defaults to true so portal pages get tenant branding for free.
   */
  showOrgName?: boolean;
}

/**
 * `PageHero` — site-wide page header treatment that mirrors the Onboarding
 * Wizard's gradient look (indigo → blue → sky) and surfaces the active
 * organization (sub-org takes precedence over org name).
 *
 * Use at the top of every top-level page so the portal feels visually unified.
 */
export default function PageHero({
  eyebrow,
  title,
  description,
  actions,
  showOrgName = true,
}: PageHeroProps) {
  const { displayName } = useOrganizationContext();

  return (
    <div className="relative -mx-6 -mt-6 mb-6 overflow-hidden bg-gradient-to-r from-indigo-700 via-indigo-700 to-sky-600 text-white shadow-sm">
      <div
        className="pointer-events-none absolute inset-0 opacity-25"
        style={{
          backgroundImage:
            'radial-gradient(circle at 20% 30%, rgba(255,255,255,.4) 0, transparent 40%), radial-gradient(circle at 80% 70%, rgba(255,255,255,.25) 0, transparent 45%)',
        }}
        aria-hidden
      />
      <div className="relative flex flex-col gap-3 px-6 py-5 sm:flex-row sm:items-end sm:justify-between">
        <div className="min-w-0">
          {eyebrow && (
            <p className="text-xs font-semibold uppercase tracking-wide text-white/75">
              {eyebrow}
            </p>
          )}
          <div className="flex flex-wrap items-center gap-3">
            <h1 className="text-2xl font-semibold tracking-tight text-white">
              {title}
            </h1>
            {showOrgName && displayName && (
              <span
                className="inline-flex items-center gap-1.5 rounded-full bg-white/15 px-3 py-1 text-sm font-medium text-white ring-1 ring-white/30 backdrop-blur"
                title="Active organization"
              >
                <svg
                  viewBox="0 0 24 24"
                  fill="none"
                  stroke="currentColor"
                  strokeWidth={1.8}
                  className="h-4 w-4"
                  aria-hidden
                >
                  <path
                    strokeLinecap="round"
                    strokeLinejoin="round"
                    d="M3 21h18M5 21V7l7-4 7 4v14M9 21V11h6v10"
                  />
                </svg>
                {displayName}
              </span>
            )}
          </div>
          {description && (
            <p className="mt-1 max-w-3xl text-sm text-white/80">{description}</p>
          )}
        </div>
        {actions && <div className="flex flex-wrap items-center gap-2">{actions}</div>}
      </div>
    </div>
  );
}
