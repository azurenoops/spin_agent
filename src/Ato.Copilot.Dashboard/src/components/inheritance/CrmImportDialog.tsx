import { useState, useEffect, useRef, useCallback } from 'react';
import type { ImportPreview, ImportApplyResult } from '../../types/inheritance';

const TARGET_FIELDS = [
  { key: 'controlId', label: 'Control ID' },
  { key: 'inheritanceType', label: 'Inheritance Type' },
  { key: 'provider', label: 'Provider' },
  { key: 'customerResponsibility', label: 'Customer Responsibility' },
] as const;

type TargetFieldKey = (typeof TARGET_FIELDS)[number]['key'];

interface CrmImportDialogProps {
  open: boolean;
  onPreview: (file: File) => Promise<ImportPreview | null>;
  onApply: (
    previewToken: string,
    columnMapping: Record<TargetFieldKey, string>,
    conflictResolution: 'skip' | 'overwrite',
  ) => Promise<ImportApplyResult | null>;
  onClose: () => void;
}

type Step = 'upload' | 'mapping' | 'result';

export default function CrmImportDialog({
  open, onPreview, onApply, onClose,
}: CrmImportDialogProps) {
  const [step, setStep] = useState<Step>('upload');
  const [preview, setPreview] = useState<ImportPreview | null>(null);
  const [mapping, setMapping] = useState<Record<TargetFieldKey, string>>({
    controlId: '', inheritanceType: '', provider: '', customerResponsibility: '',
  });
  const [conflict, setConflict] = useState<'skip' | 'overwrite'>('overwrite');
  const [loading, setLoading] = useState(false);
  const [result, setResult] = useState<ImportApplyResult | null>(null);
  const [dragOver, setDragOver] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const fileRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    if (!open) {
      setStep('upload');
      setPreview(null);
      setMapping({ controlId: '', inheritanceType: '', provider: '', customerResponsibility: '' });
      setResult(null);
      setLoading(false);
      setError(null);
    }
  }, [open]);

  const handleFile = useCallback(async (file: File) => {
    setLoading(true);
    setError(null);
    const p = await onPreview(file);
    if (p) {
      setPreview(p);
      // Apply suggested mapping
      const m: Record<TargetFieldKey, string> = {
        controlId: '', inheritanceType: '', provider: '', customerResponsibility: '',
      };
      for (const tf of TARGET_FIELDS) {
        if (p.suggestedMapping[tf.key]) m[tf.key] = p.suggestedMapping[tf.key] ?? '';
      }
      setMapping(m);
      setStep('mapping');
    } else {
      setError('Failed to parse file. Ensure the system has a baseline configured and the file is a valid CSV or Excel file.');
    }
    setLoading(false);
  }, [onPreview]);

  const handleDrop = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    setDragOver(false);
    const file = e.dataTransfer.files?.[0];
    if (file) handleFile(file);
  }, [handleFile]);

  const handleApply = async () => {
    if (!preview) return;
    setLoading(true);
    const r = await onApply(preview.previewToken, mapping, conflict);
    if (r) {
      setResult(r);
      setStep('result');
    }
    setLoading(false);
  };

  if (!open) return null;

  const allMapped = mapping.controlId && mapping.inheritanceType;

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40">
      <div className="w-full max-w-2xl rounded-xl bg-white shadow-2xl">
        {/* Header */}
        <div className="flex items-center justify-between border-b px-6 py-4">
          <h2 className="text-lg font-semibold text-gray-900">Import CRM</h2>
          <button onClick={onClose} className="text-gray-400 hover:text-gray-600">&times;</button>
        </div>

        <div className="max-h-[70vh] overflow-y-auto px-6 py-4 space-y-4">
          {/* ── Step 1: Upload ── */}
          {step === 'upload' && (
            <div
              className={`flex flex-col items-center justify-center rounded-lg border-2 border-dashed p-10 transition ${dragOver ? 'border-blue-500 bg-blue-50' : 'border-gray-300'}`}
              onDragOver={e => { e.preventDefault(); setDragOver(true); }}
              onDragLeave={() => setDragOver(false)}
              onDrop={handleDrop}
            >
              {loading ? (
                <p className="text-sm text-gray-500">Parsing file…</p>
              ) : (
                <>
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
                </>
              )}
              {error && (
                <div className="mt-3 w-full rounded-md bg-red-50 px-4 py-3 text-sm text-red-700">
                  {error}
                </div>
              )}
            </div>
          )}

          {/* ── Step 2: Column Mapping ── */}
          {step === 'mapping' && preview && (
            <>
              <div className="rounded-md bg-gray-50 p-3 text-sm text-gray-600">
                <strong>{preview.fileName}</strong> — {preview.totalRows} rows, {preview.detectedColumns.length} columns detected
              </div>

              <div className="space-y-3">
                <h3 className="text-sm font-medium text-gray-700">Column Mapping</h3>
                {TARGET_FIELDS.map(tf => (
                  <div key={tf.key} className="flex items-center gap-3">
                    <label className="w-44 text-sm text-gray-600">{tf.label}{tf.key === 'controlId' || tf.key === 'inheritanceType' ? ' *' : ''}</label>
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
              {preview.sampleRows.length > 0 && (
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

              {/* Conflict resolution */}
              <div className="space-y-2">
                <h3 className="text-sm font-medium text-gray-700">Conflict Resolution</h3>
                <label className="flex items-center gap-2 text-sm">
                  <input type="radio" checked={conflict === 'overwrite'} onChange={() => setConflict('overwrite')} />
                  Overwrite existing designations
                </label>
                <label className="flex items-center gap-2 text-sm">
                  <input type="radio" checked={conflict === 'skip'} onChange={() => setConflict('skip')} />
                  Skip controls with existing designations
                </label>
              </div>

              <div className="flex justify-end gap-3 pt-2">
                <button onClick={() => setStep('upload')} className="rounded-md border px-4 py-2 text-sm text-gray-600 hover:bg-gray-50">Back</button>
                <button
                  onClick={handleApply}
                  disabled={!allMapped || loading}
                  className="rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50"
                >
                  {loading ? 'Importing…' : 'Apply Import'}
                </button>
              </div>
            </>
          )}

          {/* ── Step 3: Result ── */}
          {step === 'result' && result && (
            <>
              <div className="rounded-md bg-green-50 p-4 text-sm text-green-800">
                <strong>Import complete!</strong>
              </div>
              <div className="grid grid-cols-2 gap-3 text-sm">
                <div className="rounded-md bg-gray-50 p-3"><span className="text-gray-500">Controls Imported</span><br /><strong>{result.controlsImported}</strong></div>
                <div className="rounded-md bg-gray-50 p-3"><span className="text-gray-500">Controls Skipped</span><br /><strong>{result.controlsSkipped}</strong></div>
                <div className="rounded-md bg-gray-50 p-3"><span className="text-gray-500">Not Found</span><br /><strong>{result.controlsNotFound}</strong></div>
                <div className="rounded-md bg-gray-50 p-3"><span className="text-gray-500">Overwritten</span><br /><strong>{result.duplicatesOverwritten}</strong></div>
              </div>
              {result.narrativesAutoUpdated > 0 && (
                <div className="rounded-md bg-blue-50 p-4 text-sm text-blue-800">
                  <strong>{result.narrativesAutoUpdated}</strong> narrative{result.narrativesAutoUpdated !== 1 ? 's' : ''} auto-updated: Inherited → Implemented, Shared → Partially Implemented
                </div>
              )}
              {result.notFoundControlIds.length > 0 && (
                <div className="rounded-md bg-yellow-50 p-3 text-sm">
                  <p className="font-medium text-yellow-800">Controls not found in baseline:</p>
                  <p className="mt-1 text-yellow-700">{result.notFoundControlIds.join(', ')}</p>
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
