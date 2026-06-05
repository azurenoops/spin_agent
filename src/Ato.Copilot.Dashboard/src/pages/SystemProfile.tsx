import { useState, useEffect, useCallback } from 'react';
import { useParams } from 'react-router-dom';
import { useSystemContext } from '../components/layout/SystemLayout';
import { useSettings } from '../hooks/useSettings';
import ProfileSectionForm from '../components/forms/ProfileSectionForm';
import { getProfileSection, saveProfileSection, submitSections, withdrawSections, reviewSection } from '../api/systemProfile';
import { getProfileCompleteness } from '../api/systemProfile';
import { formatProfileSectionLabel } from '../utils/profileSections';
import { StatusBadge } from '../components/systems/StatusBadge';
import BatchApprovePanel from '../components/systems/BatchApprovePanel';
import PtaPanel from '../components/systems/PtaPanel';
import PiaPanel from '../components/systems/PiaPanel';
import type {
  ProfileSectionDetail,
  ProfileSectionType,
  GovernanceStatus,
  ProfileCompletenessResponse,
} from '../types/dashboard';

// ─── Re-export StatusBadge so SystemLayout.tsx sidebar can import it ─────────
export { StatusBadge };

// ─── Page ───────────────────────────────────────────────────────────────────

export default function SystemProfile() {
  const { sectionType: sectionParam } = useParams<{ sectionType: string }>();
  const { detail } = useSystemContext();
  const { settings } = useSettings();

  const [section, setSection] = useState<ProfileSectionDetail | null>(null);
  const [completeness, setCompleteness] = useState<ProfileCompletenessResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [successMsg, setSuccessMsg] = useState<string | null>(null);

  // Privacy analysis state — updated by PtaPanel
  const [ptaCompleted, setPtaCompleted] = useState(false);

  const sectionType = sectionParam as ProfileSectionType;
  const systemId = detail.systemId;
  const callerRole = settings.role || null;

  const isReadOnly = section?.governanceStatus === 'UnderReview'
    || settings.role !== 'MissionOwner';

  // ── Helpers ──────────────────────────────────────────────────────────────

  /** Refresh completeness after any mutation. */
  const refreshCompleteness = useCallback(async () => {
    try {
      const comp = await getProfileCompleteness(systemId);
      setCompleteness(comp);
    } catch {
      // non-critical; swallow
    }
  }, [systemId]);

  const fetchSection = useCallback(async () => {
    try {
      const [sec, comp] = await Promise.all([
        getProfileSection(systemId, sectionType),
        getProfileCompleteness(systemId),
      ]);
      setSection(sec);
      setCompleteness(comp);
      setError(null);
    } catch {
      setSection(null);
    } finally {
      setLoading(false);
    }
  }, [systemId, sectionType]);

  useEffect(() => {
    setLoading(true);
    fetchSection();
  }, [fetchSection]);

  // ── Mutation handlers ────────────────────────────────────────────────────

  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const handleSave = async (content: string, childItems?: Record<string, any>[]) => {
    setSaving(true);
    setError(null);
    setSuccessMsg(null);
    try {
      const result = await saveProfileSection(systemId, sectionType, { content, childItems });
      setSection(result);
      setSuccessMsg('Section saved as Draft.');
      await refreshCompleteness();
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Save failed');
    } finally {
      setSaving(false);
    }
  };

  const handleSubmit = async () => {
    setSaving(true);
    setError(null);
    setSuccessMsg(null);
    try {
      await submitSections(systemId, { action: 'submit', sectionTypes: [sectionType] });
      setSuccessMsg('Section submitted for review.');
      await fetchSection();
      await refreshCompleteness();
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Submit failed');
    } finally {
      setSaving(false);
    }
  };

  const handleWithdraw = async () => {
    if (!confirm('Withdraw this section from review? It will return to Draft status.')) return;
    setSaving(true);
    setError(null);
    setSuccessMsg(null);
    try {
      await withdrawSections(systemId, [sectionType]);
      setSuccessMsg('Section withdrawn from review.');
      await fetchSection();
      await refreshCompleteness();
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Withdraw failed');
    } finally {
      setSaving(false);
    }
  };

  const handleApprove = async () => {
    setSaving(true);
    setError(null);
    setSuccessMsg(null);
    try {
      const result = await reviewSection(systemId, sectionType, { decision: 'approve' });
      setSection(result);
      setSuccessMsg('Section approved.');
      await fetchSection();
      await refreshCompleteness();
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Approve failed');
    } finally {
      setSaving(false);
    }
  };

  const handleRequestRevision = async () => {
    const comments = prompt('Enter revision comments for the Mission Owner:');
    if (!comments) return;
    setSaving(true);
    setError(null);
    setSuccessMsg(null);
    try {
      const result = await reviewSection(systemId, sectionType, { decision: 'request_revision', comments });
      setSection(result);
      setSuccessMsg('Revision requested.');
      await fetchSection();
      await refreshCompleteness();
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Review failed');
    } finally {
      setSaving(false);
    }
  };

  // ── BatchApprovePanel helpers ────────────────────────────────────────────

  /** Called by BatchApprovePanel after any approval to sync state. */
  const handleBatchApproved = useCallback(async () => {
    await fetchSection();
    await refreshCompleteness();
  }, [fetchSection, refreshCompleteness]);

  // Build sections list for BatchApprovePanel from completeness data
  const batchSections = completeness?.incompleteSections.map((s) => ({
    sectionType: s.sectionType as string,
    status: s.status as string,
    label: formatProfileSectionLabel(s.sectionType),
  })) ?? [];

  // ── Derived state ────────────────────────────────────────────────────────

  // A section is "saved" if at least one is in Draft or beyond
  const hasDraftOrBeyond = section !== null && section.governanceStatus !== 'NotStarted';

  // Map section types to their child item arrays
  const getChildItems = () => {
    if (!section) return undefined;
    switch (sectionType) {
      case 'UsersAndAccess': return section.userCategories;
      case 'DataTypes': return section.dataTypeEntries;
      case 'PortsProtocolsAndServices': return section.ppsEntries;
      case 'LeveragedAuthorizations': return section.leveragedAuthorizations;
      default: return undefined;
    }
  };

  if (loading) {
    return <p className="text-gray-500 py-8 text-center">Loading section...</p>;
  }

  const label = formatProfileSectionLabel(sectionType);
  const status: GovernanceStatus = section?.governanceStatus ?? 'NotStarted';

  return (
    <div className="space-y-6">
      {/* Completeness Header */}
      {completeness && (
        <div className="rounded-xl border border-gray-200 bg-white p-5">
          <div className="flex items-center justify-between mb-3">
            <h2 className="text-lg font-semibold text-gray-900">Profile Completeness</h2>
            {completeness.isProfileComplete && (
              <span className="rounded-full bg-green-100 text-green-700 px-3 py-0.5 text-xs font-medium">
                Profile Complete
              </span>
            )}
          </div>
          <div className="w-full bg-gray-200 rounded-full h-2.5">
            <div
              className="bg-indigo-600 h-2.5 rounded-full transition-all"
              style={{ width: `${completeness.approvedPercentage}%` }}
            />
          </div>
          <p className="text-sm text-gray-500 mt-1">
            {completeness.statusCounts['Approved'] ?? 0} / {completeness.totalSections} mandatory sections approved
          </p>
        </div>
      )}

      {/* Section Header */}
      <div className="flex items-center gap-3">
        <h1 className="text-xl font-bold text-gray-900">{label}</h1>
        <StatusBadge status={status} />
        {isReadOnly && settings.role && settings.role !== 'MissionOwner' && (
          <span className="rounded-full bg-gray-100 px-2.5 py-0.5 text-xs font-medium text-gray-500">
            Read-only
          </span>
        )}
      </div>

      {/* Success message */}
      {successMsg && (
        <div className="rounded-lg border border-green-200 bg-green-50 p-3 text-sm text-green-700">{successMsg}</div>
      )}

      {/* Section Form */}
      <div className="rounded-xl border border-gray-200 bg-white p-6">
        <ProfileSectionForm
          sectionType={sectionType}
          governanceStatus={status}
          initialContent={section?.draftContent ?? null}
          initialChildItems={getChildItems()}
          reviewerComments={section?.reviewerComments ?? null}
          isReadOnly={isReadOnly}
          userRole={settings.role}
          isSubmitting={saving}
          error={error}
          systemContext={{
            hostingEnvironment: detail.hostingEnvironment,
            systemType: detail.systemType,
            missionCriticality: detail.missionCriticality,
            impactLevel: detail.impactLevel,
            baselineLevel: detail.baselineLevel,
            categorization: detail.categorization,
          }}
          onSave={handleSave}
          onSubmit={handleSubmit}
          onWithdraw={handleWithdraw}
          onApprove={handleApprove}
          onRequestRevision={handleRequestRevision}
        />
      </div>

      {/* ISSM / SystemOwner: BatchApprovePanel — never shown to MissionOwner */}
      {callerRole !== 'MissionOwner' && (
        <BatchApprovePanel
          systemId={systemId}
          sections={batchSections}
          callerRole={callerRole}
          onApproved={handleBatchApproved}
        />
      )}

      {/* PTA — shown to all roles once at least one section is saved */}
      <PtaPanel
        systemId={systemId}
        sectionsSaved={hasDraftOrBeyond}
        onPtaComplete={setPtaCompleted}
      />

      {/* PIA — shown below PTA; gated on ptaCompleted state */}
      <PiaPanel
        systemId={systemId}
        ptaCompleted={ptaCompleted}
      />
    </div>
  );
}
