import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import SystemReadinessOverview from '../../features/systems/SystemReadinessOverview';
import { validatePackage } from '../../api/package';
import { getConMonOverview } from '../../api/conmon';
import { getSystemNextActions } from '../../api/systemNextActions';
import { getCategoryRoute } from '../../features/systems/packageReadiness';

vi.mock('../../api/package', () => ({ validatePackage: vi.fn() }));
vi.mock('../../api/conmon', () => ({ getConMonOverview: vi.fn() }));
vi.mock('../../api/systemNextActions', () => ({ getSystemNextActions: vi.fn() }));
const ownTasks: Awaited<ReturnType<typeof getSystemNextActions>> = {
  systemId: 'a', checkedAt: '2026-09-28T18:00:00Z', effectiveRoles: ['MissionOwner'],
  items: [{ id: 'data', title: 'Complete data profile', description: 'Record the mission information types.',
    path: 'profile/DataTypes', actionLabel: 'Open', responsibleRole: 'MissionOwner' }],
  waitingOnOtherRoles: [{ role: 'Issm', count: 1 }],
};
describe('Role-aware system readiness overview', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(getSystemNextActions).mockResolvedValue(ownTasks);
    vi.mocked(getConMonOverview).mockRejectedValue(new Error('Monitoring fixture unavailable'));
  });
  it('matches the unchecked mock hierarchy without sample results or a false package completion', async () => {
    // Arrange
    vi.mocked(getSystemNextActions).mockResolvedValue({ ...ownTasks, effectiveRoles: ['Issm'], items: [], waitingOnOtherRoles: [] });
    // Act
    render(<MemoryRouter><SystemReadinessOverview systemId="a" systemName="Mission Alpha" /></MemoryRouter>);
    // Assert
    expect(screen.getByRole('heading', { name: 'Find out what your package needs' })).toBeVisible();
    expect(screen.getByRole('heading', { name: 'Your work' })).toBeVisible();
    expect(await screen.findByRole('heading', { name: 'No actions assigned to you' })).toBeVisible();
    expect(screen.getByRole('heading', { name: 'Documentation at a glance' })).toBeVisible();
    expect(screen.getByText('Check readiness to see which documents need attention.')).toBeVisible();
    expect(screen.queryByRole('heading', { name: 'What the team needs to finish' })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Continue preparation' })).not.toBeInTheDocument();
    expect(validatePackage).not.toHaveBeenCalled();
  });
  it('shows returned team findings separately from personal work and uses real document gaps', async () => {
    // Arrange
    vi.mocked(getSystemNextActions).mockResolvedValue({ ...ownTasks, items: [] });
    vi.mocked(validatePackage).mockResolvedValue({ purpose: 'InitialSubmission', isValid: false,
      errorCount: 1, warningCount: 0, validatedAt: 'now', findings: [{ severity: 'error',
        category: 'boundary', artifactType: 'ssp', description: 'Review the recorded boundary.',
        remediation: 'Resolve the recorded component decisions.' }] });
    render(<MemoryRouter><SystemReadinessOverview systemId="a" systemName="Mission Alpha" /></MemoryRouter>);
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Check readiness' }));
    // Assert
    const gaps = await screen.findByRole('region', { name: 'Team readiness findings' });
    expect(within(gaps).getByRole('heading', { name: 'Review the recorded boundary.' })).toBeVisible();
    expect(within(gaps).getByText('Owner not provided by the readiness check')).toBeVisible();
    expect(screen.getByRole('heading', { name: 'What the team needs to finish' })).toBeVisible();
    expect(screen.getByRole('button', { name: 'Check again' })).toBeVisible();
    expect(screen.getByRole('region', { name: 'Documentation at a glance' })).toHaveTextContent('System Security Plan');
    expect(screen.getByRole('region', { name: 'Documentation at a glance' })).toHaveTextContent('Gaps');
  });
  it('loads the actor task queue automatically without conflating it with overall package validation', async () => {
    // Arrange / Act
    render(<MemoryRouter><SystemReadinessOverview systemId="a" systemName="Mission Alpha" /></MemoryRouter>);
    // Assert
    expect(await screen.findByRole('heading', { name: 'Complete data profile' })).toBeVisible();
    expect(validatePackage).not.toHaveBeenCalled();
    expect(getSystemNextActions).toHaveBeenCalledWith('a', expect.any(AbortSignal));
    expect(screen.getByText('Readiness not checked')).toBeVisible();
    expect(screen.getByRole('link', { name: 'Continue preparation' })).toHaveAttribute('href', '/systems/a/profile/DataTypes');
    expect(screen.queryByRole('heading', { name: 'Confirm system boundary' })).not.toBeInTheDocument();
  });
  it('does not turn an ISSM package blocker into a Mission Owner assignment', async () => {
    // Arrange
    vi.mocked(validatePackage).mockResolvedValue({ purpose: 'InitialSubmission', isValid: false, errorCount: 1, warningCount: 0, validatedAt: 'now',
      findings: [{ severity: 'error', category: 'profile-approval', artifactType: 'ssp',
        description: 'ISSM must approve the submitted profile.', remediation: 'Approve the profile.' }] });
    render(<MemoryRouter><SystemReadinessOverview systemId="a" systemName="Mission Alpha" /></MemoryRouter>);
    await screen.findByRole('heading', { name: 'Complete data profile' });
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Check readiness' }));
    // Assert
    expect(await screen.findByText('1 blocking requirement remains')).toBeVisible();
    expect(within(screen.getByRole('region', { name: 'Next actions for your system roles' })).queryByRole('heading', { name: 'ISSM must approve the submitted profile.' })).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Continue preparation' })).toHaveAttribute('href', '/systems/a/profile/DataTypes');
    expect(screen.getByText(/ISSM: 1/)).toBeVisible();
    // Act
    vi.mocked(getSystemNextActions).mockResolvedValue({ ...ownTasks, items: [] });
    fireEvent.click(screen.getByRole('button', { name: 'Refresh my tasks' }));
    // Assert
    expect(await screen.findByRole('heading', { name: 'No actions assigned to you' })).toBeVisible();
    expect(screen.getByText('1 blocking requirement remains')).toBeVisible();
    expect(screen.getByRole('link', { name: 'View package readiness' })).toHaveAttribute('href', '/systems/a/documents?purpose=InitialSubmission');
  });
  it('keeps task-service failure separate from a passed package check', async () => {
    // Arrange
    vi.mocked(getSystemNextActions).mockRejectedValue(new Error('Tasks unavailable'));
    vi.mocked(validatePackage).mockResolvedValue({ purpose: 'InitialSubmission', isValid: true, errorCount: 0, warningCount: 0, validatedAt: 'now', findings: [] });
    render(<MemoryRouter><SystemReadinessOverview systemId="a" systemName="Mission Alpha" /></MemoryRouter>);
    await screen.findByRole('alert');
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Check readiness' }));
    // Assert
    expect(await screen.findByText('No blocking requirements returned')).toBeVisible();
    expect(screen.getByRole('alert')).toHaveTextContent('Tasks unavailable');
    expect(screen.queryByText('No actions currently require your system roles.')).not.toBeInTheDocument();
  });
  it('reloads role tasks when returning from monitoring without automatically validating the package', async () => {
    // Arrange
    render(<MemoryRouter><SystemReadinessOverview systemId="a" systemName="Mission Alpha" /></MemoryRouter>);
    await screen.findByRole('heading', { name: 'Complete data profile' });
    // Act
    fireEvent.click(screen.getByRole('tab', { name: 'Monitoring & follow-up' }));
    // Assert
    expect(screen.getByRole('link', { name: 'Review monitoring' })).toHaveAttribute('href', '/systems/a/conmon');
    expect(screen.queryByRole('button', { name: 'Refresh my tasks' })).not.toBeInTheDocument();
    // Act
    fireEvent.click(screen.getByRole('tab', { name: 'Readiness' }));
    // Assert
    await waitFor(() => expect(getSystemNextActions).toHaveBeenCalledTimes(2));
    expect(validatePackage).not.toHaveBeenCalled();
  });
  it('uses the real monitoring service for an operating system without loading submission tasks', async () => {
    // Arrange
    vi.mocked(getConMonOverview).mockResolvedValue({
      systemId: 'a', systemName: 'Mission Alpha', currentPhase: 'Monitor', plan: null,
      status: { currentComplianceScore: 0, authorizedBaselineScore: null, scoreDelta: null, openFindings: 2,
        resolvedFindings: 0, openPoamItems: 1, overduePoamItems: 1, monitoringEnabled: false,
        driftAlertCount: 0, autoRemediationRuleCount: 0, lastMonitoringCheck: null },
      expiration: { hasActiveAuthorization: false, decisionType: null, decisionDate: null, expirationDate: null,
        daysUntilExpiration: null, alertLevel: 'None', alertMessage: 'No active decision recorded.', isExpired: false },
      reauthorization: { isTriggered: false, triggers: [], unreviewedChangeCount: 0 },
      agreementAlerts: [], significantChanges: [], reports: [],
    });
    // Act
    render(<MemoryRouter><SystemReadinessOverview systemId="a" systemName="Mission Alpha" currentPhase="Monitor" /></MemoryRouter>);
    // Assert
    expect(await screen.findByText('No active decision recorded.')).toBeVisible();
    expect(screen.getByText('Not enabled')).toBeVisible();
    expect(validatePackage).not.toHaveBeenCalled();
    expect(getSystemNextActions).not.toHaveBeenCalled();
  });
  it.each(['EnvironmentAndDeployment', 'UsersAndAccess', 'DataTypes', 'PortsProtocolsAndServices'])(
    'routes a package-wide profile finding to the actual %s section', section => {
      // Arrange / Act
      const route = getCategoryRoute('profile-approval', 'ssp', `Profile ${section}: approved snapshot unavailable.`);
      // Assert
      expect(route?.path).toBe(`profile/${section}`);
    });
  it('routes the verified design-approval finding to System design rather than narratives', () => {
    // Arrange / Act
    const route = getCategoryRoute('ssp', 'ssp', 'DESIGN_APPROVAL_REQUIRED: Review and approve the System design before final SSP generation.');
    // Assert
    expect(route).toEqual({ path: 'profile/SystemDesign', label: 'System design' });
  });
});
