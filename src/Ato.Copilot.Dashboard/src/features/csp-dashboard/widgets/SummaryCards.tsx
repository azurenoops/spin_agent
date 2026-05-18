import type { ReactElement } from 'react';
import type { SummaryResponse } from '../api';

/**
 * Feature 048 / US8 (Phase 3 re-scope) — Org-portfolio KPI cards for the
 * CSP Portfolio page.
 *
 * In this codebase a `Tenant` IS the unit of "org / mission owner" (every
 * compliance row carries `TenantId` only, never `OrganizationId`). The
 * cards therefore present `tenantCounts.*` to the user as *org* counts,
 * which matches the navigation contract used by `OrgsTable` (row click =
 * impersonate that org = impersonate that tenant).
 *
 * Six tiles, in order:
 *   1. Total orgs           — tenantCounts.total (excl. system tenant)
 *   2. Active orgs          — tenantCounts.active
 *   3. Total systems        — summary.systemCount
 *   4. Total ATO decisions  — sum of atoStatusCounts.* (with breakdown caption)
 *   5. Open findings        — sum of openFindingsBySeverity.* (with breakdown)
 *   6. Open POA&Ms          — summary.openPoamCount
 *
 * Suspended and Disabled tenant counts are intentionally dropped from
 * KPI prominence (still surfaced as the per-row Status badge in the
 * `OrgsTable`); they are tenant-lifecycle signals, not portfolio KPIs.
 */
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

const ACCENT_CLASSES: Record<CardSpec['accent'], string> = {
  emerald: 'border-emerald-200 bg-emerald-50',
  amber: 'border-amber-200 bg-amber-50',
  slate: 'border-slate-200 bg-slate-50',
  indigo: 'border-indigo-200 bg-indigo-50',
  sky: 'border-sky-200 bg-sky-50',
  violet: 'border-violet-200 bg-violet-50',
  rose: 'border-rose-200 bg-rose-50',
};

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
      caption: 'Eligible for entry from the orgs table below.',
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
          className={`rounded-lg border p-4 shadow-sm ${ACCENT_CLASSES[c.accent]}`}
          data-testid={c.testId}
        >
          <div className="text-xs font-medium uppercase tracking-wide text-gray-600">
            {c.label}
          </div>
          <div
            className={`mt-1 text-3xl font-semibold ${VALUE_ACCENT_CLASSES[c.accent]}`}
          >
            {c.value.toLocaleString()}
          </div>
          {c.caption && (
            <div className="mt-2 text-xs text-gray-500">{c.caption}</div>
          )}
        </div>
      ))}
    </div>
  );
}
