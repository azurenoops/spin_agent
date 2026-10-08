import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import CspWizard from '../../features/csp-onboarding/CspWizard';
import { packageRequest } from '../../features/package-imports/request';
import '../package-imports/crypto';

vi.mock('../../features/package-imports/request', async original => ({
  ...await original<typeof import('../../features/package-imports/request')>(), packageRequest: vi.fn(),
}));

const providerId = '11111111-1111-4111-8111-111111111111';
const screens = [
  ['p-details', 'Confirm provider identity'],
  ['p-access', 'Confirm access and contacts'],
  ['p-offering', 'Add the first offering'],
  ['p-authorization', 'Choose authorization starting point'],
  ['p-sources', 'Add available records'],
  ['p-uncertain', 'Check the package receipt'],
  ['p-review', 'Review and finish setup'],
  ['p-ready', 'Provider setup complete'],
] as const;

function state(currentScreen = 'p-details') {
  return {
    providerId, profileRevision: 1,
    profile: { cspProfileId: providerId, onboardingState: 'InWizard', currentStep: 'Identity' },
    draft: {
      draftId: '33333333-3333-4333-8333-333333333333', revision: 3, schemaVersion: 1,
      currentScreen, savedAt: '2026-09-30T12:00:00Z', savedBy: 'Synthetic administrator',
      fields: {
        currentScreen,
        details: { displayName: 'Synthetic provider', legalEntityName: 'Synthetic operator',
          serviceContactName: 'Synthetic contact', serviceContactEmail: 'contact@example.invalid',
          dodComponent: 'Department of the Navy', timeZoneId: 'America/New_York' },
        operationalContact: { choice: 'Deferred', deferral: { reason: 'Operations later', ownerRole: 'CSP.Admin' } },
        securityContact: { choice: 'Deferred', deferral: { reason: 'Reviewer to be designated', ownerRole: 'CSP.Admin' } },
        firstOffering: { choice: 'Deferred', deferral: { reason: 'Service details later', ownerRole: 'CSP.Admin' } },
        authorization: { choice: 'DetermineLater' },
        sources: { choice: 'Deferred', intentIds: [], deferral: { reason: 'Sources later', ownerRole: 'CSP.Admin' } },
      },
      committedOfferingId: null, completion: null,
    },
    access: {
      state: 'Authorized', checkedAt: '2026-09-30T12:00:00Z', scope: 'Provider',
      actor: { directoryTenantId: providerId, objectId: '44444444-4444-4444-8444-444444444444', displayName: 'Synthetic administrator' },
    },
    handling: { state: 'Unknown', checkedAt: '2026-09-30T12:00:00Z', allowedClassifications: [],
      allowedMarkings: [], syntheticOnly: false, uploadsPermitted: false, analysisPermitted: false,
      reasonCode: 'HANDLING_POLICY_UNKNOWN' },
    uploadIntents: [], facts: [],
  };
}

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(packageRequest).mockImplementation(async config => {
    if (config.url === '/api/csp/onboarding/setup') return state() as never;
    if (config.url?.endsWith('/actions')) return { items: [], page: 1, pageSize: 25, total: 0 } as never;
    return { items: [], page: 1, pageSize: 25, total: 0 } as never;
  });
});

describe('provider guided setup and persistent draft', () => {
  it.each(screens)('restores the mock screen %s from server-owned state', async (id, title) => {
    // Arrange
    vi.mocked(packageRequest).mockImplementation(async config => config.url === '/api/csp/onboarding/setup'
      ? state(id) as never : { items: [], page: 1, pageSize: 25, total: 0 } as never);
    // Act
    render(<MemoryRouter initialEntries={['/onboarding/csp?reentry=resume']}><CspWizard /></MemoryRouter>);
    // Assert
    expect(await screen.findByRole('heading', { name: title })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Save & finish later' })).toBeInTheDocument();
    expect(packageRequest).toHaveBeenCalledWith(expect.objectContaining({ url: '/api/csp/onboarding/setup' }));
  });

  it('explicitly saves partial details without activating the profile or executing an upload', async () => {
    // Arrange
    vi.mocked(packageRequest).mockImplementation(async config => {
      if (config.method === 'PUT') return {
        outcome: 'Committed', replayed: false,
        committedOutcome: { providerId, committedDraftRevision: 4 },
        current: { projectionState: 'Available', state: state(), access: state().access, actor: state().access.actor },
      } as never;
      return state() as never;
    });
    render(<MemoryRouter initialEntries={['/onboarding/csp']}><CspWizard /></MemoryRouter>);
    await screen.findByRole('heading', { name: 'Confirm provider identity' });
    fireEvent.change(screen.getByLabelText('Provider display name'), { target: { value: 'Partial name retained' } });
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Save & finish later' }));
    // Assert
    await waitFor(() => expect(packageRequest).toHaveBeenCalledWith(expect.objectContaining({
      method: 'PUT', url: '/api/csp/onboarding/setup/draft',
      data: expect.objectContaining({ expectedRevision: 3, draft: expect.objectContaining({
        details: expect.objectContaining({ displayName: 'Partial name retained' }),
      }) }),
    })));
    expect(vi.mocked(packageRequest).mock.calls.some(([request]) => request.url?.endsWith('/completion')
      || request.url?.endsWith('/atos/upload') || request.url?.endsWith('/package-versions'))).toBe(false);
  });

  it('shows unknown deployment handling rather than treating offering impact as permission to upload', async () => {
    // Arrange
    vi.mocked(packageRequest).mockResolvedValue(state() as never);
    // Act
    render(<MemoryRouter><CspWizard /></MemoryRouter>);
    // Assert
    await screen.findByRole('heading', { name: 'Confirm provider identity' });
    expect(screen.getByText(/handling limits.*unavailable|handling policy.*unknown/i)).toBeInTheDocument();
    expect(screen.queryByText('Synthetic data only in this mock')).not.toBeInTheDocument();
  });

  it.each([
    ['AzureGovernment', 'AzureUSGovernment'],
    ['AzureCommercial', 'AzureCloud'],
    ['Microsoft365DoD', 'Microsoft365DoD'],
    ['AwsGovCloud', 'ManualService'],
    ['Other', 'ManualService'],
  ])('saves %s as canonical %s while retaining the onboarding declaration', async (declared, canonical) => {
    // Arrange
    vi.mocked(packageRequest).mockImplementation(async config => config.method === 'PUT'
      ? { outcome: 'Committed', replayed: false, committedOutcome: { committedDraftRevision: 4 },
        current: { projectionState: 'Available', state: state('p-offering') } } as never
      : config.url === '/api/csp/onboarding/setup' ? state('p-offering') as never
        : { items: [], page: 1, pageSize: 25, total: 0 } as never);
    render(<MemoryRouter><CspWizard /></MemoryRouter>);
    await screen.findByRole('heading', { name: 'Add the first offering' });
    // Act
    fireEvent.change(screen.getByLabelText('Environment'), { target: { value: declared } });
    fireEvent.change(screen.getByLabelText('Service model'), { target: { value: 'Software' } });
    fireEvent.change(screen.getByLabelText('Managed by'), { target: { value: 'Provider' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save & finish later' }));
    // Assert
    await waitFor(() => expect(packageRequest).toHaveBeenCalledWith(expect.objectContaining({
      method: 'PUT', url: '/api/csp/onboarding/setup/draft',
      data: expect.objectContaining({ draft: expect.objectContaining({
        firstOffering: expect.objectContaining({
          environments: [canonical],
          serviceDescription: expect.objectContaining({ environmentKind: declared, serviceModel: 'Software', managedBy: 'Provider' }),
        }),
      }) }),
    })));
    expect(vi.mocked(packageRequest).mock.calls.some(([request]) =>
      request.method === 'POST' || request.method === 'PATCH')).toBe(false);
  });

  it('keeps a known commit distinct from unavailable fresh state and requires a read before further writes', async () => {
    // Arrange
    vi.mocked(packageRequest).mockImplementation(async config => config.method === 'PUT'
      ? { outcome: 'Committed', replayed: false, committedOutcome: { committedDraftRevision: 4 },
        current: { projectionState: 'Unavailable', state: null,
          error: { message: 'Saved; current status unavailable. Do not repeat the mutation.' } } } as never
      : state() as never);
    render(<MemoryRouter><CspWizard /></MemoryRouter>);
    await screen.findByRole('heading', { name: 'Confirm provider identity' });
    fireEvent.click(screen.getByRole('button', { name: 'Save & finish later' }));
    await screen.findByRole('alert');
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Save & continue' }));
    // Assert
    await waitFor(() => expect(screen.getByRole('button', { name: 'Save & continue' })).toBeDisabled());
    expect(vi.mocked(packageRequest).mock.calls.filter(([request]) => request.method === 'PUT')).toHaveLength(1);
    expect(screen.getByRole('button', { name: 'Reload saved setup' })).toBeInTheDocument();
  });

  it('captures existing authorization intent and optional facts without recording a decision', async () => {
    // Arrange
    vi.mocked(packageRequest).mockImplementation(async config => config.method === 'PUT'
      ? { outcome: 'Committed', replayed: false, committedOutcome: { committedDraftRevision: 4 },
        current: { projectionState: 'Available', state: state('p-authorization') } } as never
      : config.url === '/api/csp/onboarding/setup' ? state('p-authorization') as never
        : { items: [], page: 1, pageSize: 25, total: 0 } as never);
    render(<MemoryRouter><CspWizard /></MemoryRouter>);
    await screen.findByRole('heading', { name: 'Choose authorization starting point' });

    // Act
    fireEvent.click(screen.getByLabelText('We have an existing authorization'));
    fireEvent.change(screen.getByLabelText('Decision reference'), { target: { value: 'ATO-2025-017' } });
    fireEvent.change(screen.getByLabelText('System or boundary name'), { target: { value: 'Flank Speed Azure as stated' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save & finish later' }));

    // Assert
    await waitFor(() => expect(packageRequest).toHaveBeenCalledWith(expect.objectContaining({
      method: 'PUT', url: '/api/csp/onboarding/setup/draft',
      data: expect.objectContaining({ draft: expect.objectContaining({
        authorization: expect.objectContaining({
          choice: 'ExistingAuthorization',
          decisionReference: 'ATO-2025-017',
          systemOrBoundaryName: 'Flank Speed Azure as stated',
        }),
      }) }),
    })));
    expect(vi.mocked(packageRequest).mock.calls.some(([request]) =>
      request.url?.includes('/decisions') || request.url?.includes('/boundaries'))).toBe(false);
  });

  it('shows the onboarding summary before offering an explicit provider workspace link', async () => {
    // Arrange
    vi.mocked(packageRequest).mockResolvedValue(state('p-ready') as never);

    // Act
    render(<MemoryRouter initialEntries={['/onboarding/csp?reentry=resume']}><CspWizard /></MemoryRouter>);

    // Assert
    expect(await screen.findByRole('heading', { name: 'Provider setup complete' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Recorded onboarding facts' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Open provider workspace' }))
      .toHaveAttribute('href', '/workspaces/csp/authorizations');
  });
});
