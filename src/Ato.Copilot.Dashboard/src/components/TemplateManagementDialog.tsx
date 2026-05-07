import { useState, useEffect, useRef } from 'react';
import {
  listTemplates,
  uploadTemplate,
  deleteTemplate,
  renameTemplate,
} from '../api/exports';
import type { TemplateInfo } from '../api/exports';

interface TemplateManagementDialogProps {
  onClose: () => void;
}

function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

function formatDate(dt: string): string {
  return new Date(dt).toLocaleDateString('en-US', { year: 'numeric', month: 'short', day: 'numeric' });
}

export default function TemplateManagementDialog({ onClose }: TemplateManagementDialogProps) {
  const [templates, setTemplates] = useState<TemplateInfo[]>([]);
  const [loading, setLoading] = useState(true);
  const [showUpload, setShowUpload] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editName, setEditName] = useState('');
  const [error, setError] = useState<string | null>(null);
  const dialogRef = useRef<HTMLDivElement>(null);

  // Upload form state
  const [uploadFile, setUploadFile] = useState<File | null>(null);
  const [uploadName, setUploadName] = useState('');
  const [uploadDesc, setUploadDesc] = useState('');
  const [uploading, setUploading] = useState(false);

  const fetchTemplates = async () => {
    try {
      const res = await listTemplates({ limit: 50 });
      setTemplates(res.items);
    } catch {
      setError('Failed to load templates');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchTemplates();
  }, []);

  useEffect(() => {
    const handleKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
    };
    document.addEventListener('keydown', handleKey);
    return () => document.removeEventListener('keydown', handleKey);
  }, [onClose]);

  const handleBackdrop = (e: React.MouseEvent) => {
    if (e.target === e.currentTarget) onClose();
  };

  const handleUpload = async () => {
    if (!uploadFile || !uploadName.trim()) return;
    setUploading(true);
    setError(null);
    try {
      await uploadTemplate(uploadFile, uploadName.trim(), uploadDesc.trim() || undefined);
      setShowUpload(false);
      setUploadFile(null);
      setUploadName('');
      setUploadDesc('');
      await fetchTemplates();
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Upload failed');
    } finally {
      setUploading(false);
    }
  };

  const handleDelete = async (id: string, name: string) => {
    if (!confirm(`Delete template "${name}"? This action cannot be undone.`)) return;
    setError(null);
    try {
      await deleteTemplate(id);
      await fetchTemplates();
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Delete failed');
    }
  };

  const handleRename = async (id: string) => {
    if (!editName.trim()) return;
    setError(null);
    try {
      await renameTemplate(id, editName.trim());
      setEditingId(null);
      setEditName('');
      await fetchTemplates();
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Rename failed');
    }
  };

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 backdrop-blur-sm"
      onClick={handleBackdrop}
    >
      <div
        ref={dialogRef}
        className="w-full max-w-2xl rounded-xl bg-white shadow-2xl border border-gray-200 overflow-hidden max-h-[80vh] flex flex-col"
        role="dialog"
        aria-labelledby="template-dialog-title"
      >
        {/* Header */}
        <div className="flex items-center justify-between px-5 py-4 bg-gray-50 border-b border-gray-200">
          <h2 id="template-dialog-title" className="text-base font-semibold text-gray-900">
            Manage SSP Templates
          </h2>
          <div className="flex items-center gap-2">
            {!showUpload && (
              <button
                onClick={() => setShowUpload(true)}
                className="inline-flex items-center gap-1.5 rounded-md bg-indigo-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-indigo-700 transition-colors"
              >
                <svg className="h-3.5 w-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                  <path strokeLinecap="round" strokeLinejoin="round" d="M12 4.5v15m7.5-7.5h-15" />
                </svg>
                Upload Template
              </button>
            )}
            <button
              onClick={onClose}
              className="text-gray-400 hover:text-gray-600 transition-colors"
              aria-label="Close"
            >
              <svg className="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" />
              </svg>
            </button>
          </div>
        </div>

        {/* Error */}
        {error && (
          <div className="mx-5 mt-3 rounded-lg bg-red-50 border border-red-200 p-3">
            <p className="text-sm text-red-700">{error}</p>
          </div>
        )}

        {/* Upload form */}
        {showUpload && (
          <div className="px-5 py-4 border-b border-gray-200 bg-indigo-50 space-y-3">
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Template File (.docx)</label>
              <input
                type="file"
                accept=".docx"
                onChange={(e) => setUploadFile(e.target.files?.[0] ?? null)}
                className="block w-full text-sm text-gray-500 file:mr-4 file:py-1.5 file:px-3 file:rounded-md file:border-0 file:text-xs file:font-semibold file:bg-indigo-100 file:text-indigo-700 hover:file:bg-indigo-200"
              />
            </div>
            <div className="grid grid-cols-2 gap-3">
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">Name</label>
                <input
                  type="text"
                  value={uploadName}
                  onChange={(e) => setUploadName(e.target.value)}
                  placeholder="e.g., DoD Standard SSP"
                  className="w-full rounded-lg border border-gray-300 px-3 py-1.5 text-sm focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500"
                />
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">Description</label>
                <input
                  type="text"
                  value={uploadDesc}
                  onChange={(e) => setUploadDesc(e.target.value)}
                  placeholder="Optional description"
                  className="w-full rounded-lg border border-gray-300 px-3 py-1.5 text-sm focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500"
                />
              </div>
            </div>
            <div className="flex justify-end gap-2">
              <button
                onClick={() => { setShowUpload(false); setUploadFile(null); setUploadName(''); setUploadDesc(''); }}
                className="px-3 py-1.5 text-xs font-medium text-gray-700 bg-white border border-gray-300 rounded-md hover:bg-gray-50"
              >
                Cancel
              </button>
              <button
                onClick={handleUpload}
                disabled={!uploadFile || !uploadName.trim() || uploading}
                className="px-3 py-1.5 text-xs font-medium text-white bg-indigo-600 rounded-md hover:bg-indigo-700 disabled:opacity-50 disabled:cursor-not-allowed"
              >
                {uploading ? 'Uploading...' : 'Upload'}
              </button>
            </div>
          </div>
        )}

        {/* Template list */}
        <div className="flex-1 overflow-y-auto">
          {loading ? (
            <div className="p-8 text-center text-gray-500">Loading templates...</div>
          ) : templates.length === 0 ? (
            <div className="p-8 text-center text-gray-500">
              <p className="text-sm">No custom templates uploaded yet.</p>
              <p className="text-xs mt-1">Upload a .docx template to get started.</p>
            </div>
          ) : (
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-gray-100 text-left text-xs text-gray-500">
                  <th className="px-5 py-2 font-medium">Name</th>
                  <th className="px-5 py-2 font-medium">Size</th>
                  <th className="px-5 py-2 font-medium">Merge Fields</th>
                  <th className="px-5 py-2 font-medium">Uploaded</th>
                  <th className="px-5 py-2 font-medium"></th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-50">
                {templates.map((t) => (
                  <tr key={t.id} className="hover:bg-gray-50">
                    <td className="px-5 py-2.5">
                      {editingId === t.id ? (
                        <div className="flex items-center gap-1">
                          <input
                            type="text"
                            value={editName}
                            onChange={(e) => setEditName(e.target.value)}
                            onKeyDown={(e) => { if (e.key === 'Enter') handleRename(t.id); if (e.key === 'Escape') setEditingId(null); }}
                            className="rounded border border-indigo-300 px-2 py-1 text-sm focus:ring-1 focus:ring-indigo-500 w-40"
                            autoFocus
                          />
                          <button onClick={() => handleRename(t.id)} className="text-indigo-600 hover:text-indigo-800 text-xs font-medium">Save</button>
                          <button onClick={() => setEditingId(null)} className="text-gray-400 hover:text-gray-600 text-xs">Cancel</button>
                        </div>
                      ) : (
                        <div>
                          <span className="font-medium text-gray-900">{t.name}</span>
                          {t.isDefault && (
                            <span className="ml-2 inline-flex items-center rounded-full bg-green-100 px-2 py-0.5 text-xs font-medium text-green-700">
                              Default
                            </span>
                          )}
                          {t.description && <p className="text-xs text-gray-500 mt-0.5">{t.description}</p>}
                        </div>
                      )}
                    </td>
                    <td className="px-5 py-2.5 text-gray-500 tabular-nums">{formatBytes(t.fileSize)}</td>
                    <td className="px-5 py-2.5 text-gray-500">{t.mergeFields.length} fields</td>
                    <td className="px-5 py-2.5 text-gray-500">{formatDate(t.uploadedAt)}</td>
                    <td className="px-5 py-2.5">
                      <div className="flex items-center gap-2">
                        <button
                          onClick={() => { setEditingId(t.id); setEditName(t.name); }}
                          className="text-xs text-indigo-600 hover:text-indigo-800 font-medium"
                        >
                          Rename
                        </button>
                        <button
                          onClick={() => handleDelete(t.id, t.name)}
                          className="text-xs text-red-600 hover:text-red-800 font-medium"
                        >
                          Delete
                        </button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>

        {/* Footer */}
        <div className="flex justify-end px-5 py-3 bg-gray-50 border-t border-gray-200">
          <button
            onClick={onClose}
            className="px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-lg hover:bg-gray-50"
          >
            Close
          </button>
        </div>
      </div>
    </div>
  );
}
