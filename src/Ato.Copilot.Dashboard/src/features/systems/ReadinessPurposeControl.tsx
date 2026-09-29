import { useState } from 'react';
import type { PackagePurpose } from '../../api/package';
import PackagePurposeSelect from '../../components/PackagePurposeSelect';
import SetupDialog from '../workspace-operations/SetupDialog';
import { packagePurposeLabels } from './packageReadinessNavigation';
import { systemPrimaryAction, systemSecondaryAction } from './SystemTaskPresentation';

export default function ReadinessPurposeControl({ purpose, disabled, onChange }: {
  purpose: PackagePurpose; disabled: boolean; onChange: (purpose: PackagePurpose) => void;
}) {
  const [pending, setPending] = useState<PackagePurpose | null>(null);
  return <>
    <section aria-label="Selected package purpose" className="rounded-lg border border-slate-200 bg-white p-3 text-xs dark:border-slate-700 dark:bg-slate-900">
      <div className="flex flex-wrap items-center gap-2">
        <span className="font-semibold">Package purpose:</span>
        <span>{packagePurposeLabels[purpose]}</span>
        <button type="button" disabled={disabled} aria-label="Change package purpose" onClick={() => setPending(purpose)}
          className="text-indigo-700 underline disabled:opacity-50 dark:text-indigo-300">Change</button>
      </div>
      <p className="mt-2 text-slate-500">Validation follows this selection. Changing purpose requires a separate check.</p>
    </section>
    {pending && <SetupDialog title="Change package purpose" busy={disabled} onClose={() => setPending(null)}
      description="Choose the actual package workflow. Existing validation and export history is retained under its original purpose.">
      <PackagePurposeSelect value={pending} disabled={disabled} onChange={setPending} />
      <div className="mt-4 flex flex-wrap justify-end gap-3">
        <button type="button" disabled={disabled} onClick={() => setPending(null)} className={systemSecondaryAction}>Cancel</button>
        <button type="button" disabled={disabled} onClick={() => { onChange(pending); setPending(null); }}
          className={systemPrimaryAction}>Use selected purpose</button>
      </div>
    </SetupDialog>}
  </>;
}
