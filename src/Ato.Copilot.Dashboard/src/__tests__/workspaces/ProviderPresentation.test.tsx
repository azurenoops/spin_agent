import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { ProviderVersions } from '../../features/workspace-operations/ProviderPresentation';
import type { ProviderCatalogItem } from '../../features/workspace-operations/types';

const item: ProviderCatalogItem = {
  source: 'provider', componentId: 'component-a', capabilityId: 'capability-a', name: 'Monitoring',
  description: '', componentName: 'Platform', componentType: 'Platform', lifecycle: 'Published',
  reviewState: 'Mapped', sourceFormat: 'Manual', sourceReference: null, distinctAdoptionCount: 0,
  releasedRevision: 4, workingRevision: 4, workingApprovalState: 'Approved',
};

describe('provider publication version presentation', () => {
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
