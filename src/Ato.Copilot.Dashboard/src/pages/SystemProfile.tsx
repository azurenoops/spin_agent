import { useState, useEffect, useCallback, useRef } from 'react';
import { Link, Navigate, useLocation, useParams } from '../features/workspaces/workspaceNavigation';
import { useSystemContext } from '../components/layout/SystemLayout';
import { useSettings } from '../hooks/useSettings';
import { useWorkspaceSession } from '../features/workspaces/WorkspaceBoundary';
import { displayWorkspaceRoles } from '../features/workspaces/workspaceRoles';
import ProfileSectionForm from '../components/forms/ProfileSectionForm';
import { getProfileSection, saveProfileSection, submitSections, withdrawSections, reviewSection } from '../api/systemProfile';
import AsyncErrorState from '../components/AsyncErrorState';
import SystemHostingSummary from '../features/systems/SystemHostingSummary';
import { systemProfileTasks } from '../features/systems/systemProfileTasks';
import { SystemTaskColumns, SystemTaskHeading, SystemTaskSupport, systemPanel } from '../features/systems/SystemTaskPresentation';
import SystemInterconnections from '../features/systems/SystemInterconnections';
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

  const task = systemProfileTasks[sectionType];
  const status: GovernanceStatus = section?.governanceStatus ?? 'NotStarted';
  if (!task) return <p role="alert">This system profile section is not available.</p>;

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
          <SystemTaskHeading title={task.title} description={task.description} status={<div className="flex flex-wrap items-center gap-2">
            <span className={`rounded-full px-2.5 py-0.5 text-xs font-medium ${approvalVariant(status)}`}>
              {status}
            </span>
            {isReadOnly && (
              <span className="rounded-full bg-gray-100 px-2.5 py-0.5 text-xs font-medium text-gray-500">
                Read-only
              </span>
            )}
          </div>} />
          <SystemTaskColumns support={<>
            <SystemTaskSupport title="Contributes to"><p>{task.contribution}</p><p>{task.guidance}</p></SystemTaskSupport>
            <SystemTaskSupport title="Review state">
              <p>Current section: <strong>{status}</strong></p>
              <p>{isReadOnly ? 'This record is read-only with your current access or review state.' : 'You can edit this draft. Saving does not approve the record.'}</p>
              {section?.reviewerComments && <p className="whitespace-pre-wrap">{section.reviewerComments}</p>}
            </SystemTaskSupport>
            <SystemTaskSupport title="Next in Systems">
              {task.next.map(next => <Link key={next.path} className="block text-indigo-700 underline dark:text-indigo-300"
                to={`/systems/${encodeURIComponent(systemId)}/${next.path}`}>{next.label}</Link>)}
            </SystemTaskSupport>
          </>}>

          {/* Success message */}
          {successMsg && (
            <div className="rounded-lg border border-green-200 bg-green-50 p-3 text-sm text-green-700">{successMsg}</div>
          )}

          {/* Section Form */}
          {sectionType === 'EnvironmentAndDeployment' && <SystemHostingSummary systemId={systemId} />}
          <div className={systemPanel}>
            {!['UsersAndAccess', 'DataTypes', 'PortsProtocolsAndServices'].includes(sectionType) && <h2 className="mb-5 text-lg font-semibold">{task.record}</h2>}
            {sectionType === 'MissionAndPurpose' && <dl className="mb-6 grid gap-4 border-b border-slate-100 pb-5 text-sm sm:grid-cols-2 dark:border-slate-700">
              <div><dt className="text-xs text-slate-500">System name</dt><dd className="mt-1 font-medium">{detail.name ?? systemId}</dd></div>
              <div><dt className="text-xs text-slate-500">Accountable system roles</dt><dd className="mt-1">
                <Link className="text-indigo-700 underline dark:text-indigo-300" to={`/systems/${encodeURIComponent(systemId)}/roles`}>Review System team</Link>
              </dd></div>
            </dl>}
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
          {sectionType === 'PortsProtocolsAndServices' && <SystemInterconnections key={systemId} systemId={systemId} />}
          {sectionType === 'EnvironmentAndDeployment' && <p className="text-sm text-gray-600 dark:text-gray-300">
            Azure scan configuration is a separate task in{' '}
            <Link className="underline" to={`/systems/${encodeURIComponent(systemId)}/assessments/environment`}>
              Assessments: configure Azure assessment
            </Link>. Track overall profile completeness on the <Link className="underline" to={`/systems/${encodeURIComponent(systemId)}`}>system overview</Link>.
          </p>}
          </SystemTaskColumns>
        </div>
      )}

    </div>
  );
}
