import { fireEvent, render, screen, within } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import ProfileSectionForm from '../../components/forms/ProfileSectionForm';

vi.mock('../../features/systems/ConnectedSystemEnvironments', () => ({
  default: () => <><section aria-label="Provider services & scopes">Independent provider services</section>
    <section aria-label="System subscriptions">Independent system subscriptions</section></>,
}));
const save = vi.fn();
function mount(content: Record<string, string> = {}, isReadOnly = false) {
  return render(<MemoryRouter><ProfileSectionForm sectionType="EnvironmentAndDeployment" governanceStatus="Draft"
    initialContent={JSON.stringify(content)} reviewerComments={null} isReadOnly={isReadOnly}
    userRole="MissionOwner" isSubmitting={false} error={null} systemId="system-a"
    onSave={save} onSubmit={vi.fn()} onWithdraw={vi.fn()} /></MemoryRouter>);
}
beforeEach(() => vi.clearAllMocks());
describe('Environment documentation independent of services and subscriptions', () => {
  it.each(['Draft', 'NeedsRevision'] as const)('hides only Environment submission in %s', governanceStatus => {
    // Arrange
    const submit = vi.fn();
    // Act
    render(<MemoryRouter><ProfileSectionForm sectionType="EnvironmentAndDeployment" governanceStatus={governanceStatus}
      initialContent="{}" reviewerComments={null} isReadOnly={false} userRole="MissionOwner"
      isSubmitting={false} error={null} onSave={save} onSubmit={submit} onWithdraw={vi.fn()} /></MemoryRouter>);
    // Assert
    expect(screen.queryByRole('button', { name: 'Submit for Review' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Save Draft' })).toBeEnabled();
    expect(submit).not.toHaveBeenCalled();
  });
  it('preserves hosting choices, deployment text and recovery controls beside independent workflows', () => {
    // Arrange
    mount();
    // Act
    const model = screen.getByRole('combobox', { name: 'Hosting model' });
    // Assert
    expect(within(model).getAllByRole('option').map(option => option.textContent)).toEqual([
      '— Select —', 'Provider-managed cloud', 'Organization-managed cloud', 'On-Premises', 'Hybrid',
    ]);
    expect(screen.getByRole('textbox', { name: 'Deployment description' })).toBeVisible();
    expect(screen.getByLabelText(/^Availability Tier/)).toHaveValue('');
    expect(screen.getByRole('region', { name: 'Provider services & scopes' })).toBeVisible();
    expect(screen.getByRole('region', { name: 'System subscriptions' })).toBeVisible();
    expect(screen.queryByRole('region', { name: 'Azure assessment environment' })).not.toBeInTheDocument();
    expect(save).not.toHaveBeenCalled();
  });
  it('preserves advanced and unknown values on an explicit documentation save', () => {
    // Arrange
    const original = { hostingModel: 'CSP-hosted', additionalDetails: 'Current environment',
      rtoRpo: 'RTO < 1hr / RPO < 15min', networkZones: '["DMZ"]', customLegacyKey: 'retain me' };
    mount(original);
    // Act
    fireEvent.change(screen.getByRole('combobox', { name: 'Hosting model' }), { target: { value: 'On-Premises' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save Draft' }));
    // Assert
    expect(JSON.parse(save.mock.calls[0]![0])).toEqual({ ...original, hostingModel: 'On-Premises' });
  });
  it('retains legacy hosting values without inferring provider or subscription identity', () => {
    // Arrange
    mount({ hostingModel: 'Cloud (IaaS)', availabilityTier: '99.9% (Three 9s)' });
    // Act
    const model = screen.getByRole('combobox', { name: 'Hosting model' });
    // Assert
    expect(model).toHaveValue('Cloud (IaaS)');
    expect(screen.getByRole('option', { name: /Cloud \(IaaS\).*previously recorded/ })).toBeInTheDocument();
    expect(screen.getByLabelText(/^Availability Tier/)).toHaveValue('99.9% (Three 9s)');
    expect(save).not.toHaveBeenCalled();
  });
  it('leaves unsaved deployment text unchanged when independent workflow sections render', () => {
    // Arrange
    mount({ hostingModel: 'Hybrid', additionalDetails: 'Reviewed deployment' });
    // Act
    fireEvent.change(screen.getByRole('textbox', { name: 'Deployment description' }), { target: { value: 'Unsubmitted deployment edit' } });
    // Assert
    expect(screen.getByRole('textbox', { name: 'Deployment description' })).toHaveValue('Unsubmitted deployment edit');
    expect(screen.getByRole('combobox', { name: 'Hosting model' })).toHaveValue('Hybrid');
    expect(save).not.toHaveBeenCalled();
  });
});
