import { render, screen, waitFor } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach } from 'vitest';

// ─── Mocks ───────────────────────────────────────────────────────────────────

vi.mock('../../api/capabilities', () => ({
  getAvailableCapabilities: vi.fn(),
  createCapabilityMappings: vi.fn(),
  subscribeCapability: vi.fn(),
}));

import * as capApi from '../../api/capabilities';
import AddCapabilityDialog from '../../components/AddCapabilityDialog';
import { WorkspaceNavigationProvider } from '../../features/workspaces/workspaceNavigation';

const mockGetAvailableCapabilities = capApi.getAvailableCapabilities as ReturnType<typeof vi.fn>;
const mockCreateCapabilityMappings = capApi.createCapabilityMappings as ReturnType<typeof vi.fn>;
const mockSubscribeCapability = capApi.subscribeCapability as ReturnType<typeof vi.fn>;

// ─── Fixtures ────────────────────────────────────────────────────────────────

const makeCap = (id: string, name: string, source: 'Organization' | 'CSP' = 'Organization') => ({
  id,
  name,
  provider: 'Acme',
  category: 'IAM',
  description: `${name} description`,
  source,
  mappedControlIds: ['IA-2', 'IA-5'],
  mappedControlCount: 3,
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
  it('keeps the capability-library link inside the organization workspace', async () => {
    // Arrange
    mockGetAvailableCapabilities.mockResolvedValue({ items: [], totalCount: 0, excludedCount: 0 });

    // Act
    render(
      <WorkspaceNavigationProvider workspace={{ kind: 'organization', tenantId: 'org-alpha' }}>
        <AddCapabilityDialog {...defaultProps} />
      </WorkspaceNavigationProvider>,
    );

    // Assert
    expect(await screen.findByRole('link', { name: /capabilities hub/i }))
      .toHaveAttribute('href', '/workspaces/organizations/org-alpha/capabilities');
  });

  /**
   * SCENARIO A: org has zero capabilities (none created yet)
  * Expected: actionable copy covering both organization and CSP sources.
   * Bug before fix: showed "All capabilities are already linked to this system."
   */
  it('shows hub-redirect copy when org has no capabilities at all (totalOrgCapabilities === 0)', async () => {
    // Arrange — API returns empty items
    mockGetAvailableCapabilities.mockResolvedValue({ items: [], totalCount: 0, excludedCount: 0 });

    // Act
    render(<AddCapabilityDialog {...defaultProps} />);

    // Assert
    await waitFor(() => {
      expect(screen.queryByText(/loading/i)).not.toBeInTheDocument();
    });

    expect(
      screen.getByText('No eligible capabilities are available.'),
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
    // Arrange — 2 caps exist, both already linked
    mockGetAvailableCapabilities.mockResolvedValue({ items: [], totalCount: 2, excludedCount: 2 });

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
      screen.queryByText(/no eligible capabilities are available/i),
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
    mockGetAvailableCapabilities.mockResolvedValue({ items: caps, totalCount: 1, excludedCount: 0 });

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
      screen.queryByText(/no eligible capabilities are available/i),
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
    mockGetAvailableCapabilities.mockResolvedValue({ items: [caps[0]], totalCount: 2, excludedCount: 1 });

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
      screen.queryByText(/no eligible capabilities are available/i),
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
    mockGetAvailableCapabilities.mockReturnValue(new Promise(() => {}));
    render(<AddCapabilityDialog {...defaultProps} />);
    expect(screen.getByText(/loading capabilities/i)).toBeInTheDocument();
  });

  it('renders organization and CSP capabilities with source provenance', async () => {
    // Arrange
    mockGetAvailableCapabilities.mockResolvedValue({
      items: [
        makeCap('org-1', 'Organization MFA'),
        makeCap('csp-1', 'CSP Key Management', 'CSP'),
      ],
      totalCount: 2,
      excludedCount: 0,
    });

    // Act
    render(<AddCapabilityDialog {...defaultProps} />);

    // Assert
    await waitFor(() => expect(screen.getByText('Organization MFA')).toBeInTheDocument());
    expect(screen.getByText('CSP Key Management')).toBeInTheDocument();
    expect(screen.getByText('Organization')).toBeInTheDocument();
    expect(screen.getByText('CSP')).toBeInTheDocument();
  });

  it('subscribes a selected CSP capability without creating organization mappings', async () => {
    // Arrange
    const { fireEvent } = await import('@testing-library/react');
    mockGetAvailableCapabilities.mockResolvedValue({
      items: [makeCap('csp-1', 'CSP Key Management', 'CSP')],
      totalCount: 1,
      excludedCount: 0,
    });
    mockSubscribeCapability.mockResolvedValue({ id: 'subscription-1' });
    render(<AddCapabilityDialog {...defaultProps} />);
    await waitFor(() => expect(screen.getByText('CSP Key Management')).toBeInTheDocument());

    // Act
    fireEvent.click(screen.getByText('CSP Key Management'));
    fireEvent.click(screen.getByRole('button', { name: 'Add Capability' }));

    // Assert
    await waitFor(() => expect(mockSubscribeCapability).toHaveBeenCalledWith('sys-001', 'csp-1'));
    expect(mockCreateCapabilityMappings).not.toHaveBeenCalled();
    expect(defaultProps.onAdded).toHaveBeenCalled();
  });

  it('creates system mappings for a selected organization capability', async () => {
    // Arrange
    const { fireEvent } = await import('@testing-library/react');
    mockGetAvailableCapabilities.mockResolvedValue({
      items: [makeCap('org-1', 'Organization MFA')],
      totalCount: 1,
      excludedCount: 0,
    });
    mockCreateCapabilityMappings.mockResolvedValue({ created: 2 });
    render(<AddCapabilityDialog {...defaultProps} />);
    await waitFor(() => expect(screen.getByText('Organization MFA')).toBeInTheDocument());

    // Act
    fireEvent.click(screen.getByText('Organization MFA'));
    fireEvent.click(screen.getByRole('button', { name: 'Add Capability' }));

    // Assert
    await waitFor(() => expect(mockCreateCapabilityMappings).toHaveBeenCalledWith('org-1', {
      mappings: [
        { controlId: 'IA-2', role: 'Primary', registeredSystemId: 'sys-001' },
        { controlId: 'IA-5', role: 'Primary', registeredSystemId: 'sys-001' },
      ],
    }));
    expect(mockSubscribeCapability).not.toHaveBeenCalled();
  });

  it('shows an API failure instead of an empty catalog', async () => {
    // Arrange
    mockGetAvailableCapabilities.mockRejectedValue(new Error('network'));

    // Act
    render(<AddCapabilityDialog {...defaultProps} />);

    // Assert
    await waitFor(() => expect(screen.getByText('Failed to load capabilities')).toBeInTheDocument());
    expect(screen.queryByText(/no eligible capabilities are available/i)).not.toBeInTheDocument();
  });
});
