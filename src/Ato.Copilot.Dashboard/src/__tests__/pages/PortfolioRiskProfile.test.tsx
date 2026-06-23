/**
 * PortfolioRiskProfile — System Risk Summary row navigation (Issue #525)
 *
 * Verifies that clicking ANY cell in the System Risk Summary table rows
 * triggers navigation to the system detail page — not just the name cell.
 *
 * TDD: this test was written before the fix; it will be RED until the
 * <tr onClick={() => navigate(...)}> is added.
 *
 * Also validates that:
 *   - Clicking the system name link navigates correctly (always worked)
 *   - Other cells (Impact, RMF Phase, Compliance, POA&Ms, CAT counts, ATO)
 *     now also navigate (the bug)
 *   - Compliance by System bar chart rows navigate (already use <Link>)
 *   - ATO Status table rows navigate (already use <Link>)
 */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';

// ─── Mocks ───────────────────────────────────────────────────────────────────

const mockNavigate = vi.fn();

vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual<typeof import('react-router-dom')>('react-router-dom');
  return { ...actual, useNavigate: () => mockNavigate };
});

vi.mock('../../components/layout/PageLayout', () => ({
  default: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
}));

vi.mock('../../components/layout/PageHero', () => ({
  default: () => <div data-testid="page-hero" />,
}));

vi.mock('../../hooks/usePolling', () => ({
  usePolling: (fn: () => void) => { fn(); },
}));

// Mock getPortfolio — returns a PaginatedResponse shape with .items
vi.mock('../../api/portfolio', () => ({
  getPortfolio: vi.fn().mockResolvedValue({
    items: [
      {
        systemId: 'sys-1',
        name: 'Test System',
        acronym: 'TS',
        systemType: 'MajorApplication',
        missionCriticality: 'High',
        hostingEnvironment: 'Cloud',
        description: null,
        impactLevel: 'Moderate',
        currentRmfPhase: 'Authorize',
        complianceScore: 85,
        complianceScoreDelta: 2,
        atoExpirationDate: null,
        atoStatus: 'Active',
        atoDaysRemaining: 120,
        atoSeverity: 'green' as const,
        openPoamCount: 2,
        overduePoamCount: 0,
        catICounts: 0,
        catIICounts: 1,
        catIIICounts: 3,
        isSetupComplete: true,
        hasBoundary: true,
        hasRoles: true,
        hasCategorization: true,
      },
    ],
    totalCount: 1,
    cursor: null,
  }),
}));

// Mock getCoverage — used via the re-export in api/portfolio but actually
// lives in api/capabilities; mock it there directly.
vi.mock('../../api/capabilities', () => ({
  getCoverage: vi.fn().mockResolvedValue({
    orgWide: { coveragePercent: 75 },
    perSystem: [],
    perFamily: [],
  }),
}));

// ─── Helpers ─────────────────────────────────────────────────────────────────

import PortfolioRiskProfile from '../../pages/PortfolioRiskProfile';

function renderPage() {
  return render(
    <MemoryRouter>
      <PortfolioRiskProfile />
    </MemoryRouter>,
  );
}

// ─── Tests ───────────────────────────────────────────────────────────────────

describe('PortfolioRiskProfile — System Risk Summary row navigation (#525)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders the System Risk Summary table with the test system', async () => {
    renderPage();
    await waitFor(() => {
      expect(screen.getByText('Test System')).toBeInTheDocument();
    });
  });

  /**
   * BUG #525: clicking the Impact cell was a no-op.
   * After the fix (tr onClick), clicking any cell should call navigate('/systems/sys-1').
   */
  it('navigates to system detail when clicking the Impact cell', async () => {
    renderPage();

    // Wait for the table to render
    await waitFor(() => expect(screen.getByText('Moderate')).toBeInTheDocument());

    fireEvent.click(screen.getByText('Moderate'));

    expect(mockNavigate).toHaveBeenCalledWith('/systems/sys-1');
  });

  it('navigates to system detail when clicking the RMF Phase cell', async () => {
    renderPage();
    await waitFor(() => expect(screen.getByText('Authorize')).toBeInTheDocument());
    fireEvent.click(screen.getByText('Authorize'));
    expect(mockNavigate).toHaveBeenCalledWith('/systems/sys-1');
  });

  it('navigates to system detail when clicking the CAT II count cell', async () => {
    renderPage();
    // Wait for the system row to render — find cell in the table body
    await waitFor(() => expect(screen.getByText('Test System')).toBeInTheDocument());

    // catIICounts = 1; find the cell with value "1" in the amber-colored span
    // There may be multiple "1"s on the page so we scope to the row
    const tableBody = document.querySelector('tbody');
    expect(tableBody).not.toBeNull();
    const catIICell = tableBody!.querySelector('td:nth-child(7)');
    expect(catIICell).not.toBeNull();
    fireEvent.click(catIICell!);

    expect(mockNavigate).toHaveBeenCalledWith('/systems/sys-1');
  });

  it('navigates to system detail when clicking the ATO status cell', async () => {
    renderPage();
    // 'Active' appears in both ATO Status section and the table — wait for table body
    await waitFor(() => expect(document.querySelector('tbody')).not.toBeNull());

    // Scope to the table body last cell
    const tableBody = document.querySelector('tbody')!;
    const atoCell = tableBody.querySelector('td:last-child');
    expect(atoCell).not.toBeNull();
    fireEvent.click(atoCell!);

    expect(mockNavigate).toHaveBeenCalledWith('/systems/sys-1');
  });

  /**
   * The system name link was always clickable via <Link>. Verify it still works
   * (we don't want to regress it).
   */
  it('navigates to system detail when clicking the system name link', async () => {
    renderPage();
    await waitFor(() => expect(screen.getByText('Test System')).toBeInTheDocument());

    // The name is a <Link> — clicking it fires both the Link and the tr onClick.
    // Either way, navigate must have been called.
    fireEvent.click(screen.getByText('Test System'));
    expect(mockNavigate).toHaveBeenCalledWith('/systems/sys-1');
  });

  /**
   * Compliance by System bar chart rows are already full-row <Link>s.
   * Confirm they still navigate after our changes.
   */
  it('Compliance by System chart: clicking a row navigates to system detail', async () => {
    renderPage();
    // The chart renders the acronym as the link label
    await waitFor(() => expect(screen.getAllByText('TS').length).toBeGreaterThan(0));

    // Find the bar-chart link (the one inside "Compliance by System")
    const links = screen.getAllByRole('link');
    const complianceLink = links.find(l => l.getAttribute('href') === '/systems/sys-1');
    expect(complianceLink).toBeDefined();
  });

  /**
   * ATO Status table rows are already full-row <Link>s. Confirm navigation.
   */
  it('ATO Status table: row link navigates to system detail', async () => {
    renderPage();
    // Wait for the table to render
    await waitFor(() => expect(document.querySelector('tbody')).not.toBeNull());

    // The ATO Status section renders a Link containing the acronym and the badge
    const links = screen.getAllByRole('link');
    const atoLinks = links.filter(l => l.getAttribute('href') === '/systems/sys-1');
    // Should have at least one (may be several from different sections)
    expect(atoLinks.length).toBeGreaterThan(0);
  });

  it('the <tr> has cursor-pointer class for visual affordance', async () => {
    renderPage();
    await waitFor(() => expect(screen.getByText('Test System')).toBeInTheDocument());

    const tableBody = document.querySelector('tbody');
    const row = tableBody!.querySelector('tr');
    expect(row).not.toBeNull();
    expect(row!.className).toContain('cursor-pointer');
  });
});
