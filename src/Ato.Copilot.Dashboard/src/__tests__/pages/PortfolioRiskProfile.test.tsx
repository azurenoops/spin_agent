import '../helpers/recharts';
import { useEffect } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import PortfolioRiskProfile from '../../pages/PortfolioRiskProfile';
import { getPortfolio } from '../../api/portfolio';
import type { PortfolioSystemSummary } from '../../types/dashboard';

vi.mock('../../components/layout/PageLayout', () => ({
  default: ({ children }: { children: React.ReactNode }) => <main>{children}</main>,
}));
vi.mock('../../components/layout/PageHero', () => ({ default: () => <header /> }));
vi.mock('../../hooks/usePolling', () => ({
  usePolling: (fn: () => void) => { useEffect(() => { fn(); }, [fn]); },
}));
vi.mock('../../api/portfolio', () => ({ getPortfolio: vi.fn() }));
vi.mock('../../api/capabilities', () => ({
  getCoverage: vi.fn().mockResolvedValue({ orgWide: { coveragePercent: 75 } }),
}));

const system: PortfolioSystemSummary = {
  systemId: 'sys-1', name: 'Test System', acronym: 'TS', systemType: 'MajorApplication',
  missionCriticality: 'High', hostingEnvironment: 'Cloud', description: null,
  impactLevel: 'Moderate', currentRmfPhase: 'Authorize', complianceScore: 85, complianceScoreDelta: 2,
  atoExpirationDate: null, atoStatus: 'Active', atoDaysRemaining: 120, atoSeverity: 'green',
  openPoamCount: 2, overduePoamCount: 0, catICounts: 1, catIICounts: 2, catIIICounts: 3,
  isSetupComplete: true, hasBoundary: true, hasRoles: true, hasCategorization: true,
};

function renderPage() {
  return render(<MemoryRouter><PortfolioRiskProfile /></MemoryRouter>);
}

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(getPortfolio).mockResolvedValue({ items: [system], totalCount: 1, nextCursor: null });
});

describe('Organization portfolio graphical summary', () => {
  it('replaces all legacy panels and the table with exactly two graph cards', async () => {
    // Arrange / Act
    renderPage();
    const ato = await screen.findByRole('region', { name: 'ATO status across systems' });
    const findings = screen.getByRole('region', { name: 'Open findings by severity' });
    // Assert
    expect(within(ato).getByRole('img')).toHaveAccessibleName(/Active: 1.*Expired: 0.*Not recorded: 0/);
    expect(within(findings).getByRole('img')).toHaveAccessibleName(/CAT I: 1.*CAT II: 2.*CAT III: 3/);
    expect(document.querySelectorAll('.recharts-wrapper > svg.recharts-surface')).toHaveLength(2);
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
    for (const name of ['Compliance by System', 'Findings by Severity', 'Open POA&Ms by System', 'ATO Status', 'System Risk Summary']) {
      expect(screen.queryByRole('heading', { name })).not.toBeInTheDocument();
    }
    expect(screen.getByRole('heading', { name: 'Needs attention' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Systems/ })).toHaveAttribute('href', '/systems');
    expect(screen.getByText('Avg Compliance')).toBeInTheDocument();
    expect(findings.compareDocumentPosition(screen.getByRole('region', { name: 'Needs attention' })) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it('aggregates all authorized pages without deriving ATO status from scores or risk colors', async () => {
    // Arrange
    vi.mocked(getPortfolio).mockResolvedValueOnce({
      items: [{ ...system, atoStatus: 'None', complianceScore: 100, atoSeverity: 'green' }],
      totalCount: 3, nextCursor: 'page-two',
    }).mockResolvedValueOnce({
      items: [{ ...system, systemId: 'two', atoStatus: 'Expired' }, { ...system, systemId: 'three', atoStatus: 'Active', atoSeverity: 'red' }],
      totalCount: 3, nextCursor: null,
    });
    // Act
    renderPage();
    // Assert
    const ato = await screen.findByRole('region', { name: 'ATO status across systems' });
    expect(within(ato).getByRole('img')).toHaveAccessibleName(/Active: 1.*Expired: 1.*Not recorded: 1/);
    expect(within(screen.getByRole('region', { name: 'Open findings by severity' })).getByRole('img'))
      .toHaveAccessibleName(/CAT I: 3.*CAT II: 6.*CAT III: 9/);
    expect(getPortfolio).toHaveBeenCalledWith(expect.objectContaining({ cursor: 'page-two' }));
  });

  it('keeps zero-finding chart axes and missing authorization explicit', async () => {
    // Arrange
    vi.mocked(getPortfolio).mockResolvedValue({
      items: [{ ...system, atoStatus: '', catICounts: 0, catIICounts: 0, catIIICounts: 0 }],
      totalCount: 1, nextCursor: null,
    });
    // Act
    renderPage();
    // Assert
    const findings = await screen.findByRole('region', { name: 'Open findings by severity' });
    expect(within(findings).getByText('No open findings')).toBeInTheDocument();
    expect(within(findings).getByRole('img')).toHaveAccessibleName(/CAT I: 0.*CAT II: 0.*CAT III: 0/);
    expect(within(screen.getByRole('region', { name: 'ATO status across systems' })).getByRole('img'))
      .toHaveAccessibleName(/Not recorded: 1/);
    expect(document.querySelectorAll('.recharts-wrapper > svg.recharts-surface')).toHaveLength(2);
  });

  it('preserves unexpected recorded authorization statuses rather than implying authorization', async () => {
    // Arrange
    vi.mocked(getPortfolio).mockResolvedValue({
      items: [{ ...system, atoStatus: 'Pending review' }], totalCount: 1, nextCursor: null,
    });
    // Act
    renderPage();
    // Assert
    const ato = await screen.findByRole('region', { name: 'ATO status across systems' });
    expect(within(ato).getByRole('img')).toHaveAccessibleName(/Active: 0.*Pending review: 1/);
  });

  it('retains the real empty organization state instead of inventing summary data', async () => {
    // Arrange
    vi.mocked(getPortfolio).mockResolvedValue({ items: [], totalCount: 0, nextCursor: null });
    // Act
    renderPage();
    // Assert
    expect(await screen.findByText('No systems available in this workspace.')).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'ATO status across systems' })).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Open Systems' })).toHaveAttribute('href', '/systems');
  });
});
