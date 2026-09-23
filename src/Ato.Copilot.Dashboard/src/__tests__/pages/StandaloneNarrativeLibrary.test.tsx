import { beforeEach, describe, expect, it, vi } from 'vitest';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import StandaloneNarrativeLibrary from '../../pages/StandaloneNarrativeLibrary';
import { WorkspaceNavigationProvider } from '../../features/workspaces/workspaceNavigation';
import * as api from '../../api/narrativeLibrary';
import type { WorkspaceTarget } from '../../features/workspaces/workspaceRoutes';

const session = vi.hoisted(() => ({ target: { kind: 'organization', tenantId: 'org-a' } as WorkspaceTarget }));
vi.mock('../../features/workspaces/WorkspaceBoundary', () => ({
  useWorkspaceSession: () => ({ target: session.target, workspace: { displayName: 'Organization A' } }),
}));
vi.mock('../../components/layout/PageLayout', () => ({ default: ({ children }: { children: React.ReactNode }) => children }));
vi.mock('../../api/narrativeLibrary', async importOriginal => {
  const actual = await importOriginal<typeof import('../../api/narrativeLibrary')>();
  return { ...actual, getOrganizationLibraryAccess: vi.fn(), getProviderLibraryAccess: vi.fn(), getScopedReferences: vi.fn(),
    importScopedReference: vi.fn(), updateScopedReference: vi.fn(), publishScopedReference: vi.fn() };
});
function open() {
  const prefix = session.target.kind === 'csp' ? '/workspaces/csp' : `/workspaces/organizations/${session.target.tenantId}`;
  return render(<MemoryRouter initialEntries={[`${prefix}/narrative-library`]}>
    <WorkspaceNavigationProvider workspace={session.target}>
      <Routes><Route path={`${prefix}/narrative-library/*`} element={<StandaloneNarrativeLibrary />} /></Routes>
    </WorkspaceNavigationProvider>
  </MemoryRouter>);
}
beforeEach(() => {
  vi.clearAllMocks();
  session.target = { kind: 'organization', tenantId: 'org-a' };
  vi.mocked(api.getScopedReferences).mockResolvedValue([]);
  vi.mocked(api.getOrganizationLibraryAccess).mockResolvedValue({ tenantId: 'org-a', canPublishShared: true, capabilities: [{ id: 'org-cap', name: 'Organization capability' }] });
  vi.mocked(api.getProviderLibraryAccess).mockResolvedValue({ cspProfileId: 'provider-a', displayName: 'Provider A', canPublish: true, capabilities: [{ id: 'provider-cap', name: 'Provider capability' }] });
});
describe('standalone reference workspaces', () => {
  it.each(['success', 'denied'])('retains mappings during refresh and handles the %s result', async outcome => {
    // Arrange
    const draft: api.NarrativeReference = {
      id: 'reference-a', referenceKey: 'key-a', title: 'Synthetic policy', scope: 'Organization', scopeId: 'org-a',
      sourceName: 'pasted-reference.txt', sourceSha256: 'synthetic-hash', version: 1, revision: 1,
      isPublished: false, createdAt: '2026-01-01T00:00:00Z', createdBy: 'synthetic-author',
      publishedAt: null, publishedBy: null, passages: [{ controlId: 'AC-2', narrativeType: 'Policy', content: 'Synthetic policy.' }],
    };
    let finish!: (references: api.NarrativeReference[]) => void;
    let fail!: (error: unknown) => void;
    vi.mocked(api.getScopedReferences).mockResolvedValueOnce([])
      .mockReturnValueOnce(new Promise((resolve, reject) => { finish = resolve; fail = reject; }));
    vi.mocked(api.importScopedReference).mockResolvedValue(draft);
    open();
    fireEvent.click(await screen.findByRole('button', { name: 'Upload narratives' }));
    fireEvent.change(screen.getByLabelText('Reference title'), { target: { value: draft.title } });
    fireEvent.change(screen.getByLabelText('Paste narratives'), { target: { value: 'Synthetic policy.' } });

    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Extract passages' }));
    await waitFor(() => expect(api.getScopedReferences).toHaveBeenCalledTimes(2));

    // Assert
    expect(screen.getByRole('heading', { name: 'Review before publishing' })).toBeInTheDocument();
    expect(screen.getByLabelText('Control 1')).toHaveValue('AC-2');
    expect(screen.getByRole('button', { name: 'Publish references' })).toBeDisabled();
    if (outcome === 'success') {
      await act(async () => { finish([draft]); });
      expect(screen.getByRole('heading', { name: 'Review before publishing' })).toBeInTheDocument();
      expect(screen.getByLabelText('Control 1')).toHaveValue('AC-2');
    } else {
      await act(async () => { fail({ errorCode: 'FORBIDDEN', error: 'Membership revoked.' }); });
      expect(screen.getByRole('alert')).toHaveTextContent('Membership revoked.');
      expect(screen.queryByLabelText('Control 1')).not.toBeInTheDocument();
    }
  });

  it('uses real organization context and never enables system generation', async () => {
    // Arrange / Act
    open();
    // Assert
    expect(await screen.findByRole('button', { name: 'Upload narratives' })).toBeEnabled();
    expect(api.getScopedReferences).toHaveBeenCalledWith({ kind: 'organization' }, expect.anything());
    expect(api.getProviderLibraryAccess).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole('button', { name: 'Upload narratives' }));
    expect(screen.getByLabelText('Reference scope')).toHaveValue('Organization');
    expect(screen.queryByRole('option', { name: 'System' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Generate|Approve/ })).not.toBeInTheDocument();
  });
  it('uses provider access and real profile/capability IDs without a synthetic system', async () => {
    // Arrange
    session.target = { kind: 'csp' };
    open();
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Upload narratives' }));
    // Assert
    expect(api.getOrganizationLibraryAccess).not.toHaveBeenCalled();
    expect(screen.getByLabelText('Reference scope')).toHaveValue('Provider');
    fireEvent.change(screen.getByLabelText('Reference scope'), { target: { value: 'ProviderCapability' } });
    expect(screen.getByRole('option', { name: 'Provider capability' })).toHaveValue('provider-cap');
    expect(screen.queryByRole('option', { name: 'Organization capability' })).not.toBeInTheDocument();
  });
  it('blocks a server tenant mismatch before reading references', async () => {
    // Arrange
    vi.mocked(api.getOrganizationLibraryAccess).mockResolvedValue({ tenantId: 'org-b', canPublishShared: true, capabilities: [] });
    // Act
    open();
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent(/organization.*match/i);
    expect(api.getScopedReferences).not.toHaveBeenCalled();
    expect(screen.queryByRole('button', { name: 'Upload narratives' })).not.toBeInTheDocument();
  });
  it('preserves a denied state instead of showing an empty library', async () => {
    // Arrange
    vi.mocked(api.getOrganizationLibraryAccess).mockRejectedValue({ errorCode: 'FORBIDDEN', error: 'Membership revoked.' });
    // Act
    open();
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Membership revoked.');
    expect(screen.queryByText('No reference narratives published.')).not.toBeInTheDocument();
    expect(api.getScopedReferences).not.toHaveBeenCalled();
    expect(screen.getByRole('button', { name: 'Retry loading' })).toBeInTheDocument();
  });
  it('requires explicit publication permission even with a valid readable access DTO', async () => {
    // Arrange
    vi.mocked(api.getOrganizationLibraryAccess).mockResolvedValue({ tenantId: 'org-a', canPublishShared: false, capabilities: [] });
    // Act
    open();
    // Assert
    await waitFor(() => expect(screen.getByRole('button', { name: 'Upload narratives' })).toBeDisabled());
    expect(api.importScopedReference).not.toHaveBeenCalled();
  });
});
