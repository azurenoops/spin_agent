import type { ReactElement } from 'react';
import type { SummaryResponse } from '../api';

/** Existing server rollups, presented as organization rather than subgroup metrics. */
export interface SummaryCardsProps {
  summary: SummaryResponse;
}

interface CardSpec {
  label: string;
  value: number;
  caption?: string;
  accent: 'emerald' | 'amber' | 'slate' | 'indigo' | 'sky' | 'violet' | 'rose';
  testId: string;
}

const VALUE_ACCENT_CLASSES: Record<CardSpec['accent'], string> = {
  emerald: 'text-emerald-700',
  amber: 'text-amber-700',
  slate: 'text-slate-700',
  indigo: 'text-indigo-700',
  sky: 'text-sky-700',
  violet: 'text-violet-700',
  rose: 'text-rose-700',
};

export default function SummaryCards({ summary }: SummaryCardsProps): ReactElement {
  const ato = summary.atoStatusCounts;
  const totalAtos = ato.authorized + ato.inProcess + ato.denied;
  const sev = summary.openFindingsBySeverity;
  const totalOpenFindings = sev.critical + sev.high + sev.moderate + sev.low;

  const cards: CardSpec[] = [
    {
      label: 'Total orgs',
      value: summary.tenantCounts.total,
      caption: `${summary.tenantCounts.active} active · ${summary.tenantCounts.suspended} suspended · ${summary.tenantCounts.disabled} disabled`,
      accent: 'indigo',
      testId: 'kpi-total-orgs',
    },
    {
      label: 'Active orgs',
      value: summary.tenantCounts.active,
      caption: 'Lifecycle status; access is checked separately.',
      accent: 'emerald',
      testId: 'kpi-active-orgs',
    },
    {
      label: 'Total systems',
      value: summary.systemCount,
      accent: 'sky',
      testId: 'kpi-systems',
    },
    {
      label: 'Total ATO decisions',
      value: totalAtos,
      caption: `${ato.authorized} authorized · ${ato.inProcess} in process · ${ato.denied} denied`,
      accent: 'violet',
      testId: 'kpi-atos',
    },
    {
      label: 'Open findings',
      value: totalOpenFindings,
      caption: `${sev.critical} crit · ${sev.high} high · ${sev.moderate} mod · ${sev.low} low`,
      accent: 'rose',
      testId: 'kpi-open-findings',
    },
    {
      label: 'Open POA&Ms',
      value: summary.openPoamCount,
      caption: `${summary.openDeviationCount.toLocaleString()} open deviations`,
      accent: 'amber',
      testId: 'kpi-open-poams',
    },
  ];

  return (
    <div
      className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-6"
      data-testid="csp-dashboard-summary-cards"
    >
      {cards.map((c) => (
        <div
          key={c.testId}
          className={`rounded-xl border border-gray-200 bg-white p-5 shadow-sm dark:border-gray-700 dark:bg-gray-900`}
          data-testid={c.testId}
        >
          <div className="text-xs font-medium uppercase tracking-wide text-gray-600 dark:text-gray-400">
            {c.label}
          </div>
          <div
            className={`mt-1 text-3xl font-semibold dark:text-gray-100 ${VALUE_ACCENT_CLASSES[c.accent]}`}
          >
            {c.value.toLocaleString()}
          </div>
          {c.caption && (
            <div className="mt-2 text-xs text-gray-500 dark:text-gray-400">{c.caption}</div>
          )}
        </div>
      ))}
    </div>
  );
}
