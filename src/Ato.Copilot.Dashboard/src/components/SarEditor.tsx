import { useState, useEffect, useCallback } from 'react';
import {
  createSar,
  getSar,
  editSarSection,
  submitSarForReview,
  reviewSar,
  exportSarWord,
} from '../api/sar';
import type { SarResponse, SarSectionSummary } from '../api/sar';

interface SarEditorProps {
  systemId: string;
  sarId?: string;
  onClose: () => void;
  onSarCreated?: () => void;
}

type EditorTab = 'overview' | 'sections' | 'review';

export default function SarEditor({ systemId, sarId, onClose, onSarCreated }: SarEditorProps) {
  const [sar, setSar] = useState<SarResponse | null>(null);
  const [activeTab, setActiveTab] = useState<EditorTab>('overview');
  const [selectedSection, setSelectedSection] = useState<string | null>(null);
  const [sectionContent, setSectionContent] = useState('');
  const [title, setTitle] = useState('');
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [reviewComments, setReviewComments] = useState('');

  // Load SAR if sarId provided
  useEffect(() => {
    if (sarId) {
      setLoading(true);
      getSar(systemId, sarId)
        .then(setSar)
        .catch((err: unknown) => setError(err instanceof Error ? err.message : 'Failed to load SAR'))
        .finally(() => setLoading(false));
    }
  }, [systemId, sarId]);

  // Escape key handler
  useEffect(() => {
    const handleEsc = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
    document.addEventListener('keydown', handleEsc);
    return () => document.removeEventListener('keydown', handleEsc);
  }, [onClose]);

  const handleCreate = useCallback(async () => {
    if (!title.trim()) return;
    setSaving(true);
    setError(null);
    try {
      const newSar = await createSar(systemId, { title: title.trim() });
      setSar(newSar);
      onSarCreated?.();
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Failed to create SAR');
    } finally {
      setSaving(false);
    }
  }, [systemId, title, onSarCreated]);

  const handleSaveSection = useCallback(async () => {
    if (!sar || !selectedSection) return;
    setSaving(true);
    setError(null);
    try {
      await editSarSection(systemId, sar.sarId, selectedSection, sectionContent);
      const updated = await getSar(systemId, sar.sarId);
      setSar(updated);
      setSelectedSection(null);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Failed to save section');
    } finally {
      setSaving(false);
    }
  }, [systemId, sar, selectedSection, sectionContent]);

  const handleSubmit = useCallback(async () => {
    if (!sar) return;
    setSaving(true);
    setError(null);
    try {
      const updated = await submitSarForReview(systemId, sar.sarId);
      setSar(updated);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Failed to submit SAR');
    } finally {
      setSaving(false);
    }
  }, [systemId, sar]);

  const handleReview = useCallback(async (decision: 'approve' | 'request_revision') => {
    if (!sar) return;
    setSaving(true);
    setError(null);
    try {
      const updated = await reviewSar(systemId, sar.sarId, { decision, comments: reviewComments || undefined });
      setSar(updated);
      setReviewComments('');
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Review action failed');
    } finally {
      setSaving(false);
    }
  }, [systemId, sar, reviewComments]);

  const handleExport = useCallback(async () => {
    if (!sar) return;
    try {
      const blob = await exportSarWord(systemId, sar.sarId);
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `sar-${sar.sarId.slice(0, 8)}.docx`;
      a.click();
      URL.revokeObjectURL(url);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Export failed');
    }
  }, [systemId, sar]);

  const statusColor = (status: string) => {
    switch (status) {
      case 'Approved': return 'bg-green-100 text-green-800';
      case 'UnderReview': return 'bg-yellow-100 text-yellow-800';
      case 'Draft': return 'bg-indigo-100 text-indigo-800';
      default: return 'bg-gray-100 text-gray-800';
    }
  };

  const isEditable = sar?.status === 'Draft' || sar?.status === 'NotStarted';

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 backdrop-blur-sm" onClick={(e) => { if (e.target === e.currentTarget) onClose(); }}>
      <div className="w-full max-w-4xl max-h-[90vh] rounded-xl bg-white shadow-2xl border border-gray-200 flex flex-col overflow-hidden" role="dialog" aria-labelledby="sar-editor-title">

        {/* Header */}
        <div className="flex items-center justify-between px-6 py-4 border-b border-gray-200">
          <div>
            <h2 id="sar-editor-title" className="text-lg font-semibold text-gray-900">
              {sar ? sar.title : 'Create Security Assessment Report'}
            </h2>
            {sar && (
              <span className={`inline-block mt-1 px-2 py-0.5 rounded-full text-xs font-medium ${statusColor(sar.status)}`}>
                {sar.status}
              </span>
            )}
          </div>
          <button onClick={onClose} className="text-gray-400 hover:text-gray-600 text-xl">&times;</button>
        </div>

        {/* Error */}
        {error && (
          <div className="mx-6 mt-4 p-3 rounded-lg bg-red-50 border border-red-200 text-sm text-red-700">
            {error}
          </div>
        )}

        {/* Body */}
        <div className="flex-1 overflow-y-auto p-6">
          {loading && <div className="text-center py-12 text-gray-500">Loading SAR...</div>}

          {/* Create form */}
          {!sar && !loading && (
            <div className="space-y-4">
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">SAR Title</label>
                <input
                  type="text"
                  value={title}
                  onChange={(e) => setTitle(e.target.value)}
                  placeholder="Security Assessment Report — System Name — FY26 Q2"
                  className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500"
                />
              </div>
              <button
                onClick={handleCreate}
                disabled={saving || !title.trim()}
                className="px-4 py-2 bg-indigo-600 text-white rounded-lg text-sm font-medium hover:bg-indigo-700 disabled:opacity-50"
              >
                {saving ? 'Creating...' : 'Generate SAR'}
              </button>
            </div>
          )}

          {/* SAR loaded */}
          {sar && !loading && (
            <>
              {/* Tab nav */}
              <div className="flex gap-1 mb-6 border-b border-gray-200">
                {(['overview', 'sections', 'review'] as EditorTab[]).map((tab) => (
                  <button
                    key={tab}
                    onClick={() => { setActiveTab(tab); setSelectedSection(null); }}
                    className={`px-4 py-2 text-sm font-medium rounded-t-lg ${activeTab === tab ? 'bg-indigo-50 text-indigo-700 border-b-2 border-indigo-600' : 'text-gray-500 hover:text-gray-700'}`}
                  >
                    {tab.charAt(0).toUpperCase() + tab.slice(1)}
                  </button>
                ))}
              </div>

              {/* Overview tab */}
              {activeTab === 'overview' && (
                <div className="space-y-4">
                  <div className="grid grid-cols-2 gap-4">
                    <Metric label="Controls Assessed" value={sar.totalControlsAssessed} />
                    <Metric label="Controls Pending" value={sar.totalControlsPending} />
                    <Metric label="Satisfied" value={sar.satisfiedCount} color="text-green-600" />
                    <Metric label="Not Satisfied" value={sar.notSatisfiedCount} color="text-red-600" />
                  </div>
                  <div className="text-sm text-gray-500 mt-4">
                    <p>Created by {sar.createdBy} on {new Date(sar.createdAt).toLocaleDateString()}</p>
                    {sar.approvedBy && <p>Approved by {sar.approvedBy} on {sar.approvedAt ? new Date(sar.approvedAt).toLocaleDateString() : 'N/A'}</p>}
                  </div>
                </div>
              )}

              {/* Sections tab */}
              {activeTab === 'sections' && !selectedSection && (
                <div className="space-y-2">
                  {sar.sections.map((sec: SarSectionSummary) => (
                    <div key={sec.sectionType} className="flex items-center justify-between p-3 rounded-lg border border-gray-200 hover:bg-gray-50">
                      <div>
                        <p className="text-sm font-medium text-gray-900">{sec.title}</p>
                        <p className="text-xs text-gray-500">
                          {sec.isAutoGenerated ? 'Auto-generated' : 'User-authored'} &middot;
                          {sec.hasContent ? ' Has content' : ' Empty'}
                        </p>
                      </div>
                      {isEditable && !['FindingsSummary', 'FindingDetails'].includes(sec.sectionType) && (
                        <button
                          onClick={() => { setSelectedSection(sec.sectionType); setSectionContent(''); }}
                          className="px-3 py-1 text-xs font-medium text-indigo-600 hover:bg-indigo-50 rounded-lg"
                        >
                          Edit
                        </button>
                      )}
                    </div>
                  ))}
                </div>
              )}

              {/* Section editor */}
              {activeTab === 'sections' && selectedSection && (
                <div className="space-y-4">
                  <div className="flex items-center justify-between">
                    <h3 className="text-sm font-semibold text-gray-900">
                      Editing: {sar.sections.find(s => s.sectionType === selectedSection)?.title}
                    </h3>
                    <button onClick={() => setSelectedSection(null)} className="text-sm text-gray-500 hover:text-gray-700">
                      &larr; Back to sections
                    </button>
                  </div>
                  <textarea
                    value={sectionContent}
                    onChange={(e) => setSectionContent(e.target.value)}
                    rows={15}
                    className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm font-mono focus:ring-2 focus:ring-indigo-500"
                    placeholder="Enter section content (Markdown supported)..."
                  />
                  <button
                    onClick={handleSaveSection}
                    disabled={saving}
                    className="px-4 py-2 bg-indigo-600 text-white rounded-lg text-sm font-medium hover:bg-indigo-700 disabled:opacity-50"
                  >
                    {saving ? 'Saving...' : 'Save Section'}
                  </button>
                </div>
              )}

              {/* Review tab */}
              {activeTab === 'review' && (
                <div className="space-y-4">
                  <div className="p-4 rounded-lg bg-gray-50 border border-gray-200 text-sm">
                    <p className="font-medium text-gray-900">Current Status: {sar.status}</p>
                    {sar.reviewedBy && <p className="text-gray-600 mt-1">Last reviewed by {sar.reviewedBy}</p>}
                  </div>

                  {sar.status === 'Draft' && (
                    <button onClick={handleSubmit} disabled={saving} className="px-4 py-2 bg-yellow-600 text-white rounded-lg text-sm font-medium hover:bg-yellow-700 disabled:opacity-50">
                      {saving ? 'Submitting...' : 'Submit for Review'}
                    </button>
                  )}

                  {sar.status === 'UnderReview' && (
                    <div className="space-y-3">
                      <textarea
                        value={reviewComments}
                        onChange={(e) => setReviewComments(e.target.value)}
                        rows={3}
                        className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm"
                        placeholder="Optional review comments..."
                      />
                      <div className="flex gap-3">
                        <button
                          onClick={() => handleReview('approve')}
                          disabled={saving}
                          className="px-4 py-2 bg-green-600 text-white rounded-lg text-sm font-medium hover:bg-green-700 disabled:opacity-50"
                        >
                          Approve
                        </button>
                        <button
                          onClick={() => handleReview('request_revision')}
                          disabled={saving}
                          className="px-4 py-2 bg-orange-600 text-white rounded-lg text-sm font-medium hover:bg-orange-700 disabled:opacity-50"
                        >
                          Request Revision
                        </button>
                      </div>
                    </div>
                  )}
                </div>
              )}
            </>
          )}
        </div>

        {/* Footer */}
        {sar && (
          <div className="flex items-center justify-between px-6 py-3 border-t border-gray-200 bg-gray-50">
            <button onClick={handleExport} className="px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-200 rounded-lg">
              Export as Word
            </button>
            <button onClick={onClose} className="px-4 py-2 text-sm font-medium text-gray-500 hover:text-gray-700">
              Close
            </button>
          </div>
        )}
      </div>
    </div>
  );
}

function Metric({ label, value, color = 'text-gray-900' }: { label: string; value: number; color?: string }) {
  return (
    <div className="p-3 rounded-lg bg-gray-50 border border-gray-200">
      <p className="text-xs text-gray-500">{label}</p>
      <p className={`text-xl font-semibold ${color}`}>{value}</p>
    </div>
  );
}
