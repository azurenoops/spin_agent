import { useEffect, useState } from 'react';
import {
  type OrgControlImplementationStatus,
  type OrgControlInheritanceApplicability,
  type OrgControlOverrideDto,
  deleteOrgControlOverride,
  getOrgControlOverride,
  upsertOrgControlOverride,
} from '../../api/orgControlOverrides';

const STATUS_OPTIONS: { value: OrgControlImplementationStatus; label: string }[] = [
  { value: 'Implemented', label: 'Implemented' },
  { value: 'PartiallyImplemented', label: 'Partially Implemented' },
  { value: 'Planned', label: 'Planned' },
  { value: 'NotApplicable', label: 'Not Applicable' },
];

const APPLICABILITY_OPTIONS: { value: OrgControlInheritanceApplicability; label: string }[] = [
  { value: 'FullyInherited', label: 'Fully Inherited from CSP' },
  { value: 'Hybrid', label: 'Hybrid (CSP + Org)' },
  { value: 'NotApplicableToThisSystem', label: 'Not Applicable to This System' },
];

interface OrgControlOverridePanelProps {
  controlId: string;
  controlTitle?: string;
  onClose: () => void;
  onSaved: (override: OrgControlOverrideDto | null) => void;
}

/**
 * Slide-over panel for editing the per-org override of a single NIST
 * control (Feature 048 follow-up — user ask #2). Choosing both selects
 * back to "(use CSP default)" + Save behaves as a delete: the backend
 * removes the row and `onSaved` is invoked with `null`.
 */
export default function OrgControlOverridePanel({
  controlId,
  controlTitle,
  onClose,
  onSaved,
}: OrgControlOverridePanelProps) {
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [status, setStatus] = useState<OrgControlImplementationStatus | ''>('');
  const [applicability, setApplicability] = useState<OrgControlInheritanceApplicability | ''>('');
  const [justification, setJustification] = useState('');
  const [originalRow, setOriginalRow] = useState<OrgControlOverrideDto | null>(null);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setError(null);
    getOrgControlOverride(controlId)
      .then((row) => {
        if (cancelled) return;
        setOriginalRow(row);
        setStatus(row?.implementationStatus ?? '');
        setApplicability(row?.inheritanceApplicability ?? '');
        setJustification(row?.justification ?? '');
      })
      .catch((err: unknown) => {
        if (cancelled) return;
        setError(err instanceof Error ? err.message : 'Failed to load override.');
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [controlId]);

  const hasOverride = status !== '' || applicability !== '';
  const justificationMissing = hasOverride && justification.trim().length === 0;

  async function handleSave() {
    if (justificationMissing) {
      setError('Justification is required when an override is set.');
      return;
    }
    setSaving(true);
    setError(null);
    try {
      const saved = await upsertOrgControlOverride(controlId, {
        implementationStatus: status === '' ? null : status,
        inheritanceApplicability: applicability === '' ? null : applicability,
        justification: hasOverride ? justification.trim() : null,
      });
      onSaved(saved);
      onClose();
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Save failed.');
    } finally {
      setSaving(false);
    }
  }

  async function handleDelete() {
    if (!originalRow) {
      // Local-only reset: nothing to delete server-side.
      setStatus('');
      setApplicability('');
      setJustification('');
      return;
    }
    setSaving(true);
    setError(null);
    try {
      await deleteOrgControlOverride(controlId);
      onSaved(null);
      onClose();
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Delete failed.');
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex justify-end" onClick={onClose}>
      <div className="absolute inset-0 bg-black/30" />
      <div
        className="relative w-full max-w-md bg-white shadow-xl overflow-y-auto"
        onClick={(e) => e.stopPropagation()}
      >
        {/* Header */}
        <div className="sticky top-0 z-10 flex items-center justify-between border-b bg-white px-6 py-4">
          <div>
            <h2 className="text-lg font-bold text-gray-900">{controlId}</h2>
            <p className="text-sm text-gray-500">Org-level control override</p>
          </div>
          <button
            onClick={onClose}
            className="rounded p-1 text-gray-400 hover:bg-gray-100 hover:text-gray-600"
            aria-label="Close override panel"
          >
            <svg className="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" />
            </svg>
          </button>
        </div>

        {/* Body */}
        <div className="space-y-5 px-6 py-5">
          {controlTitle && (
            <div className="rounded-md bg-gray-50 border border-gray-200 px-3 py-2 text-sm text-gray-700">
              {controlTitle}
            </div>
          )}

          {loading ? (
            <div className="py-12 text-center text-sm text-gray-400">Loading override...</div>
          ) : (
            <>
              <div className="rounded-md border border-amber-200 bg-amber-50 px-3 py-2 text-xs text-amber-700">
                Overrides apply to this org only. CSP defaults remain visible to other orgs and to
                CSP administrators. Leave both selects on <em>“(use CSP default)”</em> and click
                <strong> Save</strong> to clear the override.
              </div>

              <label className="block">
                <span className="text-sm font-medium text-gray-700">Implementation status</span>
                <select
                  value={status}
                  onChange={(e) => setStatus(e.target.value as OrgControlImplementationStatus | '')}
                  className="mt-1 w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
                  disabled={saving}
                >
                  <option value="">(use CSP default)</option>
                  {STATUS_OPTIONS.map((o) => (
                    <option key={o.value} value={o.value}>
                      {o.label}
                    </option>
                  ))}
                </select>
              </label>

              <label className="block">
                <span className="text-sm font-medium text-gray-700">Inheritance applicability</span>
                <select
                  value={applicability}
                  onChange={(e) =>
                    setApplicability(e.target.value as OrgControlInheritanceApplicability | '')
                  }
                  className="mt-1 w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
                  disabled={saving}
                >
                  <option value="">(use CSP default)</option>
                  {APPLICABILITY_OPTIONS.map((o) => (
                    <option key={o.value} value={o.value}>
                      {o.label}
                    </option>
                  ))}
                </select>
              </label>

              <label className="block">
                <span className="text-sm font-medium text-gray-700">
                  Justification {hasOverride && <span className="text-red-600">*</span>}
                </span>
                <textarea
                  rows={5}
                  value={justification}
                  onChange={(e) => setJustification(e.target.value)}
                  placeholder={
                    hasOverride
                      ? 'Required: why this org diverges from the CSP default.'
                      : 'Optional unless an override is set.'
                  }
                  className={`mt-1 w-full rounded-md border px-3 py-2 text-sm focus:outline-none focus:ring-1 ${
                    justificationMissing
                      ? 'border-red-300 focus:border-red-500 focus:ring-red-500'
                      : 'border-gray-300 focus:border-indigo-500 focus:ring-indigo-500'
                  }`}
                  disabled={saving}
                />
              </label>

              {originalRow && (
                <p className="text-xs text-gray-400">
                  Last updated {new Date(originalRow.updatedAt).toLocaleString()} by{' '}
                  {originalRow.updatedBy}
                </p>
              )}

              {error && (
                <div className="rounded-md border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">
                  {error}
                </div>
              )}
            </>
          )}
        </div>

        {/* Footer */}
        <div className="sticky bottom-0 z-10 flex items-center justify-between border-t bg-white px-6 py-3">
          <button
            onClick={handleDelete}
            disabled={saving || loading || (!originalRow && !hasOverride)}
            className="text-sm font-medium text-red-600 hover:text-red-700 disabled:text-gray-300"
          >
            {originalRow ? 'Remove override' : 'Reset'}
          </button>
          <div className="flex gap-2">
            <button
              onClick={onClose}
              disabled={saving}
              className="rounded-md border border-gray-300 px-4 py-1.5 text-sm font-medium text-gray-700 hover:bg-gray-50"
            >
              Cancel
            </button>
            <button
              onClick={handleSave}
              disabled={saving || loading || justificationMissing}
              className="rounded-md bg-indigo-600 px-4 py-1.5 text-sm font-medium text-white hover:bg-indigo-700 disabled:opacity-50"
            >
              {saving ? 'Saving...' : 'Save'}
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}
