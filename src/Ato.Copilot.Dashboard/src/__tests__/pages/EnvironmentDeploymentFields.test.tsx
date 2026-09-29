import { fireEvent, render, screen, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import ProfileSectionForm from '../../components/forms/ProfileSectionForm';

vi.mock('../../components/forms/EnvironmentAssociations', () => ({ default: () => <section aria-label="Provider scope summary" /> }));
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
    expect(screen.getByRole('combobox', { name: 'Availability Tier' })).toBeVisible();
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
  it('opens both ATO preparation sections with honest recorded counts and field guidance', () => {
    // Arrange
    mount({ networkZones: '["DMZ"]', geographicLocations: '[]', availabilityTier: 'Not Defined' });
    // Assert
    const network = screen.getByText('Network zones & deployment locations').closest('details')!;
    const recovery = screen.getByText('Recovery, availability & operating details').closest('details')!;
    expect(network).toHaveAttribute('open');
    expect(recovery).toHaveAttribute('open');
    expect(network).toHaveTextContent('1 of 2 fields recorded');
    expect(recovery).toHaveTextContent('1 of 5 fields recorded');
    expect(screen.getByRole('combobox', { name: 'Network Zones' })).toBeVisible();
    expect(screen.getByRole('combobox', { name: 'Operating Systems' })).toBeVisible();
    expect(screen.getByText(/Recorded values still require review/)).toBeVisible();
    expect(screen.getAllByText('ATO preparation')).toHaveLength(2);
    const hosting = screen.getByRole('region', { name: 'Provider scope summary' });
    expect(hosting.compareDocumentPosition(network) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(network.compareDocumentPosition(recovery) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });
});
