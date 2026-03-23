import { useState, useEffect } from 'react';
import type { CspProfile, ApplyProfilePreview } from '../../types/inheritance';

interface CspProfileDialogProps {
  open: boolean;
  profiles: CspProfile[];
  loading: boolean;
  onPreview: (profileId: string, conflictResolution: string) => Promise<ApplyProfilePreview | null>;
  onApply: (profileId: string, conflictResolution: string) => void;
  onClose: () => void;
}

export default function CspProfileDialog({
  open, profiles, loading, onPreview, onApply, onClose,
}: CspProfileDialogProps) {
  const [selectedId, setSelectedId] = useState('');
  const [conflict, setConflict] = useState('skip');
  const [preview, setPreview] = useState<ApplyProfilePreview | null>(null);
  const [previewLoading, setPreviewLoading] = useState(false);

  useEffect(() => {
    if (!open) {
      setSelectedId('');
      setPreview(null);
    }
  }, [open]);

  const handlePreview = async () => {
    if (!selectedId) return;
    setPreviewLoading(true);
    const result = await onPreview(selectedId, conflict);
    setPreview(result);
    setPreviewLoading(false);
  };

  if (!open) return null;

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40">
      <div className="w-full max-w-lg rounded-xl bg-white shadow-2xl">
        <div className="border-b border-gray-200 px-6 py-4">
          <h3 className="text-lg font-semibold text-gray-900">Apply CSP Profile</h3>
          <p className="text-sm text-gray-500">Select a pre-built inheritance profile to bulk-designate controls.</p>
        </div>

        <div className="space-y-4 px-6 py-4">
          {loading ? (
            <div className="py-8 text-center text-gray-400 text-sm">Loading profiles...</div>
          ) : profiles.length === 0 ? (
            <div className="py-8 text-center text-gray-400 text-sm">No CSP profiles available.</div>
          ) : (
            <>
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">CSP Profile</label>
                <select
                  className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm"
                  value={selectedId}
                  onChange={e => { setSelectedId(e.target.value); setPreview(null); }}
                >
                  <option value="">Select a profile...</option>
                  {profiles.map(p => (
                    <option key={p.profileId} value={p.profileId}>
                      {p.name} ({p.controlCount} controls)
                    </option>
                  ))}
                </select>
              </div>

              {selectedId && (
                <div>
                  <label className="block text-sm font-medium text-gray-700 mb-1">Conflict Resolution</label>
                  <div className="space-y-1">
                    <label className="flex items-center gap-2 text-sm">
                      <input type="radio" name="conflict" value="skip" checked={conflict === 'skip'} onChange={() => { setConflict('skip'); setPreview(null); }} />
                      <span>Skip existing designations</span>
                    </label>
                    <label className="flex items-center gap-2 text-sm">
                      <input type="radio" name="conflict" value="overwrite" checked={conflict === 'overwrite'} onChange={() => { setConflict('overwrite'); setPreview(null); }} />
                      <span>Overwrite all existing designations</span>
                    </label>
                  </div>
                </div>
              )}

              {selectedId && !preview && (
                <button
                  onClick={handlePreview}
                  disabled={previewLoading}
                  className="rounded-lg bg-gray-100 px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-200 disabled:opacity-50"
                >
                  {previewLoading ? 'Previewing...' : 'Preview Changes'}
                </button>
              )}

              {preview && (
                <div className="rounded-lg border border-indigo-200 bg-indigo-50 p-4">
                  <h4 className="text-sm font-semibold text-indigo-900 mb-2">Preview: {preview.profileName}</h4>
                  <div className="grid grid-cols-2 gap-2 text-sm">
                    <div><span className="text-gray-600">Matched:</span> <span className="font-medium">{preview.matchedControls}</span></div>
                    <div><span className="text-gray-600">Unmatched:</span> <span className="font-medium">{preview.unmatchedControls}</span></div>
                    <div><span className="text-green-600">Inherited:</span> <span className="font-medium">{preview.willSetInherited}</span></div>
                    <div><span className="text-indigo-600">Shared:</span> <span className="font-medium">{preview.willSetShared}</span></div>
                    <div><span className="text-amber-600">Customer:</span> <span className="font-medium">{preview.willSetCustomer}</span></div>
                    <div><span className="text-gray-600">Conflicts:</span> <span className="font-medium">{preview.conflicts}</span></div>
                    {preview.willSkipExisting > 0 && (
                      <div className="col-span-2"><span className="text-gray-600">Will skip:</span> <span className="font-medium">{preview.willSkipExisting}</span></div>
                    )}
                  </div>
                </div>
              )}
            </>
          )}
        </div>

        <div className="flex justify-end gap-3 border-t border-gray-200 px-6 py-4">
          <button onClick={onClose} className="rounded-lg border border-gray-300 px-4 py-2 text-sm text-gray-600 hover:bg-gray-100">
            Cancel
          </button>
          {preview && (
            <button
              onClick={() => onApply(selectedId, conflict)}
              className="rounded-lg bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-700"
            >
              Apply Profile
            </button>
          )}
        </div>
      </div>
    </div>
  );
}
