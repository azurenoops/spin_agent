import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import OrgSettingsPage from '../../pages/settings/OrgSettingsPage';
const state = vi.hoisted(() => ({ canManage: true }));
vi.mock('../../features/workspaces/WorkspaceBoundary', () => ({ useWorkspaceSession: () => ({
  workspace: { kind: 'organization', mode: 'ordinary', permissions: { canManageOrganization: state.canManage, canManageMemberships: state.canManage } },
}) }));
vi.mock('../../features/onboarding/api/onboardingApi', () => ({ onboarding: {
  getOrganizationContext: vi.fn().mockResolvedValue({ organizationName: 'Mission organization', branch: 0, classificationPosture: 0 }),
} }));
beforeEach(() => { state.canManage = true; });
describe('Organization-owned operational administration', () => {
  it('links administrators to canonical subscription and setup workflows', async () => {
    // Arrange / Act
    render(<MemoryRouter><OrgSettingsPage /></MemoryRouter>);
    // Assert
    await screen.findByDisplayValue('Mission organization');
    expect(screen.getByRole('link', { name: 'Manage Azure subscriptions' })).toHaveAttribute('href', '/settings/azure-subscriptions');
    expect(screen.getByRole('link', { name: 'Manage organization access' })).toHaveAttribute('href', '/settings/memberships');
    expect(screen.getByRole('link', { name: 'Maintain organization setup' })).toHaveAttribute('href', '/onboarding?stepNav=admin');
    expect(screen.queryByLabelText('Session Timeout')).not.toBeInTheDocument();
  });
  it('does not advertise administration from a non-administrator workspace', async () => {
    // Arrange
    state.canManage = false;
    // Act
    render(<MemoryRouter><OrgSettingsPage /></MemoryRouter>);
    await screen.findByDisplayValue('Mission organization');
    // Assert
    expect(screen.queryByRole('link', { name: 'Manage Azure subscriptions' })).not.toBeInTheDocument();
  });
});
