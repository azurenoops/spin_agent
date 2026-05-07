import { useEffect, useState } from 'react';
import {
  onboarding,
  type BranchAffiliation,
  type ClassificationPosture,
  type OrganizationContextDto,
} from '../api/onboardingApi';

/**
 * Step 1 — Organization & Branch Context (FR-010..FR-014).
 *
 * Foundational form covering:
 *  - Organization name (required, ≤ 256 chars).
 *  - Branch dropdown with conditional qualifier when `IndustryPartnerOther`.
 *  - Optional sub-organization, classification posture, authoritative-repo URL,
 *    primary POC email.
 *
 * On save, calls {@link onboarding.upsertOrganizationContext} and surfaces the
 * Constitution-VII envelope error (errorCode + message + suggestion) inline.
 */
export interface Step1OrganizationContextProps {
  /** Invoked after a successful save so the parent can advance to Step 2. */
  onSaved?: (context: OrganizationContextDto) => void;
}

const BRANCHES: { value: BranchAffiliation; label: string }[] = [
  { value: 'Army', label: 'Army' },
  { value: 'Navy', label: 'Navy' },
  { value: 'AirForce', label: 'Air Force' },
  { value: 'MarineCorps', label: 'Marine Corps' },
  { value: 'SpaceForce', label: 'Space Force' },
  { value: 'CoastGuard', label: 'Coast Guard' },
  { value: 'CivilAgency', label: 'Civil Agency' },
  { value: 'IndustryPartnerOther', label: 'Industry Partner / Other' },
];

const POSTURES: { value: ClassificationPosture; label: string }[] = [
  { value: 'Unclassified', label: 'Unclassified' },
  { value: 'CUI', label: 'CUI' },
  { value: 'Secret', label: 'Secret' },
  { value: 'TopSecret', label: 'Top Secret' },
];

export default function Step1OrganizationContext({ onSaved }: Step1OrganizationContextProps) {
  const [organizationName, setOrganizationName] = useState('');
  const [branch, setBranch] = useState<BranchAffiliation>('CivilAgency');
  const [branchQualifier, setBranchQualifier] = useState('');
  const [subOrganization, setSubOrganization] = useState('');
  const [classificationPosture, setClassificationPosture] = useState<ClassificationPosture | ''>('');
  const [authoritativeRepositoryUrl, setAuthoritativeRepositoryUrl] = useState('');
  const [primaryPocEmail, setPrimaryPocEmail] = useState('');

  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<{ message: string; suggestion?: string } | null>(null);
  const [savedAt, setSavedAt] = useState<Date | null>(null);

  // Hydrate the form from any persisted Step 1 row.
  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const existing = await onboarding.getOrganizationContext();
        if (cancelled || !existing) return;
        setOrganizationName(existing.organizationName ?? '');
        setBranch(existing.branch ?? 'CivilAgency');
        setBranchQualifier(existing.branchQualifier ?? '');
        setSubOrganization(existing.subOrganization ?? '');
        setClassificationPosture(existing.classificationPosture ?? '');
        setAuthoritativeRepositoryUrl(existing.authoritativeRepositoryUrl ?? '');
        setPrimaryPocEmail(existing.primaryPocEmail ?? '');
      } catch (e) {
        if (!cancelled) {
          setError({
            message: (e as Error).message,
            suggestion: (e as { suggestion?: string }).suggestion,
          });
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  const requiresQualifier = branch === 'IndustryPartnerOther';
  const formValid =
    organizationName.trim().length > 0 &&
    organizationName.length <= 256 &&
    (!requiresQualifier || branchQualifier.trim().length > 0);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!formValid || saving) return;
    setSaving(true);
    setError(null);
    try {
      const payload: OrganizationContextDto = {
        organizationName: organizationName.trim(),
        branch,
        branchQualifier: requiresQualifier ? branchQualifier.trim() : null,
        subOrganization: subOrganization.trim() || null,
        classificationPosture: classificationPosture || null,
        authoritativeRepositoryUrl: authoritativeRepositoryUrl.trim() || null,
        primaryPocEmail: primaryPocEmail.trim() || null,
      };
      const saved = await onboarding.upsertOrganizationContext(payload);
      setSavedAt(new Date());
      onSaved?.(saved);
    } catch (err) {
      setError({
        message: (err as Error).message,
        suggestion: (err as { suggestion?: string }).suggestion,
      });
    } finally {
      setSaving(false);
    }
  };

  if (loading) {
    return <p role="status" className="text-sm text-slate-500">Loading organization context…</p>;
  }

  return (
    <form onSubmit={handleSubmit} className="space-y-6">
      <div className="rounded-lg border border-slate-200 bg-slate-50 p-4">
        <div className="flex items-start gap-3">
          <div className="flex h-9 w-9 flex-shrink-0 items-center justify-center rounded-md bg-indigo-100 text-indigo-700">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.8} className="h-5 w-5">
              <path strokeLinecap="round" strokeLinejoin="round" d="M13 16h-1v-4h-1m1-4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
            </svg>
          </div>
          <div className="text-sm text-slate-700">
            <div className="font-medium text-slate-900">Tell us about your organization</div>
            <p className="mt-0.5 text-slate-600">
              We use this to scope authorization boundaries, default classification, and stamp downstream documents (SSP, SAR, POA&amp;M).
            </p>
          </div>
        </div>
      </div>

      <fieldset className="space-y-4">
        <legend className="text-sm font-semibold text-slate-900">Identity</legend>

        <div>
          <label className="block text-sm font-medium text-slate-800" htmlFor="oc-org-name">
            Organization name <span className="text-rose-600">*</span>
          </label>
          <input
            id="oc-org-name"
            type="text"
            required
            maxLength={256}
            value={organizationName}
            onChange={(e) => setOrganizationName(e.target.value)}
            placeholder="e.g. Department of the Navy"
            className="mt-1.5 w-full rounded-md border border-slate-300 px-3 py-2 text-sm shadow-sm placeholder:text-slate-400 focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
          />
        </div>

        <div className="grid gap-4 sm:grid-cols-2">
          <div>
            <label className="block text-sm font-medium text-slate-800" htmlFor="oc-branch">
              Branch / service <span className="text-rose-600">*</span>
            </label>
            <select
              id="oc-branch"
              required
              value={branch}
              onChange={(e) => setBranch(e.target.value as BranchAffiliation)}
              className="mt-1.5 w-full rounded-md border border-slate-300 bg-white px-3 py-2 text-sm shadow-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
            >
              {BRANCHES.map((b) => (
                <option key={b.value} value={b.value}>
                  {b.label}
                </option>
              ))}
            </select>
          </div>

          <div>
            <label className="block text-sm font-medium text-slate-800" htmlFor="oc-sub-org">
              Sub-organization
            </label>
            <input
              id="oc-sub-org"
              type="text"
              maxLength={256}
              value={subOrganization}
              onChange={(e) => setSubOrganization(e.target.value)}
              placeholder="e.g. NAVWAR"
              className="mt-1.5 w-full rounded-md border border-slate-300 px-3 py-2 text-sm shadow-sm placeholder:text-slate-400 focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
            />
          </div>
        </div>

        {requiresQualifier && (
          <div>
            <label className="block text-sm font-medium text-slate-800" htmlFor="oc-branch-qualifier">
              Branch qualifier <span className="text-rose-600">*</span>
            </label>
            <input
              id="oc-branch-qualifier"
              type="text"
              required
              maxLength={128}
              value={branchQualifier}
              onChange={(e) => setBranchQualifier(e.target.value)}
              placeholder="Specify the partner / agency"
              className="mt-1.5 w-full rounded-md border border-slate-300 px-3 py-2 text-sm shadow-sm placeholder:text-slate-400 focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
            />
            <p className="mt-1 text-xs text-slate-500">
              Required when the branch is &quot;Industry Partner / Other.&quot;
            </p>
          </div>
        )}
      </fieldset>

      <fieldset className="space-y-4 border-t border-slate-100 pt-6">
        <legend className="text-sm font-semibold text-slate-900">Posture &amp; sources</legend>

        <div>
          <label className="block text-sm font-medium text-slate-800" htmlFor="oc-posture">
            Default classification posture
          </label>
          <select
            id="oc-posture"
            value={classificationPosture}
            onChange={(e) =>
              setClassificationPosture((e.target.value as ClassificationPosture) || '')
            }
            className="mt-1.5 w-full rounded-md border border-slate-300 bg-white px-3 py-2 text-sm shadow-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
          >
            <option value="">— Not specified —</option>
            {POSTURES.map((p) => (
              <option key={p.value} value={p.value}>
                {p.label}
              </option>
            ))}
          </select>
          <p className="mt-1 text-xs text-slate-500">
            Used as the default for new system registrations. Each system can override this.
          </p>
        </div>

        <div>
          <label className="block text-sm font-medium text-slate-800" htmlFor="oc-repo-url">
            Authoritative repository URL
          </label>
          <input
            id="oc-repo-url"
            type="url"
            value={authoritativeRepositoryUrl}
            onChange={(e) => setAuthoritativeRepositoryUrl(e.target.value)}
            className="mt-1.5 w-full rounded-md border border-slate-300 px-3 py-2 text-sm shadow-sm placeholder:text-slate-400 focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
            placeholder="https://emass.disa.mil/system/123"
          />
        </div>

        <div>
          <label className="block text-sm font-medium text-slate-800" htmlFor="oc-poc-email">
            Primary POC email
          </label>
          <input
            id="oc-poc-email"
            type="email"
            value={primaryPocEmail}
            onChange={(e) => setPrimaryPocEmail(e.target.value)}
            placeholder="issm@example.mil"
            className="mt-1.5 w-full rounded-md border border-slate-300 px-3 py-2 text-sm shadow-sm placeholder:text-slate-400 focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
          />
        </div>
      </fieldset>

      {error && (
        <div role="alert" className="rounded-md border border-rose-200 bg-rose-50 p-3 text-sm">
          <p className="font-medium text-rose-800">{error.message}</p>
          {error.suggestion && (
            <p className="mt-1 text-rose-700">{error.suggestion}</p>
          )}
        </div>
      )}

      {savedAt && !error && (
        <p role="status" className="flex items-center gap-2 text-sm text-emerald-700">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2.4} className="h-4 w-4">
            <path strokeLinecap="round" strokeLinejoin="round" d="M5 12l5 5L20 7" />
          </svg>
          Saved at {savedAt.toLocaleTimeString()}.
        </p>
      )}

      <div className="flex items-center justify-end gap-3 border-t border-slate-100 pt-5">
        <button
          type="submit"
          disabled={!formValid || saving}
          className="inline-flex items-center gap-2 rounded-md bg-indigo-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-indigo-700 disabled:cursor-not-allowed disabled:bg-slate-300"
        >
          {saving ? (
            <>
              <svg className="h-4 w-4 animate-spin" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2}>
                <circle cx="12" cy="12" r="10" className="opacity-25" />
                <path d="M4 12a8 8 0 018-8" className="opacity-75" strokeLinecap="round" />
              </svg>
              Saving…
            </>
          ) : (
            <>
              Save and continue
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2} className="h-4 w-4">
                <path strokeLinecap="round" strokeLinejoin="round" d="M5 12h14m0 0l-6-6m6 6l-6 6" />
              </svg>
            </>
          )}
        </button>
      </div>
    </form>
  );
}
