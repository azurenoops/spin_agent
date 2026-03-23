import { useState, useEffect, useRef, useCallback } from 'react';
import { importCrm } from '../../api/capabilities';
import type { CrmImportResult, CrmImportPreview, CrmColumnMapping } from '../../types/capabilities';

const TARGET_FIELDS = [
  { key: 'controlId' as const, label: 'Control ID', required: true },
  { key: 'inheritanceType' as const, label: 'Inheritance Type', required: true },
  { key: 'provider' as const, label: 'Provider', required: false },
  { key: 'customerResponsibility' as const, label: 'Customer Responsibility', required: false },
];

type Step = 'upload' | 'mapping' | 'preview' | 'result';

interface CrmImportDialogProps {
  open: boolean;
  onClose: () => void;
  onSuccess: () => void;
}

export default function CrmImportDialog({ open, onClose, onSuccess }: CrmImportDialogProps) {
  const [step, setStep] = useState<Step>('upload');
  const [file, setFile] = useState<File | null>(null);
  const [mapping, setMapping] = useState<CrmColumnMapping>({
    controlId: '', inheritanceType: '', provider: '', customerResponsibility: '',
  });
  const [conflict, setConflict] = useState<'skip' | 'overwrite'>('skip');
  const [loading, setLoading] = useState(false);
  const [elapsed, setElapsed] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const [preview, setPreview] = useState<CrmImportPreview | null>(null);
  const [result, setResult] = useState<CrmImportResult | null>(null);
  const [dragOver, setDragOver] = useState(false);
  const fileRef = useRef<HTMLInputElement>(null);
  const timerRef = useRef<ReturnType<typeof setInterval>>(undefined);

  useEffect(() => {
    if (!open) {
      setStep('upload');
      setFile(null);
      setMapping({ controlId: '', inheritanceType: '', provider: '', customerResponsibility: '' });
      setConflict('skip');
      setPreview(null);
      setResult(null);
      setLoading(false);
      setError(null);
      setElapsed(0);
    }
  }, [open]);

  useEffect(() => {
    if (loading) {
      const start = Date.now();
      timerRef.current = setInterval(() => setElapsed(Math.floor((Date.now() - start) / 1000)), 500);
    } else {
      clearInterval(timerRef.current);
      setElapsed(0);
    }
    return () => clearInterval(timerRef.current);
  }, [loading]);

  const handleFile = useCallback(async (f: File) => {
    setFile(f);
    setLoading(true);
    setError(null);
    try {
      const p = await importCrm(f, { controlId: '', inheritanceType: '', provider: '', customerResponsibility: '' }, 'skip', true) as CrmImportPreview;
      if (p.detectedColumns?.length) {
        // Auto-suggest mapping
        const m: CrmColumnMapping = { controlId: '', inheritanceType: '', provider: '', customerResponsibility: '' };
        for (const tf of TARGET_FIELDS) {
          const match = p.detectedColumns.find(c => c.toLowerCase().includes(tf.key.toLowerCase()));
          if (match) m[tf.key] = match;
        }
        setMapping(m);
        setPreview(p);
        setStep('mapping');
      } else {
        setError('No columns detected in the file. Ensure it is a valid CSV or Excel file.');
      }
    } catch {
      setError('Failed to parse file. Ensure it is a valid CSV or Excel file.');
    }
    setLoading(false);
  }, []);

  const handleDrop = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    setDragOver(false);
    const f = e.dataTransfer.files?.[0];
    if (f) handleFile(f);
  }, [handleFile]);

  const handlePreview = async () => {
    if (!file) return;
    setLoading(true);
    setError(null);
    try {
      const p = await importCrm(file, mapping, conflict, true) as CrmImportPreview;
      setPreview(p);
      setStep('preview');
    } catch {
      setError('Preview failed. Check your column mapping.');
    }
    setLoading(false);
  };

  const handleApply = async () => {
    if (!file) return;
    setLoading(true);
    setError(null);
    try {
      const r = await importCrm(file, mapping, conflict, false) as CrmImportResult;
      setResult(r);
      setStep('result');
      onSuccess();
    } catch {
      setError('Import failed. Check server logs for details.');
    }
    setLoading(false);
  };

  if (!open) return null;
  const allMapped = mapping.controlId && mapping.inheritanceType;

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40">
      <div className="w-full max-w-2xl rounded-xl bg-white shadow-2xl">
        <div className="flex items-center justify-between border-b px-6 py-4">
          <h2 className="text-lg font-semibold text-gray-900">Import CRM File</h2>
          <button onClick={onClose} className="text-gray-400 hover:text-gray-600 text-xl">&times;</button>
        </div>

        <div className="max-h-[70vh] overflow-y-auto px-6 py-4 space-y-4">
          {/* Loading spinner */}
          {loading && (
            <div className="flex items-center gap-3 rounded-md bg-blue-50 px-4 py-3 text-sm text-blue-700">
              <svg className="h-5 w-5 animate-spin" viewBox="0 0 24 24" fill="none">
                <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
              </svg>
              Processing…{elapsed >= 2 && <span className="text-blue-500">({elapsed}s)</span>}
            </div>
          )}

          {error && (
            <div className="rounded-md bg-red-50 px-4 py-3 text-sm text-red-700">{error}</div>
          )}

          {/* ── Step 1: Upload ── */}
          {step === 'upload' && !loading && (
            <div
              className={`flex flex-col items-center justify-center rounded-lg border-2 border-dashed p-10 transition ${dragOver ? 'border-blue-500 bg-blue-50' : 'border-gray-300'}`}
              onDragOver={e => { e.preventDefault(); setDragOver(true); }}
              onDragLeave={() => setDragOver(false)}
              onDrop={handleDrop}
            >
              <p className="mb-2 text-sm text-gray-500">Drag & drop a CSV or Excel file, or</p>
              <button
                onClick={() => fileRef.current?.click()}
                className="rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700"
              >
                Browse Files
              </button>
              <input
                ref={fileRef}
                type="file"
                accept=".csv,.xlsx,.xls"
                className="hidden"
                onChange={e => {
                  const f = e.target.files?.[0];
                  if (f) handleFile(f);
                }}
              />
              <p className="mt-2 text-xs text-gray-400">Supported: .csv, .xlsx, .xls</p>
            </div>
          )}

          {/* ── Step 2: Column Mapping ── */}
          {step === 'mapping' && preview && !loading && (
            <>
              <div className="rounded-md bg-gray-50 p-3 text-sm text-gray-600">
                <strong>{preview.fileName}</strong> — {preview.rowsParsed} rows, {preview.detectedColumns.length} columns detected
              </div>

              <div className="space-y-3">
                <h3 className="text-sm font-medium text-gray-700">Column Mapping</h3>
                {TARGET_FIELDS.map(tf => (
                  <div key={tf.key} className="flex items-center gap-3">
                    <label className="w-44 text-sm text-gray-600">{tf.label}{tf.required ? ' *' : ''}</label>
                    <select
                      value={mapping[tf.key]}
                      onChange={e => setMapping(prev => ({ ...prev, [tf.key]: e.target.value }))}
                      className="flex-1 rounded-md border border-gray-300 px-3 py-1.5 text-sm"
                    >
                      <option value="">— Select column —</option>
                      {preview.detectedColumns.map(c => (
                        <option key={c} value={c}>{c}</option>
                      ))}
                    </select>
                  </div>
                ))}
              </div>

              {/* Sample Data */}
              {preview.sampleRows?.length > 0 && (
                <div>
                  <h3 className="mb-1 text-sm font-medium text-gray-700">Sample Data (first {preview.sampleRows.length} rows)</h3>
                  <div className="overflow-x-auto rounded border">
                    <table className="w-full text-xs">
                      <thead className="bg-gray-50">
                        <tr>
                          {preview.detectedColumns.map(c => (
                            <th key={c} className="px-2 py-1 text-left font-medium text-gray-500">{c}</th>
                          ))}
                        </tr>
                      </thead>
                      <tbody>
                        {preview.sampleRows.map((row, i) => (
                          <tr key={i} className="border-t">
                            {preview.detectedColumns.map(c => (
                              <td key={c} className="px-2 py-1 text-gray-700 whitespace-nowrap">{row[c] ?? ''}</td>
                            ))}
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                </div>
              )}

              {/* Conflict Resolution */}
              <div className="space-y-2">
                <h3 className="text-sm font-medium text-gray-700">Conflict Resolution</h3>
                <label className="flex items-center gap-2 text-sm">
                  <input type="radio" checked={conflict === 'skip'} onChange={() => setConflict('skip')} />
                  Skip controls with existing mappings
                </label>
                <label className="flex items-center gap-2 text-sm">
                  <input type="radio" checked={conflict === 'overwrite'} onChange={() => setConflict('overwrite')} />
                  Overwrite existing mappings
                </label>
              </div>

              <div className="flex justify-end gap-3 pt-2">
                <button onClick={() => setStep('upload')} className="rounded-md border px-4 py-2 text-sm text-gray-600 hover:bg-gray-50">Back</button>
                <button
                  onClick={handlePreview}
                  disabled={!allMapped}
                  className="rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50"
                >
                  Preview Import
                </button>
              </div>
            </>
          )}

          {/* ── Step 3: Preview ── */}
          {step === 'preview' && preview && !loading && (
            <>
              <div className="rounded-md bg-blue-50 p-4 text-sm text-blue-800">
                <strong>Preview: {preview.fileName}</strong> — {preview.rowsParsed} rows parsed
              </div>
              <div className="grid grid-cols-2 gap-3 text-sm">
                <div className="rounded-md bg-gray-50 p-3"><span className="text-gray-500">Components to Create</span><br /><strong>{preview.componentsToCreate}</strong></div>
                <div className="rounded-md bg-gray-50 p-3"><span className="text-gray-500">Components to Reuse</span><br /><strong>{preview.componentsToReuse}</strong></div>
                <div className="rounded-md bg-gray-50 p-3"><span className="text-gray-500">Capabilities to Create</span><br /><strong>{preview.capabilitiesToCreate}</strong></div>
                <div className="rounded-md bg-gray-50 p-3"><span className="text-gray-500">Capabilities to Reuse</span><br /><strong>{preview.capabilitiesToReuse}</strong></div>
                <div className="rounded-md bg-gray-50 p-3"><span className="text-gray-500">Control Mappings</span><br /><strong>{preview.controlMappingsToCreate}</strong></div>
                <div className="rounded-md bg-gray-50 p-3"><span className="text-gray-500">Unmatched Rows</span><br /><strong>{preview.unmatchedRows}</strong></div>
              </div>
              {preview.conflicts > 0 && (
                <div className="rounded-md bg-yellow-50 p-3 text-sm">
                  <p className="font-medium text-yellow-800">{preview.conflicts} conflict(s) detected</p>
                  {preview.conflictDetails.length > 0 && (
                    <div className="mt-2 max-h-32 overflow-y-auto space-y-1">
                      {preview.conflictDetails.map((d, i) => (
                        <p key={i} className="text-xs text-yellow-700">
                          {d.controlId}: {d.existingRole} → {d.newRole} ({d.resolution})
                        </p>
                      ))}
                    </div>
                  )}
                </div>
              )}
              <div className="flex justify-end gap-3 pt-2">
                <button onClick={() => setStep('mapping')} className="rounded-md border px-4 py-2 text-sm text-gray-600 hover:bg-gray-50">Back</button>
                <button
                  onClick={handleApply}
                  className="rounded-md bg-green-600 px-4 py-2 text-sm font-medium text-white hover:bg-green-700"
                >
                  Confirm Import
                </button>
              </div>
            </>
          )}

          {/* ── Step 4: Result ── */}
          {step === 'result' && result && (
            <>
              <div className="rounded-md bg-green-50 p-4 text-sm text-green-800">
                <strong>Import complete!</strong>
              </div>
              <div className="grid grid-cols-2 gap-3 text-sm">
                <div className="rounded-md bg-gray-50 p-3"><span className="text-gray-500">Components Created</span><br /><strong>{result.componentsCreated}</strong></div>
                <div className="rounded-md bg-gray-50 p-3"><span className="text-gray-500">Components Reused</span><br /><strong>{result.componentsReused}</strong></div>
                <div className="rounded-md bg-gray-50 p-3"><span className="text-gray-500">Capabilities Created</span><br /><strong>{result.capabilitiesCreated}</strong></div>
                <div className="rounded-md bg-gray-50 p-3"><span className="text-gray-500">Capabilities Reused</span><br /><strong>{result.capabilitiesReused}</strong></div>
                <div className="rounded-md bg-gray-50 p-3"><span className="text-gray-500">Control Mappings</span><br /><strong>{result.controlMappingsCreated}</strong></div>
                <div className="rounded-md bg-gray-50 p-3"><span className="text-gray-500">Narratives Generated</span><br /><strong>{result.narrativesGenerated}</strong></div>
              </div>
              {result.unmatchedRows > 0 && (
                <div className="rounded-md bg-yellow-50 p-3 text-sm text-yellow-700">
                  <strong>{result.unmatchedRows}</strong> rows could not be matched to baseline controls.
                </div>
              )}
              <div className="flex justify-end pt-2">
                <button onClick={onClose} className="rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700">Done</button>
              </div>
            </>
          )}
        </div>
      </div>
    </div>
  );
}
