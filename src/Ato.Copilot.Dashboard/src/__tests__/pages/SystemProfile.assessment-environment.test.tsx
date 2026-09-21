import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import apiClient from '../../api/client';
import SystemProfile from '../../pages/SystemProfile';
import {
  configurationUrl, deferred, environment, environmentPath, governmentSubscriptionId,
  legacySubscriptionId, otherSystemId, profileCompleteness, profileSection, readiness,
  readinessPath, subscriptionId, systemDetail, systemId, unavailableSubscriptionId,
} from '../fixtures/assessmentEnvironment';

const context = vi.hoisted(() => ({ systemId: 'assessment-system-a' }));
vi.mock('../../components/layout/SystemLayout', () => ({
  useSystemContext: () => ({ detail: { ...systemDetail, systemId: context.systemId }, refetch: vi.fn() }),
}));
vi.mock('../../hooks/useSettings', () => ({ useSettings: () => ({ settings: { role: 'ISSM' } }) }));
vi.mock('../../api/client', () => ({ default: { get: vi.fn(), put: vi.fn(), post: vi.fn(), delete: vi.fn() } }));
vi.mock('../../components/forms/ProfileSectionForm', () => ({
  default: ({ isReadOnly }: { isReadOnly: boolean }) => (
    <fieldset disabled={isReadOnly}><legend>Descriptive Mission Profile</legend><input aria-label="Profile description" /></fieldset>
  ),
}));

const responses = new Map<string, () => unknown>();
const panel = () => screen.findByRole('region', { name: /Azure assessment environment/i });
const pageElement = (section = 'EnvironmentAndDeployment') => (
  <MemoryRouter initialEntries={[`/systems/${context.systemId}/profile/${section}`]}>
    <Routes><Route path="/systems/:id/profile/:sectionType" element={<SystemProfile />} /></Routes>
  </MemoryRouter>
);

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(apiClient.put).mockReset();
  vi.mocked(apiClient.delete).mockReset();
  context.systemId = systemId;
  responses.clear();
  for (const id of [systemId, otherSystemId]) {
    responses.set(`/systems/${id}/profile/EnvironmentAndDeployment`, () => ({ ...profileSection, systemId: id }));
    responses.set(`/systems/${id}/profile/MissionAndPurpose`, () => ({ ...profileSection, systemId: id, sectionType: 'MissionAndPurpose' }));
    responses.set(`/systems/${id}/profile/completeness`, () => ({ ...profileCompleteness, systemId: id }));
    responses.set(environmentPath(id), () => environment(id));
    responses.set(readinessPath(id), () => readiness(false, id));
  }
  vi.mocked(apiClient.get).mockImplementation(async (url) => {
    const respond = responses.get(url);
    if (!respond) throw new Error(`Unmocked GET ${url}`);
    return { data: await respond() };
  });
  vi.mocked(apiClient.put).mockResolvedValue({
    data: { ...environment(), cloudEnvironment: 'Commercial', subscriptionIds: [subscriptionId] },
  });
  vi.mocked(apiClient.delete).mockResolvedValue({ status: 204 });
  vi.spyOn(window, 'confirm').mockReturnValue(true);
});

afterEach(() => { cleanup(); vi.restoreAllMocks(); });

describe('Environment page Azure attachment (#981)', () => {
  it('provides the Configure Environment anchor independently of descriptive governance', async () => {
    // Arrange
    render(pageElement());

    // Act
    const attachment = await panel();

    // Assert
    expect(attachment).toHaveAttribute('id', 'azure-assessment-environment');
    expect(configurationUrl()).toContain(`#${attachment.id}`);
    expect(screen.getByRole('textbox', { name: 'Profile description' })).toBeDisabled();
    expect(within(attachment).getByRole('checkbox', { name: /Synthetic Commercial Alpha/i })).toBeEnabled();
    expect(within(attachment).queryByLabelText(/password|client secret|access key|credential/i)).not.toBeInTheDocument();
  });

  it('does not mount attachment configuration on other profile sections', async () => {
    // Arrange
    render(pageElement('MissionAndPurpose'));

    // Act
    await screen.findByRole('textbox', { name: 'Profile description' });

    // Assert
    expect(screen.queryByRole('region', { name: /Azure assessment environment/i })).not.toBeInTheDocument();
    expect(apiClient.get).not.toHaveBeenCalledWith(environmentPath());
  });

  it('keeps attachment repair available when descriptive profile loading fails', async () => {
    // Arrange
    responses.set(`/systems/${systemId}/profile/EnvironmentAndDeployment`, () => { throw new Error('Profile unavailable'); });
    render(pageElement());

    // Act
    const attachment = await panel();

    // Assert
    expect(within(attachment).getByRole('checkbox', { name: /Synthetic Commercial Alpha/i })).toBeEnabled();
    expect(screen.getByText('Unable to load profile section.')).toBeInTheDocument();
  });

  it('does not auto-select registrations and disables unavailable or mismatched subscriptions', async () => {
    // Arrange
    render(pageElement());

    // Act
    const attachment = await panel();
    await within(attachment).findByRole('checkbox', { name: /Synthetic Commercial Alpha/i });

    // Assert
    for (const checkbox of within(attachment).getAllByRole('checkbox')) expect(checkbox).not.toBeChecked();
    expect(within(attachment).getByRole('checkbox', { name: /Synthetic Unavailable/i })).toBeDisabled();
    expect(within(attachment).getByRole('checkbox', { name: /Synthetic Government/i })).toBeDisabled();
    expect(within(attachment).getByRole('checkbox', { name: /Synthetic Unknown Cloud/i })).toBeDisabled();
    expect(within(attachment).getByRole('button', { name: /save environment/i })).toBeDisabled();
    expect(apiClient.put).not.toHaveBeenCalled();
  });

  it('saves only explicitly selected matching-cloud subscriptions then checks actual readiness', async () => {
    // Arrange
    render(pageElement());
    const attachment = await panel();
    await within(attachment).findByRole('checkbox', { name: /Synthetic Commercial Alpha/i });
    const getCount = vi.mocked(apiClient.get).mock.calls.filter(([url]) => url === readinessPath()).length;
    const blocked = { ...readiness(), errorCode: 'AZURE_ACCESS_DENIED', message: 'Azure connectivity has not been verified.', suggestion: 'Grant the assessment identity read access.' };
    responses.set(readinessPath(), () => blocked);

    // Act
    fireEvent.click(within(attachment).getByRole('checkbox', { name: /Synthetic Commercial Alpha/i }));
    fireEvent.click(within(attachment).getByRole('button', { name: /save environment/i }));

    // Assert
    await waitFor(() => expect(apiClient.put).toHaveBeenCalledWith(environmentPath(), {
      cloudEnvironment: 'Commercial', subscriptionIds: [subscriptionId],
    }));
    expect(apiClient.put).toHaveBeenCalledTimes(1);
    await waitFor(() => expect(vi.mocked(apiClient.get).mock.calls.filter(([url]) => url === readinessPath()).length).toBeGreaterThan(getCount));
    expect(await within(attachment).findByText(blocked.message)).toBeInTheDocument();
    expect(within(attachment).getByText(blocked.suggestion)).toBeInTheDocument();
    expect(within(attachment).queryByText(/connection verified|ready to run/i)).not.toBeInTheDocument();
  });

  it('uses the deployment Government cloud rather than offering per-system SDK routing', async () => {
    // Arrange
    responses.set(environmentPath(), () => ({ ...environment(), deploymentCloud: 'Government' }));
    render(pageElement());
    const attachment = await panel();
    const government = await within(attachment).findByRole('checkbox', { name: /Synthetic Government/i });

    // Act
    fireEvent.click(government);
    fireEvent.click(within(attachment).getByRole('button', { name: /save environment/i }));

    // Assert
    expect(within(attachment).getByRole('checkbox', { name: /Synthetic Commercial Alpha/i })).toBeDisabled();
    await waitFor(() => expect(apiClient.put).toHaveBeenCalledWith(environmentPath(), {
      cloudEnvironment: 'Government', subscriptionIds: [governmentSubscriptionId],
    }));
  });

  it.each([
    ['Commercial', unavailableSubscriptionId],
    ['Government', governmentSubscriptionId],
    ['GovernmentAirGappedIl5', legacySubscriptionId],
    ['UnknownCloud', legacySubscriptionId],
  ])('preserves and flags legacy invalid %s bindings instead of silently replacing them', async (cloudEnvironment, binding) => {
    // Arrange
    responses.set(environmentPath(), () => ({
      ...environment(), cloudEnvironment, subscriptionIds: [binding],
    }));
    render(pageElement());

    // Act
    const attachment = await panel();

    // Assert
    expect(await within(attachment).findByText(binding, { exact: false })).toBeInTheDocument();
    expect(within(attachment).getByText(/unavailable|mismatch|unsupported|invalid|not registered/i)).toBeInTheDocument();
    expect(within(attachment).getByRole('button', { name: /save environment/i })).toBeDisabled();
    expect(within(attachment).getByRole('button', { name: /detach environment/i })).toBeEnabled();
    expect(apiClient.put).not.toHaveBeenCalled();
  });

  it('detaches the current environment and rechecks readiness without writing the Mission Profile', async () => {
    // Arrange
    responses.set(environmentPath(), () => ({ ...environment(), cloudEnvironment: 'Commercial', subscriptionIds: [subscriptionId] }));
    responses.set(readinessPath(), () => readiness(true));
    render(pageElement());
    const attachment = await panel();
    await within(attachment).findByRole('button', { name: /detach environment/i });
    const before = vi.mocked(apiClient.get).mock.calls.filter(([url]) => url === readinessPath()).length;
    responses.set(environmentPath(), () => environment());
    responses.set(readinessPath(), () => readiness());

    // Act
    fireEvent.click(within(attachment).getByRole('button', { name: /detach environment/i }));

    // Assert
    await waitFor(() => expect(apiClient.delete).toHaveBeenCalledWith(environmentPath()));
    await waitFor(() => expect(vi.mocked(apiClient.get).mock.calls.filter(([url]) => url === readinessPath()).length).toBeGreaterThan(before));
    expect(await within(attachment).findByText(readiness().message)).toBeInTheDocument();
    expect(apiClient.put).not.toHaveBeenCalled();
    expect(apiClient.post).not.toHaveBeenCalled();
  });

  it('links to organization registration management when there are no subscriptions', async () => {
    // Arrange
    responses.set(environmentPath(), () => ({ ...environment(), availableSubscriptions: [] }));
    render(pageElement());

    // Act
    const attachment = await panel();

    // Assert
    expect(await within(attachment).findByText(/no .*subscriptions/i)).toBeInTheDocument();
    expect(within(attachment).getByRole('link', { name: /register|manage|subscription/i })).toHaveAttribute('href', '/settings/azure-subscriptions');
    expect(within(attachment).getByRole('button', { name: /save environment/i })).toBeDisabled();
  });

  it('renders normalized writer-forbidden guidance without exposing editable configuration', async () => {
    // Arrange
    const error = { error: 'You do not have permission to configure this environment.', errorCode: 'FORBIDDEN', suggestion: 'Ask a ComplianceWriter to attach the environment.' };
    responses.set(environmentPath(), () => { throw error; });
    render(pageElement());

    // Act
    const attachment = await panel();

    // Assert
    expect(await within(attachment).findByText(error.error)).toBeInTheDocument();
    expect(within(attachment).getByText(error.suggestion)).toBeInTheDocument();
    expect(within(attachment).queryByRole('checkbox')).not.toBeInTheDocument();
    for (const button of within(attachment).queryAllByRole('button', { name: /save environment|detach environment/i })) expect(button).toBeDisabled();
    expect(apiClient.put).not.toHaveBeenCalled();
  });

  it('retains configuration-load retry after a connection error', async () => {
    // Arrange
    responses.set(environmentPath(), () => { throw new Error('Connection interrupted'); });
    render(pageElement());
    const attachment = await panel();
    await within(attachment).findByText(/Connection interrupted|Unable to load/i);
    responses.set(environmentPath(), () => environment());

    // Act
    fireEvent.click(within(attachment).getByRole('button', { name: /retry/i }));

    // Assert
    expect(await within(attachment).findByRole('checkbox', { name: /Synthetic Commercial Alpha/i })).toBeEnabled();
  });

  it('blocks configuration when the CSP administrator has not explicitly selected the system organization', async () => {
    // Arrange
    const error = {
      error: 'Select the system organization before configuring its Azure environment.',
      errorCode: 'ASSESSMENT_AZURE_ORGANIZATION_REQUIRED',
      suggestion: 'Use the organization selector to choose the system organization, then retry.',
    };
    responses.set(environmentPath(), () => { throw error; });
    render(pageElement());

    // Act
    const attachment = await panel();

    // Assert
    expect(await within(attachment).findByText(error.error)).toBeInTheDocument();
    expect(within(attachment).getByText(error.suggestion)).toBeInTheDocument();
    expect(within(attachment).queryByRole('checkbox')).not.toBeInTheDocument();
    expect(apiClient.put).not.toHaveBeenCalled();
    expect(apiClient.delete).not.toHaveBeenCalled();
    expect(apiClient.post).not.toHaveBeenCalled();
  });

  it('shows normalized save failures without claiming readiness or losing the selected subscription', async () => {
    // Arrange
    const error = { error: 'The selected subscription is no longer available.', errorCode: 'SUBSCRIPTION_UNAVAILABLE', suggestion: 'Refresh organization registrations and select an eligible subscription.' };
    vi.mocked(apiClient.put).mockRejectedValue(error);
    render(pageElement());
    const attachment = await panel();
    const selected = await within(attachment).findByRole('checkbox', { name: /Synthetic Commercial Alpha/i });

    // Act
    fireEvent.click(selected);
    fireEvent.click(within(attachment).getByRole('button', { name: /save environment/i }));

    // Assert
    expect(await within(attachment).findByText(error.error)).toBeInTheDocument();
    expect(within(attachment).getByText(error.suggestion)).toBeInTheDocument();
    expect(selected).toBeChecked();
    expect(within(attachment).queryByText(/environment saved|ready to run/i)).not.toBeInTheDocument();
  });

  it('ignores late environment responses after switching systems', async () => {
    // Arrange
    const previous = deferred<ReturnType<typeof environment>>();
    responses.set(environmentPath(), () => previous.promise);
    responses.set(environmentPath(otherSystemId), () => ({ ...environment(otherSystemId), availableSubscriptions: [] }));
    const page = render(pageElement());

    // Act
    context.systemId = otherSystemId;
    page.rerender(pageElement());
    const attachment = await panel();
    await within(attachment).findByText(/no .*subscriptions/i);
    await act(async () => previous.resolve(environment()));

    // Assert
    expect(within(attachment).queryByRole('checkbox', { name: /Synthetic Commercial Alpha/i })).not.toBeInTheDocument();
    expect(within(attachment).getByRole('button', { name: /save environment/i })).toBeDisabled();
  });
});
