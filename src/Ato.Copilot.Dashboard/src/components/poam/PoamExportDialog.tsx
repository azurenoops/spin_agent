import { useState, useCallback } from 'react';
import { exportPoam } from '../../api/poam';
import type { ExportFormat } from '../../types/poam';

interface PoamExportDialogProps {
  systemId: string;
  currentStatus?: string;
  currentSeverity?: string;
  onClose: () => void;
}

const formatOptions: { value: ExportFormat; label: string; description: string }[] = [
  { value: 'emass_excel', label: 'eMASS Excel', description: '24-column eMASS template (.xlsx)' },
  { value: 'oscal_json', label: 'OSCAL JSON', description: 'NIST OSCAL POA&M schema (.json)' },
  { value: 'csv', label: 'CSV', description: 'Comma-separated values (.csv)' },
];

export default function PoamExportDialog({ systemId, currentStatus, currentSeverity, onClose }: PoamExportDialogProps) {
  const [format, setFormat] = useState<ExportFormat>('emass_excel');
  const [includeAll, setIncludeAll] = useState(false);
  const [exporting, setExporting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleExport = useCallback(async () => {
    setError(null);
    setExporting(true);
    try {
      const blob = await exportPoam(
        systemId,
        format,
        includeAll ? undefined : currentStatus,
        includeAll ? undefined : currentSeverity,
        includeAll,
      );
      const url = window.URL.createObjectURL(blob);
      const ext = format === 'emass_excel' ? 'xlsx' : format === 'oscal_json' ? 'oscal.json' : 'csv';
      const link = document.createElement('a');
      link.href = url;
      link.download = `poam-${systemId}-${new Date().toISOString().slice(0, 10)}.${ext}`;
      document.body.appendChild(link);
      link.click();
      document.body.removeChild(link);
      window.URL.revokeObjectURL(url);
      onClose();
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Export failed');
    } finally {
      setExporting(false);
    }
  }, [systemId, format, includeAll, currentStatus, currentSeverity, onClose]);

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/30" onClick={onClose}>
      <div className="w-full max-w-md rounded-xl bg-white p-6 shadow-2xl" onClick={e => e.stopPropagation()}>
        <div className="mb-4 flex items-center justify-between">
          <h2 className="text-lg font-bold text-gray-900">Export POA&amp;M</h2>
          <button onClick={onClose} className="text-2xl text-gray-400 hover:text-gray-600">&times;</button>
        </div>

        {error && (
          <div className="mb-4 rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">
            {error}
          </div>
        )}

        {/* Format Selection */}
        <fieldset className="mb-4">
          <legend className="mb-2 text-sm font-semibold text-gray-700">Format</legend>
          <div className="space-y-2">
            {formatOptions.map(opt => (
              <label key={opt.value} className="flex cursor-pointer items-start gap-3 rounded-lg border border-gray-200 p-3 hover:bg-gray-50">
                <input
                  type="radio"
                  name="format"
                  value={opt.value}
                  checked={format === opt.value}
                  onChange={() => setFormat(opt.value)}
                  className="mt-0.5 h-4 w-4 text-indigo-600 focus:ring-indigo-500"
                />
                <div>
                  <span className="text-sm font-medium text-gray-900">{opt.label}</span>
                  <p className="text-xs text-gray-500">{opt.description}</p>
                </div>
              </label>
            ))}
          </div>
        </fieldset>

        {/* Scope Toggle */}
        <div className="mb-6 flex items-center gap-3">
          <button
            type="button"
            role="switch"
            aria-checked={includeAll}
            onClick={() => setIncludeAll(v => !v)}
            className={`relative inline-flex h-6 w-11 shrink-0 cursor-pointer rounded-full border-2 border-transparent transition-colors ${
              includeAll ? 'bg-indigo-600' : 'bg-gray-200'
            }`}
          >
            <span
              className={`pointer-events-none inline-block h-5 w-5 rounded-full bg-white shadow ring-0 transition-transform ${
                includeAll ? 'translate-x-5' : 'translate-x-0'
              }`}
            />
          </button>
          <span className="text-sm text-gray-700">
            {includeAll ? 'Export all items (ignore filters)' : 'Export with current filters applied'}
          </span>
        </div>

        {/* Actions */}
        <div className="flex justify-end gap-3">
          <button
            type="button"
            onClick={onClose}
            className="rounded-lg border border-gray-300 px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50"
          >
            Cancel
          </button>
          <button
            type="button"
            onClick={handleExport}
            disabled={exporting}
            className="inline-flex items-center gap-2 rounded-lg bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-700 disabled:cursor-not-allowed disabled:opacity-50"
          >
            {exporting ? 'Exporting...' : 'Download'}
          </button>
        </div>
      </div>
    </div>
  );
}
