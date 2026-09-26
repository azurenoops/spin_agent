import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { ProviderOfferingSummary, ProviderVersions } from '../../features/workspace-operations/ProviderPresentation';
import type { ProviderCatalogItem } from '../../features/workspace-operations/types';

vi.mock('../../features/workspace-operations/api', () => ({
  getProviderCatalogOverview: vi.fn(async () => ({
    providerName: 'Synthetic provider',
    sourceArtifacts: { items: [], total: 2, page: 1, pageSize: 25 },
  })),
}));
vi.mock('../../features/package-imports/api', () => ({
  listPackages: vi.fn(async () => ({ items: [], total: 0, page: 1, pageSize: 25 })),
  packageImportHref: () => '/workspaces/csp/security-capabilities/imports',
}));

const item: ProviderCatalogItem = {
  source: 'provider', componentId: 'component-a', capabilityId: 'capability-a', name: 'Monitoring',
  description: '', componentName: 'Platform', componentType: 'Platform', lifecycle: 'Published',
  reviewState: 'Mapped', sourceFormat: 'Manual', sourceReference: null, distinctAdoptionCount: 0,
  releasedRevision: 4, workingRevision: 4, workingApprovalState: 'Approved',
};

describe('provider publication version presentation', () => {
  it('labels component reference counts accurately without inventing an authorization lookup result', async () => {
    // Arrange
    render(<MemoryRouter><ProviderOfferingSummary /></MemoryRouter>);
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'View source package' }));
    // Assert
    expect(screen.getByText('2 component source references')).toBeInTheDocument();
    expect(screen.queryByText('Not recorded · separate from publication')).not.toBeInTheDocument();
    expect(screen.getByText('Source references do not verify a provider authorization or grant a mission-system ATO.')).toBeInTheDocument();
    expect(await screen.findByRole('region', { name: 'Saved source packages' })).toBeInTheDocument();
  });

  it('does not portray the already released revision as pending work', () => {
    // Arrange
    const current = { ...item };
    // Act
    render(<ProviderVersions item={current} />);
    // Assert
    expect(screen.getByText('v4 · Published')).toBeInTheDocument();
    expect(screen.getByText('No pending revision')).toBeInTheDocument();
  });

  it('distinguishes a newer working revision from the current release', () => {
    // Arrange
    const changed = { ...item, workingRevision: 5, workingApprovalState: 'NotApproved' };
    // Act
    render(<ProviderVersions item={changed} />);
    // Assert
    expect(screen.getByText('v4 · Published')).toBeInTheDocument();
    expect(screen.getByText('v5 · Needs review')).toBeInTheDocument();
  });

  it('does not manufacture versions for a source without saved working or release rows', () => {
    // Arrange
    const unsaved = { ...item, workingRevision: null, releasedRevision: null, workingApprovalState: null };
    // Act
    render(<ProviderVersions item={unsaved} />);
    // Assert
    expect(screen.getByText('Not published')).toBeInTheDocument();
    expect(screen.getByText('No saved working revision')).toBeInTheDocument();
  });
});
