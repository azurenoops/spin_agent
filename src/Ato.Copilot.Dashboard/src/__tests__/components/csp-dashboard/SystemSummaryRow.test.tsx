/**
 * Tests for PR #603 test plan item 3:
 *
 * - Item 3: Click system row in /systems per-tenant view → navigates to /systems/{id}
 *
 * SystemSummaryRow is used in PortfolioDashboard for the per-tenant systems list.
 * It uses a DIRECT navigate() call (no impersonation), so this test verifies the
 * unchanged baseline: clicking any row navigates to /systems/{systemId}.
 *
 * This component was NOT changed in PR #603 — this test confirms no regression.
 */

import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import type { PortfolioSystemSummary } from '../../../types/dashboard';

// ─── Hoisted mocks ──────────────────────────────────────────────────────

const { navigate } = vi.hoisted(() => ({ navigate: vi.fn() }));
vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual<typeof import('react-router-dom')>('react-router-dom');
  return { ...actual, useNavigate: () => navigate };
});

import SystemSummaryRow from '../../../components/cards/SystemSummaryRow';

// ─── Helpers ─────────────────────────────────────────────────────────────

const SYSTEM_ID = 'sys-bbbbbbbb-0000-0000-0000-000000000002';

function makeSystem(overrides: Partial<PortfolioSystemSummary> = {}): PortfolioSystemSummary {
  return {
    systemId: SYSTEM_ID,
    name: 'Beta System',
    acronym: 'BS',
    impactLevel: 'High',
    currentRmfPhase: 'Monitor',
    isSetupComplete: true,
    complianceScore: 92.1,
    complianceScoreDelta: 1.5,
    atoExpirationDate: null,
    atoStatus: 'Active',
    atoDaysRemaining: 180,
    openPoamCount: 0,
    overduePoamCount: 0,
    ...overrides,
  };
}

function renderRow(system: PortfolioSystemSummary = makeSystem()) {
  return render(
    <MemoryRouter>
      <table>
        <tbody>
          <SystemSummaryRow system={system} />
        </tbody>
      </table>
    </MemoryRouter>,
  );
}

beforeEach(() => {
  navigate.mockReset();
});

// ─── Test Plan Item 3 ─────────────────────────────────────────────────────
// Click system row in /systems per-tenant view → navigates to /systems/{id}

describe('PR #603 — Test Plan Item 3: per-tenant SystemSummaryRow navigation (unchanged)', () => {
  it('navigates to /systems/{systemId} on row click (direct navigate, no impersonation)', async () => {
    renderRow();

    // SystemSummaryRow renders a <tr> — click the row itself
    const row = screen.getByText('Beta System').closest('tr');
    expect(row).not.toBeNull();
    fireEvent.click(row!);

    await waitFor(() => {
      expect(navigate).toHaveBeenCalledWith(`/systems/${SYSTEM_ID}`);
    });
  });

  it('navigates correctly for an In Setup system (Prepare phase)', async () => {
    renderRow(makeSystem({ currentRmfPhase: 'Prepare', isSetupComplete: false }));

    const row = screen.getByText('Beta System').closest('tr');
    fireEvent.click(row!);

    await waitFor(() => {
      expect(navigate).toHaveBeenCalledWith(`/systems/${SYSTEM_ID}`);
    });
  });
});
