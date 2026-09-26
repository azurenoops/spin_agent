import { Building2, Check, Cloud, Info, Layers, LockKeyhole } from 'lucide-react';
import type { ReactNode } from 'react';

export function OrganizationSourceCards({ source, onChange }: {
  source: 'local' | 'provider'; onChange: (source: 'local' | 'provider') => void;
}) {
  return <fieldset><legend className="mb-2 text-xs font-semibold">Select source</legend>
    <div className="grid grid-cols-2 gap-2">
      {([
        { value: 'local', label: 'Create in organization', description: 'Create a new capability or component owned by your organization.', Icon: Building2 },
        { value: 'provider', label: 'Inherit from CSP', description: 'Use a provider offering and keep its source reference.', Icon: Cloud },
      ] as const).map(({ value, label, description, Icon }) => <label key={value}
        className={`relative flex cursor-pointer items-start gap-2 rounded-md border p-3 focus-within:ring-2 focus-within:ring-indigo-300 ${
          source === value ? 'border-indigo-500 bg-indigo-50/70 dark:bg-indigo-950' : 'border-slate-200 bg-white dark:border-gray-700 dark:bg-gray-900'}`}>
        <Icon className="mt-0.5 shrink-0 text-indigo-600 dark:text-indigo-300" size={21} aria-hidden="true" />
        <span className="min-w-0 pr-2"><span className="block text-xs font-semibold text-indigo-950 dark:text-indigo-100">{label}</span>
          <span className="mt-1 block text-[11px] leading-4 text-slate-500 dark:text-gray-400">{description}</span></span>
        <input type="radio" name="organization-source" aria-label={label} checked={source === value}
          onChange={() => onChange(value)} className="absolute right-2 top-2 h-3 w-3 accent-indigo-600" />
      </label>)}
    </div>
  </fieldset>;
}

export function OrganizationRecordType({ value, onChange }: {
  value: 'capability' | 'component'; onChange: (value: 'capability' | 'component') => void;
}) {
  return <fieldset><legend className="mb-2 text-xs font-semibold">Record type</legend>
    <div className="grid grid-cols-2 gap-2">{(['capability', 'component'] as const).map(type =>
      <label key={type} className={`flex cursor-pointer items-center gap-3 rounded-md border px-3 py-2 text-xs font-medium focus-within:ring-2 focus-within:ring-indigo-300 ${
        value === type ? 'border-indigo-400 bg-indigo-50 text-indigo-700 dark:bg-indigo-950 dark:text-indigo-200' : 'border-slate-200 dark:border-gray-700'}`}>
        <input type="radio" name="organization-record-type" checked={value === type} onChange={() => onChange(type)} className="accent-indigo-600" />
        {type === 'capability' ? 'Capability' : 'Component'}
      </label>)}</div>
    <p className="mt-1.5 text-[11px] text-slate-500">A capability describes protection. Components deliver it.</p>
  </fieldset>;
}

export function OrganizationDialogSteps({ step }: { step: number }) {
  return <ol aria-label="Organization addition progress" className="mb-5 flex items-center gap-2">
    {['Source & details', 'Organization contribution', 'Review'].map((label, index) => <li key={label}
      aria-current={step === index + 1 ? 'step' : undefined} className="flex min-w-0 flex-1 items-center gap-1.5 last:flex-none">
      <span className={`flex h-6 w-6 shrink-0 items-center justify-center rounded-full border text-[11px] font-semibold ${
        step >= index + 1 ? 'border-indigo-600 bg-indigo-600 text-white' : 'border-slate-200 text-slate-500 dark:border-gray-600'}`}>
        {step > index + 1 ? <Check size={13} aria-hidden="true" /> : index + 1}
      </span>
      <span className={`text-[10px] leading-3 sm:text-[11px] ${step >= index + 1 ? 'font-medium text-indigo-600 dark:text-indigo-300' : 'text-slate-500'}`}>{label}</span>
      {index < 2 && <span aria-hidden="true" className="ml-1 h-px min-w-1 flex-1 bg-slate-200 dark:bg-gray-700" />}
    </li>)}
  </ol>;
}

export function OrganizationInfo({ children }: { children: ReactNode }) {
  return <div className="flex gap-2 rounded-md border border-blue-100 bg-blue-50 px-3 py-2.5 text-[11px] leading-4 text-blue-900 dark:border-blue-900 dark:bg-blue-950 dark:text-blue-100">
    <Info size={15} className="mt-0.5 shrink-0" aria-hidden="true" /><div>{children}</div>
  </div>;
}

export function OrganizationReviewSummary({ source, name, owner, componentNames }: {
  source: 'local' | 'provider'; name?: string; owner: string; componentNames: string[];
}) {
  return <aside aria-label="Review changes" className="self-start rounded-md border border-indigo-100 bg-indigo-50/40 p-4 dark:border-indigo-900 dark:bg-indigo-950/30">
    <h4 className="mb-4 text-xs font-semibold">Review changes</h4>
    <ul className="space-y-4 text-xs leading-5">
      <li className="flex gap-2"><Cloud size={16} className="mt-0.5 shrink-0 text-indigo-700 dark:text-indigo-300" aria-hidden="true" />
        <span>Source: {source === 'provider' ? 'CSP offering' : 'Organization'}<strong className="block font-medium">{name || 'Select an offering'}</strong></span></li>
      <li className="flex gap-2"><Building2 size={16} className="mt-0.5 shrink-0 text-indigo-700 dark:text-indigo-300" aria-hidden="true" />
        <span>Save in organization library{owner && <span className="block text-slate-500">Owner: {owner}</span>}</span></li>
      <li className="flex gap-2"><Layers size={16} className="mt-0.5 shrink-0 text-indigo-700 dark:text-indigo-300" aria-hidden="true" />
        <span>{componentNames.length ? 'Link supporting components' : 'No additional components'}
          {!!componentNames.length && <ul className="mt-1 list-inside list-disc text-slate-500">{componentNames.map((componentName, index) => <li key={index}>{componentName}</li>)}</ul>}</span></li>
      {source === 'provider' && <li className="flex gap-2"><LockKeyhole size={16} className="shrink-0 text-indigo-700 dark:text-indigo-300" aria-hidden="true" /><span>Keep provider records read-only</span></li>}
    </ul>
    <p className="mt-4 rounded border border-amber-100 bg-amber-50 p-2.5 text-[11px] leading-4 text-amber-900 dark:border-amber-900 dark:bg-amber-950 dark:text-amber-100">Available for reuse, not applied to any system. This does not grant an ATO.</p>
  </aside>;
}
