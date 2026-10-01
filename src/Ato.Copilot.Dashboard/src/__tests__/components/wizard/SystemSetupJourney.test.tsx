import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import IntakeWizard from '../../../components/wizard/IntakeWizard';

const request = vi.hoisted(() => vi.fn());
vi.mock('../../../features/workspace-operations/workspaceRequest', () => ({ workspaceRequest: request }));

const view = {
  systemId: 'opaque-system', tenantId: 'tenant-a', displayName: 'Logistics', organizationName: 'Mission organization',
  revision: 1, identityRevision: 'identity-1', savedAt: '2026-09-30T12:00:00Z', setupState: 'draft',
  completedAt: null, effectiveTeam: [], sources: [], tasks: [], canManage: true,
  draft: { name: 'Logistics', acronym: '', missionPurpose: 'Keep cargo moving', objective: 'initialAto',
    contactPersonId: '', sourceChoice: 'blank', hostingChoice: 'deferred', monitoringChoice: 'configureLater', lastScreen: 's-team' },
  monitoring: { configuration: 'absent', access: 'notChecked', scopeReview: 'unsupported', collection: 'unknown', evaluation: 'unknown' },
  contacts: [{ personId: 'person-a', displayName: 'Preparation lead' }],
};
function mount(resumeSystemId?: string, tenantId: string | undefined = 'tenant-a') {
  const close = vi.fn();
  render(<MemoryRouter><IntakeWizard setupTenantId={tenantId} setupSystemId={resumeSystemId}
    onClose={close} /></MemoryRouter>);
  return { close };
}

describe('mock-aligned system setup through existing intake', () => {
  beforeEach(() => { request.mockReset(); });

  it('does not revive the retired editing wizard outside an authorized organization', () => {
    // Arrange
    mount(undefined, '');
    // Act / Assert
    expect(screen.getByRole('alert')).toHaveTextContent('Choose an authorized organization workspace');
    expect(screen.queryByText('Step 1: System Registration')).not.toBeInTheDocument();
    expect(request).not.toHaveBeenCalled();
  });

  it('resumes the exact saved draft and separates task authority from a document handoff', async () => {
    // Arrange
    request.mockResolvedValue({
      ...view, setupState: 'confirmed', canManage: false,
      links: { documents: '/systems/opaque-system/documents' },
      tasks: [{ id: 'setup:documents', label: 'Review system documentation', detail: 'Review required',
        state: 'reviewRequired', contribution: 'Prepare reviewed SSP evidence', link: '/systems/opaque-system/documents',
        canAct: false, ownerRole: 'ISSM' }],
    });
    const { close } = mount('opaque-system');
    // Act / Assert
    await screen.findByRole('heading', { name: 'Your system workspace is ready' });
    expect(request).toHaveBeenCalledWith(expect.objectContaining({
      method: 'GET', url: '/api/workspaces/organizations/tenant-a/systems/opaque-system/setup',
    }));
    expect(screen.getByRole('link', { name: 'Document previews' })).toHaveAttribute('href', '/systems/opaque-system/documents');
    expect(screen.queryByRole('link', { name: 'Capabilities & customer duties' })).not.toBeInTheDocument();
    expect(screen.getByText('Ask the assigned ISSM to complete this task.')).toBeVisible();
    expect(screen.getByText('No document is generated or approved by setup.')).toBeVisible();
    expect(request.mock.calls.every(([config]) => config.method === 'GET')).toBe(true);
    fireEvent.click(screen.getByRole('button', { name: 'Open system work queue' }));
    expect(close).toHaveBeenCalledOnce();
    expect(request.mock.calls.every(([config]) => config.method === 'GET')).toBe(true);
  });

  it('visits all seven mock states using the same saved system without granting authorization', async () => {
    // Arrange
    let saved = { ...view };
    request.mockImplementation(async (config: { method: string; url: string; data?: typeof view.draft }) => {
      if (config.method === 'GET') return { organizationName: view.organizationName, canCreate: true, contacts: view.contacts };
      if (config.url.endsWith('/confirm')) return { ...saved, setupState: 'confirmed', completedAt: view.savedAt };
      saved = { ...saved, revision: saved.revision + 1, draft: { ...saved.draft, ...config.data } };
      return saved;
    });
    mount();
    await screen.findByText('Mission organization');
    // Act
    fireEvent.change(screen.getByLabelText('System name'), { target: { value: 'Logistics' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save & continue' }));
    await screen.findByRole('heading', { name: 'Identify your system team' });
    fireEvent.change(screen.getByLabelText('System contact'), { target: { value: 'person-a' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save & continue' }));
    await screen.findByRole('heading', { name: 'Bring existing documentation' });
    fireEvent.click(screen.getByRole('button', { name: 'Save choice & continue' }));
    await screen.findByRole('heading', { name: 'Identify how the system is hosted' });
    fireEvent.click(screen.getByLabelText('Use organization-managed hosting'));
    fireEvent.click(screen.getByRole('button', { name: 'Save choice & continue' }));
    await screen.findByRole('heading', { name: 'Plan cloud monitoring' });
    fireEvent.click(screen.getByRole('button', { name: 'Save choice & continue' }));
    await screen.findByRole('heading', { name: 'Review system setup' });
    fireEvent.click(screen.getByLabelText('Confirm the saved identity and setup choices shown.'));
    fireEvent.click(screen.getByRole('button', { name: 'Confirm system setup' }));
    // Assert
    await screen.findByRole('heading', { name: 'Your system workspace is ready' });
    expect(screen.getByText(/package remains subject to documentation/)).toBeVisible();
    const writes = request.mock.calls.map(([config]) => config).filter(config => config.method !== 'GET');
    expect(writes.filter(config => config.url.endsWith('/setup-drafts'))).toHaveLength(1);
    expect(writes.at(-1).url).toBe('/api/workspaces/organizations/tenant-a/systems/opaque-system/setup/confirm');
    expect(writes.at(-1).data.identityRevision).toBe('identity-1');
    expect(writes.some(config => /authorization|role-assignments|advance-rmf|run-assessment/.test(config.url))).toBe(false);
  });

  it('saves a partial server draft before exiting without invoking discard', async () => {
    // Arrange
    request.mockImplementation(async (config: { method: string }) => config.method === 'GET'
      ? { organizationName: 'Mission organization', canCreate: true, contacts: [] }
      : view);
    const { close } = mount();
    await screen.findByText('Mission organization');
    fireEvent.change(screen.getByLabelText('System name'), { target: { value: 'Logistics' } });
    fireEvent.change(screen.getByLabelText('Mission purpose'), { target: { value: 'Keep cargo moving' } });
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Save & finish later' }));
    // Assert
    await waitFor(() => expect(close).toHaveBeenCalledOnce());
    expect(request.mock.calls.some(([config]) => config.method === 'DELETE')).toBe(false);
    expect(request).toHaveBeenCalledWith(expect.objectContaining({
      method: 'POST', url: '/api/workspaces/organizations/tenant-a/systems/setup-drafts',
      data: expect.objectContaining({ name: 'Logistics', missionPurpose: 'Keep cargo moving' }),
      headers: expect.objectContaining({ 'Idempotency-Key': expect.any(String) }),
    }));
  });

  it('retains an uncertain save and retries with the same request key and payload', async () => {
    // Arrange
    let failed = false;
    request.mockImplementation(async (config: { method: string }) => {
      if (config.method === 'GET') return { organizationName: 'Mission organization', canCreate: true, contacts: [] };
      if (!failed) { failed = true; throw new Error('Response lost; retry this save'); }
      return view;
    });
    const { close } = mount();
    await screen.findByText('Mission organization');
    fireEvent.change(screen.getByLabelText('System name'), { target: { value: 'Logistics' } });
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Save & finish later' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Response lost');
    expect(close).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole('button', { name: 'Save & finish later' }));
    // Assert
    await waitFor(() => expect(close).toHaveBeenCalledOnce());
    const writes = request.mock.calls.map(([config]) => config).filter(config => config.method === 'POST');
    expect(writes.at(-1).headers['Idempotency-Key']).toBe(writes.at(-2).headers['Idempotency-Key']);
    expect(writes.at(-1).data).toEqual(writes.at(-2).data);
  });
});
