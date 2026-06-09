/**
 * ComponentInventory — "Add Existing (Org-Level)" empty-state messages
 *
 * Tests the three-way empty-state branch introduced to fix the bug where
 * orgComponents.length === 0 couldn't distinguish:
 *   (A) no org-level components exist yet            → guide to Components library
 *   (B) all org components already assigned          → "all assigned" copy
 *   (C) unassigned components exist, search no match → "no match" copy
 *
 * Strategy: render ComponentInventory with useParams returning a known systemId,
 * mock the API layer (getComponents / listComponents), click the "Add Existing"
 * tab, then assert the correct empty-state copy.
 */
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { MemoryRouter, Route, Routes } from 'react-router-dom';

// ─── Mocks ───────────────────────────────────────────────────────────────────

vi.mock('../../api/components', () => ({
  getComponents: vi.fn(),
  listComponents: vi.fn(),
  createComponent: vi.fn(),
  updateComponent: vi.fn(),
  deleteComponent: vi.fn(),
  discoverSystemAzureResources: vi.fn(),
  importSystemAzureComponents: vi.fn(),
  relinkComponentFindings: vi.fn(),
  assignToSystem: vi.fn(),
}));

vi.mock('../../api/boundaries', () => ({
  fetchBoundaryDefinitions: vi.fn().mockResolvedValue([]),
}));

vi.mock('../../api/client', () => ({
  default: {
    get: vi.fn().mockResolvedValue({ data: { openCount: 0, overdueCount: 0, highestSeverity: null } }),
  },
}));

vi.mock('../../features/onboarding/api/onboardingApi', () => ({
  onboarding: { getSystemContext: vi.fn().mockResolvedValue(null) },
}));

// Stub heavy child components that would need further mocking
vi.mock('../../components/cards/ComponentSection', () => ({
  ComponentSection: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
}));
vi.mock('../../components/forms/ComponentForm', () => ({
  ComponentForm: () => <div data-testid="component-form" />,
}));
vi.mock('../../components/cards/MetricCard', () => ({
  default: () => <div />,
}));
vi.mock('../../hooks/usePolling', () => ({
  usePolling: (fn: () => void) => { fn(); },
}));

import * as compApi from '../../api/components';
import ComponentInventory from '../../pages/ComponentInventory';

const mockGetComponents = compApi.getComponents as ReturnType<typeof vi.fn>;
const mockListComponents = compApi.listComponents as ReturnType<typeof vi.fn>;

// ─── Fixtures ────────────────────────────────────────────────────────────────

const SYSTEM_ID = 'sys-abc-001';

const makeSystemComp = (id: string, name: string) => ({
  id,
  name,
  componentType: 'Software',
  subType: null,
  status: 'Operational',
  description: '',
  personName: null,
  capabilityLinks: [],
  vendorId: null,
  vendorName: null,
  scanResults: [],
  boundaries: [],
  poamCount: 0,
});

const makeOrgComp = (id: string, name: string) => ({
  id,
  name,
  componentType: 'Software',
  subType: null,
  status: 'Operational',
  personName: null,
  capabilityLinks: [],
  description: '',
});

const emptySystemResponse = { items: [], summary: { total: 0 } };

function renderPage() {
  return render(
    <MemoryRouter initialEntries={[`/systems/${SYSTEM_ID}/components`]}>
      <Routes>
        <Route path="/systems/:systemId/components" element={<ComponentInventory />} />
      </Routes>
    </MemoryRouter>,
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  // Default: system has no components (clean slate)
  mockGetComponents.mockResolvedValue(emptySystemResponse);
});

// ─── Helper: open "Add Existing" tab ─────────────────────────────────────────

async function openAddExistingTab() {
  // First click "Add Component" to show the panel (or find it if always visible)
  const addBtn = await screen.findByRole('button', { name: /add component/i });
  fireEvent.click(addBtn);
  // Then click the "Add Existing" tab
  const existingTab = await screen.findByRole('button', { name: /add existing/i });
  fireEvent.click(existingTab);
}

// ─── Tests ───────────────────────────────────────────────────────────────────

describe('ComponentInventory — Add Existing empty-state messages', () => {
  /**
   * SCENARIO A: org has zero components (none created yet)
   * Expected: actionable guidance + link to /components
   * Bug before fix: "No unassigned org-level components found."
   */
  it('(A) shows Components library link when org has no components at all', async () => {
    // Arrange — org-wide fetch returns nothing
    mockListComponents.mockResolvedValue({ items: [], totalCount: 0 });

    renderPage();
    await openAddExistingTab();

    // Assert
    await waitFor(() => {
      expect(
        screen.getByText(/no org-level components available to assign/i),
      ).toBeInTheDocument();
    });

    const link = screen.getByRole('link', { name: /components library/i });
    expect(link).toHaveAttribute('href', '/components');

    // Must NOT show the "all assigned" message
    expect(
      screen.queryByText(/all org-level components are already assigned/i),
    ).not.toBeInTheDocument();
  });

  /**
   * SCENARIO B: org has components but ALL are already assigned to this system
   * Expected: "All org-level components are already assigned to this system."
   * totalOrgComponents > 0, but orgComponents (after filter) = []
   */
  it('(B) shows "all assigned" copy when every org component is already on this system', async () => {
    // Arrange — 2 org comps, both already present as system comps
    const orgComps = [makeOrgComp('c1', 'API Gateway'), makeOrgComp('c2', 'Auth Service')];
    mockListComponents.mockResolvedValue({ items: orgComps, totalCount: 2 });
    // System already has both
    mockGetComponents.mockResolvedValue({
      items: [makeSystemComp('c1', 'API Gateway'), makeSystemComp('c2', 'Auth Service')],
      summary: { total: 2 },
    });

    renderPage();
    await openAddExistingTab();

    await waitFor(() => {
      expect(
        screen.getByText(/all org-level components are already assigned to this system/i),
      ).toBeInTheDocument();
    });

    expect(
      screen.queryByText(/no org-level components available to assign/i),
    ).not.toBeInTheDocument();
  });

  /**
   * SCENARIO C: org has unassigned components, user types a non-matching search
   * Expected: "No components match your search."
   */
  it('(C) shows search-no-match copy when search term yields no results', async () => {
    // Arrange — 1 org comp, not assigned to this system
    const orgComps = [makeOrgComp('c1', 'API Gateway')];
    mockListComponents.mockResolvedValue({ items: orgComps, totalCount: 1 });

    renderPage();
    await openAddExistingTab();

    // Wait for the component to appear in the list
    await waitFor(() => {
      expect(screen.getByText('API Gateway')).toBeInTheDocument();
    });

    // Type a non-matching query
    const searchInput = screen.getByPlaceholderText(/search by name/i);
    fireEvent.change(searchInput, { target: { value: 'zzz-no-match-zzz' } });

    await waitFor(() => {
      expect(
        screen.getByText(/no components match your search/i),
      ).toBeInTheDocument();
    });

    expect(
      screen.queryByText(/no org-level components available/i),
    ).not.toBeInTheDocument();
  });

  /**
   * SCENARIO D: happy path — unassigned org component renders in list
   */
  it('(D) renders the org component in the list when one is unassigned', async () => {
    // Arrange — 2 org comps, only c2 already on this system
    const orgComps = [makeOrgComp('c1', 'API Gateway'), makeOrgComp('c2', 'Auth Service')];
    mockListComponents.mockResolvedValue({ items: orgComps, totalCount: 2 });
    mockGetComponents.mockResolvedValue({
      items: [makeSystemComp('c2', 'Auth Service')],
      summary: { total: 1 },
    });

    renderPage();
    await openAddExistingTab();

    await waitFor(() => {
      expect(screen.getByText('API Gateway')).toBeInTheDocument();
    });

    // Already-assigned comp must be filtered out
    expect(screen.queryByText('Auth Service')).not.toBeInTheDocument();

    // No empty-state messages
    expect(screen.queryByText(/no org-level components available/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/all org-level components are already/i)).not.toBeInTheDocument();
  });

  /**
   * Loading state
   */
  it('shows loading text while org components are being fetched', async () => {
    // Arrange — never-resolving promise keeps orgLoading=true
    mockListComponents.mockReturnValue(new Promise(() => {}));

    renderPage();
    await openAddExistingTab();

    await waitFor(() => {
      expect(screen.getByText(/loading org components/i)).toBeInTheDocument();
    });
  });
});
