import { useState } from 'react';
import { batchApproveProfile, reviewSection } from '../../api/systemProfile';
import type { ProfileSectionType } from '../../types/dashboard';
import { StatusBadge } from './StatusBadge';

// ─── Types ───────────────────────────────────────────────────────────────────

interface SectionEntry {
  sectionType: string;
  status: string;
  label: string;
}

interface BatchApprovePanelProps {
  systemId: string;
  sections: SectionEntry[];
  callerRole: string | null;
  onApproved: () => void;
}

const REVIEWABLE_STATUSES = new Set(['UnderReview', 'Submitted']);

// ─── Component ───────────────────────────────────────────────────────────────

/**
 * ISSM / SystemOwner only — renders a panel to approve sections that are
 * currently in "Submitted" or "UnderReview" status.
 *
 * IMPORTANT: Mission Owners must NEVER see this component. The guard is both
 * here (early return) and at the call site in SystemProfile.tsx.
 */
export default function BatchApprovePanel({
  systemId,
  sections,
  callerRole,
  onApproved,
}: BatchApprovePanelProps) {
  // ── Role gate ──────────────────────────────────────────────────────────────
  if (callerRole !== 'ISSM' && callerRole !== 'SystemOwner') {
    return null;
  }

  const reviewable = sections.filter((s) => REVIEWABLE_STATUSES.has(s.status));

  if (reviewable.length === 0) {
    return null;
  }

  return <BatchApprovePanelInner
    systemId={systemId}
    reviewable={reviewable}
    onApproved={onApproved}
  />;
}

// Inner component so hooks run unconditionally after the guard ─────────────────

interface InnerProps {
  systemId: string;
  reviewable: SectionEntry[];
  onApproved: () => void;
}

function BatchApprovePanelInner({ systemId, reviewable, onApproved }: InnerProps) {
  const [checked, setChecked] = useState<Set<string>>(new Set());
  const [busy, setBusy] = useState(false);
  const [perBusy, setPerBusy] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [successMsg, setSuccessMsg] = useState<string | null>(null);

  const toggle = (sectionType: string) => {
    setChecked((prev) => {
      const next = new Set(prev);
      if (next.has(sectionType)) next.delete(sectionType);
      else next.add(sectionType);
      return next;
    });
  };

  const toggleAll = () => {
    if (checked.size === reviewable.length) {
      setChecked(new Set());
    } else {
      setChecked(new Set(reviewable.map((s) => s.sectionType)));
    }
  };

  const handleBatchApprove = async () => {
    if (checked.size === 0) return;
    setBusy(true);
    setError(null);
    setSuccessMsg(null);
    try {
      const result = await batchApproveProfile(systemId);
      setSuccessMsg(`Approved ${result.approvedCount} section(s).`);
      setChecked(new Set());
      onApproved();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Batch approve failed');
    } finally {
      setBusy(false);
    }
  };

  const handlePerSection = async (sectionType: string) => {
    setPerBusy(sectionType);
    setError(null);
    setSuccessMsg(null);
    try {
      await reviewSection(systemId, sectionType as ProfileSectionType, { decision: 'approve' });
      setSuccessMsg(`"${sectionType}" approved.`);
      onApproved();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Approve failed');
    } finally {
      setPerBusy(null);
    }
  };

  return (
    <div className="rounded-xl border border-gray-200 bg-white p-5 space-y-4">
      {/* Header */}
      <div className="flex items-center justify-between">
        <h2 className="text-base font-semibold text-gray-900">Approve Sections</h2>
        <span className="rounded-full bg-indigo-100 px-2.5 py-0.5 text-xs font-medium text-indigo-700">
          ISSM Review
        </span>
      </div>

      <p className="text-sm text-gray-500">
        The following sections are awaiting review. Select sections and use{' '}
        <strong>Approve All Selected</strong> for batch approval, or approve
        individual sections inline.
      </p>

      {/* Feedback */}
      {successMsg && (
        <div className="rounded-lg border border-green-200 bg-green-50 px-3 py-2 text-sm text-green-700">
          {successMsg}
        </div>
      )}
      {error && (
        <div className="rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">
          {error}
        </div>
      )}

      {/* Section list */}
      <div className="divide-y divide-gray-100 border border-gray-100 rounded-lg overflow-hidden">
        {/* Select-all row */}
        <div className="flex items-center gap-3 px-4 py-2.5 bg-gray-50">
          <input
            type="checkbox"
            id="batch-select-all"
            className="h-4 w-4 rounded border-gray-300 text-indigo-600"
            checked={checked.size === reviewable.length && reviewable.length > 0}
            onChange={toggleAll}
            aria-label="Select all sections"
          />
          <label htmlFor="batch-select-all" className="text-xs font-semibold text-gray-500 uppercase tracking-wide cursor-pointer">
            Select all
          </label>
        </div>

        {reviewable.map((s) => {
          const isChecked = checked.has(s.sectionType);
          const isThisBusy = perBusy === s.sectionType;
          return (
            <div key={s.sectionType} className="flex items-center gap-3 px-4 py-3">
              <input
                type="checkbox"
                id={`batch-${s.sectionType}`}
                className="h-4 w-4 rounded border-gray-300 text-indigo-600"
                checked={isChecked}
                onChange={() => toggle(s.sectionType)}
              />
              <label
                htmlFor={`batch-${s.sectionType}`}
                className="flex-1 text-sm text-gray-800 cursor-pointer"
              >
                {s.label}
              </label>

              {/* Status badge */}
              <StatusBadge status={s.status as import('../../types/dashboard').GovernanceStatus} />

              {/* Per-section approve button */}
              <button
                type="button"
                onClick={() => handlePerSection(s.sectionType)}
                disabled={isThisBusy || busy}
                className="text-xs text-indigo-600 hover:text-indigo-800 disabled:opacity-50 disabled:cursor-not-allowed font-medium whitespace-nowrap"
              >
                {isThisBusy ? 'Approving…' : 'Approve'}
              </button>
            </div>
          );
        })}
      </div>

      {/* Batch action */}
      <div className="flex justify-end">
        <button
          type="button"
          onClick={handleBatchApprove}
          disabled={checked.size === 0 || busy}
          className="inline-flex items-center gap-2 rounded-lg bg-green-600 px-4 py-2 text-sm font-semibold text-white hover:bg-green-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
        >
          {busy ? (
            <>
              <svg className="h-4 w-4 animate-spin" fill="none" viewBox="0 0 24 24">
                <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
              </svg>
              Approving…
            </>
          ) : (
            `Approve All Selected (${checked.size})`
          )}
        </button>
      </div>
    </div>
  );
}
