import { useCallback, useState } from 'react';
import ReactMarkdown from 'react-markdown';
import { usePolling } from '../hooks/usePolling';
import {
  getConMonOverview,
  createConMonPlan,
  generateConMonReport,
  reportSignificantChange,
  checkReauthorization,
  getConMonReport,
} from '../api/conmon';
import { useSystemContext } from '../components/layout/SystemLayout';
import type {
  AgreementExpirationInfo,
  ConMonOverviewResponse,
  ConMonReportDetail,
  ConMonReportSummary,
  ReauthorizationCheckResult,
  SignificantChangeItem,
} from '../api/conmon';

function StatusBadge({ status, variant }: { status: string; variant?: 'green' | 'amber' | 'red' | 'blue' | 'gray' }) {
  const colors = {
    green: 'bg-green-100 text-green-700',
    amber: 'bg-amber-100 text-amber-700',
    red: 'bg-red-100 text-red-700',
    blue: 'bg-indigo-100 text-indigo-700',
    gray: 'bg-gray-100 text-gray-500',
  };

  return (
    <span className={`inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium ${colors[variant ?? 'gray']}`}>
      {status}
    </span>
  );
}

function variantForStatus(status: string): 'green' | 'amber' | 'red' | 'blue' | 'gray' {
  const normalized = status.toLowerCase();
  if (normalized === 'active' || normalized === 'authorized' || normalized === 'none' || normalized === 'false') return 'green';
  if (normalized === 'triggered' || normalized === 'in review' || normalized === 'pending') return 'blue';
  if (normalized === 'info' || normalized === 'warning' || normalized === 'urgent') return 'amber';
  if (normalized === 'expired' || normalized === 'true') return 'red';
  return 'gray';
}

function formatDate(value: string | null | undefined): string {
  if (!value) return '—';
  return new Date(value).toLocaleDateString('en-US', { year: 'numeric', month: 'short', day: 'numeric' });
}

function formatDateTime(value: string | null | undefined): string {
  if (!value) return '—';
  return new Date(value).toLocaleString('en-US', {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
    hour: 'numeric',
    minute: '2-digit',
  });
}

function formatNumber(value: number | null | undefined): string {
  if (value === null || value === undefined) return '—';
  return Number.isInteger(value) ? value.toString() : value.toFixed(2);
}

function MetricCard({ label, value, detail, tone = 'gray' }: { label: string; value: string; detail?: string; tone?: 'gray' | 'green' | 'amber' | 'red' | 'blue' }) {
  const accents = {
    gray: 'border-gray-200',
    green: 'border-green-200',
    amber: 'border-amber-200',
    red: 'border-red-200',
    blue: 'border-indigo-200',
  };

  return (
    <div className={`rounded-lg border bg-white p-4 shadow-sm ${accents[tone]}`}>
      <p className="text-xs font-medium uppercase tracking-wider text-gray-500">{label}</p>
      <p className="mt-2 text-2xl font-semibold text-gray-900">{value}</p>
      {detail ? <p className="mt-1 text-xs text-gray-500">{detail}</p> : null}
    </div>
  );
}

function SectionCard({ title, subtitle, children, action }: { title: string; subtitle?: string; children: React.ReactNode; action?: React.ReactNode }) {
  return (
    <div className="overflow-hidden rounded-lg border border-gray-200 bg-white shadow-sm">
      <div className="flex items-start justify-between gap-4 border-b border-gray-200 bg-gray-50 px-5 py-4">
        <div>
          <h2 className="text-sm font-semibold text-gray-900">{title}</h2>
          {subtitle ? <p className="mt-1 text-sm text-gray-500">{subtitle}</p> : null}
        </div>
        {action}
      </div>
      <div className="p-5">{children}</div>
    </div>
  );
}

function EmptyState({ message }: { message: string }) {
  return <p className="text-sm text-gray-500">{message}</p>;
}

function PlanSection({ data, onRefresh }: { data: ConMonOverviewResponse; onRefresh: () => void }) {
  const [showForm, setShowForm] = useState(false);
  const [frequency, setFrequency] = useState(data.plan?.assessmentFrequency ?? 'Monthly');
  const [reviewDate, setReviewDate] = useState(
    data.plan?.annualReviewDate
      ? data.plan.annualReviewDate.substring(0, 10)
      : new Date(Date.now() + 365 * 86400000).toISOString().substring(0, 10),
  );
  const [distribution, setDistribution] = useState(
    (data.plan?.reportDistribution ?? []).join(', '),
  );
  const [triggers, setTriggers] = useState(
    (data.plan?.significantChangeTriggers ?? []).join(', '),
  );
  const [saving, setSaving] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);

  async function handleSave() {
    setSaving(true);
    setSaveError(null);
    try {
      await createConMonPlan(data.systemId, {
        assessmentFrequency: frequency,
        annualReviewDate: new Date(reviewDate).toISOString(),
        reportDistribution: distribution.split(',').map((s) => s.trim()).filter(Boolean),
        significantChangeTriggers: triggers.split(',').map((s) => s.trim()).filter(Boolean),
      });
      setShowForm(false);
      onRefresh();
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to save plan.';
      setSaveError(msg);
    } finally {
      setSaving(false);
    }
  }

  const editButton = (
    <button
      onClick={() => setShowForm((v) => !v)}
      className="rounded-md bg-indigo-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-indigo-700 focus:outline-none focus:ring-2 focus:ring-indigo-500"
    >
      {data.plan ? 'Edit Plan' : 'Create Plan'}
    </button>
  );

  return (
    <SectionCard title="ConMon Plan" subtitle="Monitoring cadence, annual review, distribution, and custom triggers." action={editButton}>
      {showForm ? (
        <div className="space-y-4">
          <div className="grid gap-4 md:grid-cols-2">
            <div>
              <label className="block text-xs font-medium uppercase tracking-wider text-gray-500">Assessment Frequency</label>
              <select
                className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
                value={frequency}
                onChange={(e) => setFrequency(e.target.value)}
              >
                {['Monthly', 'Quarterly', 'Annually'].map((f) => (
                  <option key={f} value={f}>{f}</option>
                ))}
              </select>
            </div>
            <div>
              <label className="block text-xs font-medium uppercase tracking-wider text-gray-500">Annual Review Date</label>
              <input
                type="date"
                className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
                value={reviewDate}
                onChange={(e) => setReviewDate(e.target.value)}
              />
            </div>
            <div className="md:col-span-2">
              <label className="block text-xs font-medium uppercase tracking-wider text-gray-500">Report Distribution <span className="normal-case font-normal text-gray-400">(comma-separated)</span></label>
              <input
                type="text"
                className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
                placeholder="ISSM, ISSO, AO"
                value={distribution}
                onChange={(e) => setDistribution(e.target.value)}
              />
            </div>
            <div className="md:col-span-2">
              <label className="block text-xs font-medium uppercase tracking-wider text-gray-500">Significant Change Triggers <span className="normal-case font-normal text-gray-400">(comma-separated)</span></label>
              <input
                type="text"
                className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
                placeholder="New hardware, OS upgrade, Architecture change"
                value={triggers}
                onChange={(e) => setTriggers(e.target.value)}
              />
            </div>
          </div>
          {saveError ? <p className="text-sm text-red-600">{saveError}</p> : null}
          <div className="flex justify-end gap-3 border-t border-gray-100 pt-4">
            <button
              onClick={() => { setShowForm(false); setSaveError(null); }}
              className="rounded-md border border-gray-300 px-3 py-1.5 text-xs font-medium text-gray-700 hover:bg-gray-50"
            >
              Cancel
            </button>
            <button
              onClick={handleSave}
              disabled={saving}
              className="rounded-md bg-indigo-600 px-4 py-1.5 text-xs font-medium text-white hover:bg-indigo-700 disabled:opacity-50"
            >
              {saving ? 'Saving…' : 'Save Plan'}
            </button>
          </div>
        </div>
      ) : !data.plan ? (
        <EmptyState message="No ConMon plan configured. Click 'Create Plan' to define monitoring cadence, recipients, and significant change triggers." />
      ) : (
        <>
          <div className="grid gap-4 md:grid-cols-2">
            <div>
              <p className="text-xs font-medium uppercase tracking-wider text-gray-500">Assessment Frequency</p>
              <p className="mt-1 text-sm font-medium text-gray-900">{data.plan.assessmentFrequency}</p>
            </div>
            <div>
              <p className="text-xs font-medium uppercase tracking-wider text-gray-500">Annual Review Date</p>
              <p className="mt-1 text-sm font-medium text-gray-900">{formatDate(data.plan.annualReviewDate)}</p>
            </div>
            <div>
              <p className="text-xs font-medium uppercase tracking-wider text-gray-500">Created</p>
              <p className="mt-1 text-sm text-gray-900">{formatDateTime(data.plan.createdAt)}</p>
            </div>
            <div>
              <p className="text-xs font-medium uppercase tracking-wider text-gray-500">Last Updated</p>
              <p className="mt-1 text-sm text-gray-900">{formatDateTime(data.plan.modifiedAt)}</p>
            </div>
          </div>
          <div className="mt-5 grid gap-5 lg:grid-cols-2">
            <div>
              <p className="text-xs font-medium uppercase tracking-wider text-gray-500">Report Distribution</p>
              <div className="mt-2 flex flex-wrap gap-2">
                {data.plan.reportDistribution.length > 0 ? data.plan.reportDistribution.map((item) => (
                  <span key={item} className="rounded-full bg-indigo-50 px-2.5 py-1 text-xs font-medium text-indigo-700">{item}</span>
                )) : <span className="text-sm text-gray-500">No recipients configured.</span>}
              </div>
            </div>
            <div>
              <p className="text-xs font-medium uppercase tracking-wider text-gray-500">Significant Change Triggers</p>
              <div className="mt-2 flex flex-wrap gap-2">
                {data.plan.significantChangeTriggers.length > 0 ? data.plan.significantChangeTriggers.map((item) => (
                  <span key={item} className="rounded-full bg-amber-50 px-2.5 py-1 text-xs font-medium text-amber-700">{item}</span>
                )) : <span className="text-sm text-gray-500">No custom triggers configured.</span>}
              </div>
            </div>
          </div>
        </>
      )}
    </SectionCard>
  );
}

function AlertsSection({ expiration, reauthorization, agreementAlerts, systemId, onRefresh }: {
  data: ConMonOverviewResponse;
  expiration: ConMonOverviewResponse['expiration'];
  reauthorization: ConMonOverviewResponse['reauthorization'];
  agreementAlerts: AgreementExpirationInfo[];
  systemId: string;
  onRefresh: () => void;
}) {
  const [checking, setChecking] = useState(false);
  const [initiating, setInitiating] = useState(false);
  const [checkResult, setCheckResult] = useState<ReauthorizationCheckResult | null>(null);
  const [checkError, setCheckError] = useState<string | null>(null);

  async function handleCheck() {
    setChecking(true);
    setCheckError(null);
    try {
      const result = await checkReauthorization(systemId, { initiateIfTriggered: false });
      setCheckResult(result);
    } catch (err: unknown) {
      setCheckError(err instanceof Error ? err.message : 'Check failed.');
    } finally {
      setChecking(false);
    }
  }

  async function handleInitiate() {
    setInitiating(true);
    setCheckError(null);
    try {
      const result = await checkReauthorization(systemId, { initiateIfTriggered: true });
      setCheckResult(result);
      onRefresh();
    } catch (err: unknown) {
      setCheckError(err instanceof Error ? err.message : 'Initiation failed.');
    } finally {
      setInitiating(false);
    }
  }

  const triggered = checkResult?.isTriggered ?? reauthorization.isTriggered;
  const triggers = checkResult?.triggers ?? reauthorization.triggers;
  const unreviewedCount = checkResult?.unreviewedChangeCount ?? reauthorization.unreviewedChangeCount;

  return (
    <div className="grid gap-6 xl:grid-cols-2">
      <SectionCard
        title="Authorization & Reauthorization"
        subtitle="ATO timeline and current trigger state."
        action={<StatusBadge status={expiration.alertLevel} variant={variantForStatus(expiration.alertLevel)} />}
      >
        <div className="space-y-4">
          <div>
            <p className="text-xs font-medium uppercase tracking-wider text-gray-500">Authorization Status</p>
            <p className="mt-1 text-sm text-gray-900">{expiration.alertMessage}</p>
          </div>
          <div className="grid gap-4 md:grid-cols-2">
            <div>
              <p className="text-xs font-medium uppercase tracking-wider text-gray-500">Decision Type</p>
              <p className="mt-1 text-sm text-gray-900">{expiration.decisionType ?? '—'}</p>
            </div>
            <div>
              <p className="text-xs font-medium uppercase tracking-wider text-gray-500">Expiration Date</p>
              <p className="mt-1 text-sm text-gray-900">{formatDate(expiration.expirationDate)}</p>
            </div>
          </div>
          <div className="border-t border-gray-100 pt-4">
            <div className="flex items-start justify-between gap-3">
              <div className="flex-1">
                <p className="text-xs font-medium uppercase tracking-wider text-gray-500">Reauthorization Check</p>
                <p className="mt-1 text-sm text-gray-900">
                  {triggered ? 'Reauthorization is currently triggered.' : 'No reauthorization triggers are currently active.'}
                </p>
                <p className="mt-1 text-xs text-gray-500">Unreviewed changes requiring attention: {unreviewedCount}</p>
              </div>
              <StatusBadge
                status={triggered ? 'Triggered' : 'Clear'}
                variant={triggered ? 'red' : 'green'}
              />
            </div>
            {triggers.length > 0 ? (
              <ul className="mt-3 space-y-2">
                {triggers.map((trigger) => (
                  <li key={trigger} className="rounded-md bg-red-50 px-3 py-2 text-sm text-red-800">{trigger}</li>
                ))}
              </ul>
            ) : null}
            {checkError ? <p className="mt-2 text-sm text-red-600">{checkError}</p> : null}
            {checkResult?.initiated ? (
              <p className="mt-2 rounded-md bg-green-50 px-3 py-2 text-sm text-green-800 font-medium">Reauthorization workflow initiated.</p>
            ) : null}
            <div className="mt-4 flex flex-wrap gap-2">
              <button
                onClick={handleCheck}
                disabled={checking}
                className="rounded-md border border-gray-300 px-3 py-1.5 text-xs font-medium text-gray-700 hover:bg-gray-50 disabled:opacity-50"
              >
                {checking ? 'Checking…' : 'Check Triggers'}
              </button>
              {triggered && !checkResult?.initiated ? (
                <button
                  onClick={handleInitiate}
                  disabled={initiating}
                  className="rounded-md bg-red-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-red-700 disabled:opacity-50"
                >
                  {initiating ? 'Initiating…' : 'Initiate Reauthorization'}
                </button>
              ) : null}
            </div>
          </div>
        </div>
      </SectionCard>

      <SectionCard title="Expiration Alerts" subtitle="ISA and PIA items within the alert window.">
        {agreementAlerts.length === 0 ? (
          <EmptyState message="No ISA or PIA expirations are currently within the 90-day monitoring window." />
        ) : (
          <div className="space-y-3">
            {agreementAlerts.map((alert) => (
              <div key={`${alert.itemType}-${alert.agreementTitle}-${alert.expirationDate ?? 'none'}`} className="rounded-lg border border-gray-200 px-4 py-3">
                <div className="flex items-start justify-between gap-3">
                  <div>
                    <p className="text-sm font-medium text-gray-900">{alert.agreementTitle}</p>
                    <p className="mt-1 text-xs text-gray-500">
                      {alert.itemType}{alert.targetSystemName ? ` · ${alert.targetSystemName}` : ''} · Expires {formatDate(alert.expirationDate)}
                    </p>
                  </div>
                  <StatusBadge status={alert.alertLevel} variant={variantForStatus(alert.alertLevel)} />
                </div>
                <p className="mt-2 text-sm text-gray-700">{alert.message}</p>
              </div>
            ))}
          </div>
        )}
      </SectionCard>
    </div>
  );
}

function SignificantChangesSection({ changes, systemId, onRefresh }: { changes: SignificantChangeItem[]; systemId: string; onRefresh: () => void }) {
  const [showForm, setShowForm] = useState(false);
  const [changeType, setChangeType] = useState('Hardware');
  const [description, setDescription] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [submitError, setSubmitError] = useState<string | null>(null);

  const CHANGE_TYPES = ['Hardware', 'Software', 'Architecture', 'Network', 'Personnel', 'Policy', 'Environment', 'Other'];

  async function handleSubmit() {
    if (!description.trim()) {
      setSubmitError('Description is required.');
      return;
    }
    setSubmitting(true);
    setSubmitError(null);
    try {
      await reportSignificantChange(systemId, { changeType, description: description.trim() });
      setShowForm(false);
      setDescription('');
      onRefresh();
    } catch (err: unknown) {
      setSubmitError(err instanceof Error ? err.message : 'Failed to submit change.');
    } finally {
      setSubmitting(false);
    }
  }

  const reportButton = (
    <button
      onClick={() => setShowForm((v) => !v)}
      className="rounded-md bg-amber-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-amber-700 focus:outline-none focus:ring-2 focus:ring-amber-500"
    >
      Report Change
    </button>
  );

  return (
    <SectionCard title="Significant Changes" subtitle="Recent changes that may impact authorization posture." action={reportButton}>
      {showForm ? (
        <div className="mb-5 space-y-4 rounded-lg border border-amber-200 bg-amber-50 p-4">
          <p className="text-sm font-medium text-amber-900">Report a Significant Change</p>
          <div className="grid gap-4 md:grid-cols-2">
            <div>
              <label className="block text-xs font-medium uppercase tracking-wider text-gray-600">Change Type</label>
              <select
                className="mt-1 block w-full rounded-md border border-gray-300 bg-white px-3 py-2 text-sm focus:border-amber-500 focus:outline-none focus:ring-1 focus:ring-amber-500"
                value={changeType}
                onChange={(e) => setChangeType(e.target.value)}
              >
                {CHANGE_TYPES.map((t) => (
                  <option key={t} value={t}>{t}</option>
                ))}
              </select>
            </div>
          </div>
          <div>
            <label className="block text-xs font-medium uppercase tracking-wider text-gray-600">Description <span className="font-normal normal-case text-red-600">*</span></label>
            <textarea
              rows={3}
              className="mt-1 block w-full rounded-md border border-gray-300 bg-white px-3 py-2 text-sm focus:border-amber-500 focus:outline-none focus:ring-1 focus:ring-amber-500"
              placeholder="Describe the change and its potential impact on authorization posture…"
              value={description}
              onChange={(e) => setDescription(e.target.value)}
            />
          </div>
          {submitError ? <p className="text-sm text-red-600">{submitError}</p> : null}
          <div className="flex justify-end gap-3">
            <button
              onClick={() => { setShowForm(false); setSubmitError(null); setDescription(''); }}
              className="rounded-md border border-gray-300 bg-white px-3 py-1.5 text-xs font-medium text-gray-700 hover:bg-gray-50"
            >
              Cancel
            </button>
            <button
              onClick={handleSubmit}
              disabled={submitting}
              className="rounded-md bg-amber-600 px-4 py-1.5 text-xs font-medium text-white hover:bg-amber-700 disabled:opacity-50"
            >
              {submitting ? 'Submitting…' : 'Submit Change'}
            </button>
          </div>
        </div>
      ) : null}

      {changes.length === 0 ? (
        <EmptyState message="No significant changes have been reported for this system." />
      ) : (
        <div className="space-y-3">
          {changes.map((change) => (
            <div key={change.id} className="rounded-lg border border-gray-200 px-4 py-3">
              <div className="flex items-start justify-between gap-3">
                <div>
                  <p className="text-sm font-medium text-gray-900">{change.changeType}</p>
                  <p className="mt-1 text-xs text-gray-500">Detected {formatDateTime(change.detectedAt)} by {change.detectedBy}</p>
                </div>
                <div className="flex items-center gap-2">
                  <StatusBadge
                    status={change.requiresReauthorization ? 'Requires Reauth' : 'Monitor'}
                    variant={change.requiresReauthorization ? 'red' : 'amber'}
                  />
                  {change.reviewedAt ? <StatusBadge status="Reviewed" variant="green" /> : <StatusBadge status="Pending Review" variant="blue" />}
                </div>
              </div>
              <p className="mt-2 text-sm text-gray-700">{change.description}</p>
              {(change.reviewedBy || change.disposition) ? (
                <div className="mt-3 border-t border-gray-100 pt-3 text-xs text-gray-500">
                  <p>Reviewed By: {change.reviewedBy ?? '—'}</p>
                  <p className="mt-1">Disposition: {change.disposition ?? '—'}</p>
                </div>
              ) : null}
            </div>
          ))}
        </div>
      )}
    </SectionCard>
  );
}

function ReportsSection({ reports, systemId, onRefresh }: { reports: ConMonReportSummary[]; systemId: string; onRefresh: () => void }) {
  const [showGenerateForm, setShowGenerateForm] = useState(false);
  const [reportType, setReportType] = useState('Monthly');
  const [period, setPeriod] = useState(new Date().toISOString().substring(0, 7));
  const [generating, setGenerating] = useState(false);
  const [generateError, setGenerateError] = useState<string | null>(null);
  const [viewReport, setViewReport] = useState<ConMonReportDetail | null>(null);
  const [loadingReport, setLoadingReport] = useState(false);
  const [reportLoadError, setReportLoadError] = useState<string | null>(null);

  async function handleGenerate() {
    setGenerating(true);
    setGenerateError(null);
    try {
      await generateConMonReport(systemId, { reportType, period });
      setShowGenerateForm(false);
      onRefresh();
    } catch (err: unknown) {
      setGenerateError(err instanceof Error ? err.message : 'Failed to generate report.');
    } finally {
      setGenerating(false);
    }
  }

  async function handleViewReport(reportId: string) {
    setLoadingReport(true);
    setReportLoadError(null);
    setViewReport(null);
    try {
      const detail = await getConMonReport(systemId, reportId);
      setViewReport(detail);
    } catch (err: unknown) {
      setReportLoadError(err instanceof Error ? err.message : 'Failed to load report.');
    } finally {
      setLoadingReport(false);
    }
  }

  const generateButton = (
    <button
      onClick={() => setShowGenerateForm((v) => !v)}
      className="rounded-md bg-indigo-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-indigo-700 focus:outline-none focus:ring-2 focus:ring-indigo-500"
    >
      Generate Report
    </button>
  );

  return (
    <>
      {/* Report detail drawer */}
      {(viewReport || loadingReport || reportLoadError) ? (
        <div className="fixed inset-0 z-50 flex">
          <div className="fixed inset-0 bg-black/40" onClick={() => { setViewReport(null); setReportLoadError(null); }} />
          <div className="relative ml-auto flex h-full w-full max-w-2xl flex-col bg-white shadow-2xl">
            <div className="flex items-center justify-between border-b border-gray-200 px-6 py-4">
              <div>
                <h2 className="text-base font-semibold text-gray-900">
                  {viewReport ? `${viewReport.reportType} Report — ${viewReport.period}` : 'Loading Report\u2026'}
                </h2>
                {viewReport ? (
                  <p className="mt-0.5 text-xs text-gray-500">
                    Generated {formatDateTime(viewReport.generatedAt)} by {viewReport.generatedBy}
                  </p>
                ) : null}
              </div>
              <button
                onClick={() => { setViewReport(null); setReportLoadError(null); }}
                className="rounded-md p-2 text-gray-400 hover:bg-gray-100 hover:text-gray-600"
              >
                <span className="sr-only">Close</span>
                <svg className="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                  <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" />
                </svg>
              </button>
            </div>
            <div className="flex-1 overflow-y-auto p-6">
              {loadingReport ? (
                <p className="text-sm text-gray-500">Loading report content\u2026</p>
              ) : reportLoadError ? (
                <p className="text-sm text-red-600">{reportLoadError}</p>
              ) : viewReport ? (
                <>
                  <div className="mb-6 grid grid-cols-2 gap-4 md:grid-cols-4">
                    <MetricCard label="Score" value={`${formatNumber(viewReport.complianceScore)}%`} tone="blue" />
                    <MetricCard label="Delta" value={viewReport.scoreDelta === null ? '\u2014' : `${viewReport.scoreDelta > 0 ? '+' : ''}${formatNumber(viewReport.scoreDelta)}%`} tone={viewReport.scoreDelta !== null && viewReport.scoreDelta < 0 ? 'amber' : 'green'} />
                    <MetricCard label="New Findings" value={String(viewReport.newFindings)} tone={viewReport.newFindings > 0 ? 'amber' : 'green'} />
                    <MetricCard label="Overdue POA&Ms" value={String(viewReport.overduePoamItems)} tone={viewReport.overduePoamItems > 0 ? 'red' : 'green'} />
                  </div>
                  {viewReport.reportContent ? (
                    <div className="prose prose-sm max-w-none text-gray-800">
                      <ReactMarkdown>{viewReport.reportContent}</ReactMarkdown>
                    </div>
                  ) : (
                    <p className="text-sm text-gray-500">No report content available for this report.</p>
                  )}
                </>
              ) : null}
            </div>
          </div>
        </div>
      ) : null}

      <SectionCard title="Report History" subtitle="Recent ConMon reports and key metrics by period." action={generateButton}>
        {showGenerateForm ? (
          <div className="mb-5 space-y-4 rounded-lg border border-indigo-200 bg-indigo-50 p-4">
            <p className="text-sm font-medium text-indigo-900">Generate a New ConMon Report</p>
            <div className="grid gap-4 md:grid-cols-2">
              <div>
                <label className="block text-xs font-medium uppercase tracking-wider text-gray-600">Report Type</label>
                <select
                  className="mt-1 block w-full rounded-md border border-gray-300 bg-white px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
                  value={reportType}
                  onChange={(e) => setReportType(e.target.value)}
                >
                  {['Monthly', 'Quarterly', 'Annual', 'Ad-Hoc'].map((t) => (
                    <option key={t} value={t}>{t}</option>
                  ))}
                </select>
              </div>
              <div>
                <label className="block text-xs font-medium uppercase tracking-wider text-gray-600">Period</label>
                <input
                  type="month"
                  className="mt-1 block w-full rounded-md border border-gray-300 bg-white px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
                  value={period}
                  onChange={(e) => setPeriod(e.target.value)}
                />
              </div>
            </div>
            {generateError ? <p className="text-sm text-red-600">{generateError}</p> : null}
            <div className="flex justify-end gap-3">
              <button
                onClick={() => { setShowGenerateForm(false); setGenerateError(null); }}
                className="rounded-md border border-gray-300 bg-white px-3 py-1.5 text-xs font-medium text-gray-700 hover:bg-gray-50"
              >
                Cancel
              </button>
              <button
                onClick={handleGenerate}
                disabled={generating}
                className="rounded-md bg-indigo-600 px-4 py-1.5 text-xs font-medium text-white hover:bg-indigo-700 disabled:opacity-50"
              >
                {generating ? 'Generating\u2026' : 'Generate'}
              </button>
            </div>
          </div>
        ) : null}

        {reports.length === 0 ? (
          <EmptyState message="No continuous monitoring reports have been generated for this system yet." />
        ) : (
          <div className="overflow-x-auto">
            <table className="min-w-full divide-y divide-gray-200 text-sm">
              <thead className="bg-gray-50">
                <tr>
                  <th className="px-4 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500">Period</th>
                  <th className="px-4 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500">Type</th>
                  <th className="px-4 py-3 text-right text-xs font-medium uppercase tracking-wider text-gray-500">Score</th>
                  <th className="px-4 py-3 text-right text-xs font-medium uppercase tracking-wider text-gray-500">Delta</th>
                  <th className="px-4 py-3 text-right text-xs font-medium uppercase tracking-wider text-gray-500">Open Findings</th>
                  <th className="px-4 py-3 text-right text-xs font-medium uppercase tracking-wider text-gray-500">Overdue POA&Ms</th>
                  <th className="px-4 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500">Generated</th>
                  <th className="px-4 py-3 text-right text-xs font-medium uppercase tracking-wider text-gray-500"></th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100 bg-white">
                {reports.map((report) => (
                  <tr key={report.reportId}>
                    <td className="whitespace-nowrap px-4 py-3 text-gray-900">{report.period}</td>
                    <td className="whitespace-nowrap px-4 py-3 text-gray-700">{report.reportType}</td>
                    <td className="whitespace-nowrap px-4 py-3 text-right text-gray-900">{formatNumber(report.complianceScore)}%</td>
                    <td className="whitespace-nowrap px-4 py-3 text-right text-gray-700">
                      {report.scoreDelta === null ? '\u2014' : `${report.scoreDelta > 0 ? '+' : ''}${formatNumber(report.scoreDelta)}%`}
                    </td>
                    <td className="whitespace-nowrap px-4 py-3 text-right text-gray-700">{report.newFindings}</td>
                    <td className="whitespace-nowrap px-4 py-3 text-right text-gray-700">{report.overduePoamItems}</td>
                    <td className="whitespace-nowrap px-4 py-3 text-gray-500">{formatDateTime(report.generatedAt)}</td>
                    <td className="whitespace-nowrap px-4 py-3 text-right">
                      <button
                        onClick={() => handleViewReport(report.reportId)}
                        className="text-xs font-medium text-indigo-600 hover:text-indigo-800"
                      >
                        View
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </SectionCard>
    </>
  );
}

export default function ConMon() {
  const { detail } = useSystemContext();
  const fetcher = useCallback(() => getConMonOverview(detail.systemId), [detail.systemId]);
  const { data, loading, error, refresh } = usePolling<ConMonOverviewResponse>(fetcher, 30000);

  if (loading) {
    return <p className="text-gray-500">Loading continuous monitoring data...</p>;
  }

  if (error || !data) {
    return (
      <div className="rounded-lg border border-yellow-200 bg-yellow-50 p-4">
        <p className="text-yellow-800 font-medium">Continuous monitoring data unavailable</p>
        <p className="mt-1 text-sm text-yellow-700">Unable to load the ConMon overview for this system.</p>
      </div>
    );
  }

  return (
    <div className="space-y-6">
      <div>
        <h2 className="text-2xl font-bold text-gray-900">Continuous Monitoring</h2>
        <p className="mt-1 text-sm text-gray-500">
          ConMon plan status, report history, drift indicators, expiration alerts, and reauthorization triggers for {data.systemName}.
        </p>
      </div>

      <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
        <MetricCard
          label="Current Compliance Score"
          value={`${formatNumber(data.status.currentComplianceScore)}%`}
          detail={data.status.authorizedBaselineScore === null ? 'No authorization baseline recorded.' : `Baseline ${formatNumber(data.status.authorizedBaselineScore)}%`}
          tone="blue"
        />
        <MetricCard
          label="Score Delta"
          value={data.status.scoreDelta === null ? '—' : `${data.status.scoreDelta > 0 ? '+' : ''}${formatNumber(data.status.scoreDelta)}%`}
          detail="Compared to the active authorization baseline."
          tone={data.status.scoreDelta !== null && data.status.scoreDelta < 0 ? 'amber' : 'green'}
        />
        <MetricCard
          label="ATO Expiration"
          value={data.expiration.daysUntilExpiration === null ? '—' : `${data.expiration.daysUntilExpiration} days`}
          detail={data.expiration.expirationDate ? `Expires ${formatDate(data.expiration.expirationDate)}` : data.expiration.alertMessage}
          tone={variantForStatus(data.expiration.alertLevel)}
        />
        <MetricCard
          label="Drift Alerts"
          value={String(data.status.driftAlertCount)}
          detail={data.status.monitoringEnabled ? `Last check ${formatDateTime(data.status.lastMonitoringCheck)}` : 'Monitoring not enabled for linked subscriptions.'}
          tone={data.status.driftAlertCount > 0 ? 'amber' : 'green'}
        />
      </div>

      <PlanSection data={data} onRefresh={refresh} />

      <div className="grid gap-6 xl:grid-cols-2">
        <SectionCard title="Monitoring Status" subtitle="Live posture metrics derived from effectiveness, findings, POA&M, and watch data.">
          <div className="grid gap-4 md:grid-cols-2">
            <MetricCard label="Open Findings" value={String(data.status.openFindings)} detail={`Resolved: ${data.status.resolvedFindings}`} tone={data.status.openFindings > 0 ? 'amber' : 'green'} />
            <MetricCard label="Open POA&Ms" value={String(data.status.openPoamItems)} detail={`Overdue: ${data.status.overduePoamItems}`} tone={data.status.overduePoamItems > 0 ? 'red' : data.status.openPoamItems > 0 ? 'amber' : 'green'} />
            <MetricCard label="Monitoring Enabled" value={data.status.monitoringEnabled ? 'Yes' : 'No'} detail={`Auto-remediation rules: ${data.status.autoRemediationRuleCount}`} tone={data.status.monitoringEnabled ? 'green' : 'gray'} />
            <MetricCard label="Last Monitoring Check" value={formatDate(data.status.lastMonitoringCheck)} detail={formatDateTime(data.status.lastMonitoringCheck)} tone="gray" />
          </div>
        </SectionCard>
        <AlertsSection data={data} expiration={data.expiration} reauthorization={data.reauthorization} agreementAlerts={data.agreementAlerts} systemId={detail.systemId} onRefresh={refresh} />
      </div>

      <SignificantChangesSection changes={data.significantChanges} systemId={detail.systemId} onRefresh={refresh} />

      <ReportsSection reports={data.reports} systemId={detail.systemId} onRefresh={refresh} />
    </div>
  );
}