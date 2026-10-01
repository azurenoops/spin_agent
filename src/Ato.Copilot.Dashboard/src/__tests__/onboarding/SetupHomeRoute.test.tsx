import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import SetupHomeRoute from '../../features/onboarding/shared/SetupHomeRoute';
import { WorkspaceOperationError } from '../../features/workspace-operations/workspaceRequest';
import { PackageImportError } from '../../features/package-imports/request';

const mocks = vi.hoisted(() => ({
  provider: vi.fn(), organizations: vi.fn(), access: vi.fn(), systems: vi.fn(), tenant: vi.fn(),
  context: { available: true, kind: 'csp' as 'csp' | 'organization', tenantId: null as string | null, support: false, canAccessCsp: true, canManageOrganization: false },
}));
vi.mock('../../features/csp-onboarding/providerSetupApi', () => ({ getSetup: mocks.provider }));
vi.mock('../../features/workspace-operations/organizationOnboardingApi', () => ({ listOrganizationDrafts: mocks.organizations }));
vi.mock('../../features/onboarding/systemSetupApi', () => ({ getSystemSetupAccess: mocks.access, listSystemSetupDrafts: mocks.systems }));
vi.mock('../../features/onboarding/TenantWizard/api', () => ({ tenantWizard: { getState: mocks.tenant } }));
vi.mock('../../features/workspaces/WorkspaceBoundary', () => ({
  useWorkspaceSession: () => mocks.context.available ? {
    identity: { directoryTenantId: 'directory-a', oid: 'actor-a' },
    workspace: { kind: mocks.context.kind, tenantId: mocks.context.tenantId, displayName: 'Authorized workspace',
      permissions: { canAccessCsp: mocks.context.kind === 'csp' && mocks.context.canAccessCsp,
        canManageOrganization: mocks.context.canManageOrganization } },
    target: { kind: mocks.context.kind, tenantId: mocks.context.tenantId, mode: mocks.context.support ? 'support' : undefined },
  } : null,
  WorkspaceStatus: ({ message }: { message: string }) => <p role="alert">{message}</p>,
}));

beforeEach(() => {
  vi.clearAllMocks();
  mocks.context = { available: true, kind: 'csp', tenantId: null, support: false, canAccessCsp: true, canManageOrganization: false };
  mocks.provider.mockResolvedValue({ profile: { identity: { displayName: 'Provider A' }, onboardingState: 'Active' }, draft: {
    draftId: 'provider-draft', currentScreen: 'p-sources', completion: null,
  } });
  mocks.organizations.mockResolvedValue({ items: [], total: 0 });
  mocks.access.mockResolvedValue({ canCreateSystem: false });
  mocks.systems.mockResolvedValue({ items: [], nextCursor: null });
  mocks.tenant.mockResolvedValue({ tenantId: 'org-a', onboardingState: 'Active', draft: null });
});

describe('setup entry canonical readers', () => {
  it('loads provider and organization saved facts without asking for customer-system data', async () => {
    // Arrange / Act
    render(<MemoryRouter><SetupHomeRoute /></MemoryRouter>);
    // Assert
    expect(await screen.findByRole('link', { name: 'Continue Provider A' })).toHaveAttribute('href', '/onboarding/csp?reentry=resume');
    expect(mocks.provider).toHaveBeenCalledWith(expect.any(AbortSignal));
    expect(mocks.organizations).toHaveBeenCalledWith(1, 25, expect.any(AbortSignal));
    expect(mocks.systems).not.toHaveBeenCalled();
    expect(mocks.access).not.toHaveBeenCalled();
  });

  it('requires the current server create permission for an ordinary organization', async () => {
    // Arrange
    mocks.context.kind = 'organization'; mocks.context.tenantId = 'org-a';
    mocks.systems.mockResolvedValue({ items: [{ systemId: 'opaque-system', displayName: 'Mission A', setupState: 'draft' }], nextCursor: null });
    // Act
    render(<MemoryRouter><SetupHomeRoute /></MemoryRouter>);
    // Assert
    expect(await screen.findByRole('link', { name: 'Continue Mission A' })).toHaveAttribute('href', '/systems/opaque-system/setup');
    expect(screen.queryByRole('link', { name: 'Set up system' })).not.toBeInTheDocument();
    expect(mocks.provider).not.toHaveBeenCalled();
    expect(mocks.organizations).not.toHaveBeenCalled();
    expect(mocks.access).toHaveBeenCalledWith('org-a', expect.any(AbortSignal));
  });

  it('loads subsequent organization pages without duplicating the provider record', async () => {
    // Arrange
    const draft = (index: number) => ({ draftId: `draft-${index}`, displayName: `Organization ${index}`,
      state: 'Draft', tenantId: null, values: {}, resumeUrl: `/organizations/new?draft=draft-${index}` });
    mocks.organizations.mockResolvedValueOnce({ items: Array.from({ length: 25 }, (_, index) => draft(index + 1)), total: 26 })
      .mockResolvedValueOnce({ items: [draft(26)], total: 26 });
    // Act
    render(<MemoryRouter><SetupHomeRoute mode="resume" /></MemoryRouter>);
    fireEvent.click(await screen.findByRole('button', { name: 'Load more saved setup' }));
    // Assert
    expect(await screen.findByRole('link', { name: 'Continue Organization 26' })).toBeInTheDocument();
    expect(screen.getAllByRole('link', { name: 'Continue Provider A' })).toHaveLength(1);
    expect(screen.queryByRole('button', { name: 'Load more saved setup' })).not.toBeInTheDocument();
    expect(mocks.organizations).toHaveBeenLastCalledWith(2, 25, expect.any(AbortSignal));
  });

  it('uses opaque server cursors for system draft pagination', async () => {
    // Arrange
    mocks.context.kind = 'organization'; mocks.context.tenantId = 'org-a';
    mocks.access.mockResolvedValue({ canCreateSystem: true });
    mocks.systems.mockResolvedValueOnce({ items: [{ systemId: 'a', displayName: 'A', setupState: 'draft' }], nextCursor: 'cursor-a' })
      .mockResolvedValueOnce({ items: [{ systemId: 'b', displayName: 'B', setupState: 'draft' }], nextCursor: null });
    // Act
    render(<MemoryRouter><SetupHomeRoute /></MemoryRouter>);
    fireEvent.click(await screen.findByRole('button', { name: 'Load more saved setup' }));
    // Assert
    expect(await screen.findByRole('link', { name: 'Continue B' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Continue A' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Set up system' })).toBeInTheDocument();
    expect(mocks.systems).toHaveBeenLastCalledWith('org-a', 'cursor-a', expect.any(AbortSignal));
  });

  it('reports an unavailable read and recovers explicitly without an empty-success state', async () => {
    // Arrange
    mocks.organizations.mockRejectedValueOnce(new Error('Organization drafts unavailable.'));
    // Act
    render(<MemoryRouter><SetupHomeRoute /></MemoryRouter>);
    expect(await screen.findByRole('alert')).toHaveTextContent('Organization drafts unavailable.');
    // Assert
    expect(screen.queryByText('No saved setup records.')).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Set up organization' })).not.toBeInTheDocument();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Retry loading setup' }));
    // Assert
    expect(await screen.findByRole('link', { name: 'Continue Provider A' })).toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it.each(['missing', 'support'])('does not call setup APIs for %s ordinary context', context => {
    // Arrange
    if (context === 'missing') mocks.context.available = false;
    else { mocks.context.kind = 'organization'; mocks.context.tenantId = 'org-a'; mocks.context.support = true; }
    // Act
    render(<MemoryRouter><SetupHomeRoute /></MemoryRouter>);
    // Assert
    expect(screen.getByRole('alert')).toHaveTextContent('Choose an ordinary authorized workspace');
    expect(mocks.provider).not.toHaveBeenCalled();
    expect(mocks.access).not.toHaveBeenCalled();
  });

  it('aborts stale reads when the workspace identity changes', async () => {
    // Arrange
    let finish: (value: unknown) => void = () => {};
    mocks.provider.mockReturnValueOnce(new Promise(resolve => { finish = resolve; }));
    const view = render(<MemoryRouter><SetupHomeRoute /></MemoryRouter>);
    await waitFor(() => expect(mocks.provider).toHaveBeenCalledOnce());
    const signal = mocks.provider.mock.calls[0]![0] as AbortSignal;
    // Act
    mocks.context.kind = 'organization'; mocks.context.tenantId = 'org-b';
    view.rerender(<MemoryRouter><SetupHomeRoute /></MemoryRouter>);
    await act(async () => { finish({ profile: { identity: { displayName: 'Stale provider' } }, draft: { draftId: 'old', completion: null } }); });
    // Assert
    expect(signal.aborted).toBe(true);
    expect(screen.queryByText('Stale provider')).not.toBeInTheDocument();
    await waitFor(() => expect(screen.getByText('No saved setup records.')).toBeInTheDocument());
  });

  it('handles a fresh provider with no saved draft without inventing a record', async () => {
    // Arrange
    mocks.provider.mockResolvedValue({ profile: { identity: null, onboardingState: 'Active' }, draft: null });
    // Act
    render(<MemoryRouter><SetupHomeRoute /></MemoryRouter>);
    // Assert
    expect(await screen.findByText('No saved setup records.')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Set up provider' })).toBeInTheDocument();
  });

  it('names partial drafts honestly and separates committed organizations from draft creation', async () => {
    // Arrange
    mocks.provider.mockResolvedValue({ profile: { identity: null, onboardingState: 'Active' }, draft: { draftId: 'p', completion: {} } });
    mocks.organizations.mockResolvedValue({ items: [
      { draftId: 'o1', displayName: null, values: { displayName: 'Saved name' }, tenantId: 'org-a', state: 'Confirmed', resumeUrl: '/organizations/org-a/provisioning' },
      { draftId: 'o2', displayName: null, values: { displayName: null }, tenantId: null, state: 'Draft', resumeUrl: '/organizations/new?draft=o2' },
    ], total: 2 });
    // Act
    render(<MemoryRouter><SetupHomeRoute mode="resume" /></MemoryRouter>);
    // Assert
    expect(await screen.findByRole('link', { name: 'Continue Provider setup' })).toBeInTheDocument();
    expect(screen.getByText('Saved', { exact: true })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Continue Saved name' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Continue Organization draft' })).toBeInTheDocument();
    expect(screen.getByText('Organization created; review the exact enrollment outcome.')).toBeInTheDocument();
  });

  it.each(['provider-denied', 'organization-missing', 'permission-unknown'])('rejects %s without an available-action guess', async condition => {
    // Arrange
    if (condition === 'provider-denied') mocks.context.canAccessCsp = false;
    else {
      mocks.context.kind = 'organization';
      mocks.context.tenantId = condition === 'organization-missing' ? null : 'org-a';
      mocks.access.mockResolvedValue({});
    }
    // Act
    render(<MemoryRouter><SetupHomeRoute /></MemoryRouter>);
    // Assert
    expect(await screen.findByRole('alert')).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /Set up/ })).not.toBeInTheDocument();
    expect(screen.queryByText('No saved setup records.')).not.toBeInTheDocument();
  });

  it('ignores an aborted request failure after switching identity', async () => {
    // Arrange
    let reject!: (error: Error) => void;
    mocks.provider.mockReturnValueOnce(new Promise((_, fail) => { reject = fail; }));
    const view = render(<MemoryRouter><SetupHomeRoute /></MemoryRouter>);
    await waitFor(() => expect(mocks.provider).toHaveBeenCalledOnce());
    // Act
    mocks.context.kind = 'organization'; mocks.context.tenantId = 'org-b';
    view.rerender(<MemoryRouter><SetupHomeRoute /></MemoryRouter>);
    await act(async () => { reject(new Error('Stale request failed')); });
    // Assert
    await waitFor(() => expect(screen.getByText('No saved setup records.')).toBeInTheDocument());
    expect(screen.queryByText('Stale request failed')).not.toBeInTheDocument();
  });

  it('navigates explicitly between start and resume', async () => {
    // Arrange
    function Location() { return <output data-testid="path">{useLocation().pathname}</output>; }
    // Act
    const view = render(<MemoryRouter><SetupHomeRoute /><Location /></MemoryRouter>);
    await screen.findByRole('link', { name: 'Continue Provider A' });
    fireEvent.click(screen.getByRole('button', { name: 'View saved setup' }));
    // Assert
    expect(screen.getByTestId('path')).toHaveTextContent('/setup/resume');
    // Act
    view.rerender(<MemoryRouter><SetupHomeRoute mode="resume" /><Location /></MemoryRouter>);
    fireEvent.click(screen.getByRole('button', { name: 'Back' }));
    // Assert
    expect(screen.getByTestId('path')).toHaveTextContent('/setup');
  });

  it.each([
    new WorkspaceOperationError('Access revoked.', 403),
    new PackageImportError('Sign in again.', 401),
  ])('clears retained private records when authorization is lost: $message', async failure => {
    // Arrange
    mocks.context.kind = 'organization'; mocks.context.tenantId = 'org-a';
    mocks.systems.mockResolvedValueOnce({ items: [{ systemId: 'private', displayName: 'Private mission', setupState: 'draft' }], nextCursor: 'next' })
      .mockRejectedValueOnce(failure);
    render(<MemoryRouter><SetupHomeRoute mode="resume" /></MemoryRouter>);
    await screen.findByRole('link', { name: 'Continue Private mission' });
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Load more saved setup' }));
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent(failure.message);
    expect(screen.queryByText('Private mission')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Load more saved setup' })).not.toBeInTheDocument();
  });

  it('retains an earlier read on transient failure but requires explicit retry instead of inert pagination', async () => {
    // Arrange
    mocks.context.kind = 'organization'; mocks.context.tenantId = 'org-a';
    mocks.systems.mockResolvedValueOnce({ items: [{ systemId: 'a', displayName: 'Earlier mission', setupState: 'draft' }], nextCursor: 'next' })
      .mockRejectedValueOnce(new WorkspaceOperationError('Service temporarily unavailable.', 503));
    render(<MemoryRouter><SetupHomeRoute mode="resume" /></MemoryRouter>);
    await screen.findByRole('link', { name: 'Continue Earlier mission' });
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Load more saved setup' }));
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Service temporarily unavailable.');
    expect(screen.getByRole('link', { name: 'Continue Earlier mission' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Retry loading setup' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Load more saved setup' })).not.toBeInTheDocument();
  });

  it('resumes pending tenant activation without probing system APIs blocked by the activation gate', async () => {
    // Arrange
    mocks.context.kind = 'organization'; mocks.context.tenantId = 'org-a'; mocks.context.canManageOrganization = true;
    mocks.tenant.mockResolvedValue({ tenantId: 'org-a', onboardingState: 'InWizard',
      draft: { revision: 2, values: { legalEntity: { legalEntityName: 'Saved organization' } } } });
    // Act
    render(<MemoryRouter><SetupHomeRoute mode="resume" /></MemoryRouter>);
    // Assert
    expect(await screen.findByRole('link', { name: 'Continue Organization activation' })).toHaveAttribute('href', '/onboarding/tenant');
    expect(screen.getByText('Draft saved; tenant activation is unfinished.')).toBeInTheDocument();
    expect(mocks.systems).not.toHaveBeenCalled();
    expect(mocks.access).not.toHaveBeenCalled();
  });

  it('keeps active administrator tenant drafts separate from system drafts', async () => {
    // Arrange
    mocks.context.kind = 'organization'; mocks.context.tenantId = 'org-a'; mocks.context.canManageOrganization = true;
    mocks.tenant.mockResolvedValue({ tenantId: 'org-a', onboardingState: 'Active', draft: { revision: 1 } });
    // Act
    render(<MemoryRouter><SetupHomeRoute mode="resume" /></MemoryRouter>);
    // Assert
    expect(await screen.findByRole('link', { name: 'Continue Organization activation' })).toBeInTheDocument();
    expect(screen.getByText('Tenant is active; saved profile edits remain unapplied.')).toBeInTheDocument();
    expect(mocks.access).toHaveBeenCalled();
  });

  it.each([
    { tenantId: 'org-a', onboardingState: 'Pending', draft: null },
    { tenantId: 'org-a', onboardingState: 'Active', draft: null },
    { tenantId: 'different-org', onboardingState: 'InWizard', draft: null },
  ])('handles administrator activation state $onboardingState for $tenantId without inventing a draft', async progress => {
    // Arrange
    mocks.context.kind = 'organization'; mocks.context.tenantId = 'org-a'; mocks.context.canManageOrganization = true;
    mocks.tenant.mockResolvedValue(progress);
    // Act
    render(<MemoryRouter><SetupHomeRoute mode="resume" /></MemoryRouter>);
    // Assert
    if (progress.tenantId !== 'org-a') {
      expect(await screen.findByRole('alert')).toHaveTextContent('does not match');
      expect(mocks.access).not.toHaveBeenCalled();
    } else if (progress.onboardingState === 'Pending') {
      expect(await screen.findByText('Complete the required tenant details before system setup.')).toBeInTheDocument();
      expect(mocks.access).not.toHaveBeenCalled();
    } else {
      expect(await screen.findByText('No saved setup records.')).toBeInTheDocument();
      expect(mocks.access).toHaveBeenCalled();
    }
  });

  it('resumes an unfinished provider without calling organization APIs blocked before activation', async () => {
    // Arrange
    mocks.provider.mockResolvedValue({ profile: { identity: { displayName: 'Pending provider' }, onboardingState: 'InWizard' },
      draft: { draftId: 'p', completion: null } });
    mocks.organizations.mockRejectedValue(new WorkspaceOperationError('CSP onboarding incomplete.', 503));
    // Act
    render(<MemoryRouter><SetupHomeRoute mode="resume" /></MemoryRouter>);
    // Assert
    expect(await screen.findByRole('link', { name: 'Continue Pending provider' })).toBeInTheDocument();
    expect(mocks.organizations).not.toHaveBeenCalled();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('clears tenant activation records when the tenant client reports access revoked', async () => {
    // Arrange
    mocks.context.kind = 'organization'; mocks.context.tenantId = 'org-a'; mocks.context.canManageOrganization = true;
    mocks.tenant.mockResolvedValueOnce({ tenantId: 'org-a', onboardingState: 'Active', draft: { revision: 1 } })
      .mockRejectedValueOnce({ isAxiosError: true, message: 'Tenant access revoked', response: { status: 403 } });
    mocks.systems.mockResolvedValueOnce({ items: [], nextCursor: 'next' });
    render(<MemoryRouter><SetupHomeRoute mode="resume" /></MemoryRouter>);
    await screen.findByRole('link', { name: 'Continue Organization activation' });
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Load more saved setup' }));
    // Assert
    await screen.findByRole('alert');
    expect(screen.queryByRole('link', { name: 'Continue Organization activation' })).not.toBeInTheDocument();
  });
});
