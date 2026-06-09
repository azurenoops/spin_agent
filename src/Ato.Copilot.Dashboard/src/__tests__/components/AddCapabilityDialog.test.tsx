import { render, screen, waitFor } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach } from 'vitest';

// ─── Mocks ───────────────────────────────────────────────────────────────────

vi.mock('../../api/capabilities', () => ({
  getCapabilities: vi.fn(),
  getCapabilityMappings: vi.fn(),
  createCapabilityMappings: vi.fn(),
}));

import * as capApi from '../../api/capabilities';
import AddCapabilityDialog from '../../components/AddCapabilityDialog';

const mockGetCapabilities = capApi.getCapabilities as ReturnType<typeof vi.fn>;

// ─── Fixtures ────────────────────────────────────────────────────────────────

const makeCap = (id: string, name: string) => ({
  id,
  name,
  provider: 'Acme',
  category: 'IAM',
  categoryName: 'Identity & Access Management',
  description: `${name} description`,
  implementationStatus: 'Implemented' as const,
  mappedControlCount: 3,
  systemsUsingCount: 1,
});

const defaultProps = {
  systemId: 'sys-001',
  existingCapabilityIds: [] as string[],
  onClose: vi.fn(),
  onAdded: vi.fn(),
};

beforeEach(() => {
  vi.clearAllMocks();
});

// ─── Empty-state tests (the regression being fixed) ──────────────────────────

describe('AddCapabilityDialog — empty-state messages', () => {
  /**
   * SCENARIO A: org has zero capabilities (none created yet)
   * Expected: "No capabilities in your organization yet." + Capabilities Hub link
   * Bug before fix: showed "All capabilities are already linked to this system."
   */
  it('shows hub-redirect copy when org has no capabilities at all (totalOrgCapabilities === 0)', async () => {
    // Arrange — API returns empty items
    mockGetCapabilities.mockResolvedValue({ items: [], totalCount: 0 });

    // Act
    render(<AddCapabilityDialog {...defaultProps} />);

    // Assert
    await waitFor(() => {
      expect(screen.queryByText(/loading/i)).not.toBeInTheDocument();
    });

    expect(
      screen.getByText('No capabilities in your organization yet.'),
    ).toBeInTheDocument();

    expect(
      screen.getByRole('link', { name: /capabilities hub/i }),
    ).toHaveAttribute('href', '/capabilities');

    // Must NOT show the "all linked" message
    expect(
      screen.queryByText(/all capabilities are already linked/i),
    ).not.toBeInTheDocument();
  });

  /**
   * SCENARIO B: org has capabilities AND all are already linked to this system
   * Expected: "All capabilities are already linked to this system."
   * existingCapabilityIds matches every cap in the org → filtered list is empty,
   * but totalOrgCapabilities > 0.
   */
  it('shows "all linked" copy when org has caps but all are already linked', async () => {
    // Arrange — 2 caps in org, both already linked
    const caps = [makeCap('cap-1', 'MFA Enforcement'), makeCap('cap-2', 'RBAC Roles')];
    mockGetCapabilities.mockResolvedValue({ items: caps, totalCount: 2 });

    render(
      <AddCapabilityDialog
        {...defaultProps}
        existingCapabilityIds={['cap-1', 'cap-2']}
      />,
    );

    // Assert
    await waitFor(() => {
      expect(screen.queryByText(/loading/i)).not.toBeInTheDocument();
    });

    expect(
      screen.getByText(/all capabilities are already linked to this system/i),
    ).toBeInTheDocument();

    expect(
      screen.queryByText(/no capabilities in your organization yet/i),
    ).not.toBeInTheDocument();
  });

  /**
   * SCENARIO C: org has unlinked caps, but none match the current search term
   * Expected: "No capabilities match your search."
   * This path requires the user to type a search term — we verify the copy
   * is correct by mocking 1 unlinked cap and relying on the filter logic.
   *
   * Note: the search input is rendered before loading resolves. We type after
   * waitFor ensures the cap list is rendered so we can trigger a re-filter.
   */
  it('shows "no match" copy when unlinked caps exist but search has no results', async () => {
    // Arrange — 1 cap in org, not yet linked
    const caps = [makeCap('cap-1', 'MFA Enforcement')];
    mockGetCapabilities.mockResolvedValue({ items: caps, totalCount: 1 });

    const { getByPlaceholderText } = render(
      <AddCapabilityDialog {...defaultProps} existingCapabilityIds={[]} />,
    );

    // Wait for the cap to appear before searching
    await waitFor(() => {
      expect(screen.getByText('MFA Enforcement')).toBeInTheDocument();
    });

    // Type a query that won't match anything
    const searchInput = getByPlaceholderText(/search capabilities/i);
    searchInput.focus();
    // Simulate controlled input via fireEvent
    const { fireEvent } = await import('@testing-library/react');
    fireEvent.change(searchInput, { target: { value: 'zzz-no-match-zzz' } });

    // Assert
    await waitFor(() => {
      expect(
        screen.getByText('No capabilities match your search.'),
      ).toBeInTheDocument();
    });

    expect(
      screen.queryByText(/no capabilities in your organization yet/i),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByText(/all capabilities are already linked/i),
    ).not.toBeInTheDocument();
  });

  // ─── Happy-path sanity check ──────────────────────────────────────────────

  it('renders cap list when org has unlinked caps and no search is active', async () => {
    const caps = [
      makeCap('cap-1', 'MFA Enforcement'),
      makeCap('cap-2', 'RBAC Roles'),
    ];
    mockGetCapabilities.mockResolvedValue({ items: caps, totalCount: 2 });

    render(
      <AddCapabilityDialog
        {...defaultProps}
        existingCapabilityIds={['cap-2']} // cap-2 already linked; only cap-1 visible
      />,
    );

    await waitFor(() => {
      expect(screen.getByText('MFA Enforcement')).toBeInTheDocument();
    });

    expect(screen.queryByText('RBAC Roles')).not.toBeInTheDocument();

    // None of the empty-state messages should appear
    expect(
      screen.queryByText(/no capabilities in your organization yet/i),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByText(/all capabilities are already linked/i),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByText(/no capabilities match your search/i),
    ).not.toBeInTheDocument();
  });

  it('shows loading state while fetch is in-flight', () => {
    // Arrange — never-resolving promise keeps loading=true
    mockGetCapabilities.mockReturnValue(new Promise(() => {}));
    render(<AddCapabilityDialog {...defaultProps} />);
    expect(screen.getByText(/loading capabilities/i)).toBeInTheDocument();
  });
});
