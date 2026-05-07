import { useEffect, useState } from 'react';
import {
  onboarding,
  type NarrativeSeedDocumentDto,
} from '../api/onboardingApi';

interface Step7NarrativeSeedsProps {
  onComplete?: () => void;
}

export default function Step7NarrativeSeeds({ onComplete }: Step7NarrativeSeedsProps) {
  const [rows, setRows] = useState<NarrativeSeedDocumentDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [label, setLabel] = useState('');
  const [tagsRaw, setTagsRaw] = useState('');
  const [file, setFile] = useState<File | null>(null);

  async function refresh() {
    setLoading(true);
    setError(null);
    try {
      setRows(await onboarding.listNarrativeSeeds());
    } catch (e: unknown) {
      setError((e as Error).message ?? 'Failed to list narrative seeds.');
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void refresh();
  }, []);

  async function handleUpload(e: React.FormEvent) {
    e.preventDefault();
    if (!file || !label.trim()) return;
    setBusy(true);
    setError(null);
    try {
      const tags = tagsRaw
        .split(',')
        .map(t => t.trim())
        .filter(Boolean);
      await onboarding.uploadNarrativeSeed(file, label.trim(), tags);
      setLabel('');
      setTagsRaw('');
      setFile(null);
      await refresh();
    } catch (err: unknown) {
      setError((err as Error).message ?? 'Upload failed.');
    } finally {
      setBusy(false);
    }
  }

  async function handleDelete(id: string, indexed: boolean) {
    setBusy(true);
    try {
      let confirmed = false;
      if (indexed) {
        confirmed = window.confirm(
          'This document is indexed and may be cited by generated narratives. Confirm deletion?',
        );
        if (!confirmed) {
          setBusy(false);
          return;
        }
      }
      await onboarding.deleteNarrativeSeed(id, confirmed);
      await refresh();
    } catch (e: unknown) {
      setError((e as Error).message ?? 'Delete failed.');
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="space-y-6">
      <div>
        <h2 className="text-2xl font-semibold mb-2">Step 7 — Narrative Seed Documents</h2>
        <p className="text-gray-600">
          Upload reference documents (existing SSPs, ConOps narratives, mission descriptions) so
          the assistant can cite them when drafting control narratives. Files are stored in the
          evidence repository and indexed in the background.
        </p>
      </div>

      {error && (
        <div className="rounded border border-red-200 bg-red-50 p-3 text-sm text-red-800">
          {error}
        </div>
      )}

      <form onSubmit={handleUpload} className="rounded border border-gray-200 p-4 space-y-3">
        <h3 className="font-semibold">Upload a new seed document</h3>
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
          <input
            className="rounded border border-gray-300 px-2 py-1 text-sm"
            placeholder="Label (e.g. 'Legacy SSP — XYZ system')"
            value={label}
            onChange={(e) => setLabel(e.target.value)}
            required
          />
          <input
            className="rounded border border-gray-300 px-2 py-1 text-sm"
            placeholder="Tags (comma-separated)"
            value={tagsRaw}
            onChange={(e) => setTagsRaw(e.target.value)}
          />
          <input
            className="text-sm sm:col-span-2"
            type="file"
            onChange={(e) => setFile(e.target.files?.[0] ?? null)}
          />
        </div>
        <div className="flex justify-end">
          <button
            type="submit"
            disabled={busy || !file || !label.trim()}
            className="rounded bg-indigo-600 px-3 py-1 text-sm text-white disabled:opacity-50"
          >
            Upload
          </button>
        </div>
      </form>

      <section>
        <h3 className="font-semibold mb-2">Uploaded seed documents</h3>
        {loading && <div className="text-gray-500">Loading…</div>}
        {!loading && rows.length === 0 && (
          <div className="text-sm text-gray-500">No seed documents uploaded yet.</div>
        )}
        <ul className="space-y-2">
          {rows.map((r) => {
            const indexed = r.indexingStatus === 'Indexed';
            return (
              <li key={r.id} className="flex items-center justify-between rounded bg-gray-50 px-3 py-2 text-sm">
                <div>
                  <div className="font-medium">{r.label}</div>
                  <div className="text-xs text-gray-500">
                    {r.indexingStatus} · tags: {tryFormatTags(r.tags)}
                  </div>
                </div>
                <button
                  type="button"
                  disabled={busy}
                  onClick={() => void handleDelete(r.id, indexed)}
                  className="rounded border border-red-300 px-2 py-1 text-xs text-red-700 hover:bg-red-50"
                >
                  Delete
                </button>
              </li>
            );
          })}
        </ul>
      </section>

      <div className="flex justify-end gap-2">
        <button
          type="button"
          onClick={onComplete}
          className="rounded bg-emerald-600 px-4 py-2 text-white hover:bg-emerald-700"
        >
          Finish onboarding
        </button>
      </div>
    </div>
  );
}

function tryFormatTags(raw: string): string {
  try {
    const parsed = JSON.parse(raw);
    if (Array.isArray(parsed)) return parsed.length ? parsed.join(', ') : '(none)';
  } catch {
    // ignore
  }
  return raw || '(none)';
}
