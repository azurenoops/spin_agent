import { useState, useEffect, useCallback, useRef } from 'react';
import { Link, Navigate, useLocation, useParams } from '../features/workspaces/workspaceNavigation';
import { useSystemContext } from '../components/layout/SystemLayout';
import { useSettings } from '../hooks/useSettings';
import { useWorkspaceSession } from '../features/workspaces/WorkspaceBoundary';
import { displayWorkspaceRoles } from '../features/workspaces/workspaceRoles';
import ProfileSectionForm from '../components/forms/ProfileSectionForm';
import { getProfileSection, saveProfileSection, submitSections, withdrawSections, reviewSection } from '../api/systemProfile';
import { formatProfileSectionLabel } from '../utils/profileSections';
import AsyncErrorState from '../components/AsyncErrorState';
import type {
  ProfileSectionDetail,
  ProfileSectionType,
  GovernanceStatus,
} from '../types/dashboard';

// ─── Governance badge color mapping ─────────────────────────────────────────

function approvalVariant(status: GovernanceStatus) {
  switch (status) {
    case 'NotStarted': return 'bg-gray-100 text-gray-600';
    case 'Draft': return 'bg-amber-100 text-amber-700';
    case 'UnderReview': return 'bg-indigo-100 text-indigo-700';
    case 'Approved': return 'bg-green-100 text-green-700';
    case 'NeedsRevision': return 'bg-red-100 text-red-700';
    default: return 'bg-gray-100 text-gray-600';
  }
}

export function computeIsReadOnly(governanceStatus: string | undefined, canEditProfile: boolean | undefined): boolean {
  return governanceStatus === 'UnderReview' || canEditProfile !== true;
}

// ─── Page ───────────────────────────────────────────────────────────────────

export default function SystemProfile() {
  const { sectionType } = useParams<{ sectionType: string }>();
  const { detail } = useSystemContext();
  const location = useLocation();
  if (sectionType === 'EnvironmentAndDeployment' && location.hash === '#azure-assessment-environment') {
    return <Navigate replace to={`/systems/${encodeURIComponent(detail.systemId)}/assessments/environment#azure-assessment-environment`} />;
  }
  return <SystemProfileSection key={`${detail.systemId}/${sectionType}`} />;
}

function SystemProfileSection() {
  const { sectionType: sectionParam } = useParams<{ sectionType: string }>();
  const { detail } = useSystemContext();
  const { settings } = useSettings();
  const workspace = useWorkspaceSession();

  const [section, setSection] = useState<ProfileSectionDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [successMsg, setSuccessMsg] = useState<string | null>(null);

  const sectionType = sectionParam as ProfileSectionType;
  const systemId = detail.systemId;
  const requestVersion = useRef(0);
  const isReadOnly = loading || computeIsReadOnly(section?.governanceStatus, section?.canEditProfile);

  const fetchSection = useCallback(async () => {
    const version = ++requestVersion.current;
    setLoading(true);
    try {
      const sec = await getProfileSection(systemId, sectionType);
      if (version !== requestVersion.current) return;
      setSection(sec);
      setError(null);
    } catch {
      if (version !== requestVersion.current) return;
      setSection(null);
      setError('Unable to load profile section.');
    } finally {
      if (version === requestVersion.current) setLoading(false);
    }
  }, [systemId, sectionType]);

  useEffect(() => {
    fetchSection();
    return () => { requestVersion.current++; };
  }, [fetchSection]);

  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const handleSave = async (content: string, childItems?: Record<string, any>[]) => {
    setSaving(true);
    setError(null);
    setSuccessMsg(null);
    try {
      const result = await saveProfileSection(systemId, sectionType, { content, childItems });
      setSection(result);
      setSuccessMsg('Section saved as Draft.');
    } catch (err: unknown) {
      const message = err && typeof err === 'object' && 'error' in err ? err.error : undefined;
      setError(typeof message === 'string' && message.trim() ? message : err instanceof Error ? err.message : 'Save failed');
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
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Review failed');
    } finally {
      setSaving(false);
    }
  };

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

  const label = formatProfileSectionLabel(sectionType);
  const status: GovernanceStatus = section?.governanceStatus ?? 'NotStarted';

  return (
    <div className="space-y-6">
      {loading ? (
        <p className="text-gray-500 py-8 text-center">Loading section...</p>
      ) : error && !section ? (
        <AsyncErrorState
          title={error}
          onRetry={() => {
            setLoading(true);
            void fetchSection();
          }}
        />
      ) : (
        <div className="space-y-6">
          {/* Section Header */}
          <div className="flex items-center gap-3">
            <h1 className="text-xl font-bold text-gray-900 dark:text-gray-100">{label}</h1>
            <span className={`rounded-full px-2.5 py-0.5 text-xs font-medium ${approvalVariant(status)}`}>
              {status}
            </span>
            {isReadOnly && (
              <span className="rounded-full bg-gray-100 px-2.5 py-0.5 text-xs font-medium text-gray-500">
                Read-only
              </span>
            )}
          </div>
          {sectionType === 'EnvironmentAndDeployment' && <p className="text-sm text-gray-600 dark:text-gray-300">
            Where does this system run, and which provider services does it use?
            {' '}Describe the environment below. Network, recovery and operating details can be expanded when needed.
          </p>}

          {/* Success message */}
          {successMsg && (
            <div className="rounded-lg border border-green-200 bg-green-50 p-3 text-sm text-green-700">{successMsg}</div>
          )}

          {/* Section Form */}
          <div className="rounded-xl border border-gray-200 bg-white p-6 dark:border-gray-700 dark:bg-gray-900 dark:text-gray-100">
            <ProfileSectionForm
              systemId={systemId}
              sectionType={sectionType}
              governanceStatus={status}
              initialContent={section?.draftContent ?? null}
              initialChildItems={getChildItems()}
              reviewerComments={section?.reviewerComments ?? null}
              isReadOnly={isReadOnly}
              userRole={settings.role}
              effectiveRoles={workspace ? displayWorkspaceRoles(workspace.roles) : undefined}
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
          {sectionType === 'EnvironmentAndDeployment' && <p className="text-sm text-gray-600 dark:text-gray-300">
            Azure scan configuration is a separate task in{' '}
            <Link className="underline" to={`/systems/${encodeURIComponent(systemId)}/assessments/environment`}>
              Assessments: configure Azure assessment
            </Link>. Track overall profile completeness on the <Link className="underline" to={`/systems/${encodeURIComponent(systemId)}`}>system overview</Link>.
          </p>}
        </div>
      )}

    </div>
  );
}
