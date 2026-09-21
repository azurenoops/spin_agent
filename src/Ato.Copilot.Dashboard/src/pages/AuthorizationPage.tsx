import { useState, useCallback } from 'react';
import { useParams } from 'react-router-dom';
import apiClient from '../api/client';
import { usePolling } from '../hooks/usePolling';
import { useSettings } from '../hooks/useSettings';
import { useWorkspaceSession } from '../features/workspaces/WorkspaceBoundary';

// ─── Types ───────────────────────────────────────────────────────────────────

interface AuthorizationDecision {
  id: string;
  decisionType: string;
  expirationDate: string | null;
  residualRiskLevel: string;
  issuedBy: string;
  issuedByName: string;
  decisionDate: string;
  override: AuthorizationOverride | null;
}

interface AuthorizationOverride {
  id: string;
  overrideStatus: string;
  appliedBy: string;
  appliedByName: string;
  appliedAt: string;
  justification: string;
  expirationDate: string;
}

interface RiskAcceptance {
  id: string;
  controlId: string;
  catSeverity: string;
  justification: string;
  expirationDate: string;
  compensatingControl: string | null;
  acceptedBy: string;
  isActive: boolean;
}

interface IssueAuthorizationBody {
  decisionType: string;
  expirationDate: string | null;
  residualRiskLevel: string;
  termsAndConditions: string;
  residualRiskJustification: string;
  riskAcceptances: null;
}

// ─── API ─────────────────────────────────────────────────────────────────────

async function getDecision(systemId: string): Promise<AuthorizationDecision | null> {
  try {
    const { data } = await apiClient.get<AuthorizationDecision>(
      `/systems/${systemId}/authorization`,
    );
    return data;
  } catch (err: unknown) {
    const status = (err as { response?: { status?: number } })?.response?.status;
    if (status === 404) return null;
    throw err;
  }
}

async function getRiskAcceptances(systemId: string): Promise<RiskAcceptance[]> {
  try {
    const { data } = await apiClient.get<RiskAcceptance[]>(
      `/systems/${systemId}/risk-acceptances`,
    );
    return data;
  } catch {
    return [];
  }
}

async function issueAuthorization(
  systemId: string,
  body: IssueAuthorizationBody,
): Promise<AuthorizationDecision> {
  const { data } = await apiClient.post<AuthorizationDecision>(
    `/systems/${systemId}/authorization`,
    body,
  );
  return data;
}

async function applyOverride(
  systemId: string,
  body: { overrideStatus: string; justification: string; expirationDate: string },
): Promise<void> {
  await apiClient.post(`/systems/${systemId}/authorization/override`, body);
}

// ─── Component ───────────────────────────────────────────────────────────────

const DECISION_TYPES = ['ATO', 'ATOwC', 'IATT', 'DATO'] as const;
const RISK_LEVELS = ['Low', 'Medium', 'High', 'Critical'] as const;

/**
 * Epic #121 / Task #146 — Authorize phase first-class page.
 *
 * Provides:
 * - Active ATO decision display (if one exists)
 * - Issue authorization form (ATO / ATOwC / IATT / DATO + dates + residual risk)
 * - Risk acceptances table
 *
 * Wires: GET /api/dashboard/systems/{id}/authorization
 *        POST /api/dashboard/systems/{id}/authorization
 *        GET  /api/dashboard/systems/{id}/risk-acceptances
 */
export default function AuthorizationPage() {
  const { id: systemId = '' } = useParams<{ id: string }>();
  const { settings } = useSettings();
  const workspace = useWorkspaceSession();
  const canIssue = workspace ? workspace.systemAccess?.permissions.canDecideAuthorization === true : true;
  const canApplyOverride = workspace ? canIssue : settings.role === 'AO';

  const [formOpen, setFormOpen] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);

  // Form state
  const [decisionType, setDecisionType] = useState<string>('ATO');
  const [expirationDate, setExpirationDate] = useState('');
  const [residualRisk, setResidualRisk] = useState<string>('Medium');
  const [terms, setTerms] = useState('');
  const [riskJustification, setRiskJustification] = useState('');
  const [overrideFormOpen, setOverrideFormOpen] = useState(false);
  const [overrideStatus, setOverrideStatus] = useState<string>('ATO');
  const [overrideExpiration, setOverrideExpiration] = useState('');
  const [overrideJustification, setOverrideJustification] = useState('');

  const fetchDecision = useCallback(() => getDecision(systemId), [systemId]);
  const fetchRisks = useCallback(() => getRiskAcceptances(systemId), [systemId]);

  const { data: decision, refresh: refreshDecision } = usePolling(fetchDecision, 30_000);
  const { data: risks } = usePolling(fetchRisks, 30_000);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!canIssue) {
      setError('The active workspace does not permit authorization decisions.');
      return;
    }
    setSubmitting(true);
    setError(null);
    setSuccess(null);
    try {
      await issueAuthorization(systemId, {
        decisionType,
        expirationDate: expirationDate || null,
        residualRiskLevel: residualRisk,
        termsAndConditions: terms,
        residualRiskJustification: riskJustification,
        riskAcceptances: null,
      });
      setSuccess(`Authorization decision (${decisionType}) issued successfully.`);
      setFormOpen(false);
      refreshDecision();
    } catch (err: unknown) {
      const msg =
        (err as { response?: { data?: { error?: string } } })?.response?.data?.error ??
        'Failed to issue authorization. Please check the form and try again.';
      setError(msg);
    } finally {
      setSubmitting(false);
    }
  };

  const handleOverrideSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!canApplyOverride) {
      setError('The active workspace does not permit authorization overrides.');
      return;
    }
    setSubmitting(true);
    setError(null);
    setSuccess(null);
    try {
      await applyOverride(systemId, {
        overrideStatus,
        justification: overrideJustification,
        expirationDate: new Date(`${overrideExpiration}T23:59:59Z`).toISOString(),
      });
      setSuccess('Authorization override applied without changing the underlying verdict.');
      setOverrideFormOpen(false);
      refreshDecision();
    } catch (err: unknown) {
      const msg =
        (err as { response?: { data?: { error?: string } } })?.response?.data?.error ??
        'Failed to apply authorization override.';
      setError(msg);
    } finally {
      setSubmitting(false);
    }
  };

  const decisionBadgeColor = (type: string) => {
    switch (type) {
      case 'ATO': return 'bg-green-100 text-green-800';
      case 'ATOwC': return 'bg-yellow-100 text-yellow-800';
      case 'IATT': return 'bg-blue-100 text-blue-800';
      case 'DATO': return 'bg-red-100 text-red-800';
      default: return 'bg-gray-100 text-gray-800';
    }
  };

  return (
    <div className="space-y-6 p-6">
      {/* Page header */}
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-semibold text-gray-900">Authorization</h1>
          <p className="mt-1 text-sm text-gray-500">
            RMF Step 5 — Issue or review the Authorization to Operate (ATO) decision.
          </p>
        </div>
        {canIssue && !formOpen && (
          <button
            type="button"
            onClick={() => { setFormOpen(true); setError(null); setSuccess(null); }}
            className="inline-flex items-center rounded-md bg-indigo-600 px-4 py-2 text-sm font-medium text-white shadow-sm hover:bg-indigo-700 focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:ring-offset-2"
          >
            Issue Authorization
          </button>
        )}
      </div>

      {/* Feedback */}
      {success && (
        <div className="rounded-md bg-green-50 p-4 text-sm text-green-800">{success}</div>
      )}
      {error && (
        <div className="rounded-md bg-red-50 p-4 text-sm text-red-800">{error}</div>
      )}

      {/* Active decision */}
      {decision && (
        <section aria-label="Active authorization decision">
          <div className="rounded-lg border border-gray-200 bg-white p-6 shadow-sm">
            <div className="flex items-center gap-3">
              <h2 className="text-lg font-medium text-gray-900">Underlying AO Verdict</h2>
              <span
                className={`inline-flex rounded-full px-2.5 py-0.5 text-xs font-medium ${decisionBadgeColor(decision.decisionType)}`}
              >
                {decision.decisionType}
              </span>
            </div>
            <dl className="mt-4 grid grid-cols-2 gap-4 sm:grid-cols-3">
              <div>
                <dt className="text-xs font-medium uppercase tracking-wide text-gray-500">Issued By</dt>
                <dd className="mt-1 text-sm text-gray-900">{decision.issuedByName} ({decision.issuedBy})</dd>
              </div>
              <div>
                <dt className="text-xs font-medium uppercase tracking-wide text-gray-500">Issued At</dt>
                <dd className="mt-1 text-sm text-gray-900">
                  {new Date(decision.decisionDate).toLocaleDateString()}
                </dd>
              </div>
              <div>
                <dt className="text-xs font-medium uppercase tracking-wide text-gray-500">Expires</dt>
                <dd className="mt-1 text-sm text-gray-900">
                  {decision.expirationDate
                    ? new Date(decision.expirationDate).toLocaleDateString()
                    : 'No expiration'}
                </dd>
              </div>
              <div>
                <dt className="text-xs font-medium uppercase tracking-wide text-gray-500">Residual Risk</dt>
                <dd className="mt-1 text-sm text-gray-900">{decision.residualRiskLevel}</dd>
              </div>
            </dl>
            {decision.override && (
              <div className="mt-6 border-t border-amber-200 pt-4" role="region" aria-label="Active authorization override">
                <div className="flex items-center gap-3">
                  <h3 className="text-sm font-semibold text-gray-900">Temporary Override</h3>
                  <span className={`inline-flex rounded-full px-2.5 py-0.5 text-xs font-medium ${decisionBadgeColor(decision.override.overrideStatus)}`}>
                    {decision.override.overrideStatus}
                  </span>
                </div>
                <dl className="mt-3 grid grid-cols-1 gap-4 sm:grid-cols-3">
                  <div><dt className="text-xs font-medium uppercase text-gray-500">Applied By</dt><dd className="mt-1 text-sm text-gray-900">{decision.override.appliedByName} ({decision.override.appliedBy})</dd></div>
                  <div><dt className="text-xs font-medium uppercase text-gray-500">Applied At</dt><dd className="mt-1 text-sm text-gray-900">{new Date(decision.override.appliedAt).toLocaleString()}</dd></div>
                  <div><dt className="text-xs font-medium uppercase text-gray-500">Override Expires</dt><dd className="mt-1 text-sm text-gray-900">{new Date(decision.override.expirationDate).toLocaleString()}</dd></div>
                  <div className="sm:col-span-3"><dt className="text-xs font-medium uppercase text-gray-500">Justification</dt><dd className="mt-1 text-sm text-gray-900">{decision.override.justification}</dd></div>
                </dl>
              </div>
            )}
            {canApplyOverride && !overrideFormOpen && (
              <button type="button" onClick={() => setOverrideFormOpen(true)} className="mt-5 rounded-md border border-gray-300 bg-white px-4 py-2 text-sm font-medium text-gray-700 shadow-sm hover:bg-gray-50">
                Apply Temporary Override
              </button>
            )}
          </div>
        </section>
      )}

      {canApplyOverride && decision && overrideFormOpen && (
        <section aria-label="Apply authorization override form">
          <div className="rounded-lg border border-gray-200 bg-white p-6 shadow-sm">
            <h2 className="text-lg font-medium text-gray-900">Apply Temporary Override</h2>
            <form onSubmit={(e) => { void handleOverrideSubmit(e); }} className="mt-4 space-y-4">
              <div>
                <label htmlFor="override-status" className="block text-sm font-medium text-gray-700">Override Status</label>
                <select id="override-status" value={overrideStatus} onChange={(e) => setOverrideStatus(e.target.value)} className="mt-1 block w-full rounded-md border-gray-300 shadow-sm">
                  {DECISION_TYPES.map((type) => <option key={type} value={type}>{type}</option>)}
                </select>
              </div>
              <div>
                <label htmlFor="override-expiration" className="block text-sm font-medium text-gray-700">Override Expiration</label>
                <input id="override-expiration" type="date" required value={overrideExpiration} onChange={(e) => setOverrideExpiration(e.target.value)} className="mt-1 block w-full rounded-md border-gray-300 shadow-sm" />
              </div>
              <div>
                <label htmlFor="override-justification" className="block text-sm font-medium text-gray-700">Justification</label>
                <textarea id="override-justification" required rows={3} value={overrideJustification} onChange={(e) => setOverrideJustification(e.target.value)} className="mt-1 block w-full rounded-md border-gray-300 shadow-sm" />
              </div>
              <div className="flex justify-end gap-3">
                <button type="button" onClick={() => setOverrideFormOpen(false)} className="rounded-md border border-gray-300 bg-white px-4 py-2 text-sm font-medium text-gray-700">Cancel</button>
                <button type="submit" disabled={submitting} className="rounded-md bg-indigo-600 px-4 py-2 text-sm font-medium text-white disabled:opacity-50">{submitting ? 'Applying...' : 'Apply Override'}</button>
              </div>
            </form>
          </div>
        </section>
      )}

      {/* Issue authorization form */}
      {canIssue && formOpen && (
        <section aria-label="Issue authorization form">
          <div className="rounded-lg border border-gray-200 bg-white p-6 shadow-sm">
            <h2 className="text-lg font-medium text-gray-900">Issue Authorization Decision</h2>
            <form onSubmit={(e) => { void handleSubmit(e); }} className="mt-4 space-y-4">
              {/* Decision type */}
              <div className="grid grid-cols-2 gap-4">
                <div>
                  <label htmlFor="decision-type" className="block text-sm font-medium text-gray-700">
                    Decision Type <span className="text-red-500">*</span>
                  </label>
                  <select
                    id="decision-type"
                    value={decisionType}
                    onChange={(e) => setDecisionType(e.target.value)}
                    required
                    className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm"
                  >
                    {DECISION_TYPES.map((t) => (
                      <option key={t} value={t}>{t}</option>
                    ))}
                  </select>
                </div>
                <div>
                  <label htmlFor="residual-risk" className="block text-sm font-medium text-gray-700">
                    Residual Risk Level <span className="text-red-500">*</span>
                  </label>
                  <select
                    id="residual-risk"
                    value={residualRisk}
                    onChange={(e) => setResidualRisk(e.target.value)}
                    required
                    className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm"
                  >
                    {RISK_LEVELS.map((l) => (
                      <option key={l} value={l}>{l}</option>
                    ))}
                  </select>
                </div>
              </div>

              {/* Expiration date */}
              <div>
                <label htmlFor="expiration" className="block text-sm font-medium text-gray-700">
                  Expiration Date
                </label>
                <input
                  id="expiration"
                  type="date"
                  value={expirationDate}
                  onChange={(e) => setExpirationDate(e.target.value)}
                  className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm"
                />
              </div>

              {/* Terms */}
              <div>
                <label htmlFor="terms" className="block text-sm font-medium text-gray-700">
                  Terms and Conditions
                </label>
                <textarea
                  id="terms"
                  rows={3}
                  value={terms}
                  onChange={(e) => setTerms(e.target.value)}
                  placeholder="Optional conditions attached to this authorization..."
                  className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm"
                />
              </div>

              {/* Residual risk justification */}
              <div>
                <label htmlFor="risk-justification" className="block text-sm font-medium text-gray-700">
                  Residual Risk Justification
                </label>
                <textarea
                  id="risk-justification"
                  rows={3}
                  value={riskJustification}
                  onChange={(e) => setRiskJustification(e.target.value)}
                  placeholder="Explain why the residual risk is acceptable..."
                  className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm"
                />
              </div>

              {/* Actions */}
              <div className="flex justify-end gap-3 pt-2">
                <button
                  type="button"
                  onClick={() => setFormOpen(false)}
                  className="rounded-md border border-gray-300 bg-white px-4 py-2 text-sm font-medium text-gray-700 shadow-sm hover:bg-gray-50"
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  disabled={submitting}
                  className="inline-flex items-center rounded-md bg-indigo-600 px-4 py-2 text-sm font-medium text-white shadow-sm hover:bg-indigo-700 disabled:opacity-50"
                >
                  {submitting ? 'Issuing…' : 'Issue Authorization'}
                </button>
              </div>
            </form>
          </div>
        </section>
      )}

      {/* Risk acceptances table */}
      <section aria-label="Risk acceptances">
        <div className="rounded-lg border border-gray-200 bg-white shadow-sm">
          <div className="px-6 py-4 border-b border-gray-200">
            <h2 className="text-lg font-medium text-gray-900">Risk Acceptances</h2>
            <p className="mt-1 text-sm text-gray-500">
              Residual risks formally accepted as part of the authorization.
            </p>
          </div>
          {!risks || risks.length === 0 ? (
            <div className="px-6 py-8 text-center text-sm text-gray-500">
              No risk acceptances on record for this system.
            </div>
          ) : (
            <div className="overflow-x-auto">
              <table className="min-w-full divide-y divide-gray-200">
                <thead className="bg-gray-50">
                  <tr>
                    {['Control', 'Severity', 'Justification', 'Expires', 'Accepted By', 'Active'].map((h) => (
                      <th
                        key={h}
                        scope="col"
                        className="px-4 py-3 text-left text-xs font-medium uppercase tracking-wide text-gray-500"
                      >
                        {h}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody className="divide-y divide-gray-200 bg-white">
                  {risks.map((r) => (
                    <tr key={r.id}>
                      <td className="whitespace-nowrap px-4 py-3 text-sm font-medium text-gray-900">
                        {r.controlId}
                      </td>
                      <td className="whitespace-nowrap px-4 py-3 text-sm text-gray-500">
                        <span
                          className={`inline-flex rounded-full px-2 py-0.5 text-xs font-medium ${
                            r.catSeverity === 'Critical'
                              ? 'bg-red-100 text-red-800'
                              : r.catSeverity === 'High'
                              ? 'bg-orange-100 text-orange-800'
                              : r.catSeverity === 'Medium'
                              ? 'bg-yellow-100 text-yellow-800'
                              : 'bg-blue-100 text-blue-800'
                          }`}
                        >
                          {r.catSeverity}
                        </span>
                      </td>
                      <td className="max-w-xs truncate px-4 py-3 text-sm text-gray-500">
                        {r.justification}
                      </td>
                      <td className="whitespace-nowrap px-4 py-3 text-sm text-gray-500">
                        {new Date(r.expirationDate).toLocaleDateString()}
                      </td>
                      <td className="whitespace-nowrap px-4 py-3 text-sm text-gray-500">
                        {r.acceptedBy}
                      </td>
                      <td className="whitespace-nowrap px-4 py-3 text-sm">
                        <span
                          className={`inline-flex rounded-full px-2 py-0.5 text-xs font-medium ${
                            r.isActive
                              ? 'bg-green-100 text-green-800'
                              : 'bg-gray-100 text-gray-500'
                          }`}
                        >
                          {r.isActive ? 'Active' : 'Expired'}
                        </span>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      </section>
    </div>
  );
}
