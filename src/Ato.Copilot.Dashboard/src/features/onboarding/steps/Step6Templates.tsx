import { useEffect, useState } from 'react';
import {
  onboarding,
  type OrganizationDocumentTemplateDto,
  type TemplateType,
} from '../api/onboardingApi';

const SLOTS: { type: TemplateType; label: string; accept: string }[] = [
  { type: 'Ssp', label: 'SSP — System Security Plan', accept: '.docx' },
  { type: 'Sar', label: 'SAR — Security Assessment Report', accept: '.docx' },
  { type: 'Sap', label: 'SAP — Security Assessment Plan', accept: '.docx' },
  { type: 'Crm', label: 'CRM — Control Responsibility Matrix', accept: '.xlsx' },
  { type: 'HwSwInventory', label: 'HW/SW Inventory', accept: '.xlsx' },
];

interface Step6TemplatesProps {
  onSaved?: () => void;
}

export default function Step6Templates({ onSaved }: Step6TemplatesProps) {
  const [rows, setRows] = useState<OrganizationDocumentTemplateDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);

  async function refresh() {
    setLoading(true);
    setError(null);
    try {
      const all = await onboarding.listTemplates();
      setRows(all);
    } catch (e: unknown) {
      setError((e as Error).message ?? 'Failed to load templates.');
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void refresh();
  }, []);

  async function handleUpload(slot: TemplateType, file: File, label: string, version: string, makeDefault: boolean) {
    setBusy(slot);
    setError(null);
    try {
      const res = await onboarding.uploadTemplate({
        templateType: slot, label, version, file, isDefault: makeDefault,
      });
      if (res.warnings.length > 0) {
        setError(`Template uploaded with ${res.warnings.length} warning(s):\n${res.warnings.join('\n')}`);
      }
      await refresh();
    } catch (e: unknown) {
      setError((e as Error).message ?? 'Upload failed.');
    } finally {
      setBusy(null);
    }
  }

  async function handleMarkDefault(id: string) {
    setBusy(id);
    try {
      await onboarding.markTemplateDefault(id);
      await refresh();
    } catch (e: unknown) {
      setError((e as Error).message ?? 'Mark-default failed.');
    } finally {
      setBusy(null);
    }
  }

  async function handleDelete(id: string) {
    setBusy(id);
    try {
      await onboarding.deleteTemplate(id);
      await refresh();
    } catch (e: unknown) {
      setError((e as Error).message ?? 'Delete failed.');
    } finally {
      setBusy(null);
    }
  }

  return (
    <div className="space-y-6">
      <div>
        <h2 className="text-2xl font-semibold mb-2">Step 4 — Custom Document Templates</h2>
        <p className="text-gray-600">
          Upload your organization's branded SSP / SAR / SAP / CRM / Inventory templates.
          Mark one per type as <strong>default</strong> so wizard exports use it automatically.
          Replacing a template flags any rendered exports that derive from it as stale.
        </p>
      </div>

      {error && (
        <div className="rounded border border-red-200 bg-red-50 p-3 text-sm text-red-800 whitespace-pre-line">
          {error}
        </div>
      )}

      {loading && <div className="text-gray-500">Loading…</div>}

      {!loading && SLOTS.map((slot) => {
        const ofType = rows.filter(r => r.templateType === slot.type && r.status !== 'Deleted');
        const def = ofType.find(r => r.isDefault);
        return (
          <section key={slot.type} className="rounded border border-gray-200 p-4">
            <header className="flex items-center justify-between mb-3">
              <h3 className="font-semibold">{slot.label}</h3>
              <span className="text-xs text-gray-500">
                {ofType.length} uploaded · default: {def ? def.label : '(built-in)'}
              </span>
            </header>

            {ofType.length === 0 && (
              <div className="text-sm text-gray-500">No templates uploaded yet.</div>
            )}

            <ul className="space-y-2">
              {ofType.map((r) => (
                <li key={r.id} className="flex items-center justify-between rounded bg-gray-50 px-3 py-2 text-sm">
                  <div>
                    <div className="font-medium">{r.label}{r.isDefault && <span className="ml-2 rounded bg-emerald-100 px-2 py-0.5 text-emerald-800 text-xs">DEFAULT</span>}</div>
                    <div className="text-xs text-gray-500">
                      {r.originalFileName} · {(r.fileSizeBytes / 1024).toFixed(0)} KB · {r.validationStatus}
                    </div>
                  </div>
                  <div className="flex gap-2">
                    {!r.isDefault && (
                      <button
                        type="button"
                        disabled={busy === r.id}
                        onClick={() => void handleMarkDefault(r.id)}
                        className="rounded border border-emerald-300 px-2 py-1 text-xs text-emerald-700 hover:bg-emerald-50"
                      >
                        Mark default
                      </button>
                    )}
                    <button
                      type="button"
                      disabled={busy === r.id || r.isDefault}
                      onClick={() => void handleDelete(r.id)}
                      className="rounded border border-red-300 px-2 py-1 text-xs text-red-700 hover:bg-red-50 disabled:opacity-40"
                      title={r.isDefault ? 'Demote first' : ''}
                    >
                      Delete
                    </button>
                  </div>
                </li>
              ))}
            </ul>

            <UploadForm
              slot={slot.type}
              accept={slot.accept}
              busy={busy === slot.type}
              onUpload={handleUpload}
            />
          </section>
        );
      })}

      <div className="flex justify-end">
        <button
          type="button"
          onClick={onSaved}
          className="rounded bg-indigo-600 px-4 py-2 text-white hover:bg-indigo-700"
        >
          Continue
        </button>
      </div>
    </div>
  );
}

function UploadForm(props: {
  slot: TemplateType;
  accept: string;
  busy: boolean;
  onUpload: (slot: TemplateType, file: File, label: string, version: string, isDefault: boolean) => void | Promise<void>;
}) {
  const [label, setLabel] = useState('');
  const [version, setVersion] = useState('v1.0');
  const [isDefault, setIsDefault] = useState(false);
  const [file, setFile] = useState<File | null>(null);
  return (
    <form
      className="mt-3 grid grid-cols-1 sm:grid-cols-5 gap-2 items-end"
      onSubmit={(e) => {
        e.preventDefault();
        if (!file || !label.trim()) return;
        void props.onUpload(props.slot, file, label.trim(), version.trim(), isDefault);
        setFile(null);
        setLabel('');
      }}
    >
      <input
        className="rounded border border-gray-300 px-2 py-1 text-sm"
        placeholder="Label"
        value={label}
        onChange={(e) => setLabel(e.target.value)}
        required
      />
      <input
        className="rounded border border-gray-300 px-2 py-1 text-sm"
        placeholder="Version"
        value={version}
        onChange={(e) => setVersion(e.target.value)}
      />
      <input
        className="text-sm"
        type="file"
        accept={props.accept}
        onChange={(e) => setFile(e.target.files?.[0] ?? null)}
      />
      <label className="flex items-center gap-1 text-xs">
        <input type="checkbox" checked={isDefault} onChange={(e) => setIsDefault(e.target.checked)} />
        Mark as default
      </label>
      <button
        type="submit"
        disabled={props.busy || !file || !label.trim()}
        className="rounded bg-indigo-600 px-3 py-1 text-sm text-white disabled:opacity-50"
      >
        Upload
      </button>
    </form>
  );
}
