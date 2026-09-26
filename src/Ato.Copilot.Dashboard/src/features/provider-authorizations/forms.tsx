import { useEffect, useRef, useState, type ReactNode } from 'react';
import { buttonClass, errorClass, inputClass, message, secondaryButtonClass } from '../workspace-operations/workspaceUi';
import { PackageImportError } from '../package-imports/request';
import type { AzureScope, Citation } from './types';

export function Field({ label, value, onChange, required = false, multiline = false, type = 'text', maxLength = 2000 }: {
  label: string; value: string; onChange: (value: string) => void; required?: boolean; multiline?: boolean; type?: string; maxLength?: number;
}) {
  return <label className="grid min-w-0 gap-1 text-sm">{label}{multiline
    ? <textarea className={inputClass} value={value} required={required} rows={3} maxLength={maxLength} onChange={event => onChange(event.target.value)} />
    : <input className={inputClass} type={type} value={value} required={required} maxLength={maxLength} onChange={event => onChange(event.target.value)} />}</label>;
}
export function Lines({ label, values, onChange }: { label: string; values: string[]; onChange: (values: string[]) => void }) {
  return <Field label={`${label} (one per line)`} value={values.join('\n')} multiline onChange={text => onChange(text.split('\n'))} maxLength={8000} />;
}
export const compact = (values: string[]) => values.map(value => value.trim()).filter(Boolean);

export function CitationFields({ value, onChange }: { value: Citation[]; onChange: (value: Citation[]) => void }) {
  return <fieldset className="space-y-3 rounded border border-slate-200 p-3"><legend className="text-sm font-semibold">Supporting source citations</legend>
    <p className="text-xs text-slate-600">Use exact retained source identities. A source reference does not grant download permission.</p>
    {value.map((citation, index) => <div key={index} className="grid gap-2 border-t pt-3 sm:grid-cols-2">
      {(['packageId', 'artifactId', 'archivePath', 'locator', 'quote'] as const).map(field =>
        <Field key={field} label={`${field} ${index + 1}`} value={citation[field]} required multiline={field === 'quote'}
          onChange={text => onChange(value.map((item, row) => row === index ? { ...item, [field]: text } : item))} />)}
      <button type="button" className={secondaryButtonClass} onClick={() => onChange(value.filter((_, row) => row !== index))}>Remove citation {index + 1}</button>
    </div>)}
    <button type="button" className={secondaryButtonClass} disabled={value.length >= 100}
      onClick={() => onChange([...value, { packageId: '', artifactId: '', archivePath: '', locator: '', quote: '' }])}>Add citation</button>
  </fieldset>;
}
export function ScopeFields({ label, value, onChange, maxItems = 100 }: { label: string; value: AzureScope[]; onChange: (value: AzureScope[]) => void; maxItems?: number }) {
  return <fieldset className="space-y-3 rounded border border-slate-200 p-3"><legend className="text-sm font-semibold">{label}</legend>
    {value.map((scope, index) => <div key={index} className="grid gap-2 border-t pt-3 sm:grid-cols-2">
      <label className="grid gap-1 text-sm">Cloud {index + 1}<select className={inputClass} value={scope.cloud} onChange={event => {
        const cloud = event.target.value === 'AzureCloud' ? 'AzureCloud' : 'AzureUSGovernment';
        onChange(value.map((item, row) => row === index ? { ...item, cloud } : item));
      }}><option value="AzureCloud">Azure Commercial</option><option value="AzureUSGovernment">Azure Government</option></select></label>
      {(['directoryTenantId', 'subscriptionId', 'resourceId'] as const).map(field => <Field key={field} label={`${field} ${index + 1}`} value={scope[field]} required
        onChange={text => onChange(value.map((item, row) => row === index ? { ...item, [field]: text } : item))} />)}
      <button type="button" className={secondaryButtonClass} onClick={() => onChange(value.filter((_, row) => row !== index))}>Remove scope {index + 1}</button>
    </div>)}
    <button type="button" className={secondaryButtonClass} disabled={value.length >= maxItems} onClick={() => onChange([...value,
      { cloud: 'AzureUSGovernment', directoryTenantId: '', subscriptionId: '', resourceId: '' }])}>Add scope</button>
  </fieldset>;
}

export function MutationForm({ children, label, submit, onSaved, disabled = false, submitDisabled = false, onPendingChange }: {
  children: ReactNode; label: string; submit: (key: string) => Promise<unknown>; onSaved: () => void; disabled?: boolean;
  onPendingChange?: (pending: boolean) => void;
  submitDisabled?: boolean;
}) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [uncertain, setUncertain] = useState(false);
  const lock = useRef(false);
  const key = useRef<string | null>(null);
  const intent = useRef<((key: string) => Promise<unknown>) | null>(null);
  useEffect(() => { onPendingChange?.(busy || uncertain); }, [busy, uncertain, onPendingChange]);
  useEffect(() => {
    if (!busy && !uncertain) return;
    const warn = (event: BeforeUnloadEvent) => { event.preventDefault(); event.returnValue = ''; };
    window.addEventListener('beforeunload', warn);
    return () => window.removeEventListener('beforeunload', warn);
  }, [busy, uncertain]);
  const save = async () => {
    if (lock.current || disabled || (submitDisabled && !intent.current)) return;
    lock.current = true; setBusy(true); setError(null);
    key.current ??= crypto.randomUUID();
    intent.current ??= submit;
    try {
      await intent.current(key.current);
      key.current = null; intent.current = null; setUncertain(false);
      onSaved();
    } catch (reason) {
      const knownRejection = reason instanceof PackageImportError && [400, 401, 403, 404, 409, 422].includes(reason.status ?? 0);
      if (knownRejection) { key.current = null; intent.current = null; }
      setUncertain(!knownRejection);
      setError(`${message(reason)} ${knownRejection ? 'Inputs retained. Reload current records before retrying a stale revision.' : 'Outcome uncertain. Retry the same intent; do not replace this operation.'}`);
    } finally { lock.current = false; setBusy(false); }
  };
  return <form className="space-y-4" onSubmit={event => { event.preventDefault(); void save(); }}>
    <fieldset disabled={busy || disabled || uncertain} className="space-y-4">{children}</fieldset>
    {error && <p role="alert" className={errorClass}>{error}</p>}
    <button className={buttonClass} type="submit" disabled={busy || disabled || (submitDisabled && !uncertain)}>{busy ? 'Saving…' : uncertain ? 'Retry same operation' : label}</button>
  </form>;
}
