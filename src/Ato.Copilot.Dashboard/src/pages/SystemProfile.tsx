import { useState, useEffect, useCallback, useRef, type ReactNode } from 'react';
import { Link, Navigate, useLocation, useParams } from '../features/workspaces/workspaceNavigation';
import { useSystemContext } from '../components/layout/SystemLayout';
import { useSettings } from '../hooks/useSettings';
import { useWorkspaceSession } from '../features/workspaces/WorkspaceBoundary';
import { displayWorkspaceRoles } from '../features/workspaces/workspaceRoles';
import ProfileSectionForm from '../components/forms/ProfileSectionForm';
import { getProfileSection, saveProfileSection, submitSections, withdrawSections, reviewSection, reviewUserCategory } from '../api/systemProfile';
import AsyncErrorState from '../components/AsyncErrorState';
import { systemProfileTasks } from '../features/systems/systemProfileTasks';
import { SystemTaskColumns, SystemTaskHeading, systemPanel, systemPrimaryAction, systemSecondaryAction } from '../features/systems/SystemTaskPresentation';
import SystemTaskNavigation from '../features/systems/SystemTaskNavigation';
import SetupDialog from '../features/workspace-operations/SetupDialog';
import { rolesApi } from '../api/roles';
import SystemConnections from '../features/systems/SystemConnections';
import '../features/systems/systemRecordPages.css';
import type {
  ProfileSectionDetail,
  ProfileSectionType,
  GovernanceStatus,
  UserCategoryReviewRequest,
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

function profileError(error: unknown, fallback: string) {
  if (error && typeof error === 'object' && 'error' in error && typeof error.error === 'string' && error.error.trim()) return error.error;
  return error instanceof Error ? error.message : fallback;
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
  const [reviewAction, setReviewAction] = useState<'withdraw' | 'revision' | 'approve' | null>(null);
  const [reviewComments, setReviewComments] = useState('');
  const [addingProfileEntry, setAddingProfileEntry] = useState(false);
  const [contextDialogOpen, setContextDialogOpen] = useState(false);
  const [hostingStatus, setHostingStatus] = useState('Hosting association · Loading scopes');
  const [canAddConnection, setCanAddConnection] = useState(false);
  const writing = useRef(false);

  const sectionType = sectionParam as ProfileSectionType;
  const isPorts = sectionType === 'PortsProtocolsAndServices';
  const systemId = detail.systemId;
  const headerAddEntry = sectionType === 'UsersAndAccess' || sectionType === 'DataTypes';
  const requestVersion = useRef(0);
  const isReadOnly = loading || computeIsReadOnly(sectionType === 'UsersAndAccess' ? undefined : section?.governanceStatus, section?.canEditProfile);

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

  const handleSave = async (content: string, childItems?: unknown[]) => {
    if (writing.current) return false;
    writing.current = true;
    setSaving(true);
    setError(null);
    setSuccessMsg(null);
    try {
      const result = await saveProfileSection(systemId, sectionType, { content, childItems });
      setSection(result);
      setContextDialogOpen(false);
      setSuccessMsg(result.userCategories?.some(row => row.pendingDeletion)
        ? 'Draft saved. Removal requests require individual review before approved categories are removed.'
        : sectionType === 'UsersAndAccess' ? 'Users working data saved. This does not approve any category.' : 'Section saved as Draft.');
      return true;
    } catch (err: unknown) {
      setError(profileError(err, 'Save failed'));
      return false;
    } finally {
      writing.current = false;
      setSaving(false);
    }
  };

  const handleSubmit = async () => {
    if (writing.current) return;
    writing.current = true;
    setSaving(true);
    setError(null);
    setSuccessMsg(null);
    try {
      const result = await submitSections(systemId, { action: 'submit', sectionTypes: [sectionType] });
      if (!result.submittedSections.includes(sectionType)) {
        throw new Error(result.skippedSections.find(item => item.sectionType === sectionType)?.reason ?? 'The server did not confirm submission of this section.');
      }
      setSuccessMsg(sectionType === 'UsersAndAccess' ? 'Access context submitted for review. Categories are reviewed separately.' : 'Section submitted for review.');
      setContextDialogOpen(false);
      await fetchSection();
    } catch (err: unknown) {
      setError(profileError(err, 'Submit failed'));
    } finally {
      writing.current = false;
      setSaving(false);
    }
  };

  const handleUserCategoryReview = async (categoryId: string, request: UserCategoryReviewRequest): Promise<boolean> => {
    if (writing.current) return false;
    writing.current = true; setSaving(true); setError(null); setSuccessMsg(null);
    try {
      const approvingRemoval = request.action === 'approve' && section?.userCategories.some(row => row.id === categoryId && row.pendingDeletion) === true;
      const result = await reviewUserCategory(systemId, categoryId, request, approvingRemoval);
      setSection(result);
      setSuccessMsg(approvingRemoval ? 'User category removal approved.' : { submit: 'User category submitted for review.', withdraw: 'User category withdrawn to Draft.',
        approve: 'User category approved.', request_revision: 'Revision requested for this user category.' }[request.action]);
      return true;
    } catch (reason) {
      setError(profileError(reason, 'User category review failed.'));
      return false;
    } finally {
      writing.current = false; setSaving(false);
    }
  };

  const handleWithdraw = async () => {
    if (writing.current) return;
    writing.current = true;
    setSaving(true);
    setError(null);
    setSuccessMsg(null);
    try {
      const result = await withdrawSections(systemId, [sectionType]);
      if (!result.withdrawnSections.includes(sectionType)) {
        throw new Error(result.skippedSections.find(item => item.sectionType === sectionType)?.reason ?? 'The server did not confirm withdrawal of this section.');
      }
      setSuccessMsg(sectionType === 'UsersAndAccess' ? 'Access context withdrawn from review.' : 'Section withdrawn from review.');
      setReviewAction(null);
      await fetchSection();
    } catch (err: unknown) {
      setError(profileError(err, 'Withdraw failed'));
    } finally {
      writing.current = false;
      setSaving(false);
    }
  };

  const handleApprove = async () => {
    if (writing.current) return;
    writing.current = true;
    setSaving(true);
    setError(null);
    setSuccessMsg(null);
    try {
      const result = await reviewSection(systemId, sectionType, { decision: 'approve' });
      if (result.governanceStatus !== 'Approved') throw new Error('The server did not confirm section approval.');
      setSection(result);
      setSuccessMsg(sectionType === 'UsersAndAccess' ? 'Access context approved. Category reviews are unchanged.' : 'Section approved.');
      setReviewAction(null);
    } catch (err: unknown) {
      setError(profileError(err, 'Approve failed'));
    } finally {
      writing.current = false;
      setSaving(false);
    }
  };

  const handleRequestRevision = async () => {
    const comments = reviewComments.trim();
    if (!comments) { setError('Enter revision comments before requesting changes.'); return; }
    if (writing.current) return;
    writing.current = true;
    setSaving(true);
    setError(null);
    setSuccessMsg(null);
    try {
      const result = await reviewSection(systemId, sectionType, { decision: 'request_revision', comments });
      if (result.governanceStatus !== 'NeedsRevision') throw new Error('The server did not confirm the revision request.');
      setSection(result);
      setSuccessMsg(sectionType === 'UsersAndAccess' ? 'Access-context revision requested.' : 'Revision requested.');
      setReviewAction(null);
    } catch (err: unknown) {
      setError(profileError(err, 'Review failed'));
    } finally {
      writing.current = false;
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

  const profileForm = <ProfileSectionForm
    hideChildItems={isPorts}
    onHostingStatusChange={sectionType === 'EnvironmentAndDeployment' ? setHostingStatus : undefined}
    formId={isPorts || headerAddEntry || sectionType === 'EnvironmentAndDeployment' ? undefined : 'system-profile-editor'}
    addEntryOpen={headerAddEntry && addingProfileEntry}
    onAddEntryClose={headerAddEntry ? () => setAddingProfileEntry(false) : undefined}
    onReviewUserCategory={sectionType === 'UsersAndAccess' ? handleUserCategoryReview : undefined}
    contextDialogOpen={headerAddEntry && contextDialogOpen}
    onContextDialogClose={() => setContextDialogOpen(false)}
    systemId={systemId} sectionType={sectionType} governanceStatus={status}
    initialContent={section?.draftContent ?? null} initialChildItems={getChildItems()}
    reviewerComments={section?.reviewerComments ?? null} isReadOnly={isReadOnly}
    userRole={settings.role} effectiveRoles={workspace ? displayWorkspaceRoles(workspace.roles) : undefined}
    isSubmitting={saving} error={error}
    systemContext={{ hostingEnvironment: detail.hostingEnvironment, systemType: detail.systemType,
      missionCriticality: detail.missionCriticality, impactLevel: detail.impactLevel,
      baselineLevel: detail.baselineLevel, categorization: detail.categorization }}
    onSave={handleSave} onSubmit={handleSubmit}
    onWithdraw={() => { setError(null); setContextDialogOpen(false); setReviewAction('withdraw'); }}
    onApprove={() => { setError(null); setContextDialogOpen(false); setReviewAction('approve'); }}
    onRequestRevision={() => { setError(null); setContextDialogOpen(false); setReviewComments(''); setReviewAction('revision'); }}
  />;

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
        <div className={isPorts ? 'ports-workspace' : 'space-y-6'}>
          {/* Section Header */}
          <SystemTaskHeading eyebrow={detail.name} title={task.title} description={task.description}
            action={isPorts ? <button type="button" disabled={saving || !canAddConnection}
              onClick={() => { setError(null); setAddingProfileEntry(true); }} className={systemPrimaryAction}>
              Add connection<span aria-hidden="true"> →</span>
            </button> : sectionType === 'EnvironmentAndDeployment'
              ? undefined
              : !isReadOnly && (headerAddEntry
              ? <button type="button" disabled={saving} onClick={() => setAddingProfileEntry(true)} className={systemPrimaryAction}>
                {sectionType === 'DataTypes' ? 'Add data type' : 'Add user category'}
              </button>
              : <button type="submit" form="system-profile-editor" disabled={saving} className={systemPrimaryAction}>
              {saving ? 'Saving...' : 'Save Draft'}
            </button>)} />
          {sectionType !== 'LeveragedAuthorizations' && <SystemTaskNavigation definitionOnly />}
          <div aria-label="Profile review status" className="mb-[22px] flex flex-wrap items-center justify-between gap-3 rounded-lg border border-[#dedaf5] bg-[#f1effc] px-[17px] py-3 text-xs dark:border-indigo-900 dark:bg-indigo-950">
            <div className="flex flex-wrap items-center gap-2">
            <span className={sectionType === 'EnvironmentAndDeployment' || isPorts ? 'text-xs'
              : `rounded-full px-2.5 py-0.5 text-xs font-medium ${approvalVariant(status)}`}>
              {isPorts ? `Network profile: ${status}` : sectionType === 'EnvironmentAndDeployment' ? hostingStatus : sectionType === 'UsersAndAccess' ? `Access context: ${status}`
                : sectionType === 'DataTypes' ? `Data profile: ${status}` : status}
            </span>
            {isReadOnly && (
              <span className="rounded-full bg-gray-100 px-2.5 py-0.5 text-xs font-medium text-gray-500">
                {isPorts ? 'Network profile read-only' : 'Read-only'}
              </span>
            )}
            </div>
            <span>{isPorts ? 'Saving does not approve connectivity or agreements' : sectionType === 'UsersAndAccess' ? 'Each user category is reviewed individually · Saving does not approve'
              : sectionType === 'EnvironmentAndDeployment' ? `Deployment profile: ${status} · Association does not authorize`
              : sectionType === 'DataTypes' ? 'Context and information types are reviewed together · Recording sensitivity does not approve categorization'
              : 'Mission Owner authors / ISSM reviews · Saving does not approve'}</span>
          </div>
          <SystemTaskColumns stretch={isPorts} support={isPorts ? <PortsDocumentation systemId={systemId} busy={saving}
            onContext={() => { setError(null); setContextDialogOpen(true); }} /> : <ProfileDocumentation environment={sectionType === 'EnvironmentAndDeployment'}>
            <section className="border-l-2 border-[#d9d3f9] pl-[18px] text-xs text-slate-500">
              <p className="mb-2 text-[10px] font-semibold uppercase tracking-[1.2px]">Used in your package</p>
              <h2 className="mb-2 text-sm font-semibold text-slate-700 dark:text-slate-200">{task.contribution}</h2>
              <p className="mb-3 leading-relaxed">Reviewed records supply the package. Draft edits do not replace the approved baseline.</p>
              <Link className={systemSecondaryAction} to={`/systems/${systemId}/documents/preview?contribution=${sectionType}`}>Preview contribution</Link>
              <p className="mt-2 leading-relaxed">Preview uses current records, not an approved export.</p>
            </section>
            <section className="space-y-2 border-l-2 border-[#d9d3f9] pl-[18px] text-xs text-slate-500">
              <p className="text-[10px] font-semibold uppercase tracking-[1.2px]">Review &amp; ownership</p>
              <h2 className="text-sm font-semibold text-slate-700 dark:text-slate-200">Keep the next action clear</h2>
              <p>{sectionType === 'UsersAndAccess' ? 'Access context' : 'Current section'}: <strong>{status}</strong></p>
              {sectionType === 'UsersAndAccess' && section?.userCategoriesReview && <p>
                Categories: {section.userCategoriesReview.approvedCount} approved · {section.userCategoriesReview.underReviewCount} under review · {section.userCategoriesReview.pendingDeletionCount} removal requests.
              </p>}
              <p>{isReadOnly ? 'This record is read-only with your current access or review state.'
                : sectionType === 'UsersAndAccess' ? 'Save changes, then open a category to submit or review that record. Other categories keep their own status.'
                  : 'Save your changes, then submit the saved draft for review.'}</p>
              <p>Last edited: {section?.lastEditedAt ?? 'Not recorded'}</p>
              <p>Approved snapshot: {section?.approvedContent ? 'Retained' : 'Not recorded'}</p>
              {sectionType === 'UsersAndAccess' && <button type="button" disabled={saving} onClick={() => { setError(null); setContextDialogOpen(true); }}
                className="block text-indigo-700 underline disabled:opacity-50 dark:text-indigo-300">Manage access context</button>}
              {sectionType === 'DataTypes' && <button type="button" disabled={saving} onClick={() => { setError(null); setContextDialogOpen(true); }}
                className="block text-indigo-700 underline disabled:opacity-50 dark:text-indigo-300">Manage information handling context</button>}
              {section?.reviewerComments && <p className="whitespace-pre-wrap break-words">Reviewer comments: {section.reviewerComments}</p>}
              <Link className="inline-block underline" to={`/systems/${systemId}/roles`}>Review System team</Link>
              <Link className="block underline" to={`/systems/${systemId}/history`}>View activity history</Link>
            </section>
            <section className="space-y-3 border-l-2 border-[#d9d3f9] pl-[18px] text-xs text-slate-500">
              <p className="text-[10px] font-semibold uppercase tracking-[1.2px]">Related work</p>
              <Link className={systemSecondaryAction} to={`/systems/${systemId}/documents?purpose=InitialSubmission`}>View package readiness</Link>
              {task.next.map(next => <Link key={next.path} className="block text-indigo-700 underline dark:text-indigo-300"
                to={`/systems/${encodeURIComponent(systemId)}/${next.path}`}>{next.label}</Link>)}
              {sectionType === 'EnvironmentAndDeployment' && <>
                <Link className="block text-indigo-700 underline dark:text-indigo-300" to={`/systems/${encodeURIComponent(systemId)}/assessments/environment`}>
                  Assessments: configure Azure assessment
                </Link>
                <p>Assessment collection is separate from the recorded hosting association.</p>
              </>}
            </section>
          </ProfileDocumentation>}>

          {/* Success message */}
          {successMsg && (
            <div role="status" className="rounded-lg border border-green-200 bg-green-50 p-3 text-sm text-green-700">{successMsg}</div>
          )}

          {/* Section Form */}
          {isPorts && section ? <SystemConnections systemId={systemId} profile={section} profileReadOnly={isReadOnly}
            profileError={error} saving={saving} addOpen={addingProfileEntry}
            onAddClose={() => setAddingProfileEntry(false)} onCanAddChange={setCanAddConnection}
            onSaveProfile={handleSave} /> : <div className={sectionType === 'EnvironmentAndDeployment' ? 'min-w-0' : systemPanel}>
            {!['UsersAndAccess', 'DataTypes', 'PortsProtocolsAndServices', 'EnvironmentAndDeployment'].includes(sectionType) && <h2 className="mb-5 text-lg font-semibold">{task.record}</h2>}
            {sectionType === 'MissionAndPurpose' && <div className="mb-5 grid gap-[18px] sm:grid-cols-2">
              <label className="text-xs text-slate-600">System name<input readOnly value={detail.name ?? systemId}
                className="mt-1.5 block w-full rounded-[7px] border border-slate-300 bg-slate-50 px-3 py-2.5 text-sm" /></label>
              <SystemOwner systemId={systemId} />
              <p className="text-xs text-slate-500 sm:col-span-2">Identity comes from the system registration. Manage owner assignments in System team; profile saving does not change either.</p>
            </div>}
            {profileForm}
            {sectionType !== 'EnvironmentAndDeployment' && <p className="mt-4 rounded-lg bg-[#f4f5f9] p-4 text-xs leading-relaxed text-slate-700 dark:bg-slate-800 dark:text-slate-200">{task.guidance}</p>}
          </div>}
          </SystemTaskColumns>
          {isPorts && <p className="ports-workflow-note">Inputs → reviewed records → document output → ongoing change review</p>}
          {isPorts && contextDialogOpen && <SetupDialog placement="right" title="Communication context & review"
            description="Manage the network overview and review the saved network-interface profile. External interconnection agreements are reviewed separately."
            busy={saving} onClose={() => setContextDialogOpen(false)}>
            <div className="mb-4 space-y-2 text-xs text-slate-500">
              <p>Network profile: {status}</p><p>Last edited: {section?.lastEditedAt ?? 'Not recorded'}</p>
              <p>Approved snapshot: {section?.approvedContent ? 'Retained' : 'Not recorded'}</p>
            </div>
            {profileForm}
          </SetupDialog>}
          {reviewAction && <SetupDialog title={sectionType === 'UsersAndAccess'
            ? reviewAction === 'revision' ? 'Request access-context revision' : reviewAction === 'approve' ? 'Approve access context' : 'Withdraw access context from review'
            : reviewAction === 'revision' ? 'Request profile revision' : reviewAction === 'approve' ? 'Approve profile section' : 'Withdraw profile from review'}
            description={reviewAction === 'revision' ? 'Explain what must change in the saved section.' : reviewAction === 'approve'
              ? sectionType === 'UsersAndAccess' ? 'Approve only the saved access context. Individual user-category reviews are unchanged.'
                : 'Approve the saved scalar values and structured rows together. This does not issue an ATO.'
              : 'The saved section will return to Draft. No approved baseline is deleted.'}
            busy={saving} onClose={() => setReviewAction(null)}>
            <form className="space-y-4" onSubmit={event => { event.preventDefault(); void (reviewAction === 'revision' ? handleRequestRevision() : reviewAction === 'approve' ? handleApprove() : handleWithdraw()); }}>
              {error && <p role="alert" className="text-sm text-red-700">{error}</p>}
              {reviewAction === 'revision' && <label className="block text-sm">Revision comments
                <textarea autoFocus required maxLength={4000} disabled={saving} className="mt-2 block w-full rounded border p-3"
                  value={reviewComments} onChange={event => setReviewComments(event.target.value)} />
              </label>}
              <div className="flex flex-wrap justify-end gap-3">
                <button type="button" disabled={saving} className={systemSecondaryAction} onClick={() => setReviewAction(null)}>Cancel</button>
                <button type="submit" disabled={saving || reviewAction === 'revision' && !reviewComments.trim()} className={systemPrimaryAction}>
                  {sectionType === 'UsersAndAccess'
                    ? reviewAction === 'revision' ? 'Request access-context revision' : reviewAction === 'approve' ? 'Approve access context' : 'Withdraw access context'
                    : reviewAction === 'revision' ? 'Request revision' : reviewAction === 'approve' ? 'Approve section' : 'Withdraw section'}
                </button>
              </div>
            </form>
          </SetupDialog>}
        </div>
      )}

    </div>
  );
}

function PortsDocumentation({ systemId, busy, onContext }: {
  systemId: string; busy: boolean; onContext: () => void;
}) {
  return <>
    <section className="ports-support">
      <h2 className="mb-2 text-[10px] font-semibold uppercase tracking-[1.2px] text-slate-500">Used in your package</h2>
      <h3 className="text-sm font-semibold">SSP · Network interfaces / Interconnection register</h3>
      <p>This page contributes communication records to the SSP. Draft edits must not replace the approved baseline.</p>
      <Link className={systemSecondaryAction} to={`/systems/${systemId}/documents/preview?contribution=PortsProtocolsAndServices`}>
        Preview contribution<span aria-hidden="true"> →</span>
      </Link>
    </section>
    <section className="ports-support">
      <h2 className="mb-2 text-[10px] font-semibold uppercase tracking-[1.2px] text-slate-500">Review &amp; ownership</h2>
      <h3 className="text-sm font-semibold">Keep the next action clear</h3>
      <p>Review the network profile separately from external agreements and authorization to connect.</p>
      <button type="button" disabled={busy} className="text-xs text-indigo-700 underline disabled:opacity-50" onClick={onContext}>Manage communication context &amp; review</button>
    </section>
    <section className="ports-support">
      <h2 className="mb-2 text-[10px] font-semibold uppercase tracking-[1.2px] text-slate-500">Related work</h2>
      <Link className={systemSecondaryAction} to={`/systems/${systemId}/documents?purpose=InitialSubmission`}>
        View package readiness<span aria-hidden="true"> →</span>
      </Link>
    </section>
  </>;
}

function ProfileDocumentation({ environment, children }: { environment: boolean; children: ReactNode }) {
  if (!environment) return <>{children}</>;
  return <div className="space-y-3">
    <p className="text-xs leading-relaxed text-slate-500 dark:text-slate-300">Contributes to your SSP’s environment and hosting section.</p>
    <section aria-label="Documentation & review" className="rounded-lg border border-slate-200 bg-white p-4 dark:border-slate-700 dark:bg-slate-900">
      <h2 className="text-sm font-semibold">Documentation &amp; review</h2>
      <div className="mt-4 space-y-5">{children}</div>
    </section>
  </div>;
}

function SystemOwner({ systemId }: { systemId: string }) {
  const [name, setName] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [attempt, setAttempt] = useState(0);
  useEffect(() => {
    let current = true;
    setName(null); setError(null);
    void rolesApi.getSystemRoles(systemId).then(result => {
      if (!current) return;
      if (result.systemId !== systemId) throw new Error('Owner context does not match this system.');
      const owner = result.roles.find(role => role.role === 'SystemOwner');
      if (!owner) throw new Error('System owner role was not returned.');
      setName(owner.person?.displayName ?? 'Not assigned');
    }).catch(reason => { if (current) setError(profileError(reason, 'System owner is unavailable.')); });
    return () => { current = false; };
  }, [systemId, attempt]);
  return <div>
    <label className="text-xs text-slate-600">System owner<input readOnly value={error ? 'Unavailable' : name ?? 'Loading owner...'}
      className="mt-1.5 block w-full rounded-[7px] border border-slate-300 bg-slate-50 px-3 py-2.5 text-sm" /></label>
    {error && <div className="mt-2 text-xs text-amber-800"><p role="alert">{error}</p>
      <button type="button" className="underline" onClick={() => setAttempt(value => value + 1)}>Retry system owner</button></div>}
  </div>;
}
