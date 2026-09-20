import { render, screen, waitFor } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach } from 'vitest';

// ─────────────────────────────────────────────────────────────────────────────
// T034 [US2] — Failing test pinning the RoleAssignmentPanel contract.
//
// Drives FR-008, FR-010, FR-011, FR-013, FR-020, FR-027:
//   (1) Panel renders 7 rows (one per role in the canonical 7-role universe).
//   (2) `inherited` rows show an "Override" button (per FR-010).
//   (3) `override` rows show a "Remove override" button (per FR-011).
//   (4) `org-fallback` rows show a "Pending" badge with a tooltip.
//   (5) `legacy` rows show a "Legacy" badge (FR-024 read-side compatibility).
//   (6) `not-assigned` rows show an "Assign" button gated by RBAC.
// ─────────────────────────────────────────────────────────────────────────────

vi.mock('../../../api/roles', async (orig) => {
  const actual = await orig<typeof import('../../../api/roles')>();
  return {
    ...actual,
    rolesApi: {
      assignOrgRole: vi.fn(),
      removeOrgRole: vi.fn(),
      assignSystemRole: vi.fn(),
      removeSystemRole: vi.fn(),
      getSystemRoles: vi.fn(),
      getEffectiveRole: vi.fn(),
    },
  };
});

import RoleAssignmentPanel from '../../../components/cards/RoleAssignmentPanel';
import { rolesApi } from '../../../api/roles';

const mockedApi = rolesApi as unknown as {
  getSystemRoles: ReturnType<typeof vi.fn>;
};

const sevenRowResponse = {
  systemId: 'sys-1',
  roles: [
    { role: 'AuthorizingOfficial', person: null, source: 'not-assigned' },
    {
      role: 'Issm',
      person: { id: 'p-issm', displayName: 'Iris ISSM' },
      source: 'inherited',
      orgRoleId: '11111111-1111-1111-1111-111111111111',
    },
    {
      role: 'Isso',
      person: { id: 'p-isso', displayName: 'Owen ISSO' },
      source: 'override',
    },
    {
      role: 'Sca',
      person: { id: 'p-sca', displayName: 'Sam SCA' },
      source: 'org-fallback',
      orgRoleId: '22222222-2222-2222-2222-222222222222',
    },
    {
      role: 'SystemOwner',
      person: { id: 'p-so', displayName: 'Sue SystemOwner' },
      source: 'legacy',
    },
    { role: 'MissionOwner', person: null, source: 'not-assigned' },
    {
      role: 'Administrator',
      person: { id: 'p-admin', displayName: 'Admin Adams' },
      source: 'override',
    },
  ],
};

describe('RoleAssignmentPanel', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockedApi.getSystemRoles.mockResolvedValue(sevenRowResponse);
  });

  it('renders 7 rows (one per role in the 7-role universe)', async () => {
    // Arrange + Act
    render(<RoleAssignmentPanel registeredSystemId="sys-1" callerEffectiveRole="Issm" />);

    // Assert — every role appears in canonical order
    const allSevenRoles = [
      'AuthorizingOfficial',
      'Issm',
      'Isso',
      'Sca',
      'SystemOwner',
      'MissionOwner',
      'Administrator',
    ];

    await waitFor(() => {
      // Each role row must be present (search by data-testid for reliability)
      for (const role of allSevenRoles) {
        expect(screen.getByTestId(`role-row-${role}`)).toBeInTheDocument();
      }
    });
  });

  it('shows "Override" button on inherited rows (FR-010)', async () => {
    // Arrange + Act
    render(<RoleAssignmentPanel registeredSystemId="sys-1" callerEffectiveRole="Issm" />);

    // Assert
    await waitFor(() => {
      const issmRow = screen.getByTestId('role-row-Issm');
      const overrideBtn = issmRow.querySelector('button[data-action="override"]');
      expect(overrideBtn).not.toBeNull();
      expect(overrideBtn?.textContent).toMatch(/override/i);
    });
  });

  it('shows "Remove override" button on override rows (FR-011)', async () => {
    // Arrange + Act
    render(<RoleAssignmentPanel registeredSystemId="sys-1" callerEffectiveRole="Issm" />);

    // Assert
    await waitFor(() => {
      const issoRow = screen.getByTestId('role-row-Isso');
      const removeBtn = issoRow.querySelector('button[data-action="remove-override"]');
      expect(removeBtn).not.toBeNull();
      expect(removeBtn?.textContent).toMatch(/remove/i);
    });
  });

  it('shows "Pending" badge with tooltip on org-fallback rows', async () => {
    // Arrange + Act
    render(<RoleAssignmentPanel registeredSystemId="sys-1" callerEffectiveRole="Issm" />);

    // Assert
    await waitFor(() => {
      const scaRow = screen.getByTestId('role-row-Sca');
      const pendingBadge = scaRow.querySelector('[data-badge="pending"]');
      expect(pendingBadge).not.toBeNull();
      expect(pendingBadge?.getAttribute('title') ?? '').toMatch(/pending|propagat|fan.?out/i);
    });
  });

  it('shows "Legacy" badge on legacy rows (FR-024 read-side compatibility)', async () => {
    // Arrange + Act
    render(<RoleAssignmentPanel registeredSystemId="sys-1" callerEffectiveRole="Issm" />);

    // Assert
    await waitFor(() => {
      const soRow = screen.getByTestId('role-row-SystemOwner');
      const legacyBadge = soRow.querySelector('[data-badge="legacy"]');
      expect(legacyBadge).not.toBeNull();
    });
  });

  it('shows "Assign" button only for roles allowed by RBAC_ASSIGNABLE_BY[caller]', async () => {
    // Arrange — Isso caller: only MissionOwner + SystemOwner are assignable.
    // Test against the response we control: AO and MissionOwner are both
    // not-assigned. AO must NOT show an Assign button (ISSO can't assign AO).
    // MissionOwner MUST show one (ISSO can assign MissionOwner).
    render(<RoleAssignmentPanel registeredSystemId="sys-1" callerEffectiveRole="Isso" />);

    // Assert
    await waitFor(() => {
      const aoRow = screen.getByTestId('role-row-AuthorizingOfficial');
      expect(aoRow.querySelector('button[data-action="assign"]')).toBeNull();
      const moRow = screen.getByTestId('role-row-MissionOwner');
      expect(moRow.querySelector('button[data-action="assign"]')).not.toBeNull();
    });
  });

  it('explains read-only access when the caller has no assignable role', async () => {
    // Arrange
    render(<RoleAssignmentPanel registeredSystemId="sys-1" callerEffectiveRole={null} />);

    // Act
    await screen.findByTestId('role-row-MissionOwner');

    // Assert
    expect(screen.getByText(/read-only access/i)).toBeInTheDocument();
    expect(screen.getByText(/administrator, ISSM, or ISSO/i)).toBeInTheDocument();
  });
});
