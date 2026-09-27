import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import SystemReadinessOverview from '../../features/systems/SystemReadinessOverview';
import { validatePackage } from '../../api/package';
import { getConMonOverview } from '../../api/conmon';

vi.mock('../../api/package', () => ({ validatePackage: vi.fn() }));
vi.mock('../../api/conmon', () => ({ getConMonOverview: vi.fn() }));
describe('System readiness overview', () => {
  beforeEach(() => vi.clearAllMocks());
  it('uses InitialSubmission server findings as the next actions, not an existing ATO prerequisite', async () => {
    // Arrange
    vi.mocked(validatePackage).mockResolvedValue({
      purpose: 'InitialSubmission', isValid: false, errorCount: 1, warningCount: 0, validatedAt: '2026-09-26T12:00:00Z',
      findings: [{ severity: 'error', category: 'boundary', artifactType: null, description: 'System boundary has not been confirmed.', remediation: 'Review included resources.' }],
    });
    // Act
    render(<MemoryRouter><SystemReadinessOverview systemId="a" systemName="Mission Alpha" /></MemoryRouter>);
    expect(validatePackage).not.toHaveBeenCalled();
    expect(screen.getByLabelText('Package purpose')).toHaveValue('InitialSubmission');
    fireEvent.click(screen.getByRole('button', { name: 'Validate current package' }));
    // Assert
    expect(await screen.findByRole('heading', { name: 'System boundary has not been confirmed.' })).toBeVisible();
    expect(screen.getByRole('link', { name: 'Review system boundary' })).toHaveAttribute('href', '/systems/a/boundaries');
    expect(validatePackage).toHaveBeenCalledWith('a', expect.any(AbortSignal), 'InitialSubmission');
    expect(screen.getByText(/does not require an already-issued ATO/)).toBeVisible();
  });
  it('does not treat a failed readiness read as an empty, ready checklist', async () => {
    // Arrange
    vi.mocked(validatePackage).mockRejectedValueOnce(new Error('Readiness service unavailable'));
    vi.mocked(validatePackage).mockResolvedValue({ purpose: 'InitialSubmission', isValid: true, errorCount: 0, warningCount: 0, validatedAt: 'now', findings: [] });
    render(<MemoryRouter><SystemReadinessOverview systemId="a" systemName="Mission Alpha" /></MemoryRouter>);
    // Act
    fireEvent.change(screen.getByLabelText('Package purpose'), { target: { value: 'InitialSubmission' } });
    fireEvent.click(screen.getByRole('button', { name: 'Validate current package' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Readiness service unavailable');
    fireEvent.click(screen.getByRole('button', { name: 'Validate current package' }));
    // Assert
    await waitFor(() => expect(screen.getByText('Current package validation passed')).toBeVisible());
    expect(screen.getByText(/not an authorization decision or eMASS acceptance/)).toBeVisible();
  });
  it('clears the previous result when package purpose changes through the shared widget', async () => {
    // Arrange
    vi.mocked(validatePackage).mockResolvedValue({ purpose: 'InitialSubmission', isValid: true, errorCount: 0, warningCount: 0, validatedAt: 'now', findings: [] });
    // Act
    render(<MemoryRouter><SystemReadinessOverview systemId="a" systemName="Mission Alpha" /></MemoryRouter>);
    fireEvent.click(screen.getByRole('button', { name: 'Validate current package' }));
    await screen.findByText('Current package validation passed');
    fireEvent.change(screen.getByLabelText('Package purpose'), { target: { value: 'Legacy' } });
    // Assert
    expect(screen.queryByText('Current package validation passed')).not.toBeInTheDocument();
  });
  it('resets preparation to initial submission when the selected system changes', () => {
    // Arrange
    const { rerender } = render(<MemoryRouter><SystemReadinessOverview systemId="a" systemName="Mission Alpha" /></MemoryRouter>);
    fireEvent.change(screen.getByLabelText('Package purpose'), { target: { value: 'Legacy' } });
    // Act
    rerender(<MemoryRouter><SystemReadinessOverview systemId="b" systemName="Mission Bravo" /></MemoryRouter>);
    // Assert
    expect(screen.getByLabelText('Package purpose')).toHaveValue('InitialSubmission');
    expect(validatePackage).not.toHaveBeenCalled();
  });
  it('uses the real monitoring service for an operating system rather than fabricating submission readiness', async () => {
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
    expect(getConMonOverview).toHaveBeenCalledWith('a');
  });
});
