import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import apiClient from '../../api/client';
import Assessments from '../../pages/Assessments';
import {
  configurationUrl, deferred, historicalAssessment, otherSystemId, readiness,
  readinessPath, systemDetail, systemId,
} from '../fixtures/assessmentEnvironment';

const context = vi.hoisted(() => ({ systemId: 'assessment-system-a' }));
vi.mock('../../components/layout/SystemLayout', () => ({
  useSystemContext: () => ({ detail: { ...systemDetail, systemId: context.systemId }, refetch: vi.fn() }),
}));
vi.mock('../../api/client', () => ({ default: { get: vi.fn(), post: vi.fn() } }));
vi.mock('../../api/sap', () => ({
  getLatestSap: vi.fn().mockResolvedValue(null), generateSap: vi.fn(), finalizeSap: vi.fn(),
}));
vi.mock('../../api/sar', () => ({
  getLatestSar: vi.fn().mockResolvedValue(null), createSar: vi.fn(),
}));
vi.mock('../../components/remediation/CreateRemediationTaskModal', () => ({ default: () => null }));
vi.mock('../../components/AddDeviationDialog', () => ({ default: () => null }));

const responses = new Map<string, () => unknown>();
const runButton = () => screen.getByRole('button', { name: 'Run Assessment' });
const renderPage = () => render(<MemoryRouter><Assessments /></MemoryRouter>);

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(apiClient.post).mockReset();
  context.systemId = systemId;
  responses.clear();
  responses.set('/assessments', () => [historicalAssessment]);
  responses.set(readinessPath(), () => readiness());
  vi.mocked(apiClient.get).mockImplementation(async (url) => {
    const respond = responses.get(url);
    if (!respond) throw new Error(`Unmocked GET ${url}`);
    return { data: await respond() };
  });
  vi.mocked(apiClient.post).mockResolvedValue({ data: { assessmentId: 'azure-run', status: 'Completed', systemId } });
});

afterEach(cleanup);

describe('Assessments Azure admission (#981)', () => {
  it('keeps Run visible and disabled while readiness is loading', async () => {
    // Arrange
    const pending = deferred<ReturnType<typeof readiness>>();
    responses.set(readinessPath(), () => pending.promise);
    renderPage();

    // Act
    await act(async () => { await Promise.resolve(); });

    // Assert
    expect(runButton()).toBeVisible();
    expect(runButton()).toBeDisabled();
    expect(screen.getByText(/checking.*(Azure|environment|readiness)/i)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /configure environment/i })).toHaveAttribute('href', configurationUrl());
    expect(apiClient.post).not.toHaveBeenCalled();
  });

  it('explains missing configuration and cannot open or submit a run', async () => {
    // Arrange
    renderPage();

    // Act
    await screen.findByText(readiness().message);
    fireEvent.click(runButton());

    // Assert
    expect(runButton()).toBeDisabled();
    expect(screen.getByText(readiness().suggestion!)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /configure environment/i })).toHaveAttribute('href', configurationUrl());
    expect(screen.queryByRole('button', { name: 'Cancel' })).not.toBeInTheDocument();
    expect(apiClient.post).not.toHaveBeenCalled();
  });

  it.each([
    ['CLOUD_MISMATCH', 'The environment does not match the deployment cloud.'],
    ['CLOUD_UNSUPPORTED', 'Air-gapped assessments are not supported.'],
    ['SUBSCRIPTION_UNAVAILABLE', 'The attached subscription is unavailable.'],
  ])('blocks a normal not-ready response: %s', async (errorCode, message) => {
    // Arrange
    responses.set(readinessPath(), () => ({ ...readiness(), errorCode, message }));
    renderPage();

    // Act
    await screen.findByText(message);

    // Assert
    expect(runButton()).toBeDisabled();
    expect(screen.getByRole('link', { name: /configure environment/i })).toHaveAttribute('href', configurationUrl());
  });

  it('renders normalized provider errors and retries without allowing an assessment', async () => {
    // Arrange
    const error = { error: 'Azure access could not be verified.', errorCode: 'AZURE_ACCESS_DENIED', suggestion: 'Ask an administrator to grant the assessment identity read access.' };
    responses.set(readinessPath(), () => { throw error; });
    renderPage();

    // Act
    await screen.findByText(error.error);

    // Assert
    expect(runButton()).toBeDisabled();
    expect(screen.getByText(error.suggestion)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /configure environment/i })).toHaveAttribute('href', configurationUrl());

    // Act
    responses.set(readinessPath(), () => readiness(true));
    fireEvent.click(screen.getByRole('button', { name: /retry/i }));

    // Assert
    await waitFor(() => expect(runButton()).toBeEnabled());
    expect(apiClient.post).not.toHaveBeenCalled();
  });

  it('requires explicit CSP organization selection and never silently executes under All organizations', async () => {
    // Arrange
    const blocked = {
      ...readiness(),
      errorCode: 'ASSESSMENT_AZURE_ORGANIZATION_REQUIRED',
      message: 'Select the system organization before assessing its Azure environment.',
      suggestion: 'Use the organization selector to choose the system organization, then retry.',
    };
    responses.set(readinessPath(), () => blocked);
    renderPage();

    // Act
    await screen.findByText(blocked.message);
    fireEvent.click(runButton());

    // Assert
    expect(runButton()).toBeDisabled();
    expect(screen.getByText(blocked.suggestion)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /configure environment/i })).toHaveAttribute('href', configurationUrl());
    expect(screen.queryByRole('button', { name: 'Cancel' })).not.toBeInTheDocument();
    expect(apiClient.post).not.toHaveBeenCalled();
  });

  it('treats a normalized readiness writer-forbidden rejection as blocked rather than ready', async () => {
    // Arrange
    const error = {
      error: 'You do not have permission to run assessments.',
      errorCode: 'FORBIDDEN',
      suggestion: 'Ask a ComplianceWriter to configure and run this assessment.',
    };
    responses.set(readinessPath(), () => { throw error; });
    renderPage();

    // Act
    await screen.findByText(error.error);
    fireEvent.click(runButton());

    // Assert
    expect(runButton()).toBeDisabled();
    expect(screen.getByText(error.suggestion)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /configure environment/i })).toHaveAttribute('href', configurationUrl());
    expect(apiClient.post).not.toHaveBeenCalled();
  });

  it('allows a ready system to run and disables duplicate submissions while posting', async () => {
    // Arrange
    responses.set(readinessPath(), () => readiness(true));
    const pending = deferred<{ data: { assessmentId: string; status: string; systemId: string } }>();
    vi.mocked(apiClient.post).mockReturnValue(pending.promise);
    renderPage();
    await waitFor(() => expect(apiClient.get).toHaveBeenCalledWith(readinessPath()));
    await waitFor(() => expect(runButton()).toBeEnabled());

    // Act
    fireEvent.click(runButton());
    fireEvent.click(screen.getAllByRole('button', { name: 'Run Assessment' }).at(-1)!);

    // Assert
    expect(screen.getByRole('button', { name: /running/i })).toBeDisabled();
    expect(apiClient.post).toHaveBeenCalledTimes(1);
    expect(apiClient.post).toHaveBeenCalledWith(`/systems/${systemId}/run-assessment`);

    // Act
    await act(async () => pending.resolve({ data: { assessmentId: 'azure-run', status: 'Completed', systemId } }));

    // Assert
    expect(screen.queryByRole('button', { name: 'Cancel' })).not.toBeInTheDocument();
  });

  it('keeps a normalized POST prerequisite rejection actionable after previously being ready', async () => {
    // Arrange
    responses.set(readinessPath(), () => readiness(true));
    const error = { error: 'The Azure environment was detached.', errorCode: 'ASSESSMENT_ENVIRONMENT_NOT_CONFIGURED', suggestion: 'Attach an Azure subscription before trying again.' };
    vi.mocked(apiClient.post).mockRejectedValue(error);
    renderPage();
    await act(async () => { await Promise.resolve(); });

    // Act
    fireEvent.click(runButton());
    fireEvent.click(screen.getAllByRole('button', { name: 'Run Assessment' }).at(-1)!);

    // Assert
    expect(await screen.findByText(error.error)).toBeInTheDocument();
    expect(screen.getByText(error.suggestion)).toBeInTheDocument();
    expect(screen.getAllByRole('link', { name: /configure environment/i })[0]).toHaveAttribute('href', configurationUrl());
    expect(screen.queryByText('Assessment failed')).not.toBeInTheDocument();
  });

  it('immediately discards ready state and the open run dialog when the system changes', async () => {
    // Arrange
    responses.set(readinessPath(), () => readiness(true));
    responses.set(readinessPath(otherSystemId), () => new Promise<never>(() => {}));
    const page = renderPage();
    await act(async () => { await Promise.resolve(); });
    fireEvent.click(runButton());

    // Act
    context.systemId = otherSystemId;
    page.rerender(<MemoryRouter><Assessments /></MemoryRouter>);

    // Assert
    expect(screen.queryByRole('button', { name: 'Cancel' })).not.toBeInTheDocument();
    expect(runButton()).toBeDisabled();
    expect(screen.getByRole('link', { name: /configure environment/i })).toHaveAttribute('href', configurationUrl(otherSystemId));
  });

  it('ignores a late ready response from the previous system', async () => {
    // Arrange
    const previous = deferred<ReturnType<typeof readiness>>();
    responses.set(readinessPath(), () => previous.promise);
    responses.set(readinessPath(otherSystemId), () => readiness(false, otherSystemId));
    const page = renderPage();

    // Act
    context.systemId = otherSystemId;
    page.rerender(<MemoryRouter><Assessments /></MemoryRouter>);
    await screen.findByText(readiness().message);
    await act(async () => previous.resolve(readiness(true)));

    // Assert
    expect(runButton()).toBeDisabled();
    expect(screen.getByRole('link', { name: /configure environment/i })).toHaveAttribute('href', configurationUrl(otherSystemId));
    expect(apiClient.post).not.toHaveBeenCalled();
  });

  it('preserves historical manual assessment provenance and SAP/SAR controls while blocked', async () => {
    // Arrange
    responses.set('/assessments/historical-manual-assessment', () => ({
      ...historicalAssessment, findings: [], familyResults: [], notAssessedControls: 0,
      completedAt: historicalAssessment.assessedAt, executiveSummary: null,
      criticalCount: 0, highCount: 0, mediumCount: 1, lowCount: 0,
    }));
    responses.set(`/systems/${systemId}/assessments/historical-manual-assessment/component-risks`, () => null);
    renderPage();

    // Act
    const table = await screen.findByRole('table');
    await waitFor(() => expect(within(table).getByText('75%')).toBeInTheDocument());

    // Assert
    expect(within(table).getByText('Completed')).toBeInTheDocument();
    expect(within(table).queryByText(/Azure/i)).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: /generate SAP/i })).toBeEnabled();
    expect(screen.getByRole('button', { name: /generate SAR/i })).toBeEnabled();

    // Act
    fireEvent.click(within(table).getByRole('button', { name: /view/i }));

    // Assert
    expect(await screen.findByText('Manual')).toBeInTheDocument();
  });
});
