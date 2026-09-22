import { useState, type FormEvent } from 'react';
import { addValidationLink, type ControlValidationLinkType } from '../api/complianceApi';
import { useSystemMutationPermission } from '../../../components/permissions/useSystemMutationPermission';

interface Props {
  systemId: string;
  controlId: string;
  onClose: () => void;
  onAdded: () => void;
}

const LINK_TYPES: Array<{ value: ControlValidationLinkType; label: string }> = [
  { value: 'AzureResource', label: 'Azure Resource' },
  { value: 'ScanFinding', label: 'Scan Finding' },
  { value: 'EvidenceArtifact', label: 'Evidence Artifact' },
  { value: 'ExternalUrl', label: 'External URL' },
];

export default function AddValidationLinkModal({ systemId, controlId, onClose, onAdded }: Props) {
  const canManage = useSystemMutationPermission(systemId, null);
  const [linkType, setLinkType] = useState<ControlValidationLinkType>('AzureResource');
  const [linkTarget, setLinkTarget] = useState('');
  const [description, setDescription] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState('');

  const handleSubmit = async (event: FormEvent) => {
    event.preventDefault();
    if (!canManage) { setError('Validation-link management permission is not available for this workspace.'); return; }
    if (!linkTarget.trim()) return;
    setSubmitting(true);
    setError('');
    try {
      await addValidationLink(systemId, controlId, {
        linkType,
        linkTarget: linkTarget.trim(),
        description: description.trim() || undefined,
      });
      onAdded();
    } catch {
      setError('Unable to add the validation link.');
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 px-4" role="presentation">
      <form
        onSubmit={handleSubmit}
        className="w-full max-w-lg rounded-lg bg-white shadow-xl"
        role="dialog"
        aria-modal="true"
        aria-labelledby="add-validation-link-title"
      >
        <div className="border-b border-gray-200 px-6 py-4">
          <h3 id="add-validation-link-title" className="text-base font-semibold text-gray-900">Add validation link</h3>
        </div>
        <div className="space-y-4 px-6 py-5">
          <label className="block text-sm font-medium text-gray-700">
            Type
            <select
              value={linkType}
              onChange={(event) => setLinkType(event.target.value as ControlValidationLinkType)}
              className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2 text-sm"
            >
              {LINK_TYPES.map((type) => <option key={type.value} value={type.value}>{type.label}</option>)}
            </select>
          </label>
          <label className="block text-sm font-medium text-gray-700">
            Target
            <input
              required
              value={linkTarget}
              onChange={(event) => setLinkTarget(event.target.value)}
              className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2 text-sm"
            />
          </label>
          {linkType === 'AzureResource' && (
            <p className="text-xs text-gray-500">Enter Azure resource ID (e.g., /subscriptions/.../resourceGroups/...)</p>
          )}
          <label className="block text-sm font-medium text-gray-700">
            Description
            <textarea
              rows={3}
              value={description}
              onChange={(event) => setDescription(event.target.value)}
              className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2 text-sm"
            />
          </label>
          {error && <p className="text-sm text-red-600" role="alert">{error}</p>}
        </div>
        <div className="flex justify-end gap-2 border-t border-gray-200 px-6 py-4">
          <button type="button" onClick={onClose} className="rounded-md border border-gray-300 px-4 py-2 text-sm text-gray-700">Cancel</button>
          <button type="submit" disabled={!canManage || submitting || !linkTarget.trim()} className="rounded-md bg-indigo-600 px-4 py-2 text-sm font-medium text-white disabled:opacity-50">
            {submitting ? 'Adding...' : 'Add link'}
          </button>
        </div>
      </form>
    </div>
  );
}