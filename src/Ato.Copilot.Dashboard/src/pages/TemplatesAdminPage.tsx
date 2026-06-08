import { useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import {
  onboarding,
  type OrganizationDocumentTemplateDto,
  type NarrativeSeedDocumentDto,
  type TemplateType,
} from '../features/onboarding/api/onboardingApi';

// ─── Template slot definitions ───────────────────────────────────────────────

const SLOTS: { type: TemplateType; label: string; accept: string }[] = [
  { type: 'Ssp',          label: 'SSP — System Security Plan',          accept: '.docx' },
  { type: 'Sar',          label: 'SAR — Security Assessment Report',     accept: '.docx' },
  { type: 'Sap',          label: 'SAP — Security Assessment Plan',       accept: '.docx' },
  { type: 'Crm',          label: 'CRM — Control Responsibility Matrix',  accept: '.xlsx' },
  { type: 'HwSwInventory',label: 'HW/SW Inventory',                      accept: '.xlsx' },
];

// ─── Tag formatting helper ────────────────────────────────────────────────────

function tryFormatTags(raw: string): string {
  try {
    const parsed = JSON.parse(raw);
    if (Array.isArray(parsed)) return parsed.length ? parsed.join(', ') : '(none)';
  } catch {
    // ignore
  }
  return raw || '(none)';
}

// ─── Indexing status badge ────────────────────────────────────────────────────

function IndexingBadge({ status }: { status: string }) {
  const cls =
    status === 'Indexed'
      ? 'bg-emerald-100 text-emerald-800'
      : status === 'Failed'
      ? 'bg-red-100 text-red-800'
      : 'bg-amber-100 text-amber-800';
  return (
    <span className={`rounded px-2 py-0.5 text-xs font-medium ${cls}`}>{status}</span>
  );
}

// ─── Template upload form ─────────────────────────────────────────────────────

function TemplateUploadForm(props: {
  slot: TemplateType;
  accept: string;
  busy: boolean;
  onUpload: (
    slot: TemplateType,
    file: File,
    label: string,
    version: string,
    isDefault: boolean,
  ) => void | Promise<void>;
}) {
  const [label, setLabel] = useState('');
  const [version, setVersion] = useState('v1.0');
  const [isDefault, setIsDefault] = useState(false);
  const [file, setFile] = useState<File | null>(null);

  return (
    <form
      className="mt-3 grid grid-cols-1 sm:grid-cols-5 gap-2 items-end border-t border-gray-100 pt-3"
      onSubmit={(e) => {
        e.preventDefault();
        if (!file || !label.trim()) return;
        void props.onUpload(props.slot, file, label.trim(), version.trim(), isDefault);
        setFile(null);
        setLabel('');
      }}
    >
      <input
        className="rounded border border-gray-300 px-2 py-1 text-sm col-span-1"
        placeholder="Label"
        value={label}
        onChange={(e) => setLabel(e.target.value)}
        required
      />
      <input
        className="rounded border border-gray-300 px-2 py-1 text-sm col-span-1"
        placeholder="Version (e.g. v1.0)"
        value={version}
        onChange={(e) => setVersion(e.target.value)}
      />
      <input
        className="text-sm col-span-1"
        type="file"
        accept={props.accept}
        onChange={(e) => {
          const picked = e.target.files?.[0] ?? null;
          // T011: client-side 50 MB cap before upload attempt
          if (picked && picked.size > 50 * 1024 * 1024) {
            alert('File exceeds the 50 MB limit. Please choose a smaller file.');
            e.target.value = '';
            return;
          }
          setFile(picked);
        }}
      />
      <label className="flex items-center gap-1 text-xs col-span-1">
        <input
          type="checkbox"
          checked={isDefault}
          onChange={(e) => setIsDefault(e.target.checked)}
        />
        Mark as default
      </label>
      <button
        type="submit"
        disabled={props.busy || !file || !label.trim()}
        className="rounded bg-indigo-600 px-3 py-1.5 text-sm text-white hover:bg-indigo-700 disabled:opacity-50 col-span-1"
      >
        {props.busy ? 'Uploading…' : 'Upload'}
      </button>
    </form>
  );
}

// ─── Main page ────────────────────────────────────────────────────────────────

export default function TemplatesAdminPage() {
  // ── Templates state ───────────────────────────────────────────────────────
  const [templates, setTemplates] = useState<OrganizationDocumentTemplateDto[]>([]);
  const [templatesLoading, setTemplatesLoading] = useState(true);
  const [templatesError, setTemplatesError] = useState<string | null>(null);
  const [templateBusy, setTemplateBusy] = useState<string | null>(null);

  // ── Seeds state ───────────────────────────────────────────────────────────
  const [seeds, setSeeds] = useState<NarrativeSeedDocumentDto[]>([]);
  const [seedsLoading, setSeedsLoading] = useState(true);
  const [seedsError, setSeedsError] = useState<string | null>(null);
  const [seedBusy, setSeedBusy] = useState(false);

  // ── Seed upload form state ────────────────────────────────────────────────
  const [seedLabel, setSeedLabel] = useState('');
  const [seedTagsRaw, setSeedTagsRaw] = useState('');
  const [seedFile, setSeedFile] = useState<File | null>(null);

  // T016: Polling ref for Pending seeds
  const pollTimerRef = useRef<ReturnType<typeof setInterval> | null>(null);

  // ─── Template helpers ───────────────────────────────────────────────────

  async function refreshTemplates() {
    setTemplatesLoading(true);
    setTemplatesError(null);
    try {
      setTemplates(await onboarding.listTemplates());
    } catch (e: unknown) {
      setTemplatesError((e as Error).message ?? 'Failed to load templates.');
    } finally {
      setTemplatesLoading(false);
    }
  }

  async function handleTemplateUpload(
    slot: TemplateType,
    file: File,
    label: string,
    version: string,
    makeDefault: boolean,
  ) {
    setTemplateBusy(slot);
    setTemplatesError(null);
    try {
      const res = await onboarding.uploadTemplate({
        templateType: slot,
        label,
        version,
        file,
        isDefault: makeDefault,
      });
      if (res.warnings.length > 0) {
        setTemplatesError(
          `Template uploaded with ${res.warnings.length} warning(s):\n${res.warnings.join('\n')}`,
        );
      }
      await refreshTemplates();
    } catch (e: unknown) {
      setTemplatesError((e as Error).message ?? 'Upload failed.');
    } finally {
      setTemplateBusy(null);
    }
  }

  async function handleMarkDefault(id: string) {
    setTemplateBusy(id);
    setTemplatesError(null);
    try {
      await onboarding.markTemplateDefault(id);
      await refreshTemplates();
    } catch (e: unknown) {
      setTemplatesError((e as Error).message ?? 'Mark-default failed.');
    } finally {
      setTemplateBusy(null);
    }
  }

  async function handleClearDefault(id: string) {
    setTemplateBusy(id);
    setTemplatesError(null);
    try {
      await onboarding.clearTemplateDefault(id);
      await refreshTemplates();
    } catch (e: unknown) {
      setTemplatesError((e as Error).message ?? 'Clear-default failed.');
    } finally {
      setTemplateBusy(null);
    }
  }

  async function handleTemplateDelete(id: string) {
    if (!window.confirm('Delete this template? This cannot be undone.')) return;
    setTemplateBusy(id);
    setTemplatesError(null);
    try {
      await onboarding.deleteTemplate(id);
      await refreshTemplates();
    } catch (e: unknown) {
      setTemplatesError((e as Error).message ?? 'Delete failed.');
    } finally {
      setTemplateBusy(null);
    }
  }

  async function handleTemplateDownload(tpl: OrganizationDocumentTemplateDto) {
    // Open the blob key URL or fall back to a simple anchor download via the API
    const url = `/api/onboarding/templates/${tpl.id}/download`;
    const a = document.createElement('a');
    a.href = url;
    a.download = tpl.originalFileName;
    a.click();
  }

  // ─── Seed helpers ─────────────────────────────────────────────────────────

  async function refreshSeeds(): Promise<NarrativeSeedDocumentDto[]> {
    setSeedsLoading(true);
    setSeedsError(null);
    try {
      const data = await onboarding.listNarrativeSeeds();
      setSeeds(data);
      return data;
    } catch (e: unknown) {
      setSeedsError((e as Error).message ?? 'Failed to list narrative seeds.');
      return [];
    } finally {
      setSeedsLoading(false);
    }
  }

  async function handleSeedUpload(e: React.FormEvent) {
    e.preventDefault();
    if (!seedFile || !seedLabel.trim()) return;
    setSeedBusy(true);
    setSeedsError(null);
    try {
      const tags = seedTagsRaw
        .split(',')
        .map((t) => t.trim())
        .filter(Boolean);
      await onboarding.uploadNarrativeSeed(seedFile, seedLabel.trim(), tags);
      setSeedLabel('');
      setSeedTagsRaw('');
      setSeedFile(null);
      await refreshSeeds();
    } catch (err: unknown) {
      setSeedsError((err as Error).message ?? 'Upload failed.');
    } finally {
      setSeedBusy(false);
    }
  }

  async function handleSeedDelete(id: string, indexed: boolean) {
    setSeedBusy(true);
    try {
      let confirmed = false;
      if (indexed) {
        confirmed = window.confirm(
          'This document is indexed and may be cited by generated narratives. Confirm deletion?',
        );
        if (!confirmed) {
          setSeedBusy(false);
          return;
        }
      }
      await onboarding.deleteNarrativeSeed(id, confirmed);
      await refreshSeeds();
    } catch (e: unknown) {
      setSeedsError((e as Error).message ?? 'Delete failed.');
    } finally {
      setSeedBusy(false);
    }
  }

  // ─── Initial load ─────────────────────────────────────────────────────────

  // T016: Initial load + poll every 5s while any seed is Pending; stop when all leave Pending
  useEffect(() => {
    void refreshTemplates();

    const runPoll = async () => {
      const initial = await refreshSeeds();
      if (!initial.some((s) => s.indexingStatus === 'Pending')) return;

      pollTimerRef.current = setInterval(async () => {
        const latest = await refreshSeeds();
        if (!latest.some((s) => s.indexingStatus === 'Pending') && pollTimerRef.current !== null) {
          clearInterval(pollTimerRef.current);
          pollTimerRef.current = null;
        }
      }, 5000);
    };
    void runPoll();

    return () => {
      if (pollTimerRef.current !== null) {
        clearInterval(pollTimerRef.current);
      }
    };
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // ─── Render ───────────────────────────────────────────────────────────────

  return (
    <div className="p-6 space-y-8 max-w-6xl mx-auto">
      {/* ── Page header ─────────────────────────────────────────────────── */}
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-semibold">Templates &amp; Narrative Seeds</h1>
          <p className="mt-1 text-sm text-gray-600">
            Manage organization document templates and narrative seed documents.{' '}
            <Link
              to="/admin/imported-documents"
              className="text-indigo-600 hover:underline"
            >
              View all imported documents →
            </Link>
          </p>
        </div>
      </div>

      {/* ══════════════════════════════════════════════════════════════════ */}
      {/* Section 1 — Templates                                            */}
      {/* ══════════════════════════════════════════════════════════════════ */}
      <section className="space-y-4">
        <div className="border-b border-gray-200 pb-2">
          <h2 className="text-xl font-semibold">Document Templates</h2>
          <p className="text-sm text-gray-500 mt-0.5">
            Upload branded SSP / SAR / SAP / CRM / Inventory templates. Mark one per type as
            default so wizard exports use it automatically.
          </p>
        </div>

        {templatesError && (
          <div className="rounded border border-red-200 bg-red-50 p-3 text-sm text-red-800 whitespace-pre-line">
            {templatesError}
          </div>
        )}

        {templatesLoading && (
          <div className="text-sm text-gray-500">Loading templates…</div>
        )}

        {!templatesLoading && (
          <div className="space-y-4">
            {SLOTS.map((slot) => {
              const ofType = templates.filter(
                (r) => r.templateType === slot.type && r.status !== 'Deleted',
              );
              const def = ofType.find((r) => r.isDefault);

              return (
                <div
                  key={slot.type}
                  className="rounded-lg border border-gray-200 bg-white p-4 shadow-sm"
                >
                  {/* Slot header */}
                  <div className="flex items-center justify-between mb-3">
                    <h3 className="font-semibold text-gray-900">{slot.label}</h3>
                    <span className="text-xs text-gray-400">
                      {ofType.length} uploaded · default:{' '}
                      {def ? (
                        <span className="text-emerald-700 font-medium">{def.label}</span>
                      ) : (
                        <span className="italic">(built-in)</span>
                      )}
                    </span>
                  </div>

                  {/* Template rows */}
                  {ofType.length === 0 ? (
                    <div className="text-sm text-gray-400 italic">Not uploaded</div>
                  ) : (
                    <ul className="space-y-2 mb-2">
                      {ofType.map((r) => (
                        <li
                          key={r.id}
                          className="flex items-center justify-between rounded bg-gray-50 px-3 py-2 text-sm"
                        >
                          <div className="min-w-0 flex-1">
                            <div className="font-medium truncate">
                              {r.label}
                              {r.isDefault && (
                                <span className="ml-2 rounded bg-emerald-100 px-2 py-0.5 text-emerald-800 text-xs">
                                  DEFAULT
                                </span>
                              )}
                            </div>
                            <div className="text-xs text-gray-500 truncate">
                              {r.originalFileName} ·{' '}
                              {(r.fileSizeBytes / 1024).toFixed(0)} KB ·{' '}
                              v{r.version} · {r.validationStatus} ·{' '}
                              {new Date(r.createdAt).toLocaleDateString()}
                            </div>
                          </div>
                          <div className="flex gap-1.5 ml-3 shrink-0">
                            {/* Download */}
                            <button
                              type="button"
                              onClick={() => void handleTemplateDownload(r)}
                              className="rounded border border-gray-300 px-2 py-1 text-xs text-gray-700 hover:bg-gray-100"
                            >
                              Download
                            </button>
                            {/* Set Default / Clear Default */}
                            {r.isDefault ? (
                              <button
                                type="button"
                                disabled={templateBusy === r.id}
                                onClick={() => void handleClearDefault(r.id)}
                                className="rounded border border-amber-300 px-2 py-1 text-xs text-amber-700 hover:bg-amber-50 disabled:opacity-40"
                              >
                                Clear Default
                              </button>
                            ) : (
                              <button
                                type="button"
                                disabled={templateBusy === r.id}
                                onClick={() => void handleMarkDefault(r.id)}
                                className="rounded border border-emerald-300 px-2 py-1 text-xs text-emerald-700 hover:bg-emerald-50 disabled:opacity-40"
                              >
                                Set Default
                              </button>
                            )}
                            {/* Delete */}
                            <button
                              type="button"
                              disabled={templateBusy === r.id || r.isDefault}
                              onClick={() => void handleTemplateDelete(r.id)}
                              className="rounded border border-red-300 px-2 py-1 text-xs text-red-700 hover:bg-red-50 disabled:opacity-40"
                              title={r.isDefault ? 'Clear default first before deleting' : ''}
                            >
                              Delete
                            </button>
                          </div>
                        </li>
                      ))}
                    </ul>
                  )}

                  {/* Upload form for this slot */}
                  <TemplateUploadForm
                    slot={slot.type}
                    accept={slot.accept}
                    busy={templateBusy === slot.type}
                    onUpload={handleTemplateUpload}
                  />
                </div>
              );
            })}
          </div>
        )}
      </section>

      {/* ══════════════════════════════════════════════════════════════════ */}
      {/* Section 2 — Narrative Seeds                                       */}
      {/* ══════════════════════════════════════════════════════════════════ */}
      <section className="space-y-4">
        <div className="border-b border-gray-200 pb-2">
          <h2 className="text-xl font-semibold">Narrative Seed Documents</h2>
          <p className="text-sm text-gray-500 mt-0.5">
            Upload reference documents (existing SSPs, ConOps narratives, mission descriptions) so
            the assistant can cite them when drafting control narratives. Files are indexed in the
            background.
          </p>
        </div>

        {seedsError && (
          <div className="rounded border border-red-200 bg-red-50 p-3 text-sm text-red-800">
            {seedsError}
          </div>
        )}

        {/* Upload form */}
        <div className="rounded-lg border border-gray-200 bg-white p-4 shadow-sm">
          <h3 className="font-semibold mb-3">Upload a new seed document</h3>
          <form onSubmit={(e) => void handleSeedUpload(e)} className="space-y-3">
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
              <input
                className="rounded border border-gray-300 px-2 py-1.5 text-sm"
                placeholder="Label (e.g. 'Legacy SSP — XYZ system')"
                value={seedLabel}
                onChange={(e) => setSeedLabel(e.target.value)}
                required
              />
              <input
                className="rounded border border-gray-300 px-2 py-1.5 text-sm"
                placeholder="Tags (comma-separated)"
                value={seedTagsRaw}
                onChange={(e) => setSeedTagsRaw(e.target.value)}
              />
              <input
                className="text-sm sm:col-span-2"
                type="file"
                onChange={(e) => {
          const picked = e.target.files?.[0] ?? null;
          // T011: client-side 50 MB cap for seed documents
          if (picked && picked.size > 50 * 1024 * 1024) {
            alert('File exceeds the 50 MB limit. Please choose a smaller file.');
            e.target.value = '';
            return;
          }
          setSeedFile(picked);
        }}
              />
            </div>
            <div className="flex justify-end">
              <button
                type="submit"
                disabled={seedBusy || !seedFile || !seedLabel.trim()}
                className="rounded bg-indigo-600 px-4 py-1.5 text-sm font-medium text-white hover:bg-indigo-700 disabled:opacity-50"
              >
                {seedBusy ? 'Uploading…' : 'Upload Seed'}
              </button>
            </div>
          </form>
        </div>

        {/* Seed list */}
        <div className="rounded-lg border border-gray-200 bg-white shadow-sm">
          <div className="px-4 py-3 border-b border-gray-100">
            <h3 className="font-semibold text-gray-900">Uploaded seed documents</h3>
          </div>

          {seedsLoading && (
            <div className="px-4 py-4 text-sm text-gray-500">Loading…</div>
          )}

          {!seedsLoading && seeds.length === 0 && (
            <div className="px-4 py-4 text-sm text-gray-400 italic">
              No seed documents uploaded yet.
            </div>
          )}

          {!seedsLoading && seeds.length > 0 && (
            <ul className="divide-y divide-gray-100">
              {seeds.map((r) => {
                const indexed = r.indexingStatus === 'Indexed';
                return (
                  <li
                    key={r.id}
                    className="flex items-center justify-between px-4 py-3 text-sm"
                  >
                    <div className="min-w-0 flex-1">
                      <div className="font-medium text-gray-900 truncate">
                        {r.label}
                      </div>
                      <div className="text-xs text-gray-500 mt-0.5 flex items-center gap-2">
                        <IndexingBadge status={r.indexingStatus} />
                        <span>tags: {tryFormatTags(r.tags)}</span>
                        <span className="text-gray-400">
                          {new Date(r.createdAt).toLocaleDateString()}
                        </span>
                      </div>
                    </div>
                    <button
                      type="button"
                      disabled={seedBusy}
                      onClick={() => void handleSeedDelete(r.id, indexed)}
                      className="ml-4 shrink-0 rounded border border-red-300 px-2 py-1 text-xs text-red-700 hover:bg-red-50 disabled:opacity-40"
                    >
                      Delete
                    </button>
                  </li>
                );
              })}
            </ul>
          )}
        </div>
      </section>
    </div>
  );
}
