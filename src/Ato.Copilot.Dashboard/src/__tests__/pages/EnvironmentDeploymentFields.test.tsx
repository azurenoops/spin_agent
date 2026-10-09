import { fireEvent, render, screen, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import ProfileSectionForm from '../../components/forms/ProfileSectionForm';
vi.mock('../../features/system-design/UnsavedDesignGuard', () => ({ default: () => null }));

vi.mock('../../features/systems/ConnectedSystemEnvironments', () => ({ default: () => <section aria-label="System subscriptions" /> }));
function mount(content: Record<string, string>, locked = false) {
  const save = vi.fn();
  render(<ProfileSectionForm systemId="system-a" sectionType="EnvironmentAndDeployment" governanceStatus="Draft"
    initialContent={JSON.stringify(content)} reviewerComments={null} isReadOnly={locked}
    userRole="MissionOwner" isSubmitting={false} error={null} onSave={save} onSubmit={vi.fn()} onWithdraw={vi.fn()} />);
  return save;
}
describe('Environment primary fields', () => {
  it('leads with hosting model, cloud environment and deployment description while preserving keys', () => {
    // Arrange
    const original = { hostingModel: 'Hybrid', cloudProvider: '["Azure Government","AWS GovCloud"]',
      additionalDetails: 'Saved deployment.', customLegacyField: 'retain', rtoRpo: 'Recorded recovery target' };
    const save = mount(original);
    // Act
    fireEvent.change(screen.getByRole('textbox', { name: 'Deployment description' }), { target: { value: 'Updated deployment.' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save Draft' }));
    // Assert
    expect(screen.getByRole('combobox', { name: 'Hosting model' })).toBeVisible();
    expect(screen.getByRole('option', { name: 'Provider-managed cloud' })).toHaveValue('CSP-hosted');
    expect(screen.getByRole('combobox', { name: 'Cloud environment' })).toBeVisible();
    expect(screen.getByRole('combobox', { name: 'Cloud environment' })).toHaveTextContent('Azure Government');
    expect(screen.getByLabelText(/^Availability Tier/)).toHaveValue('');
    expect(JSON.parse(save.mock.calls[0]![0])).toEqual({ ...original, additionalDetails: 'Updated deployment.' });
  });
  it('supports keyboard cloud selection without dropping legacy values', () => {
    // Arrange
    const save = mount({ cloudProvider: 'Legacy sovereign cloud' });
    const cloud = screen.getByRole('combobox', { name: 'Cloud environment' });
    // Act
    cloud.focus();
    fireEvent.keyDown(cloud, { key: 'ArrowDown' });
    fireEvent.click(screen.getByRole('option', { name: 'Azure Government' }));
    fireEvent.click(screen.getByRole('button', { name: 'Save Draft' }));
    // Assert
    expect(cloud).toHaveTextContent('Legacy sovereign cloud');
    expect(JSON.parse(JSON.parse(save.mock.calls[0]![0]).cloudProvider)).toEqual(['Legacy sovereign cloud', 'Azure Government']);
  });
  it('selects a filtered cloud with Enter without submitting the deployment form', () => {
    // Arrange
    const save = mount({ cloudProvider: '[]' });
    const cloud = screen.getByRole('combobox', { name: 'Cloud environment' });
    // Act
    cloud.focus();
    fireEvent.keyDown(cloud, { key: 'ArrowDown' });
    const filter = screen.getByPlaceholderText('Type to filter...');
    fireEvent.change(filter, { target: { value: 'Azure Government' } });
    fireEvent.keyDown(filter, { key: 'Enter' });
    // Assert
    expect(cloud).toHaveTextContent('Azure Government');
    expect(save).not.toHaveBeenCalled();
  });
  it('keeps all existing hosting choices and prevents read-only edits', () => {
    // Arrange / Act
    mount({ hostingModel: 'On-Premises', cloudProvider: '["Google Cloud"]' }, true);
    // Assert
    expect(within(screen.getByRole('combobox', { name: 'Hosting model' })).getAllByRole('option')).toHaveLength(5);
    expect(screen.getByRole('combobox', { name: 'Cloud environment' })).toHaveAttribute('aria-disabled', 'true');
    expect(screen.getByRole('textbox', { name: 'Deployment description' })).toBeDisabled();
    expect(screen.queryByRole('button', { name: 'Save Draft' })).not.toBeInTheDocument();
  });
  it('shows every deployment field together in one actual form without disclosures before independent connections', () => {
    // Arrange
    mount({ networkZones: '["DMZ"]', geographicLocations: '[]', availabilityTier: 'Not Defined' });
    // Assert
    const form = screen.getByRole('textbox', { name: 'Deployment description' }).closest('form')!;
    expect(document.querySelectorAll('form')).toHaveLength(1);
    expect(form.querySelectorAll('details, form')).toHaveLength(0);
    for (const name of ['Hosting model', 'Cloud environment', 'Network Zones', 'Geographic Locations',
      'Availability Tier', 'Disaster Recovery Strategy', 'RTO / RPO Targets', 'Maintenance Windows', 'Operating Systems']) {
      expect(within(form).getByRole('combobox', { name })).toBeVisible();
    }
    expect(within(form).getByRole('textbox', { name: 'Deployment description' })).toBeVisible();
    expect(screen.getByText(/Recorded values still require review/)).toBeVisible();
    const hosting = screen.getByRole('region', { name: 'System subscriptions' });
    expect(form.compareDocumentPosition(hosting) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(hosting.closest('form')).toBeNull();
  });
});
