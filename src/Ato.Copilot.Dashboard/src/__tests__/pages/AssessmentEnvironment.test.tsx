import { act, fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import AssessmentEnvironment from '../../pages/AssessmentEnvironment';
import { getAssessmentReadiness, type AssessmentReadiness } from '../../api/assessments';
const state = vi.hoisted(() => ({ systemId: 'system-a', canRun: true }));
vi.mock('../../components/layout/SystemLayout', () => ({
  useSystemContext: () => ({ detail: { systemId: state.systemId, name: 'Mission system' } }),
}));
vi.mock('../../components/permissions/useSystemMutationPermission', () => ({ useSystemMutationPermission: () => state.canRun }));
vi.mock('../../api/assessments', () => ({ getAssessmentReadiness: vi.fn() }));
const result = (id = 'system-a'): AssessmentReadiness => ({ systemId: id, isReady: false, errorCode: 'ASSESSMENT_SCOPE_UNSUPPORTED',
  message: 'Scoped collection is not supported by this collector.', suggestion: 'Review the canonical environment.',
  configurationUrl: `/systems/${id}/profile/EnvironmentAndDeployment`, deploymentCloud: 'Government',
  cloudEnvironment: 'Government', subscriptions: [], checkedAt: '2026-09-29' });
beforeEach(() => { vi.clearAllMocks(); state.systemId = 'system-a'; state.canRun = true; vi.mocked(getAssessmentReadiness).mockResolvedValue(result()); });
describe('Assessments reuse the shared environment', () => {
  it('has no independent subscription selector or attachment mutation', async () => {
    // Arrange / Act
    render(<MemoryRouter><AssessmentEnvironment /></MemoryRouter>);
    await screen.findByText(/Scoped collection is not supported/);
    // Assert
    expect(screen.getByRole('link', { name: 'Manage system subscriptions' })).toHaveAttribute('href', '/systems/system-a/profile/EnvironmentAndDeployment');
    expect(screen.queryByRole('checkbox')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Save attachment|Detach/ })).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Back to Assessments' })).toHaveAttribute('href', '/systems/system-a/assessments');
  });
  it('preserves execution permission independently of shared environment management', () => {
    // Arrange
    state.canRun = false;
    // Act
    render(<MemoryRouter><AssessmentEnvironment /></MemoryRouter>);
    // Assert
    expect(screen.getByRole('button', { name: 'Check assessment prerequisites' })).toBeDisabled();
    expect(getAssessmentReadiness).not.toHaveBeenCalled();
    expect(screen.getByRole('link', { name: 'Manage system subscriptions' })).toBeVisible();
  });
  it('reports prerequisite failure without implying an empty successful assessment', async () => {
    // Arrange
    vi.mocked(getAssessmentReadiness).mockRejectedValue(new Error('Readiness unavailable'));
    // Act
    render(<MemoryRouter><AssessmentEnvironment /></MemoryRouter>);
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Readiness unavailable');
    expect(screen.queryByText('Ready')).not.toBeInTheDocument();
  });
  it('allows a fresh explicit check after prerequisites change', async () => {
    // Arrange
    render(<MemoryRouter><AssessmentEnvironment /></MemoryRouter>);
    await screen.findByText(/Scoped collection is not supported/);
    // Act
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Check assessment prerequisites' })); });
    // Assert
    expect(getAssessmentReadiness).toHaveBeenCalledTimes(2);
  });
  it('ignores stale results from another selected system', async () => {
    // Arrange
    let finish!: (value: ReturnType<typeof result>) => void;
    vi.mocked(getAssessmentReadiness).mockImplementationOnce(() => new Promise(resolve => { finish = resolve; }))
      .mockResolvedValueOnce({ ...result('system-b'), message: 'System B checks required.' });
    const page = render(<MemoryRouter><AssessmentEnvironment /></MemoryRouter>);
    // Act
    state.systemId = 'system-b';
    page.rerender(<MemoryRouter><AssessmentEnvironment /></MemoryRouter>);
    await screen.findByText(/System B checks required/);
    await act(async () => finish(result()));
    // Assert
    expect(screen.queryByText(/Scoped collection is not supported/)).not.toBeInTheDocument();
  });
});
