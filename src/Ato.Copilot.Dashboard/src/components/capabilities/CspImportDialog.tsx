import { useState, useEffect, useRef } from 'react';
import { getCspProfiles, importCspProfile } from '../../api/capabilities';
import type { CspProfile } from '../../types/inheritance';
import type { CspImportResult, CspImportPreview, ConflictDetail } from '../../types/capabilities';

interface CspImportDialogProps {
  open: boolean;
  onClose: () => void;
  onSuccess: () => void;
}

type Step = 'select' | 'preview' | 'result';

export default function CspImportDialog({ open, onClose, onSuccess }: CspImportDialogProps) {
  const [step, setStep] = useState<Step>('select');
  const [profiles, setProfiles] = useState<CspProfile[]>([]);
  const [profilesLoading, setProfilesLoading] = useState(false);
  const [selectedId, setSelectedId] = useState('');
  const [conflict, setConflict] = useState<'skip' | 'overwrite'>('skip');
  const [preview, setPreview] = useState<CspImportPreview | null>(null);
  const [result, setResult] = useState<CspImportResult | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [elapsed, setElapsed] = useState(0);
  const timerRef = useRef<ReturnType<typeof setInterval> | null>(null);

  useEffect(() => {
    if (open) {
      setStep('select');
      setSelectedId('');
      setPreview(null);
      setResult(null);
      setError(null);
      setElapsed(0);
      loadProfiles();
    }
    return () => stopTimer();
  }, [open]);

  const loadProfiles = async () => {
    setProfilesLoading(true);
    try {
      const data = await getCspProfiles();
      setProfiles(data);
    } catch {
      setError('Failed to load CSP profiles');
    } finally {
      setProfilesLoading(false);
    }
  };

  const startTimer = () => {
    setElapsed(0);
    timerRef.current = setInterval(() => setElapsed(s => s + 1), 1000);
  };

  const stopTimer = () => {
    if (timerRef.current) {
      clearInterval(timerRef.current);
      timerRef.current = null;
    }
  };

  const handlePreview = async () => {
    if (!selectedId) return;
    setLoading(true);
    setError(null);
    startTimer();
    try {
      const data = await importCspProfile({ profileId: selectedId, conflictResolution: conflict, dryRun: true });
      setPreview(data as CspImportPreview);
      setStep('preview');
    } catch (err: any) {
      setError(err?.response?.data?.error ?? 'Preview failed');
    } finally {
      setLoading(false);
      stopTimer();
    }
  };

  const handleApply = async () => {
    if (!selectedId) return;
    setLoading(true);
    setError(null);
    startTimer();
    try {
      const data = await importCspProfile({ profileId: selectedId, conflictResolution: conflict, dryRun: false });
      setResult(data as CspImportResult);
      setStep('result');
      onSuccess();
    } catch (err: any) {
      setError(err?.response?.data?.error ?? 'Import failed');
    } finally {
      setLoading(false);
      stopTimer();
    }
  };

  if (!open) return null;

  const selectedProfile = profiles.find(p => p.profileId === selectedId);

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40">
      <div className="w-full max-w-lg rounded-xl bg-white shadow-2xl">
        <div className="border-b border-gray-200 px-6 py-4">
          <h3 className="text-lg font-semibold text-gray-900">Import CSP Profile</h3>
          <p className="text-sm text-gray-500">
            Import a cloud service provider profile to create components, capabilities, and control mappings.
          </p>
        </div>

        <div className="space-y-4 px-6 py-4">
          {error && (
            <div className="rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-700">
              {error}
            </div>
          )}

          {loading && (
            <div className="flex items-center gap-3 py-4">
              <svg className="h-5 w-5 animate-spin text-indigo-600" viewBox="0 0 24 24" fill="none">
                <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
              </svg>
              <span className="text-sm text-gray-600">
                {step === 'select' ? 'Generating preview...' : 'Importing profile...'}
                {elapsed >= 2 && <span className="ml-1 text-gray-400">({elapsed}s)</span>}
              </span>
            </div>
          )}

          {/* Step 1: Select profile */}
          {step === 'select' && !loading && (
            <>
              {profilesLoading ? (
                <div className="py-8 text-center text-sm text-gray-400">Loading profiles...</div>
              ) : profiles.length === 0 ? (
                <div className="py-8 text-center text-sm text-gray-400">No CSP profiles available.</div>
              ) : (
                <>
                  <div>
                    <label className="mb-1 block text-sm font-medium text-gray-700">CSP Profile</label>
                    <select
                      className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm"
                      value={selectedId}
                      onChange={e => { setSelectedId(e.target.value); setError(null); }}
                    >
                      <option value="">Select a profile...</option>
                      {profiles.map(p => (
                        <option key={p.profileId} value={p.profileId}>
                          {p.name} ({p.controlCount} controls)
                        </option>
                      ))}
                    </select>
                  </div>

                  {selectedProfile && (
                    <div className="rounded-lg border border-gray-200 bg-gray-50 p-3 text-sm">
                      <p className="font-medium text-gray-800">{selectedProfile.name}</p>
                      <p className="text-gray-500">{selectedProfile.description}</p>
                      <div className="mt-1 flex gap-4 text-xs text-gray-500">
                        <span>Provider: {selectedProfile.provider}</span>
                        <span>Baseline: {selectedProfile.baselineLevel}</span>
                        <span>v{selectedProfile.version}</span>
                      </div>
                    </div>
                  )}

                  {selectedId && (
                    <div>
                      <label className="mb-1 block text-sm font-medium text-gray-700">Conflict Resolution</label>
                      <div className="space-y-1">
                        <label className="flex items-center gap-2 text-sm">
                          <input type="radio" name="csp-conflict" value="skip" checked={conflict === 'skip'} onChange={() => setConflict('skip')} />
                          <span>Keep existing mappings (skip conflicts)</span>
                        </label>
                        <label className="flex items-center gap-2 text-sm">
                          <input type="radio" name="csp-conflict" value="overwrite" checked={conflict === 'overwrite'} onChange={() => setConflict('overwrite')} />
                          <span>Overwrite existing mappings</span>
                        </label>
                      </div>
                    </div>
                  )}
                </>
              )}
            </>
          )}

          {/* Step 2: Preview */}
          {step === 'preview' && !loading && preview && (
            <div className="space-y-3">
              <div className="rounded-lg border border-indigo-200 bg-indigo-50 p-4">
                <h4 className="mb-2 text-sm font-semibold text-indigo-900">
                  Preview: {preview.profileName}
                </h4>
                <div className="grid grid-cols-2 gap-2 text-sm">
                  <div>
                    <span className="text-gray-600">Components to create:</span>{' '}
                    <span className="font-medium text-green-700">{preview.componentsToCreate}</span>
                  </div>
                  <div>
                    <span className="text-gray-600">Components to reuse:</span>{' '}
                    <span className="font-medium">{preview.componentsToReuse}</span>
                  </div>
                  <div>
                    <span className="text-gray-600">Capabilities to create:</span>{' '}
                    <span className="font-medium text-green-700">{preview.capabilitiesToCreate}</span>
                  </div>
                  <div>
                    <span className="text-gray-600">Capabilities to reuse:</span>{' '}
                    <span className="font-medium">{preview.capabilitiesToReuse}</span>
                  </div>
                  <div>
                    <span className="text-gray-600">Control mappings:</span>{' '}
                    <span className="font-medium">{preview.controlMappingsToCreate}</span>
                  </div>
                  <div>
                    <span className="text-gray-600">Conflicts:</span>{' '}
                    <span className="font-medium text-amber-600">{preview.conflicts}</span>
                  </div>
                  <div>
                    <span className="text-gray-600">Systems affected:</span>{' '}
                    <span className="font-medium">{preview.systemsAffected}</span>
                  </div>
                </div>
              </div>

              {preview.conflictDetails.length > 0 && (
                <div className="max-h-40 overflow-y-auto rounded-lg border border-amber-200 bg-amber-50 p-3">
                  <h5 className="mb-1 text-xs font-semibold text-amber-800">
                    Conflict Details ({preview.conflictDetails.length})
                  </h5>
                  <div className="space-y-1">
                    {preview.conflictDetails.slice(0, 20).map((c: ConflictDetail, i: number) => (
                      <div key={i} className="text-xs text-amber-700">
                        <span className="font-mono">{c.controlId}</span>: {c.existingRole} → {c.newRole} ({c.resolution})
                      </div>
                    ))}
                    {preview.conflictDetails.length > 20 && (
                      <div className="text-xs text-amber-500">
                        ...and {preview.conflictDetails.length - 20} more
                      </div>
                    )}
                  </div>
                </div>
              )}
            </div>
          )}

          {/* Step 3: Result */}
          {step === 'result' && result && (
            <div className="rounded-lg border border-green-200 bg-green-50 p-4">
              <h4 className="mb-2 text-sm font-semibold text-green-900">
                Import Complete: {result.profileName}
              </h4>
              <div className="grid grid-cols-2 gap-2 text-sm">
                <div>
                  <span className="text-gray-600">Components created:</span>{' '}
                  <span className="font-medium text-green-700">{result.componentsCreated}</span>
                </div>
                <div>
                  <span className="text-gray-600">Components reused:</span>{' '}
                  <span className="font-medium">{result.componentsReused}</span>
                </div>
                <div>
                  <span className="text-gray-600">Capabilities created:</span>{' '}
                  <span className="font-medium text-green-700">{result.capabilitiesCreated}</span>
                </div>
                <div>
                  <span className="text-gray-600">Capabilities reused:</span>{' '}
                  <span className="font-medium">{result.capabilitiesReused}</span>
                </div>
                <div>
                  <span className="text-gray-600">Control mappings:</span>{' '}
                  <span className="font-medium">{result.controlMappingsCreated}</span>
                </div>
                <div>
                  <span className="text-gray-600">Org defaults derived:</span>{' '}
                  <span className="font-medium">{result.orgDefaultsDerived}</span>
                </div>
                <div>
                  <span className="text-gray-600">Narratives generated:</span>{' '}
                  <span className="font-medium">{result.narrativesGenerated}</span>
                </div>
                <div>
                  <span className="text-gray-600">Systems affected:</span>{' '}
                  <span className="font-medium">{result.systemsAffected}</span>
                </div>
                {result.conflicts > 0 && (
                  <div>
                    <span className="text-gray-600">Conflicts resolved:</span>{' '}
                    <span className="font-medium text-amber-600">{result.conflicts}</span>
                  </div>
                )}
                {result.skipped > 0 && (
                  <div>
                    <span className="text-gray-600">Skipped:</span>{' '}
                    <span className="font-medium">{result.skipped}</span>
                  </div>
                )}
              </div>
            </div>
          )}
        </div>

        <div className="flex justify-end gap-3 border-t border-gray-200 px-6 py-4">
          <button
            onClick={onClose}
            className="rounded-lg border border-gray-300 px-4 py-2 text-sm text-gray-600 hover:bg-gray-100"
          >
            {step === 'result' ? 'Close' : 'Cancel'}
          </button>
          {step === 'select' && selectedId && !loading && (
            <button
              onClick={handlePreview}
              className="rounded-lg bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-700"
            >
              Preview Import
            </button>
          )}
          {step === 'preview' && !loading && (
            <>
              <button
                onClick={() => { setStep('select'); setPreview(null); }}
                className="rounded-lg bg-gray-100 px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-200"
              >
                Back
              </button>
              <button
                onClick={handleApply}
                className="rounded-lg bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-700"
              >
                Apply Import
              </button>
            </>
          )}
        </div>
      </div>
    </div>
  );
}
