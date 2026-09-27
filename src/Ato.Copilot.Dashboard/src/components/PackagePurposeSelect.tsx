import type { PackagePurpose } from '../api/package';

export default function PackagePurposeSelect({ value, onChange, disabled = false }: {
  value: PackagePurpose;
  onChange: (purpose: PackagePurpose) => void;
  disabled?: boolean;
}) {
  return <div className="my-4">
    <label className="block text-sm font-medium text-slate-700 dark:text-slate-200">Package purpose
      <select value={value} disabled={disabled}
        onChange={event => {
          if (event.target.value === 'Legacy' || event.target.value === 'InitialSubmission'
            || event.target.value === 'AuthorizedBaselineArchive' || event.target.value === 'ChangeSubmission') onChange(event.target.value);
        }}
        className="mt-2 block w-full rounded-md border border-slate-300 bg-white px-3 py-2 text-sm text-slate-900 disabled:opacity-50 dark:border-slate-600 dark:bg-slate-900 dark:text-slate-100">
        <option value="Legacy">Existing authorization package (legacy validation)</option>
        <option value="InitialSubmission">Initial ATO submission</option>
        <option value="AuthorizedBaselineArchive">Authorized baseline archive</option>
        <option value="ChangeSubmission">Change submission — retained baseline and SSP change</option>
      </select>
    </label>
    <p className="mt-2 text-xs text-slate-500">
      {value === 'InitialSubmission'
        ? 'No prior authorization decision is required. Reviewed documents and evidence are still required; generation does not grant authorization.'
        : value === 'AuthorizedBaselineArchive' ? 'Archive the selected retained package and recorded decision without regenerating mutable source content.'
          : value === 'ChangeSubmission' ? 'Retain the selected baseline, decision, and reviewed SSP change. This does not generate a new AO decision or claim a complete reassessment.'
            : 'Retains the existing authorization-decision requirement. Select Initial ATO submission explicitly when preparing the first submission.'}
    </p>
  </div>;
}
