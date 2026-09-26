import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { MemoryRouter, useLocation } from 'react-router-dom';
import type { ReactNode } from 'react';
import CspInheritedComponentsPage from '../../features/csp-inherited-components/CspInheritedComponentsPage';
import * as api from '../../features/csp-inherited-components/api';

vi.mock('../../components/layout/useCspDashboardAvailable', () => ({ useCspDashboardAvailable: () => true }));
vi.mock('../../components/layout/PageLayout', () => ({ default: ({ children }: { children: ReactNode }) => <main>{children}</main> }));
vi.mock('../../components/layout/PageHero', () => ({ default: () => <h1>Component Library</h1> }));
vi.mock('../../features/csp-inherited-components/api', () => ({
  listCspInheritedComponents: vi.fn<typeof api.listCspInheritedComponents>(async () => ({ items: [], total: 0, page: 1, pageSize: 200 })),
  listCspInheritedCapabilities: vi.fn(async () => []), isUnavailable: () => false, importCspInheritedComponents: vi.fn(),
  archiveCspInheritedComponent: vi.fn(),
}));
function CurrentRoute() { return <output aria-label="Route">{useLocation().pathname}</output>; }

describe('legacy provider import entry', () => {
  it('continues to show archive errors after the legacy upload form is removed', async () => {
    // Arrange
    vi.mocked(api.listCspInheritedComponents).mockResolvedValueOnce({
      items: [{ id: 'component-a', cspProfileId: 'provider-a', name: 'Synthetic existing component',
        componentType: 'Service', sourceFormat: 'Package', status: 'Published', importedAt: '2026-09-23T12:00:00Z' }],
      total: 1, page: 1, pageSize: 200,
    });
    vi.mocked(api.archiveCspInheritedComponent).mockRejectedValueOnce(new Error('Archive permission denied.'));
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(true);
    render(<MemoryRouter><CspInheritedComponentsPage /></MemoryRouter>);
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Archive Synthetic existing component' }));
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Archive permission denied.');
    confirm.mockRestore();
  });

  it('hands import to the durable portal flow without issuing legacy synchronous ingestion', async () => {
    // Arrange
    render(<MemoryRouter initialEntries={['/csp/inherited-components']}><CurrentRoute /><CspInheritedComponentsPage /></MemoryRouter>);
    await screen.findByText('0 components');
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Import ATO documents' }));
    // Assert
    expect(screen.getByLabelText('Route')).toHaveTextContent('/workspaces/csp/security-capabilities/imports');
    expect(api.importCspInheritedComponents).not.toHaveBeenCalled();
    expect(screen.queryByLabelText('Import ATO documents')).not.toBeInTheDocument();
  });
});
