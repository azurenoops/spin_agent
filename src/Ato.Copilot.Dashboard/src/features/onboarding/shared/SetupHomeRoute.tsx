import { useEffect, useState } from 'react';
import axios from 'axios';
import { useWorkspaceSession, WorkspaceStatus, type WorkspaceSession } from '../../workspaces/WorkspaceBoundary';
import { useNavigate } from '../../workspaces/workspaceNavigation';
import { workspaceErrorMessage } from '../../workspaces/api';
import { listOrganizationDrafts } from '../../workspace-operations/organizationOnboardingApi';
import { getSetup as getProviderSetup } from '../../csp-onboarding/providerSetupApi';
import { WorkspaceOperationError } from '../../workspace-operations/workspaceRequest';
import { PackageImportError } from '../../package-imports/request';
import { tenantWizard } from '../TenantWizard/api';
import { getSystemSetupAccess, listSystemSetupDrafts } from '../systemSetupApi';
import SetupHome, { type SavedSetupRecord, type SetupOption } from './SetupHome';

interface HomeState {
  options: SetupOption[];
  records: SavedSetupRecord[];
  nextPage: number | null;
  nextCursor: string | null;
}

export default function SetupHomeRoute({ mode = 'start' }: { mode?: 'start' | 'resume' }) {
  const session = useWorkspaceSession();
  if (!session || session.target.kind === 'organization' && session.target.mode === 'support') {
    return <WorkspaceStatus message="Choose an ordinary authorized workspace to create or resume setup. Support access does not enroll you in an organization." />;
  }
  return <AuthorizedSetupHome key={`${session.identity.directoryTenantId}:${session.identity.oid}:${session.workspace.kind}:${session.workspace.tenantId}`}
    session={session} mode={mode} />;
}

function AuthorizedSetupHome({ session, mode }: { session: WorkspaceSession; mode: 'start' | 'resume' }) {
  const navigate = useNavigate();
  const [data, setData] = useState<HomeState>({ options: [], records: [], nextPage: null, nextCursor: null });
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [reload, setReload] = useState(0);
  const [page, setPage] = useState(1);
  const [cursor, setCursor] = useState<string | null>(null);
  const kind = session.target.kind;
  const tenantId = session.workspace.tenantId;
  const canAccessCsp = session.workspace.permissions.canAccessCsp;
  const canManageOrganization = session.workspace.permissions.canManageOrganization;

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setError(null);
    loadSetup(kind, tenantId, canAccessCsp, canManageOrganization, controller.signal, page, cursor ?? undefined)
      .then(result => {
        if (!controller.signal.aborted) setData(previous => ({
          ...result,
          records: page === 1 && !cursor ? result.records
            : [...new Map([...previous.records, ...result.records].map(record => [`${record.kind}:${record.id}`, record])).values()],
        }));
      })
      .catch(reason => {
        if (!controller.signal.aborted) {
          const accessLost = kind === 'csp' && !canAccessCsp
            || (reason instanceof WorkspaceOperationError || reason instanceof PackageImportError)
              && (reason.status === 401 || reason.status === 403)
            || axios.isAxiosError(reason) && (reason.response?.status === 401 || reason.response?.status === 403);
          setData(previous => accessLost
            ? { options: [], records: [], nextPage: null, nextCursor: null }
            : { ...previous, options: [] });
          setError(workspaceErrorMessage(reason));
        }
      })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [kind, tenantId, canAccessCsp, canManageOrganization, reload, page, cursor]);

  return <SetupHome mode={mode} workspaceName={session.workspace.displayName} options={data.options}
    records={data.records} loading={loading} error={error} onRetry={() => {
      setPage(1); setCursor(null); setReload(value => value + 1);
    }}
    onLoadMore={!error && (data.nextPage || data.nextCursor) ? () => {
      if (data.nextPage) setPage(data.nextPage);
      else setCursor(data.nextCursor);
    } : undefined}
    onChangeMode={next => navigate(next === 'resume' ? '/setup/resume' : '/setup')} />;
}

async function loadSetup(kind: 'csp' | 'organization', tenantId: string | null, canAccessCsp: boolean, canManageOrganization: boolean, signal: AbortSignal, page: number, cursor?: string): Promise<HomeState> {
  if (kind === 'csp') {
    if (!canAccessCsp) throw new Error('Provider setup is not available with your current access.');
    const provider = await getProviderSetup(signal);
    signal.throwIfAborted();
    const records: SavedSetupRecord[] = [];
    if (provider.draft) records.push({
      id: provider.draft.draftId, kind: 'provider', name: provider.profile.identity?.displayName ?? 'Provider setup',
      detail: 'Saved provider facts and source outcomes. Review and publication remain separate.',
      state: provider.draft.completion ? 'Saved' : 'In progress',
      destination: '/onboarding/csp?reentry=resume',
    });
    const options: SetupOption[] = [{ kind: 'provider', destination: '/onboarding/csp?reentry=admin' }];
    if (provider.profile.onboardingState !== 'Active') return { options, records, nextPage: null, nextCursor: null };
    const organizations = await listOrganizationDrafts(page, 25, signal);
    records.push(...organizations.items.map(item => ({
      id: item.draftId, kind: 'organization' as const, name: item.displayName ?? item.values.displayName ?? 'Organization draft',
      detail: item.tenantId ? 'Organization created; review the exact enrollment outcome.' : 'Saved details; organization creation is not yet confirmed.',
      state: item.state, destination: item.resumeUrl,
    })));
    return { options: [...options, { kind: 'organization', destination: '/organizations/new' }], records,
      nextPage: organizations.total > page * 25 ? page + 1 : null, nextCursor: null };
  }
  if (!tenantId) throw new Error('The organization workspace did not provide its identity.');
  const tenantRecords: SavedSetupRecord[] = [];
  if (canManageOrganization) {
    const progress = await tenantWizard.getState(signal);
    signal.throwIfAborted();
    if (progress.tenantId !== tenantId) throw new Error('The saved tenant setup does not match the selected organization.');
    if (progress.onboardingState !== 'Active' || progress.draft) tenantRecords.push({
      id: `tenant:${tenantId}`, kind: 'organization', name: 'Organization activation',
      detail: progress.onboardingState === 'Active' ? 'Tenant is active; saved profile edits remain unapplied.'
        : progress.draft ? 'Draft saved; tenant activation is unfinished.' : 'Complete the required tenant details before system setup.',
      state: progress.onboardingState === 'Active' ? 'Saved edits' : 'Activation pending',
      destination: '/onboarding/tenant',
    });
    if (progress.onboardingState !== 'Active') return { options: [], records: tenantRecords, nextPage: null, nextCursor: null };
  }
  const [access, drafts] = await Promise.all([
    getSystemSetupAccess(tenantId, signal), listSystemSetupDrafts(tenantId, cursor, signal),
  ]);
  if (typeof access.canCreateSystem !== 'boolean') throw new Error('System setup permissions are unavailable.');
  return {
    options: access.canCreateSystem ? [{ kind: 'system', destination: '/systems/new' }] : [],
    records: [...tenantRecords, ...drafts.items.map(item => ({
      id: item.systemId, kind: 'system' as const, name: item.displayName, state: item.setupState,
      detail: 'Saved system identity and preparation choices. Authorization and package readiness remain separate.',
      destination: `/systems/${encodeURIComponent(item.systemId)}/setup`,
    }))],
    nextCursor: drafts.nextCursor, nextPage: null,
  };
}
