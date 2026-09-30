import { beforeEach, expect, it, vi } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { PackagesSection } from '../../features/provider-authorizations/AuthorizationsPage';
import * as api from '../../features/provider-authorizations/api';
import * as packages from '../../features/package-imports/api';
import { offering } from './testData';
import { offeringOverview, recordedAuthorization } from './overviewFixtures';
import { entry, page } from '../package-imports/fixtures';

vi.mock('../../features/provider-authorizations/api', async original => ({
  ...await original<typeof api>(), getOfferingOverview: vi.fn(), listPackageVersions: vi.fn(),
}));
vi.mock('../../features/package-imports/api', async original => ({
  ...await original<typeof packages>(), getPackageEntries: vi.fn(),
}));

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(api.getOfferingOverview).mockResolvedValue({
    ...offeringOverview(), authorizations: { ...page([recordedAuthorization]), recorded: 1, unconfirmed: 0, rejected: 0 },
  });
  vi.mocked(api.listPackageVersions).mockResolvedValue(page([]));
  vi.mocked(packages.getPackageEntries).mockResolvedValue(page([
    entry({ entryId: 'archive', fileName: 'source.zip', mediaType: 'application/zip' }),
    entry({ fileName: 'customer-responsibilities.json' }),
    entry({ entryId: 'private', fileName: 'restricted.pdf', status: 'Unreadable', reason: 'Encrypted source' }),
  ]));
});

it('shows retained source documents below their package without inventing review or access approval', async () => {
  // Arrange / Act
  render(<MemoryRouter><PackagesSection offering={offering} /></MemoryRouter>);
  const table = await screen.findByRole('table', { name: 'Source packages and documents' });
  // Assert
  expect(await within(table).findByText('customer-responsibilities.json')).toBeVisible();
  expect(within(table).getByText('restricted.pdf')).toBeVisible();
  expect(within(table).queryByText('source.zip')).not.toBeInTheDocument();
  expect(within(table).getByText('Unreadable')).toBeVisible();
  expect(within(table).queryByText('Shareable')).not.toBeInTheDocument();
  expect(screen.getByRole('link', { name: 'View record' })).toHaveAttribute('href',
    api.authorizationHref(offering.offeringId, 'decisions/decision-1'));
  expect(screen.queryByText('Import another source package')).not.toBeInTheDocument();
});

it('shows document read failures as retryable errors, not an empty source inventory', async () => {
  // Arrange
  vi.mocked(packages.getPackageEntries).mockRejectedValue(new Error('Source entries unavailable'));
  // Act
  render(<MemoryRouter><PackagesSection offering={offering} /></MemoryRouter>);
  // Assert
  expect(await screen.findByRole('alert')).toHaveTextContent('Source entries unavailable');
  expect(screen.getByRole('button', { name: 'Retry source documents' })).toBeVisible();
});
