import { useState, useEffect, useCallback, useRef, useId, lazy, Suspense } from 'react';
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
import SystemOperationalStatus from '../features/systems/SystemOperationalStatus';
import '../features/systems/systemRecordPages.css';
import '../features/systems/missionRecordForm.css';
import type {
  ProfileSectionDetail,
  ProfileSectionType,
  GovernanceStatus,
  UserCategoryReviewRequest,
} from '../types/dashboard';
import { userDocumentation, type UserDocumentation } from '../features/systems/userCategoryPresentation';
import { dataDocumentation, type DataDocumentation } from '../features/systems/dataTypePresentation';

const SystemDesign = lazy(() => import('../features/system-design/SystemDesign'));

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
  if (sectionType === 'SystemDesign') {
    return <Suspense fallback={<p role="status">Loading System design…</p>}>
      <SystemDesign key={detail.systemId} systemId={detail.systemId} />
    </Suspense>;
  }
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
  const [usersDocumentation, setUsersDocumentation] = useState<UserDocumentation>(userDocumentation([]));
  const [openUserCategoryId, setOpenUserCategoryId] = useState<string | null>(null);
  const userCategoryOpened = useCallback(() => setOpenUserCategoryId(null), []);
  const [dataReadiness, setDataReadiness] = useState<DataDocumentation>(dataDocumentation([]));
  const [openDataTypeId, setOpenDataTypeId] = useState<string | null>(null);
  const dataTypeOpened = useCallback(() => setOpenDataTypeId(null), []);

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
    missionIdentityFields={sectionType === 'MissionAndPurpose' ? {
      name: <MissionIdentity label="System name" value={detail.name} />,
      owner: <SystemOwner systemId={systemId} />,
      acronym: <MissionIdentity label="System acronym" value={detail.acronym} />,
      emass: <MissionIdentity label="eMASS system ID" value={detail.emassId} hint="Required before package reconciliation" />,
      ditpr: <MissionIdentity label="DITPR identifier" value={detail.ditprId} />,
    } : undefined}
    systemDisplayName={detail.name}
    hideChildItems={isPorts}
    onHostingStatusChange={sectionType === 'EnvironmentAndDeployment' ? setHostingStatus : undefined}
    formId={isPorts ? undefined : 'system-profile-editor'}
    onUserDocumentationChange={sectionType === 'UsersAndAccess' ? setUsersDocumentation : undefined}
    openUserCategoryId={openUserCategoryId} onUserCategoryOpened={userCategoryOpened}
    onDataDocumentationChange={sectionType === 'DataTypes' ? setDataReadiness : undefined}
    openDataTypeId={openDataTypeId} onDataTypeOpened={dataTypeOpened}
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
            </button> : ['UsersAndAccess', 'DataTypes'].includes(sectionType) ? <div className="flex flex-wrap gap-3">
              <Link className={systemSecondaryAction} to={`/systems/${systemId}/documents/preview?contribution=${sectionType}`}>Preview SSP contribution</Link>
              {!isReadOnly && <button type="submit" form="system-profile-editor" disabled={saving} className={systemPrimaryAction}>{saving ? 'Saving...' : 'Save Draft'}</button>}
            </div> : !isReadOnly && (headerAddEntry
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
          {sectionType === 'EnvironmentAndDeployment' && <p className="rounded-lg border border-indigo-200 bg-indigo-50 p-3 text-sm text-indigo-900 dark:border-indigo-900 dark:bg-indigo-950 dark:text-indigo-200">
            Prepare for review: complete the applicable deployment, network/location and recovery/operating details.
            Review the visible fields below for missing information. Provider relationships and subscription connections are managed separately.
          </p>}
          <SystemTaskColumns stretch={isPorts} support={isPorts ? <PortsDocumentation systemId={systemId} busy={saving}
            onContext={() => { setError(null); setContextDialogOpen(true); }} /> : <>
            <section className="border-l-2 border-[#d9d3f9] pl-[18px] text-xs text-slate-500">
              <p className="mb-2 text-[10px] font-semibold uppercase tracking-[1.2px]">Used in your package</p>
              <h2 className="mb-2 text-sm font-semibold text-slate-700 dark:text-slate-200">{task.contribution}</h2>
              <p className="mb-3 leading-relaxed">Reviewed records supply the package. Draft edits do not replace the approved baseline.</p>
              <Link className={systemSecondaryAction} to={`/systems/${systemId}/documents/preview?contribution=${sectionType}`}>Preview contribution</Link>
              <p className="mt-2 leading-relaxed">Preview uses current records, not an approved export.</p>
            </section>
            {sectionType === 'UsersAndAccess' && <section aria-label="Users documentation readiness" className="space-y-2 border-l-2 border-[#d9d3f9] pl-[18px] text-xs text-slate-500">
              <p className="text-[10px] font-semibold uppercase tracking-[1.2px]">Section readiness</p>
              <strong className="block text-2xl text-indigo-600">{usersDocumentation.recorded}/{usersDocumentation.total}</strong>
              <p>{usersDocumentation.categories ? `${usersDocumentation.missing} user documentation fields need attention across ${usersDocumentation.categories} categories.` : 'No active categories are recorded.'}</p>
              <p>Counts cover ten documentation fields per active category, not approval or ATO readiness. Optional count/description/sensitivity remain in category details.</p>
              {usersDocumentation.firstIncompleteId && <button type="button" disabled={saving}
                onClick={() => setOpenUserCategoryId(usersDocumentation.firstIncompleteId ?? null)}
                className="text-indigo-700 underline dark:text-indigo-300">
                {usersDocumentation.workloadName ? 'Record identity owner, environment and missing details' : 'Review missing user details'} →
              </button>}
            </section>}
            {sectionType === 'DataTypes' && <section aria-label="Data documentation readiness" className="space-y-2 border-l-2 border-[#d9d3f9] pl-[18px] text-xs text-slate-500">
              <p className="text-[10px] font-semibold uppercase tracking-[1.2px]">Section readiness</p>
              <strong className="block text-2xl text-indigo-600">{dataReadiness.recorded}/{dataReadiness.total}</strong>
              <p>{dataReadiness.types ? `${dataReadiness.missing} information-handling fields need attention across ${dataReadiness.types} types.` : 'No information types are recorded.'}</p>
              <p>Counts cover ten documentation fields plus CUI category where CUI is declared. They do not establish approved categorization, a privacy decision or ATO readiness.</p>
              {dataReadiness.firstIncompleteId && <button type="button" disabled={saving}
                onClick={() => setOpenDataTypeId(dataReadiness.firstIncompleteId ?? null)}
                className="text-indigo-700 underline dark:text-indigo-300">Complete {dataReadiness.firstIncompleteName} handling details →</button>}
              <Link className="block text-indigo-700 underline dark:text-indigo-300" to={`/systems/${systemId}/baseline`}>Review authoritative categorization →</Link>
              <Link className="block text-indigo-700 underline dark:text-indigo-300" to={`/systems/${systemId}/documents`}>Review recorded privacy documents →</Link>
            </section>}
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
          </>}>

          {/* Success message */}
          {successMsg && (
            <div role="status" className="rounded-lg border border-green-200 bg-green-50 p-3 text-sm text-green-700">{successMsg}</div>
          )}

          {/* Section Form */}
          {isPorts && section ? <SystemConnections systemId={systemId} profile={section} profileReadOnly={isReadOnly}
            profileError={error} saving={saving} addOpen={addingProfileEntry}
            onAddClose={() => setAddingProfileEntry(false)} onCanAddChange={setCanAddConnection}
            onSaveProfile={handleSave} /> : <div className={['EnvironmentAndDeployment', 'MissionAndPurpose', 'UsersAndAccess', 'DataTypes'].includes(sectionType) ? 'min-w-0' : systemPanel}>
            {!['MissionAndPurpose', 'UsersAndAccess', 'DataTypes', 'PortsProtocolsAndServices', 'EnvironmentAndDeployment'].includes(sectionType) && <h2 className="mb-5 text-lg font-semibold">{task.record}</h2>}
            {profileForm}
            {sectionType === 'MissionAndPurpose' && <details className="mt-5"><summary className="cursor-pointer text-sm text-indigo-700 dark:text-indigo-300">Operating status and canonical source records</summary>
              <div className="mt-4"><SystemOperationalStatus key={systemId} systemId={systemId} /></div>
              <p className="mt-3 text-xs text-slate-500 dark:text-slate-400">Identity and identifiers come from system registration; the owner comes from System team. Profile saving does not change those records.</p>
              <Link className="mt-3 inline-block text-sm underline" to={`/systems/${systemId}/roles`}>Manage System team assignments</Link>
            </details>}
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

function SystemOwner({ systemId }: { systemId: string }) {
  const id = useId();
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
    <div className="mission-identity-label">
      <label htmlFor={id}>System owner</label>
      <input id={id} readOnly value={error ? 'Unavailable' : name ?? 'Loading owner...'} aria-describedby={`${id}-source`} />
      <span id={`${id}-source`}>Sourced from the System team role assignment</span>
    </div>
    {error && <div className="mt-2 text-xs text-amber-800"><p role="alert">{error}</p>
      <button type="button" className="underline" onClick={() => setAttempt(value => value + 1)}>Retry system owner</button></div>}
  </div>;
}

function MissionIdentity({ label, value, hint }: { label: string; value?: string | null; hint?: string }) {
  const id = useId();
  return <div className="mission-identity-label">
    <label htmlFor={id}>{label}</label>
    <input id={id} readOnly value={value ?? ''} placeholder="Not recorded" aria-describedby={hint ? `${id}-hint` : undefined} />
    {hint && <span id={`${id}-hint`}>{hint}</span>}
  </div>;
}
