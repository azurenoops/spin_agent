import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, expect, it, vi } from 'vitest';
import AssessmentComponentRiskSummary from '../../features/assessment-workspace/AssessmentComponentRiskSummary';
import { getAssessmentComponentRisks } from '../../api/components';
vi.mock('../../api/components', () => ({ getAssessmentComponentRisks: vi.fn() }));
afterEach(() => { cleanup(); vi.clearAllMocks(); });
it('retains the existing scoped component-risk view through progressive disclosure', async () => {
  // Arrange
  vi.mocked(getAssessmentComponentRisks).mockResolvedValue({ componentRisks: [{
    componentId: 'component-a', componentName: 'System firewall', componentType: 'Thing',
    openFindingCount: 2, highestSeverity: 'High', overdueRemediationCount: 1,
  }], totalFindingCount: 3, unlinkedFindingCount: 1 });
  render(<AssessmentComponentRiskSummary systemId="system-a" assessmentId="assessment-a" />);
  // Act
  fireEvent.click(screen.getByText('Current component risk summary'));
  // Assert
  expect(await screen.findByText('System firewall')).toBeVisible();
  expect(getAssessmentComponentRisks).toHaveBeenCalledWith('system-a', 'assessment-a', expect.any(AbortSignal));
});
it('does not replace a failed component-risk read with zero risk', async () => {
  // Arrange
  vi.mocked(getAssessmentComponentRisks).mockRejectedValue(new Error('Risk summary unavailable.'));
  render(<AssessmentComponentRiskSummary systemId="system-a" assessmentId="assessment-a" />);
  // Act
  fireEvent.click(screen.getByText('Current component risk summary'));
  // Assert
  expect(await screen.findByRole('alert')).toHaveTextContent('Risk summary unavailable.');
  expect(screen.getByRole('button', { name: 'Retry component risks' })).toBeVisible();
});
