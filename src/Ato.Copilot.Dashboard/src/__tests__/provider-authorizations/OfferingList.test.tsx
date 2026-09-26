import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { OfferingList } from '../../features/provider-authorizations/OfferingList';
import { WorkspaceNavigationProvider } from '../../features/workspaces/workspaceNavigation';
import * as api from '../../features/provider-authorizations/api';
import type { Offering } from '../../features/provider-authorizations/types';

vi.mock('../../features/provider-authorizations/api', async original => ({ ...await original<typeof api>(), listOfferings: vi.fn() }));
const offering: Offering = { offeringId: 'sample', providerId: 'provider', name: 'Azure IL5', description: '', environments: ['AzureUSGovernment'], revision: 1, lifecycle: 'Draft', currentBoundaryRevisionId: null, currentHostingScopeRevisionId: null };
const page = (items: Offering[]) => ({ items, page: 1, pageSize: 25, total: items.length });
const mount = () => render(<MemoryRouter><WorkspaceNavigationProvider workspace={{ kind: 'csp' }}><OfferingList /></WorkspaceNavigationProvider></MemoryRouter>);
beforeEach(() => { vi.clearAllMocks(); vi.mocked(api.listOfferings).mockResolvedValue(page([offering])); });
describe('offering landing presentation', () => {
  it('uses recorded scope to link the next setup step without inventing authorization status', async () => {
    // Arrange
    mount();
    // Act
    const link = await screen.findByRole('link', { name: 'Define boundary for Azure IL5' });
    // Assert
    expect(link).toHaveAttribute('href', '/workspaces/csp/authorizations/offerings/sample/boundary');
    expect(within(screen.getByRole('article', { name: 'Azure IL5' })).getByText('Azure Government')).toBeInTheDocument();
    expect(screen.getByText('1 offering')).toBeInTheDocument();
    expect(screen.queryByText('Authorized')).not.toBeInTheDocument();
  });
  it('offers hosting setup only after the boundary is recorded', async () => {
    // Arrange
    vi.mocked(api.listOfferings).mockResolvedValue(page([{ ...offering, currentBoundaryRevisionId: 'boundary' }]));
    // Act
    mount();
    // Assert
    expect(await screen.findByRole('link', { name: 'Define hosting scope for Azure IL5' })).toHaveAttribute('href', '/workspaces/csp/authorizations/offerings/sample/inherited-coverage');
  });
  it('clears a no-match search without presenting an empty catalog', async () => {
    // Arrange
    vi.mocked(api.listOfferings).mockImplementation(async (_page, search) => page(search ? [] : [offering]));
    mount();
    await screen.findByText('1 offering');
    // Act
    fireEvent.change(screen.getByRole('searchbox', { name: 'Search offerings' }), { target: { value: 'missing' } });
    // Assert
    expect(await screen.findByText('No matching offerings')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Clear search' }));
    await waitFor(() => expect(screen.getByRole('searchbox')).toHaveValue(''));
    expect(await screen.findByRole('link', { name: 'Azure IL5' })).toBeInTheDocument();
  });
  it('keeps search and offering cards without an inline creation form', async () => {
    // Arrange
    mount();
    const card = await screen.findByRole('article', { name: 'Azure IL5' });
    // Act
    const search = screen.getByRole('searchbox');
    // Assert
    expect(screen.queryByText('Create an offering', { selector: 'summary' })).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Offering name')).not.toBeInTheDocument();
    expect(search.compareDocumentPosition(card) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(screen.getByRole('complementary', { name: 'Authorization workflow' })).toBeInTheDocument();
  });
  it('shows empty catalog guidance without restoring inline creation', async () => {
    // Arrange
    vi.mocked(api.listOfferings).mockResolvedValue(page([]));
    // Act
    mount();
    // Assert
    expect(await screen.findByText('Create your first offering')).toBeInTheDocument();
    expect(screen.queryByText('Create an offering', { selector: 'summary' })).not.toBeInTheDocument();
    expect(screen.getByText('0 offerings')).toBeInTheDocument();
    expect(screen.queryByText('No matching offerings')).not.toBeInTheDocument();
  });
  it('shows recorded references without inventing authorization or another setup step', async () => {
    // Arrange
    vi.mocked(api.listOfferings).mockResolvedValue(page([{ ...offering, description: 'Tenant collaboration services',
      environments: ['AzureCloud', 'AzureUSGovernment'], currentBoundaryRevisionId: 'boundary', currentHostingScopeRevisionId: 'hosting' }]));
    // Act
    mount();
    const card = await screen.findByRole('article', { name: 'Azure IL5' });
    // Assert
    expect(within(card).getAllByText('Recorded revision')).toHaveLength(2);
    expect(within(card).getByText('Azure Commercial · Azure Government')).toBeInTheDocument();
    expect(within(card).getByText('Tenant collaboration services')).toBeInTheDocument();
    expect(within(card).queryByRole('link', { name: /Define/ })).not.toBeInTheDocument();
    expect(within(card).getByRole('link', { name: 'Manage Azure IL5' })).toHaveAttribute('href', '/workspaces/csp/authorizations/offerings/sample');
    expect(screen.queryByText('Authorized')).not.toBeInTheDocument();
  });
  it('uses server totals and resets pagination when searching', async () => {
    // Arrange
    vi.mocked(api.listOfferings).mockImplementation(async (requestedPage = 1, search) => ({
      items: [offering], page: requestedPage, pageSize: 25, total: search ? 1 : 26,
    }));
    mount();
    await screen.findByText('26 offerings');
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Next' }));
    await waitFor(() => expect(api.listOfferings).toHaveBeenLastCalledWith(2, '', expect.any(AbortSignal)));
    fireEvent.change(screen.getByRole('searchbox'), { target: { value: 'Azure' } });
    // Assert
    expect(await screen.findByText('1 offering found')).toBeInTheDocument();
    expect(api.listOfferings).toHaveBeenLastCalledWith(1, 'Azure', expect.any(AbortSignal));
    expect(screen.queryByRole('navigation', { name: 'Pagination' })).not.toBeInTheDocument();
  });
});
