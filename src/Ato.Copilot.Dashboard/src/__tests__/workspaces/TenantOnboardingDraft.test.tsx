import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import TenantWizard from '../../features/onboarding/TenantWizard';

const api = vi.hoisted(() => ({
  getState: vi.fn(), saveDraft: vi.fn(), submitLegalEntity: vi.fn(),
  submitHqAddress: vi.fn(), submitClassification: vi.fn(), submitAo: vi.fn(),
  submitPrimaryPoc: vi.fn(), submitOrgProfile: vi.fn(), submitFinal: vi.fn(),
}));
const authorization = vi.hoisted(() => ({ enabled: false, provider: false, support: false, canManage: true }));
vi.mock('../../features/workspaces/WorkspaceBoundary', () => ({
  useWorkspaceSession: () => authorization.enabled ? {
    target: { kind: authorization.provider ? 'csp' : 'organization', mode: authorization.support ? 'support' : undefined },
    workspace: { permissions: { canManageOrganization: authorization.canManage } },
  } : null,
}));
vi.mock('../../features/onboarding/TenantWizard/api', () => ({ tenantWizard: api }));
vi.mock('../../features/onboarding/api/onboardingApi', () => ({ onboarding: { skipStep: vi.fn() } }));

const values = {
  legalEntity: { legalEntityName: 'Retained legal entity', doDComponent: 'NAVY', timeZone: 'America/New_York' },
  hqAddress: { hqAddressLine1: '10 Test Way', hqAddressLine2: '', hqCity: 'Norfolk', hqStateOrProvince: 'VA', hqPostalCode: '23501', hqCountry: 'USA' },
  classification: { defaultClassificationLevel: 'CUI' },
  ao: { authorizingOfficialName: 'Recorded AO', authorizingOfficialEmail: 'ao@example.invalid' },
  primaryPoc: { primaryPocName: 'Recorded contact', primaryPocEmail: 'poc@example.invalid', primaryPocPhone: '' },
  orgProfile: { firstOrganizationId: 'subgroup-a', name: 'Retained subgroup', description: 'Retained description' },
};
const state = {
  tenantId: 'tenant-a', currentStep: 'Tenant.LegalEntity', completedSteps: [],
  onboardingState: 'Pending', firstOrganizationId: 'subgroup-a',
  submittedValues: values, draft: null, draftRevision: 0,
};

beforeEach(() => {
  vi.resetAllMocks();
  Object.assign(authorization, { enabled: false, provider: false, support: false, canManage: true });
  api.getState.mockResolvedValue(state);
  api.saveDraft.mockResolvedValue({ ...state, draftRevision: 1 });
  for (const action of [api.submitLegalEntity, api.submitHqAddress, api.submitClassification, api.submitAo, api.submitPrimaryPoc, api.submitOrgProfile])
    action.mockResolvedValue({ ...state, draftRevision: 1 });
  api.submitFinal.mockResolvedValue({ ...state, onboardingState: 'Active', currentStep: 'Submitted' });
});

describe('tenant activation domain draft', () => {
  it.each(['provider', 'support', 'member'])('denies tenant edits to a %s context', async kind => {
    // Arrange
    Object.assign(authorization, { enabled: true, provider: kind === 'provider', support: kind === 'support', canManage: kind !== 'member' });
    // Act
    render(<MemoryRouter><TenantWizard /></MemoryRouter>);
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('ordinary organization administrator');
    expect(screen.queryByRole('button', { name: 'Save & finish later' })).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Legal entity name')).not.toBeInTheDocument();
    expect(api.saveDraft).not.toHaveBeenCalled();
  });
  it('an authorized tenant administrator saves all slices with the original revision', async () => {
    // Arrange
    authorization.enabled = true;
    render(<MemoryRouter><TenantWizard /></MemoryRouter>);
    await screen.findByDisplayValue('Retained legal entity');
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Save & finish later' }));
    // Assert
    await waitFor(() => expect(api.saveDraft).toHaveBeenCalledWith({
      schemaVersion: 1, expectedRevision: 0, currentStep: 'Tenant.LegalEntity', values,
    }));
  });
  it('applies all six hydrated slices through their existing domain commands', async () => {
    // Arrange
    render(<MemoryRouter><TenantWizard /></MemoryRouter>);
    await screen.findByDisplayValue('Retained legal entity');
    // Act
    for (const action of [api.submitLegalEntity, api.submitHqAddress, api.submitClassification, api.submitAo, api.submitPrimaryPoc, api.submitOrgProfile]) {
      for (const field of screen.queryAllByRole('textbox')) {
        const input = field as HTMLInputElement;
        const original = input.value;
        fireEvent.change(input, { target: { value: `${original} updated` } });
        fireEvent.change(input, { target: { value: original } });
      }
      if (action === api.submitClassification) {
        fireEvent.click(screen.getByRole('radio', { name: /^Unclassified/ }));
        fireEvent.click(screen.getByRole('radio', { name: /^CUI/ }));
      }
      fireEvent.click(screen.getByRole('button', { name: 'Apply step & continue' }));
      await waitFor(() => expect(action).toHaveBeenCalledOnce());
    }
    // Assert
    expect(api.submitLegalEntity).toHaveBeenCalledWith({ ...values.legalEntity, expectedRevision: 0 });
    expect(api.submitHqAddress).toHaveBeenCalledWith({ ...values.hqAddress, hqAddressLine2: null, expectedRevision: 1 });
    expect(api.submitClassification).toHaveBeenCalledWith({ ...values.classification, expectedRevision: 1 });
    expect(api.submitAo).toHaveBeenCalledWith({ ...values.ao, expectedRevision: 1 });
    expect(api.submitPrimaryPoc).toHaveBeenCalledWith({ ...values.primaryPoc, primaryPocPhone: null, expectedRevision: 1 });
    expect(api.submitOrgProfile).toHaveBeenCalledWith({ ...values.orgProfile, expectedRevision: 1 });
    expect(screen.getByRole('heading', { name: 'Applied tenant facts' })).toBeInTheDocument();
  });

  it.each([
    ['Legal entity', 'submitLegalEntity'],
    ['Headquarters address', 'submitHqAddress'],
    ['Default classification', 'submitClassification'],
    ['Authorizing Official', 'submitAo'],
    ['Primary POC', 'submitPrimaryPoc'],
    ['First organization', 'submitOrgProfile'],
  ] as const)('surfaces %s submission errors without leaving the step', async (label, method) => {
    // Arrange
    api[method].mockRejectedValue(new Error('Server rejected stale tenant values'));
    render(<MemoryRouter><TenantWizard /></MemoryRouter>);
    await screen.findByDisplayValue('Retained legal entity');
    fireEvent.click(screen.getByRole('button', { name: new RegExp(`\\d ${label}`) }));
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Apply step & continue' }));
    // Assert
    expect(await screen.findByText('Server rejected stale tenant values')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: label, level: 1 })).toBeInTheDocument();
  });

  it('retains active reentry and reports an activation failure', async () => {
    // Arrange
    api.getState.mockResolvedValue({ ...state, onboardingState: 'Active', currentStep: 'Submitted' });
    api.submitFinal.mockRejectedValue(new Error('Unapplied draft remains'));
    render(<MemoryRouter><TenantWizard /></MemoryRouter>);
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Activate tenant' }));
    // Assert
    expect(await screen.findByText('Unapplied draft remains')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Review tenant activation', level: 1 })).toBeInTheDocument();
    expect(api.submitFinal).toHaveBeenCalledWith(0);
  });

  it('activates applied facts and does not apply any other step implicitly', async () => {
    // Arrange
    api.getState.mockResolvedValue({ ...state, currentStep: 'Submitted' });
    render(<MemoryRouter><TenantWizard /></MemoryRouter>);
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Activate tenant' }));
    // Assert
    await waitFor(() => expect(api.submitFinal).toHaveBeenCalledWith(0));
    expect(api.submitLegalEntity).not.toHaveBeenCalled();
  });

  it('restores saved partial draft values over submitted fields and keeps missing slices', async () => {
    // Arrange
    api.getState.mockResolvedValue({ ...state, draft: { revision: 2, schemaVersion: 1, currentStep: 'Tenant.Ao',
      values: { ao: { authorizingOfficialName: 'Draft official' } }, savedAt: '2026-09-30T12:00:00Z' }, draftRevision: 2 });
    render(<MemoryRouter><TenantWizard /></MemoryRouter>);
    // Assert
    expect(await screen.findByDisplayValue('Draft official')).toBeInTheDocument();
    expect(screen.getByDisplayValue('ao@example.invalid')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Back' }));
    expect(screen.getByRole('radio', { name: /^CUI/ })).toBeChecked();
  });

  it('does not fabricate editable data when the state endpoint lacks hydration', async () => {
    // Arrange
    api.getState.mockResolvedValue({ ...state, submittedValues: undefined });
    render(<MemoryRouter><TenantWizard /></MemoryRouter>);
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Saved tenant values are unavailable');
    expect(screen.getByRole('button', { name: 'Apply step & continue' })).toBeDisabled();
  });

  it('surfaces failed state reads explicitly', async () => {
    // Arrange
    api.getState.mockRejectedValue(new Error('Tenant state unavailable'));
    render(<MemoryRouter><TenantWizard /></MemoryRouter>);
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Tenant state unavailable');
  });
  it('aborts the hydration request when the tenant wizard unmounts', async () => {
    // Arrange
    api.getState.mockImplementation(() => new Promise(() => {}));
    const view = render(<MemoryRouter><TenantWizard /></MemoryRouter>);
    // Act
    view.unmount();
    // Assert
    const signal = api.getState.mock.calls[0]?.[0];
    expect(signal).toBeInstanceOf(AbortSignal);
    expect(signal.aborted).toBe(true);
  });
  it('hydrates submitted fields and preserves dirty values across step navigation', async () => {
    // Arrange
    render(<MemoryRouter><TenantWizard /></MemoryRouter>);
    await screen.findByRole('button', { name: /Headquarters address/i });
    // Act
    fireEvent.change(screen.getByLabelText('Legal entity name', { exact: false }), { target: { value: 'Locally edited name' } });
    fireEvent.click(screen.getByRole('button', { name: /Headquarters address/i }));
    // Assert
    expect(screen.getByDisplayValue('Norfolk')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /Legal entity/i }));
    expect(screen.getByDisplayValue('Locally edited name')).toBeInTheDocument();
    expect(api.submitLegalEntity).not.toHaveBeenCalled();
  });

  it('restores saved classification instead of silently selecting Unclassified', async () => {
    // Arrange
    render(<MemoryRouter><TenantWizard /></MemoryRouter>);
    // Act
    fireEvent.click(await screen.findByRole('button', { name: /Default classification/i }));
    // Assert
    expect(screen.getByRole('radio', { name: /^CUI/ })).toBeChecked();
    expect(screen.getByRole('radio', { name: /^Unclassified/ })).not.toBeChecked();
  });

  it('save and finish later waits for a server save and keeps edits on failure', async () => {
    // Arrange
    api.saveDraft.mockRejectedValue(new Error('Draft storage unavailable'));
    render(<MemoryRouter><TenantWizard /></MemoryRouter>);
    await screen.findByRole('button', { name: /Headquarters address/i });
    // Act
    fireEvent.change(screen.getByLabelText('Legal entity name', { exact: false }), { target: { value: 'Retain on failure' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save & finish later' }));
    // Assert
    await waitFor(() => expect(api.saveDraft).toHaveBeenCalledOnce());
    expect(await screen.findByText('Draft storage unavailable')).toBeInTheDocument();
    expect(screen.getByDisplayValue('Retain on failure')).toBeInTheDocument();
  });
});
