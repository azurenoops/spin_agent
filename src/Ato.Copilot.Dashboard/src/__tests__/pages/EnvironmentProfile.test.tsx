import { fireEvent, render, screen, within } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import type { ReactNode } from 'react';
import ProfileSectionForm from '../../components/forms/ProfileSectionForm';
import { WorkspaceNavigationProvider } from '../../features/workspaces/workspaceNavigation';
import * as hostingApi from '../../features/provider-relationships/api';
import * as capabilityApi from '../../features/workspace-operations/system-capabilities/systemCapabilityApi';
import { allocation } from '../provider-relationships/fixtures';
import type { SystemCapabilityPage } from '../../features/workspace-operations/system-capabilities/systemCapabilityTypes';

vi.mock('../../features/provider-relationships/api', () => ({ listAllSystemHostingAllocations: vi.fn() }));
vi.mock('../../features/workspace-operations/system-capabilities/systemCapabilityApi', () => ({ listSystemCapabilities: vi.fn() }));
vi.mock('../../features/workspace-operations/SetupDialog', () => ({
  default: ({ title, children }: { title: string; children: ReactNode }) => <div role="dialog" aria-label={title}>{children}</div>,
}));
vi.mock('../../features/workspaces/WorkspaceBoundary', () => ({
  useWorkspaceSession: () => ({
    workspace: { kind: 'organization', tenantId: 'org-a', mode: 'ordinary' },
    systemAccess: { systemId: 'system-a', permissions: { canRead: true } },
  }),
}));
const save = vi.fn();
const emptyCapabilities: SystemCapabilityPage = {
  items: [], page: 1, pageSize: 10, total: 0, scope: 'applied', grouping: 'capability', boundaries: [],
  permissions: { canRead: true, canManage: false, canReviewResponsibilities: false, canAuthorNarratives: false,
    canManageEvidence: false, canReviewNarratives: false },
};
function mount(content: Record<string, string> = {}, isReadOnly = false) {
  return render(<MemoryRouter>
    <WorkspaceNavigationProvider workspace={{ kind: 'organization', tenantId: 'org-a' }}>
      <ProfileSectionForm sectionType="EnvironmentAndDeployment" governanceStatus="Draft"
        initialContent={JSON.stringify(content)} reviewerComments={null} isReadOnly={isReadOnly}
        userRole="MissionOwner" isSubmitting={false} error={null} systemId="system-a"
        onSave={save} onSubmit={vi.fn()} onWithdraw={vi.fn()} />
    </WorkspaceNavigationProvider>
  </MemoryRouter>);
}
beforeEach(() => {
  vi.resetAllMocks();
  vi.mocked(hostingApi.listAllSystemHostingAllocations).mockResolvedValue([{ ...allocation, relationshipId: 'associated-a' }]);
  vi.mocked(capabilityApi.listSystemCapabilities).mockResolvedValue(emptyCapabilities);
});

describe('task-led Environment profile', () => {
  it('links applied local and provider capabilities by source and only offers library changes when permitted', async () => {
    // Arrange
    vi.mocked(capabilityApi.listSystemCapabilities).mockResolvedValue({
      ...emptyCapabilities, total: 2, permissions: { ...emptyCapabilities.permissions, canManage: true },
      items: (['local', 'provider'] as const).map(source => ({
        source, recordType: 'capability', recordId: 'shared-id', name: `${source} monitoring`, description: '',
        sourceName: `${source} source`, mutationAuthority: source, sourceRevision: 'rev-1', isApplied: true, isAvailable: true,
        status: 'Active', componentType: null, subType: null, components: [], capabilities: [], placements: [], controlIds: ['AU-2'], reviewRequiredCount: 1,
      })),
    });
    // Act
    mount({ hostingModel: 'Organization-managed cloud' });
    // Assert
    expect(await screen.findByRole('link', { name: 'local monitoring' })).toHaveAttribute('href',
      '/workspaces/organizations/org-a/systems/system-a/security-capabilities/local/shared-id');
    expect(screen.getByRole('link', { name: 'provider monitoring' })).toHaveAttribute('href',
      '/workspaces/organizations/org-a/systems/system-a/security-capabilities/provider/shared-id');
    expect(screen.getByRole('link', { name: 'Add from library' })).toBeVisible();
    expect(screen.queryByRole('link', { name: 'Associate hosting & capabilities' })).not.toBeInTheDocument();
  });

  it('retains Hybrid when inserting a reviewed provider scope', async () => {
    // Arrange
    mount({ hostingModel: 'Hybrid' });
    await screen.findByText('Harbor hosting');
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Use these hosting details' }));
    fireEvent.click(screen.getByRole('checkbox'));
    fireEvent.click(screen.getByRole('button', { name: 'Use in draft' }));
    // Assert
    expect(screen.getByRole('combobox', { name: 'Hosting model' })).toHaveValue('Hybrid');
    expect(save).not.toHaveBeenCalled();
  });

  it('does not turn failed association reads into empty successful records', async () => {
    // Arrange
    vi.mocked(hostingApi.listAllSystemHostingAllocations).mockRejectedValue(new Error('Hosting service unavailable'));
    vi.mocked(capabilityApi.listSystemCapabilities).mockRejectedValue(new Error('Capability service unavailable'));
    // Act
    mount();
    // Assert
    await screen.findByText('Hosting service unavailable');
    await screen.findByText('Capability service unavailable');
    expect(screen.queryByText('No hosting association is recorded for this system.')).not.toBeInTheDocument();
    expect(screen.queryByText(/No security capabilities are applied/)).not.toBeInTheDocument();
  });

  it('leads with four hosting choices and description, not assessment configuration or completeness', async () => {
    // Arrange / Act
    mount();
    // Assert
    const model = screen.getByRole('combobox', { name: 'Hosting model' });
    expect(within(model).getAllByRole('option').map(option => option.textContent)).toEqual([
      '— Select —', 'CSP-hosted', 'Organization-managed cloud', 'On-Premises', 'Hybrid',
    ]);
    expect(screen.getByRole('textbox', { name: 'Environment description' })).toBeVisible();
    expect(screen.getByLabelText('Availability Tier')).not.toBeVisible();
    expect(screen.queryByText('Profile Completeness')).not.toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Azure assessment environment' })).not.toBeInTheDocument();
    await screen.findByText('Harbor hosting');
    expect(screen.queryByRole('link', { name: 'Associate hosting & capabilities' })).not.toBeInTheDocument();
  });

  it('shows the CSP action conditionally and preserves advanced and unknown values on save', async () => {
    // Arrange
    const original = { hostingModel: 'CSP-hosted', additionalDetails: 'Current environment',
      rtoRpo: 'RTO < 1hr / RPO < 15min', networkZones: '["DMZ"]', customLegacyKey: 'retain me' };
    mount(original);
    await screen.findByText('Harbor hosting');
    // Assert
    expect(screen.getByRole('link', { name: 'Associate hosting & capabilities' })).toHaveAttribute(
      'href', '/workspaces/organizations/org-a/systems/system-a/profile/EnvironmentAndDeployment/hosting');
    // Act
    fireEvent.change(screen.getByRole('combobox', { name: 'Hosting model' }), { target: { value: 'On-Premises' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save Draft' }));
    // Assert
    expect(JSON.parse(save.mock.calls[0]![0])).toEqual({ ...original, hostingModel: 'On-Premises' });
    expect(screen.queryByRole('link', { name: 'Associate hosting & capabilities' })).not.toBeInTheDocument();
    expect(screen.getByText('Harbor hosting')).toBeVisible();
  });

  it('retains legacy hosting values without silently classifying them as provider-hosted', async () => {
    // Arrange / Act
    mount({ hostingModel: 'Cloud (IaaS)', availabilityTier: '99.9% (Three 9s)' });
    // Assert
    expect(screen.getByRole('combobox', { name: 'Hosting model' })).toHaveValue('Cloud (IaaS)');
    expect(screen.getByRole('option', { name: /Cloud \(IaaS\).*previously recorded/ })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Associate hosting & capabilities' })).not.toBeInTheDocument();
    fireEvent.click(screen.getByText('Recovery, availability & operating details'));
    expect(screen.getByRole('combobox', { name: 'Availability Tier' })).toHaveValue('99.9% (Three 9s)');
    await screen.findByText('Harbor hosting');
  });

  it('previews actual associated scope and requires confirmation before inserting into the unsaved draft', async () => {
    // Arrange
    mount({ hostingModel: 'CSP-hosted', additionalDetails: 'Do not silently overwrite', rtoRpo: 'Not Defined' });
    await screen.findByText('Harbor hosting');
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Use these hosting details' }));
    const dialog = screen.getByRole('dialog', { name: 'Review hosting details' });
    // Assert
    expect(within(dialog).getByText(/Do not silently overwrite/)).toBeVisible();
    expect(within(dialog).getByRole('button', { name: 'Use in draft' })).toBeDisabled();
    expect(save).not.toHaveBeenCalled();
    // Act
    fireEvent.click(within(dialog).getByRole('checkbox'));
    fireEvent.click(within(dialog).getByRole('button', { name: 'Use in draft' }));
    // Assert
    expect(screen.getByRole('textbox', { name: 'Environment description' })).toHaveProperty('value',
      expect.stringContaining('Harbor hosting'));
    expect(save).not.toHaveBeenCalled();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Save Draft' }));
    // Assert
    expect(JSON.parse(save.mock.calls[0]![0])).toMatchObject({ hostingModel: 'CSP-hosted', rtoRpo: 'Not Defined' });
  });

  it('does not offer allocated-but-unassociated scopes as saved hosting or prefill for read-only users', async () => {
    // Arrange
    vi.mocked(hostingApi.listAllSystemHostingAllocations).mockResolvedValue([allocation]);
    mount({ hostingModel: 'CSP-hosted' }, true);
    // Assert
    await screen.findByText('No hosting association is recorded for this system.');
    expect(screen.queryByText('Harbor hosting')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Use these hosting details' })).not.toBeInTheDocument();
    expect(screen.getByRole('combobox', { name: 'Hosting model' })).toBeDisabled();
    expect(capabilityApi.listSystemCapabilities).toHaveBeenCalledWith('org-a', 'system-a',
      { scope: 'applied', grouping: 'capability', page: 1, pageSize: 10 }, expect.any(AbortSignal));
  });
});
