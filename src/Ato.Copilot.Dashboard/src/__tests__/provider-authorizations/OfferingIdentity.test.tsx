import { beforeEach, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { OfferingCreate } from '../../features/provider-authorizations/OfferingIntake';
import { OfferingIdentityEditor } from '../../features/provider-authorizations/OfferingIdentity';
import * as api from '../../features/provider-authorizations/api';
import { offering } from './testData';
import { PackageImportError } from '../../features/package-imports/request';
import '../package-imports/crypto';

vi.mock('../../features/provider-authorizations/api', async original => ({
  ...await original<typeof api>(), createOffering: vi.fn(), updateOffering: vi.fn(), getOffering: vi.fn(),
}));
beforeEach(() => vi.clearAllMocks());
const change = (label: string, value: string) => fireEvent.change(screen.getByLabelText(label), { target: { value } });

it.each([true, false])('compares recorded and missing metadata without inferred service duties: recorded=%s', async recorded => {
  // Arrange
  const dirty = vi.fn();
  const current = { ...offering, serviceModel: recorded ? 'SoftwareAsAService' as const : null,
    managementArrangement: recorded ? 'SharedOperations' as const : null,
    serviceOwner: recorded ? 'Recorded owner' : null, securityContact: recorded ? 'Recorded contact' : null };
  render(<OfferingIdentityEditor offering={current} onSaved={vi.fn()} onDirtyChange={dirty} />);
  // Act
  change('Service model', recorded ? '' : 'PlatformService');
  change('Management arrangement', recorded ? '' : 'ProviderManaged');
  change('Service owner', recorded ? '' : 'Proposed owner');
  change('Security contact', recorded ? '' : 'Proposed contact');
  change('Description', 'Proposed description');
  fireEvent.click(screen.getByRole('button', { name: 'Compare identity edits' }));
  // Assert
  expect(screen.getByRole('table', { name: 'Identity changes' })).toHaveTextContent('Not recorded');
  expect(dirty).toHaveBeenLastCalledWith(true);
  expect(api.updateOffering).not.toHaveBeenCalled();
});

it('shows an explicit no-change identity comparison and rejects a foreign refresh', async () => {
  // Arrange
  vi.mocked(api.getOffering).mockResolvedValue({ ...offering, offeringId: 'foreign-offering' });
  render(<OfferingIdentityEditor offering={offering} onSaved={vi.fn()} />);
  // Act
  fireEvent.click(screen.getByRole('button', { name: 'Compare identity edits' }));
  // Assert
  expect(screen.getByText('No identity changes.')).toBeInTheDocument();
  // Act
  fireEvent.click(screen.getByRole('button', { name: 'Reload current service identity' }));
  // Assert
  expect(await screen.findByRole('alert')).toHaveTextContent('did not match the selected offering and revision');
  expect(screen.queryByRole('button', { name: 'Use refreshed revision with these edits' })).not.toBeInTheDocument();
});

it('compares unsaved identity with its opening persisted revision without inventing a published identity', async () => {
  // Arrange
  render(<OfferingIdentityEditor offering={offering} onSaved={vi.fn()} />);
  // Act
  change('Service owner', 'Proposed service team');
  fireEvent.click(screen.getByRole('button', { name: 'Compare identity edits' }));
  // Assert
  expect(screen.getByRole('table', { name: 'Identity changes' })).toHaveTextContent('Proposed service team');
  expect(screen.getByText(/Published offering-identity snapshot is not available/)).toBeInTheDocument();
  expect(api.updateOffering).not.toHaveBeenCalled();
});

it('creates the explicit service identity without manufacturing a connector or authorization', async () => {
  // Arrange
  vi.mocked(api.createOffering).mockResolvedValue(offering);
  render(<OfferingCreate expanded onCreated={vi.fn()} />);
  // Act
  change('Offering name', 'Synthetic platform');
  fireEvent.click(screen.getByLabelText('Azure Government'));
  change('Service model', 'PlatformService');
  change('Management arrangement', 'SharedOperations');
  change('Service owner', 'Synthetic service team');
  change('Security contact', 'Synthetic ISSM');
  fireEvent.click(screen.getByRole('button', { name: 'Create offering' }));
  // Assert
  await waitFor(() => expect(api.createOffering).toHaveBeenCalledWith({
    name: 'Synthetic platform', description: '', environments: ['AzureUSGovernment'],
    serviceModel: 'PlatformService', managementArrangement: 'SharedOperations',
    serviceOwner: 'Synthetic service team', securityContact: 'Synthetic ISSM',
  }, expect.any(String)));
});

it('creates an explicit manual M365 offering only after service model and management are supplied', async () => {
  // Arrange
  vi.mocked(api.createOffering).mockResolvedValue({ ...offering, environments: ['Microsoft365DoD'] });
  render(<OfferingCreate expanded onCreated={vi.fn()} />);
  // Act
  change('Offering name', 'Synthetic M365');
  fireEvent.click(screen.getByLabelText('Microsoft 365 DoD (manual service)'));
  // Assert
  expect(screen.getByRole('button', { name: 'Create offering' })).toBeDisabled();
  change('Service model', 'SoftwareAsAService');
  change('Management arrangement', 'SharedOperations');
  fireEvent.click(screen.getByRole('button', { name: 'Create offering' }));
  await waitFor(() => expect(api.createOffering).toHaveBeenCalledWith({
    name: 'Synthetic M365', description: '', environments: ['Microsoft365DoD'],
    serviceModel: 'SoftwareAsAService', managementArrangement: 'SharedOperations',
  }, expect.any(String)));
  expect(screen.getByLabelText('Azure Commercial')).not.toBeChecked();
  expect(screen.getByLabelText('Azure Government')).not.toBeChecked();
});

it('retains an identity edit after a revision conflict instead of overwriting newer metadata', async () => {
  // Arrange
  vi.mocked(api.updateOffering).mockRejectedValue(new PackageImportError('Offering revision changed', 409));
  render(<OfferingIdentityEditor offering={offering} onSaved={vi.fn()} />);
  // Act
  change('Service owner', 'Keep this owner');
  fireEvent.click(screen.getByRole('button', { name: 'Save service identity' }));
  // Assert
  expect(await screen.findByRole('alert')).toHaveTextContent('Offering revision changed');
  expect(screen.getByLabelText('Service owner')).toHaveValue('Keep this owner');
  expect(api.updateOffering).toHaveBeenCalledWith(offering.offeringId, expect.objectContaining({
    expectedRevision: offering.revision, serviceOwner: 'Keep this owner',
  }));
});
it('rebases only explicit edits while preserving concurrent environment and identity changes', async () => {
  // Arrange
  vi.mocked(api.getOffering).mockResolvedValue({ ...offering, revision: 9, name: 'New server name', description: 'New server description', environments: ['AzureCloud'] });
  vi.mocked(api.updateOffering).mockResolvedValue({ ...offering, revision: 10 });
  render(<OfferingIdentityEditor offering={offering} onSaved={vi.fn()} />);
  change('Service owner', 'My explicit owner edit');
  // Act
  fireEvent.click(screen.getByRole('button', { name: 'Reload current service identity' }));
  fireEvent.click(await screen.findByRole('button', { name: 'Use refreshed revision with these edits' }));
  fireEvent.click(screen.getByRole('button', { name: 'Save service identity' }));
  // Assert
  await waitFor(() => expect(api.updateOffering).toHaveBeenCalledWith(offering.offeringId, expect.objectContaining({
    expectedRevision: 9, name: 'New server name', description: 'New server description',
    environments: ['AzureCloud'], serviceOwner: 'My explicit owner edit',
  })));
});
