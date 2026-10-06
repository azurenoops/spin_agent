import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import SystemReadinessOverview from '../../features/systems/SystemReadinessOverview';
import { getCategoryRoute } from '../../features/systems/packageReadiness';
import { getSystemNextActions } from '../../api/systemNextActions';
import { getPackageReadinessWorkspace, validatePackageReadiness } from '../../api/packageReadiness';
import { getOverviewWork } from '../../api/systemOverview';
import { getMonitoringWorkspace } from '../../api/scopedMonitoring';
import { getConMonOverview } from '../../api/conmon';
import { overviewWorkspace, overviewWork } from '../fixtures/systemOverview';

vi.mock('../../api/systemNextActions', () => ({ getSystemNextActions: vi.fn() }));
vi.mock('../../api/packageReadiness', () => ({ getPackageReadinessWorkspace: vi.fn(), validatePackageReadiness: vi.fn() }));
vi.mock('../../api/systemOverview', async importOriginal => ({
  ...await importOriginal<typeof import('../../api/systemOverview')>(), getOverviewWork: vi.fn(),
}));
vi.mock('../../api/scopedMonitoring', () => ({ getMonitoringWorkspace: vi.fn() }));
vi.mock('../../api/conmon', () => ({ getConMonOverview: vi.fn() }));
describe('System overview contract compatibility', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(getPackageReadinessWorkspace).mockResolvedValue(overviewWorkspace());
    vi.mocked(getOverviewWork).mockResolvedValue(overviewWork());
    vi.mocked(getMonitoringWorkspace).mockRejectedValue(new Error('Scoped monitoring unavailable'));
    vi.mocked(getConMonOverview).mockRejectedValue(new Error('ConMon unavailable'));
  });
  it('reads the canonical saved run without automatically validating or creating role-based assignments', async () => {
    // Arrange / Act
    render(<MemoryRouter><SystemReadinessOverview systemId="a" systemName="Mission Alpha" /></MemoryRouter>);
    // Assert
    await screen.findByText('5 returned findings');
    expect(getSystemNextActions).not.toHaveBeenCalled();
    expect(validatePackageReadiness).not.toHaveBeenCalled();
    expect(getPackageReadinessWorkspace).toHaveBeenCalledWith('a', { purpose: 'InitialSubmission' }, expect.any(AbortSignal));
  });
  it('retains package preparation when browsing Monitor and loads monitoring only on section selection', async () => {
    // Arrange
    render(<MemoryRouter><SystemReadinessOverview systemId="a" systemName="Mission Alpha" currentPhase="Monitor" /></MemoryRouter>);
    await screen.findByText('5 returned findings');
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'View Monitor phase' }));
    // Assert
    expect(getMonitoringWorkspace).not.toHaveBeenCalled();
    expect(screen.getByText('Viewing phase: Monitor')).toBeVisible();
    // Act
    fireEvent.click(screen.getByRole('tab', { name: 'Monitoring & follow-up' }));
    // Assert
    await waitFor(() => expect(getMonitoringWorkspace).toHaveBeenCalledWith('a', expect.any(AbortSignal)));
    expect(screen.getByRole('tab', { name: 'Package preparation' })).toBeVisible();
    expect(validatePackageReadiness).not.toHaveBeenCalled();
  });
  it.each(['EnvironmentAndDeployment', 'UsersAndAccess', 'DataTypes', 'PortsProtocolsAndServices'])(
    'preserves existing package-wide %s source routing', section => {
      // Arrange / Act
      const route = getCategoryRoute('profile-approval', 'ssp', `Profile ${section}: approved snapshot unavailable.`);
      // Assert
      expect(route?.path).toBe(`profile/${section}`);
    });
});
