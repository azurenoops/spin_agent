import { useEffect, useId, useRef, type ReactNode } from 'react';
import spinLogo from '../../../assets/2026-04-22_15-58-30.png';

export interface SetupStep {
  id: string;
  label: string;
  disabled?: boolean;
  status?: 'saved' | 'deferred' | 'needsAttention';
}

export interface SetupFrameProps {
  journey: 'Start' | 'Provider' | 'Organization' | 'System' | 'Tenant';
  title: string;
  description: string;
  currentStep: string;
  steps: readonly SetupStep[];
  onStepChange?: (id: string) => void;
  onSaveLater?: () => void;
  onBack?: () => void;
  primaryAction?: { label: string; onClick: () => void; disabled?: boolean };
  busy?: boolean;
  saveStatus?: string;
  error?: string | null;
  children: ReactNode;
  guidance?: ReactNode;
  footerHelp?: string;
  onChoosePath?: () => void;
  stepLabel?: string;
}

const statusLabels = { saved: 'Saved', deferred: 'Deferred', needsAttention: 'Needs attention' };
const buttonStyle = 'rounded-lg border border-slate-300 bg-white px-4 py-2.5 text-sm font-medium text-slate-700 hover:bg-slate-50 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-indigo-500 disabled:cursor-not-allowed disabled:opacity-50 dark:border-gray-600 dark:bg-gray-900 dark:text-gray-100 dark:hover:bg-gray-800';

export default function SetupFrame({
  journey, title, description, currentStep, steps, onStepChange, onSaveLater, onBack,
  primaryAction, busy = false, saveStatus, error, children, guidance, footerHelp,
  onChoosePath, stepLabel,
}: SetupFrameProps) {
  const heading = useRef<HTMLHeadingElement>(null);
  const errorSummary = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (error) errorSummary.current?.focus();
    else heading.current?.focus();
  }, [currentStep, error]);

  return <div className="min-h-full bg-slate-50 text-slate-800 dark:bg-gray-950 dark:text-gray-100">
    <header className="flex min-h-20 flex-wrap items-center gap-4 border-b border-slate-200 bg-white px-4 py-4 sm:gap-6 sm:px-6 dark:border-gray-700 dark:bg-gray-900">
      <img src={spinLogo} alt="SPIN Agent" className="h-11 w-20 object-contain" />
      <div className="min-w-0 border-l border-slate-200 pl-4 sm:pl-6 dark:border-gray-700">
        <p className="text-sm font-semibold">{journey === 'Start' ? 'Guided setup' : `${journey} setup`}</p>
        <p className="mt-1 text-xs text-slate-500 dark:text-gray-400">Prepare once. Continue in your workspace.</p>
      </div>
      {onSaveLater && <button type="button" onClick={onSaveLater} disabled={busy}
        className={`${buttonStyle} ml-auto max-w-full`}>Save &amp; finish later</button>}
    </header>
    <div className="grid min-w-0 md:grid-cols-[210px_minmax(0,1fr)] xl:grid-cols-[240px_minmax(0,1fr)]">
      <aside className="hidden min-w-0 border-r border-slate-200 bg-white px-3 py-7 md:block dark:border-gray-700 dark:bg-gray-900">
        <p className="px-3 text-xs uppercase tracking-widest text-slate-500 dark:text-gray-400">{journey} setup</p>
        <nav aria-label={`${journey} setup steps`} className="mt-4">
          <ol className="flex flex-wrap gap-1 md:block md:space-y-1">
            {steps.map((step, index) => <li key={step.id}>
              <button type="button" aria-current={step.id === currentStep ? 'step' : undefined}
                aria-label={`${index + 1} ${step.label}${step.status ? ` ${statusLabels[step.status]}` : ''}`}
                disabled={busy || step.disabled || !onStepChange} onClick={() => onStepChange?.(step.id)}
                className={`flex w-full items-start gap-3 rounded-lg px-3 py-3 text-left text-sm focus-visible:outline focus-visible:outline-2 focus-visible:outline-indigo-500 disabled:cursor-not-allowed ${
                  step.id === currentStep ? 'bg-indigo-50 font-semibold text-indigo-700 dark:bg-indigo-950 dark:text-indigo-200'
                    : 'text-slate-600 hover:bg-slate-50 disabled:opacity-50 dark:text-gray-300 dark:hover:bg-gray-800'}`}>
                <span aria-hidden="true" className={`flex h-6 w-6 shrink-0 items-center justify-center rounded-full border text-xs ${
                  step.id === currentStep ? 'border-indigo-600 bg-indigo-600 text-white' : 'border-slate-300 dark:border-gray-600'}`}>{index + 1}</span>
                <span className="min-w-0 break-words">{step.label}
                  {step.status && <span className="mt-1 block text-xs font-normal">{statusLabels[step.status]}</span>}
                </span>
              </button>
            </li>)}
          </ol>
        </nav>
        <div className="mx-3 mt-6 border-t border-slate-200 pt-5 text-xs text-slate-500 md:mt-9 dark:border-gray-700 dark:text-gray-400">
          <p className="font-semibold text-slate-600 dark:text-gray-300">Set up, then continue in your workspace</p>
          <p className="mt-2 leading-relaxed">Detailed documentation, approval, and monitoring stay in the workspace.</p>
          {onChoosePath && <button type="button" disabled={busy} onClick={onChoosePath}
            className="mt-3 text-left text-indigo-700 underline disabled:opacity-50 dark:text-indigo-300">Choose a different path</button>}
        </div>
      </aside>
      <main aria-busy={busy} className="mx-auto w-full min-w-0 max-w-[1240px] px-4 py-6 sm:px-6 xl:px-9 xl:py-8">
        <p className="mb-5 text-xs text-slate-500 dark:text-gray-400">Setup / {journey} / {steps.find(step => step.id === currentStep)?.label ?? title}</p>
        <div className="mb-6 flex flex-wrap items-start justify-between gap-4">
          <div className="min-w-0">
            <p className="mb-2 text-xs font-semibold uppercase tracking-widest text-slate-500 dark:text-gray-400">
              {journey === 'Start' ? 'Start with the right workspace' : `${journey} onboarding`}
            </p>
            <h1 ref={heading} tabIndex={-1} className="text-2xl font-semibold tracking-tight outline-none sm:text-3xl">{title}</h1>
            <p className="mt-2 max-w-2xl text-sm leading-relaxed text-slate-500 dark:text-gray-400">{description}</p>
          </div>
          {stepLabel && <span className="rounded bg-indigo-50 px-2.5 py-1 text-xs font-medium text-indigo-700 dark:bg-indigo-950 dark:text-indigo-200">{stepLabel}</span>}
        </div>
        {error && <div ref={errorSummary} tabIndex={-1} role="alert"
          className="mb-6 rounded-lg border border-red-200 bg-red-50 p-4 text-sm text-red-800 focus:outline focus:outline-2 focus:outline-red-500 dark:border-red-900 dark:bg-red-950 dark:text-red-200">{error}</div>}
        <div className={`grid min-w-0 gap-6 ${guidance ? 'xl:grid-cols-[minmax(0,1fr)_270px]' : ''}`}>
          <div className="min-w-0 space-y-5">{children}</div>
          {guidance && <aside aria-label="Setup guidance" className="min-w-0 space-y-6">{guidance}</aside>}
        </div>
        <footer className="mt-7 flex flex-wrap items-center gap-3 border-t border-slate-200 pt-5 dark:border-gray-700">
          {onBack && <button type="button" disabled={busy} onClick={onBack} className={buttonStyle}>Back</button>}
          <div className="min-w-0 flex-1 text-xs text-slate-500 dark:text-gray-400">
            {saveStatus && <p role="status">{saveStatus}</p>}
            {footerHelp && <p className={saveStatus ? 'mt-1' : ''}>{footerHelp}</p>}
          </div>
          {primaryAction && <button type="button" onClick={primaryAction.onClick} disabled={busy || primaryAction.disabled}
            className="max-w-full rounded-lg bg-indigo-600 px-4 py-2.5 text-sm font-semibold text-white hover:bg-indigo-700 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-indigo-500 disabled:cursor-not-allowed disabled:opacity-50">{primaryAction.label}</button>}
        </footer>
      </main>
    </div>
  </div>;
}

export function SetupPanel({ title, children }: { title: string; children: ReactNode }) {
  const id = useId();
  return <section aria-labelledby={id} className="min-w-0 rounded-xl border border-slate-200 bg-white p-5 sm:p-6 dark:border-gray-700 dark:bg-gray-900">
    <h2 id={id} className="mb-3 text-base font-semibold">{title}</h2>{children}
  </section>;
}

export function SetupGuidance({ title, children }: { title: string; children: ReactNode }) {
  return <section className="border-l-2 border-indigo-200 pl-4 dark:border-indigo-800">
    <h2 className="mb-2 text-sm font-semibold">{title}</h2>
    <div className="text-xs leading-relaxed text-slate-500 dark:text-gray-400">{children}</div>
  </section>;
}
