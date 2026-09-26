import { useState } from 'react';
import { PackageReceipts } from '../../package-imports/PackageReceipts';
import { OfferingIntake } from '../../provider-authorizations/OfferingIntake';
import { buttonClass, errorClass, secondaryButtonClass } from '../../workspace-operations/workspaceUi';

interface AtoDocumentsStepProps {
  saving: boolean;
  errorMessage: string | null;
  onContinue: () => void;
  onBack: () => void;
  onPendingChange?: (pending: boolean) => void;
}

export default function AtoDocumentsStep({ saving, errorMessage, onContinue, onBack, onPendingChange }: AtoDocumentsStepProps) {
  const [pending, setPending] = useState(false);

  return <div className="space-y-5" aria-labelledby="ato-documents-step-heading">
    <div>
      <h2 id="ato-documents-step-heading" className="text-lg font-semibold text-gray-900">
        Import an existing authorization package <span className="ml-2 rounded-full bg-indigo-50 px-2 py-1 text-xs text-indigo-700">Optional</span>
      </h2>
      <p className="mt-2 text-sm text-gray-600">Upload an archive or supported source documents as one package.
        After the server confirms receipt, continue setup while analysis runs in the background.</p>
      <p className="mt-2 text-sm text-gray-600">Review components, capabilities, control mappings and responsibilities later
        in Authorizations. Uploading or completing onboarding never approves or publishes records.</p>
    </div>
    <fieldset disabled={saving}><OfferingIntake onPendingChange={value => { setPending(value); onPendingChange?.(value); }} /></fieldset>
    <PackageReceipts />
    {errorMessage && <p role="alert" className={errorClass}>{errorMessage}</p>}
    {pending && <p className="text-sm text-gray-600">Upload the selected files or remove them before continuing. An uncertain upload must be retried with the same files and key.</p>}
    <div className="flex flex-wrap justify-between gap-3 pt-2">
      <button type="button" onClick={onBack} disabled={saving || pending} className={secondaryButtonClass}>Back</button>
      <button type="button" onClick={onContinue} disabled={saving || pending} className={buttonClass}>Continue</button>
    </div>
  </div>;
}
