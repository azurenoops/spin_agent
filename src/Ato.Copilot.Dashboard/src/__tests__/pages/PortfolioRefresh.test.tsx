import '../helpers/recharts';
import { useEffect } from 'react';
import { render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import PortfolioRiskProfile from '../../pages/PortfolioRiskProfile';
import CspDashboardPage from '../../features/csp-dashboard/CspDashboardPage';
import { getPortfolio } from '../../api/portfolio';
import { getCoverage } from '../../api/capabilities';
import { getCspDashboardSummary } from '../../features/csp-dashboard/api';
vi.mock('../../components/layout/PageLayout', () => ({ default: ({ children }: { children: React.ReactNode }) => <main>{children}</main> }));
vi.mock('../../components/layout/PageHero', () => ({ default: ({ title, actions }: { title: string; actions: React.ReactNode }) => <header><h1>{title}</h1>{actions}</header> }));
vi.mock('../../components/layout/useCspBranding', () => ({ useCspBranding: () => ({ displayName: 'Test provider' }) }));
vi.mock('../../hooks/usePolling', () => ({ usePolling: (fn: () => void) => { useEffect(() => { fn(); }, [fn]); } }));
vi.mock('../../api/portfolio', () => ({ getPortfolio: vi.fn() }));
vi.mock('../../api/capabilities', () => ({ getCoverage: vi.fn() }));
vi.mock('../../features/csp-dashboard/api', () => ({ getCspDashboardSummary: vi.fn(), isUnavailable: (v: object) => 'reason' in v }));
vi.mock('../../features/csp-dashboard/OrgsTable', () => ({ default: () => <div>Legacy organization management</div> }));
vi.mock('../../features/csp-dashboard/widgets/AtoStatusChart', () => ({ default: () => <div>Authorization chart</div> }));
vi.mock('../../features/csp-dashboard/widgets/FindingsBySeverityChart', () => ({ default: () => <div>Findings chart</div> }));
const system = { systemId: 'one', name: 'Test system', acronym: 'TS', complianceScore: 0, openPoamCount: 0, overduePoamCount: 0, catICounts: 0, catIICounts: 0, catIIICounts: 0, atoSeverity: 'none', atoDaysRemaining: null, currentRmfPhase: 'Prepare', isSetupComplete: false };
const page = (items: unknown[], nextCursor: string | null = null) => ({ items, totalCount: 2, nextCursor });
beforeEach(() => { vi.resetAllMocks(); vi.mocked(getCoverage).mockResolvedValue({ orgWide: { coveragePercent: 0 } } as never); });
describe('Portfolio landing-page refresh', () => {
  it('shows a failed portfolio as an error rather than an empty organization', async () => {
    // Arrange / Act
    vi.mocked(getPortfolio).mockRejectedValue(new Error('Service unavailable'));
    render(<MemoryRouter><PortfolioRiskProfile /></MemoryRouter>);
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Service unavailable');
    expect(screen.queryByText('No systems registered yet.')).not.toBeInTheDocument();
  });
  it('loads every cursor page and distinguishes unavailable coverage from zero', async () => {
    // Arrange
    vi.mocked(getPortfolio).mockResolvedValueOnce(page([system], 'next') as never).mockResolvedValue(page([{ ...system, systemId: 'two', name: 'Second system' }]) as never);
    vi.mocked(getCoverage).mockRejectedValue(new Error('Coverage unavailable'));
    // Act
    render(<MemoryRouter><PortfolioRiskProfile /></MemoryRouter>);
    // Assert
    expect(within(await screen.findByRole('region', { name: 'ATO status across systems' })).getByRole('img')).toHaveAccessibleName(/Not recorded: 2/);
    expect(await screen.findByText('Unavailable')).toBeInTheDocument();
    expect(getPortfolio).toHaveBeenCalledWith(expect.objectContaining({ cursor: 'next' }));
    expect(screen.queryByTitle('No compliance assessments have been run yet')).not.toBeInTheDocument();
  });
  it('links the CSP overview to dedicated screens without embedding organization management', async () => {
    // Arrange
    vi.mocked(getCspDashboardSummary).mockResolvedValue({ tenantCounts: { total: 0, active: 0, suspended: 0, disabled: 0 }, systemCount: 0, atoStatusCounts: { authorized: 0, inProcess: 0, denied: 0 }, openFindingsBySeverity: { critical: 0, high: 0, moderate: 0, low: 0 }, openPoamCount: 0, openDeviationCount: 0, generatedAt: '2026-09-23T12:00:00Z' } as never);
    // Act
    render(<MemoryRouter><CspDashboardPage /></MemoryRouter>);
    // Assert
    await waitFor(() => expect(screen.getByRole('link', { name: /Organizations/ })).toHaveAttribute('href', '/organizations'));
    expect(screen.getByRole('link', { name: /Security capabilities/ })).toHaveAttribute('href', '/security-capabilities');
    expect(screen.queryByText('Legacy organization management')).not.toBeInTheDocument();
  });
});
