/**
 * PortfolioDashboard — auto-open wizard via location.state.openWizard
 * fix(#522): navigating to /systems/new → /systems with state { openWizard: true }
 * should cause PortfolioDashboard to open the IntakeWizard immediately.
 *
 * TDD: these tests are written first; they will be RED until the useEffect
 * that reads location.state.openWizard is added to PortfolioDashboard.
 */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';

// ─── Mocks ───────────────────────────────────────────────────────────────────

const mockNavigate = vi.fn();
const mockWizardOpen = vi.fn();

vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual<typeof import('react-router-dom')>('react-router-dom');
  return { ...actual, useNavigate: () => mockNavigate };
});

vi.mock('../../hooks/useIntakeWizard', () => ({
  useIntakeWizard: () => ({
    state: { isOpen: false, currentStep: 1, systemId: null, stepData: {}, validationErrors: {}, completedSteps: [] },
    open: mockWizardOpen,
    cancel: vi.fn(),
    cancelWithCleanup: vi.fn(),
    reset: vi.fn(),
    nextStep: vi.fn(),
    prevStep: vi.fn(),
    skipStep: vi.fn(),
    goToStep: vi.fn(),
    setSystemId: vi.fn(),
    setValidationErrors: vi.fn(),
    clearValidationErrors: vi.fn(),
    finish: vi.fn(),
  }),
}));

vi.mock('../../components/layout/PageLayout', () => ({
  default: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
}));

vi.mock('../../components/layout/PageHero', () => ({
  default: () => <div data-testid="page-hero" />,
}));

vi.mock('../../hooks/usePolling', () => ({
  usePolling: (fn: () => void) => { fn(); },
}));

vi.mock('../../api/portfolio', () => ({
  getPortfolio: vi.fn().mockResolvedValue({ items: [], totalCount: 0, cursor: null }),
  getPortfolioLegacy: vi.fn().mockResolvedValue({ items: [], totalCount: 0 }),
  updateSystem: vi.fn(),
  generateSystemDescription: vi.fn(),
}));

vi.mock('../../components/portfolio/AoPendingDecisionsWidget', () => ({
  default: () => null,
}));

// ─── Subject ─────────────────────────────────────────────────────────────────

import PortfolioDashboard from '../../pages/PortfolioDashboard';

// ─── Tests ───────────────────────────────────────────────────────────────────

describe('PortfolioDashboard — auto-open wizard via location.state.openWizard (fix #522)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('does NOT call wizard.open() when location.state has no openWizard flag', async () => {
    render(
      <MemoryRouter initialEntries={[{ pathname: '/systems', state: {} }]}>
        <Routes>
          <Route path="/systems" element={<PortfolioDashboard />} />
        </Routes>
      </MemoryRouter>,
    );

    // Give the useEffect time to run
    await waitFor(() => {
      expect(mockWizardOpen).not.toHaveBeenCalled();
    });
  });

  it('calls wizard.open() when location.state.openWizard is true', async () => {
    render(
      <MemoryRouter initialEntries={[{ pathname: '/systems', state: { openWizard: true } }]}>
        <Routes>
          <Route path="/systems" element={<PortfolioDashboard />} />
        </Routes>
      </MemoryRouter>,
    );

    await waitFor(() => {
      expect(mockWizardOpen).toHaveBeenCalledOnce();
    });
  });

  it('clears location state after opening wizard to prevent re-open on refresh', async () => {
    render(
      <MemoryRouter initialEntries={[{ pathname: '/systems', state: { openWizard: true } }]}>
        <Routes>
          <Route path="/systems" element={<PortfolioDashboard />} />
        </Routes>
      </MemoryRouter>,
    );

    await waitFor(() => {
      expect(mockNavigate).toHaveBeenCalledWith('/systems', { replace: true, state: {} });
    });
  });
});
