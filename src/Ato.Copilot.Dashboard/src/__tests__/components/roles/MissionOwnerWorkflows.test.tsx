import { render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import GateActionDialog from '../../../components/cards/GateActionDialog';
import AssignRoles from '../../../components/wizard/steps/AssignRoles';
import VerifyRoles from '../../../components/wizard/steps/VerifyRoles';
import Step2RoleAssignments from '../../../features/onboarding/steps/Step2RoleAssignments';
import { rolesApi } from '../../../api/roles';
import { listComponents } from '../../../api/components';
import { onboarding } from '../../../features/onboarding/api/onboardingApi';

vi.mock('../../../api/roles', () => ({
  rolesApi: {
    getSystemRoles: vi.fn(),
    assignSystemRole: vi.fn(),
    removeSystemRole: vi.fn(),
  },
}));

vi.mock('../../../api/components', () => ({
  listComponents: vi.fn(),
}));

vi.mock('../../../features/onboarding/api/onboardingApi', () => ({
  onboarding: {
    listPersons: vi.fn(),
    listRoleAssignments: vi.fn(),
  },
}));

describe('Mission Owner assignment workflows', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(rolesApi.getSystemRoles).mockResolvedValue({ systemId: 'sys-1', roles: [] });
    vi.mocked(listComponents).mockResolvedValue({ items: [], totalCount: 0, page: 1, pageSize: 200 });
    vi.mocked(onboarding.listPersons).mockResolvedValue([]);
    vi.mocked(onboarding.listRoleAssignments).mockResolvedValue([]);
  });

  it('offers Mission Owner from the readiness assignment dialog', async () => {
    // Arrange
    render(
      <GateActionDialog
        action="roles"
        systemId="sys-1"
        onClose={vi.fn()}
        onSuccess={vi.fn()}
      />,
    );

    // Act
    const option = await screen.findByRole('option', { name: 'Mission Owner' });

    // Assert
    expect(option).toHaveValue('MissionOwner');
  });

  it('offers Mission Owner during system registration', async () => {
    // Arrange
    render(<AssignRoles systemId="sys-1" onNext={vi.fn()} onErrors={vi.fn()} />);

    // Act
    const label = await screen.findByText('Mission Owner');

    // Assert
    expect(label).toBeInTheDocument();
  });

  it('offers all seven roles during organization onboarding', async () => {
    // Arrange
    render(<Step2RoleAssignments />);

    // Act
    const rolePicker = await screen.findByLabelText('Role');

    // Assert
    expect(rolePicker).toHaveValue('Issm');
    expect(screen.getAllByRole('option')).toHaveLength(8);
    expect(screen.getByRole('option', { name: /Mission Owner/ })).toHaveValue('MissionOwner');
    expect(screen.getByRole('option', { name: /Authorizing Official/ })).toHaveValue('AuthorizingOfficial');
    expect(screen.getByRole('option', { name: /System Owner/ })).toHaveValue('SystemOwner');
  });

  it('shows the canonical Mission Owner label during role verification', async () => {
    // Arrange
    vi.mocked(rolesApi.getSystemRoles).mockResolvedValueOnce({
      systemId: 'sys-1',
      roles: [{
        role: 'MissionOwner',
        person: { id: 'person-1', displayName: 'Morgan Owner' },
        source: 'override',
      }],
    });
    render(<VerifyRoles systemId="sys-1" onNext={vi.fn()} />);

    // Act
    const label = await screen.findByText('Mission Owner');

    // Assert
    expect(label).toBeInTheDocument();
  });
});