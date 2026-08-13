import { render, screen, fireEvent, waitFor, act } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach } from 'vitest';

// ─────────────────────────────────────────────────────────────────────────────
// T033 [US2] — Failing test pinning the AssignRoleDialog contract.
//
// Drives FR-008, FR-014, FR-026, FR-027:
//   (a) Role dropdown filters by RBAC_ASSIGNABLE_BY[callerEffectiveRole].
//   (b) lockRole=true disables the role dropdown.
//   (c) SoD warning from the response renders inline.
//   (d) bootstrap prop wires bootstrap=true into the POST body.
//
// T033-b [#713 / BUG-14] — Person-picker dropdown populates via
// onboarding.listPersons() (previously used a deprecated API that always
// returned an empty list, so no RMF role could ever be assigned org-wide).
// ─────────────────────────────────────────────────────────────────────────────

// Mock the rolesApi module BEFORE importing the dialog.
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

// Mock onboarding so listPersons() is controllable. Default: empty array so
// the existing tests (which use the GUID text-input fallback) continue to pass.
vi.mock('../../../features/onboarding/api/onboardingApi', async (orig) => {
  const actual = await orig<typeof import('../../../features/onboarding/api/onboardingApi')>();
  return {
    ...actual,
    onboarding: {
      ...actual.onboarding,
      listPersons: vi.fn().mockResolvedValue([]),
    },
  };
});

import AssignRoleDialog from '../../../components/roles/AssignRoleDialog';
import { rolesApi } from '../../../api/roles';
import { onboarding } from '../../../features/onboarding/api/onboardingApi';

const mockedApi = rolesApi as unknown as {
  assignOrgRole: ReturnType<typeof vi.fn>;
  assignSystemRole: ReturnType<typeof vi.fn>;
};

const mockedOnboarding = onboarding as unknown as {
  listPersons: ReturnType<typeof vi.fn>;
};

describe('AssignRoleDialog', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    // Default: listPersons returns empty → existing tests use GUID text-input path.
    mockedOnboarding.listPersons.mockResolvedValue([]);
  });

  it('filters role dropdown by RBAC_ASSIGNABLE_BY for Isso callers', () => {
    // Arrange — ISSO callers may assign ONLY MissionOwner and SystemOwner.
    render(
      <AssignRoleDialog
        open
        onClose={vi.fn()}
        scope={{ kind: 'organization' }}
        callerEffectiveRole="Isso"
        onAssigned={vi.fn()}
      />,
    );

    // Act
    const dropdown = screen.getByLabelText(/role/i) as HTMLSelectElement;
    const optionValues = Array.from(dropdown.options).map((o) => o.value);

    // Assert — ISSO may assign only MissionOwner + SystemOwner
    expect(optionValues).toEqual(expect.arrayContaining(['MissionOwner', 'SystemOwner']));
    expect(optionValues).not.toEqual(expect.arrayContaining(['AuthorizingOfficial']));
    expect(optionValues).not.toEqual(expect.arrayContaining(['Issm']));
    expect(optionValues).not.toEqual(expect.arrayContaining(['Sca']));
    expect(optionValues).not.toEqual(expect.arrayContaining(['Administrator']));
  });

  it('disables role dropdown when lockRole=true and pre-selects initialRole', () => {
    // Arrange
    render(
      <AssignRoleDialog
        open
        onClose={vi.fn()}
        scope={{ kind: 'organization' }}
        initialRole="MissionOwner"
        lockRole
        callerEffectiveRole="Issm"
        onAssigned={vi.fn()}
      />,
    );

    // Act
    const dropdown = screen.getByLabelText(/role/i) as HTMLSelectElement;

    // Assert
    expect(dropdown.disabled).toBe(true);
    expect(dropdown.value).toBe('MissionOwner');
  });

  it('renders inline SoD warning when server response contains one', async () => {
    // Arrange — server returns success with SoD warning
    mockedApi.assignOrgRole.mockResolvedValueOnce({
      status: 'success',
      data: {
        role: 'Issm',
        person: { id: 'p-1', displayName: 'Conflicted Carol' },
        source: 'override',
      },
      warnings: [
        {
          code: 'SOD_VIOLATION',
          message:
            'Person already holds AuthorizingOfficial; assigning Issm would violate DoDI 8510.01 separation of duties.',
          roleConflict: ['AuthorizingOfficial', 'Issm'],
          dodiReference: 'DoDI 8510.01 Enclosure 3 § 4.b',
          suggestedAction: 'Assign Issm to a different person.',
        },
      ],
    });
    const onAssigned = vi.fn();

    render(
      <AssignRoleDialog
        open
        onClose={vi.fn()}
        scope={{ kind: 'organization' }}
        initialRole="Issm"
        callerEffectiveRole="Issm"
        onAssigned={onAssigned}
      />,
    );

    // Provide person id + click Assign
    const personInput = screen.getByLabelText(/person/i) as HTMLInputElement;
    fireEvent.change(personInput, { target: { value: '11111111-1111-1111-1111-111111111111' } });

    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: /assign/i }));
    });

    // Assert — the SoD warning is visible inline. We use the unique part of
    // the dodiReference string ("Enclosure 3") to disambiguate from the
    // matching substring in the user-facing message.
    await waitFor(() => {
      expect(screen.getByText(/DoDI 8510\.01 Enclosure/i)).toBeInTheDocument();
    });
    expect(screen.getByText(/violate.*separation of duties/i)).toBeInTheDocument();
    expect(onAssigned).toHaveBeenCalled();
  });

  it('passes bootstrap=true in the Org-role POST body when prop is set', async () => {
    // Arrange
    mockedApi.assignOrgRole.mockResolvedValueOnce({
      status: 'success',
      data: { role: 'Administrator', person: { id: 'p-1', displayName: 'A' }, source: 'override' },
    });
    render(
      <AssignRoleDialog
        open
        onClose={vi.fn()}
        scope={{ kind: 'organization' }}
        initialRole="Administrator"
        lockRole
        callerEffectiveRole={null}
        bootstrap
        onAssigned={vi.fn()}
      />,
    );

    // Act — fill person + submit
    const personInput = screen.getByLabelText(/person/i) as HTMLInputElement;
    fireEvent.change(personInput, { target: { value: '22222222-2222-2222-2222-222222222222' } });
    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: /assign/i }));
    });

    // Assert — POST body included bootstrap: true
    await waitFor(() => {
      expect(mockedApi.assignOrgRole).toHaveBeenCalledWith(
        expect.objectContaining({ bootstrap: true, role: 'Administrator' }),
      );
    });
  });

  it('renders inline error block when server returns 403 RBAC_ROLE_ASSIGN_DENIED', async () => {
    // Arrange
    mockedApi.assignSystemRole.mockResolvedValueOnce({
      status: 'error',
      error: {
        code: 'RBAC_ROLE_ASSIGN_DENIED',
        message: 'Callers with effective role Isso may not assign AuthorizingOfficial.',
        callerEffectiveRole: 'Isso',
        targetRole: 'AuthorizingOfficial',
      },
    });

    render(
      <AssignRoleDialog
        open
        onClose={vi.fn()}
        scope={{ kind: 'system', registeredSystemId: 'sys-1' }}
        initialRole="AuthorizingOfficial"
        callerEffectiveRole="Isso"
        onAssigned={vi.fn()}
      />,
    );

    fireEvent.change(screen.getByLabelText(/person/i), {
      target: { value: '33333333-3333-3333-3333-333333333333' },
    });
    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: /assign/i }));
    });

    // Assert — error code surfaced
    await waitFor(() => {
      expect(screen.getByText(/may not assign/i)).toBeInTheDocument();
    });
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// T033-b [#713 / BUG-14] — Person-picker dropdown populates via listPersons()
//
// Root cause: the dialog previously called a deprecated component API that
// always returned an empty array, so the person-picker SELECT never rendered
// and no RMF role could be assigned org-wide (open 43 days).
// Fix: dialog now calls onboarding.listPersons() on open.
// ─────────────────────────────────────────────────────────────────────────────
describe('AssignRoleDialog — #713 person-picker via listPersons()', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders a SELECT dropdown (not a text input) when listPersons() returns records', async () => {
    // Arrange — listPersons returns two persons
    mockedOnboarding.listPersons.mockResolvedValue([
      { id: 'p-alice', displayName: 'Alice Adams', email: 'alice@example.mil', isLinkedToDirectory: false },
      { id: 'p-bob',   displayName: 'Bob Baker',   email: 'bob@example.mil',   isLinkedToDirectory: false },
    ]);

    render(
      <AssignRoleDialog
        open
        onClose={vi.fn()}
        scope={{ kind: 'organization' }}
        callerEffectiveRole="Issm"
        onAssigned={vi.fn()}
      />,
    );

    // Assert — after listPersons resolves, a SELECT with both person options is present
    const select = await screen.findByRole('combobox', { name: /person/i });
    expect(select).toBeInTheDocument();

    const options = Array.from((select as HTMLSelectElement).options).map((o) => o.text);
    expect(options).toContain('Alice Adams');
    expect(options).toContain('Bob Baker');

    // The raw GUID text input must NOT be rendered when people are loaded
    expect(screen.queryByPlaceholderText(/guid of the person/i)).not.toBeInTheDocument();
  });

  it('submits the correct personId when a person is selected from the dropdown', async () => {
    // Arrange
    mockedOnboarding.listPersons.mockResolvedValue([
      { id: 'p-carol', displayName: 'Carol Chen', email: 'carol@example.mil', isLinkedToDirectory: true },
    ]);
    mockedApi.assignOrgRole.mockResolvedValueOnce({
      status: 'success',
      data: { role: 'Issm', person: { id: 'p-carol', displayName: 'Carol Chen' }, source: 'override' },
    });
    const onAssigned = vi.fn();

    render(
      <AssignRoleDialog
        open
        onClose={vi.fn()}
        scope={{ kind: 'organization' }}
        initialRole="Issm"
        callerEffectiveRole="Issm"
        onAssigned={onAssigned}
      />,
    );

    // Wait for the person dropdown to appear then select Carol
    const select = await screen.findByRole('combobox', { name: /person/i });
    await act(async () => {
      fireEvent.change(select, { target: { value: 'p-carol' } });
    });

    // Submit
    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: /assign/i }));
    });

    // Assert — the correct personId was posted
    await waitFor(() => {
      expect(mockedApi.assignOrgRole).toHaveBeenCalledWith(
        expect.objectContaining({ personId: 'p-carol', role: 'Issm' }),
      );
    });
    expect(onAssigned).toHaveBeenCalled();
  });

  it('falls back to GUID text input when listPersons() returns an empty array', async () => {
    // Arrange
    mockedOnboarding.listPersons.mockResolvedValue([]);

    render(
      <AssignRoleDialog
        open
        onClose={vi.fn()}
        scope={{ kind: 'organization' }}
        callerEffectiveRole="Issm"
        onAssigned={vi.fn()}
      />,
    );

    // Assert — GUID text input (manual entry fallback) is present
    await waitFor(() => {
      expect(screen.getByPlaceholderText(/guid of the person/i)).toBeInTheDocument();
    });
  });
});
