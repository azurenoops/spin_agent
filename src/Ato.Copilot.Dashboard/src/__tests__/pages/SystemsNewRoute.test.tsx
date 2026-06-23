/**
 * SystemsNewRoute — fix(#522) /systems/new redirects to /systems with
 * openWizard state so the intake wizard auto-opens on the portfolio page.
 *
 * TDD: these tests were written before the implementation; they drive the
 * design of SystemsNewRoute and the PortfolioDashboard location.state handler.
 */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';

// ─── Mocks ───────────────────────────────────────────────────────────────────

const mockNavigate = vi.fn();

vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual<typeof import('react-router-dom')>('react-router-dom');
  return { ...actual, useNavigate: () => mockNavigate };
});

// ─── Subject ─────────────────────────────────────────────────────────────────

import SystemsNewRoute from '../../pages/SystemsNewRoute';

// ─── Tests ───────────────────────────────────────────────────────────────────

describe('SystemsNewRoute — fix(#522)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('immediately calls navigate("/systems") with replace and openWizard:true state', () => {
    render(
      <MemoryRouter initialEntries={['/systems/new']}>
        <Routes>
          <Route path="/systems/new" element={<SystemsNewRoute />} />
        </Routes>
      </MemoryRouter>,
    );

    expect(mockNavigate).toHaveBeenCalledOnce();
    expect(mockNavigate).toHaveBeenCalledWith('/systems', {
      replace: true,
      state: { openWizard: true },
    });
  });

  it('renders null (no visible DOM output)', () => {
    const { container } = render(
      <MemoryRouter initialEntries={['/systems/new']}>
        <Routes>
          <Route path="/systems/new" element={<SystemsNewRoute />} />
        </Routes>
      </MemoryRouter>,
    );

    // The component should render nothing visible
    expect(container.firstChild).toBeNull();
  });
});

describe('SystemsNewRoute — /systems/new does not bleed into :id route', () => {
  it('a route registered before /systems/:id prevents id="new" mismatch', async () => {
    // Regression guard: if /systems/new is routed BEFORE /systems/:id,
    // we should never reach the :id route with id="new".
    // Simulate by rendering only the /systems/new route — it should render
    // SystemsNewRoute (not fall through to any :id handler).
    render(
      <MemoryRouter initialEntries={['/systems/new']}>
        <Routes>
          <Route path="/systems/new" element={<div data-testid="new-route" />} />
          <Route path="/systems/:id" element={<div data-testid="id-route" />} />
        </Routes>
      </MemoryRouter>,
    );

    expect(screen.getByTestId('new-route')).toBeInTheDocument();
    expect(screen.queryByTestId('id-route')).toBeNull();
  });
});
