import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import SystemHostingSummary from '../../features/systems/SystemHostingSummary';
import { listProviderRelationships } from '../../features/provider-relationships/api';

vi.mock('../../features/provider-relationships/api', () => ({ listProviderRelationships: vi.fn() }));
const list = vi.mocked(listProviderRelationships);
const relationship = {
  relationshipId: 'relationship-1', revision: 4, assignmentId: 'allocation-1', assignmentRevision: 3,
  offeringId: 'offering-1', systemId: 'system-a', state: 'SeparateBoundaryConsumer' as const,
  reviewRequired: false, authorizationRevisionId: 'source-2', boundaryRevisionId: 'scope-3',
  reviewedBy: 'Reviewer', reviewedAt: '2026-09-25T12:00:00Z',
  assignedScopes: [{ cloud: 'AzureUSGovernment' as const, directoryTenantId: 'directory', subscriptionId: 'subscription', resourceId: '/subscriptions/subscription/resourceGroups/mission' }],
  offeringName: 'Mission hosting', providerName: 'Provider A', systemName: 'Mission A',
  hostingScopeName: 'Allocated production scope', canAssociate: false,
};

describe('System hosting summary', () => {
  beforeEach(() => vi.clearAllMocks());

  it('shows real association and source versions without inferring a mission authorization', async () => {
    // Arrange
    list.mockResolvedValue({ items: [relationship], page: 1, pageSize: 25, total: 1 });
    // Act
    render(<MemoryRouter><SystemHostingSummary systemId="system-a" /></MemoryRouter>);
    // Assert
    expect(await screen.findByText('Mission hosting')).toBeVisible();
    expect(screen.getByText('Provider A')).toBeVisible();
    expect(screen.getByText('Separate boundary consumer')).toBeVisible();
    expect(screen.getByText('source-2')).toBeVisible();
    expect(screen.getByRole('link', { name: 'Review hosting scope' })).toHaveAttribute('href', '/systems/system-a/profile/EnvironmentAndDeployment/hosting');
    expect(screen.getByText(/does not authorize this mission system/)).toBeVisible();
    expect(list).toHaveBeenCalledWith('system-a', 1, expect.any(AbortSignal));
  });

  it('distinguishes an available allocation from a confirmed association', async () => {
    // Arrange
    list.mockResolvedValue({ items: [{ ...relationship, relationshipId: null, reviewedBy: null, reviewedAt: null, reviewRequired: true }], page: 1, pageSize: 25, total: 1 });
    // Act
    render(<MemoryRouter><SystemHostingSummary systemId="system-a" /></MemoryRouter>);
    // Assert
    expect(await screen.findByText('Available for association')).toBeVisible();
    expect(screen.queryByText('Separate boundary consumer')).not.toBeInTheDocument();
  });

  it('offers retry after failure rather than presenting an empty hosting inventory', async () => {
    // Arrange
    list.mockRejectedValueOnce(new Error('Access to provider context is unavailable.'));
    list.mockResolvedValue({ items: [], page: 1, pageSize: 25, total: 0 });
    render(<MemoryRouter><SystemHostingSummary systemId="system-a" /></MemoryRouter>);
    // Act
    expect(await screen.findByRole('alert')).toHaveTextContent('Access to provider context is unavailable.');
    fireEvent.click(screen.getByRole('button', { name: 'Retry hosting context' }));
    // Assert
    expect(await screen.findByText('No provider hosting scope is available for this system.')).toBeVisible();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('clears old records and rejects responses for a different selected system', async () => {
    // Arrange
    list.mockResolvedValue({ items: [relationship], page: 1, pageSize: 25, total: 1 });
    const view = render(<MemoryRouter><SystemHostingSummary systemId="system-a" /></MemoryRouter>);
    await screen.findByText('Mission hosting');
    // Act
    view.rerender(<MemoryRouter><SystemHostingSummary systemId="system-b" /></MemoryRouter>);
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Hosting context does not match the selected system.');
    expect(screen.queryByText('Mission hosting')).not.toBeInTheDocument();
  });
});
