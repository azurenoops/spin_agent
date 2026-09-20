import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import AuthorizationBoundaries from '../../../../components/wizard/steps/AuthorizationBoundaries';

vi.mock('../../../../api/boundaries', () => ({
  fetchBoundaryDefinitions: vi.fn(),
  createBoundaryDefinition: vi.fn(),
  listBoundaryComponentCandidates: vi.fn(),
  assignComponent: vi.fn(),
  listBoundaryComponents: vi.fn(),
  addBoundaryResource: vi.fn(),
}));

vi.mock('../../../../api/components', () => ({
  getComponents: vi.fn().mockResolvedValue({ items: [] }),
  listComponents: vi.fn().mockResolvedValue({ items: [] }),
}));

import * as boundaryApi from '../../../../api/boundaries';

const mockFetchBoundaries = boundaryApi.fetchBoundaryDefinitions as ReturnType<typeof vi.fn>;
const mockListCandidates = boundaryApi.listBoundaryComponentCandidates as ReturnType<typeof vi.fn>;
const mockListAssignments = boundaryApi.listBoundaryComponents as ReturnType<typeof vi.fn>;
const mockAssignComponent = boundaryApi.assignComponent as ReturnType<typeof vi.fn>;

describe('AuthorizationBoundaries component picker', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockFetchBoundaries.mockResolvedValue([
      { id: 'boundary-1', name: 'Primary', boundaryType: 'Logical', isPrimary: true },
    ]);
    mockListAssignments.mockResolvedValue({ items: [], totalCount: 0, page: 1, pageSize: 200 });
    mockListCandidates.mockResolvedValue([
      { id: 'org-1', name: 'Organization Policy', componentType: 'Policy', description: null, source: 'Organization' },
      { id: 'system-1', name: 'System Workload', componentType: 'Thing', description: null, source: 'System' },
      { id: 'csp-1', name: 'Azure Key Vault', componentType: 'Thing', description: null, source: 'CSP' },
    ]);
    mockAssignComponent.mockResolvedValue({});
  });

  it('shows unified candidates with provenance and sends the selected source', async () => {
    // Arrange
    render(<AuthorizationBoundaries systemId="system-1" onNext={vi.fn()} onErrors={vi.fn()} />);

    // Act
    fireEvent.click((await screen.findAllByText('Primary'))[0]!);

    // Assert
    expect(await screen.findByText('Organization Policy')).toBeInTheDocument();
    expect(screen.getByText('System Workload')).toBeInTheDocument();
    expect(screen.getByText('Azure Key Vault')).toBeInTheDocument();
    expect(screen.getByText('Organization')).toBeInTheDocument();
    expect(screen.getByText('System')).toBeInTheDocument();
    expect(screen.getByText('CSP')).toBeInTheDocument();
    expect(screen.queryByText('Person')).not.toBeInTheDocument();

    const cspRow = screen.getByText('Azure Key Vault').closest('div.flex');
    fireEvent.click(cspRow!.querySelector('button')!);

    await waitFor(() => {
      expect(mockAssignComponent).toHaveBeenCalledWith('system-1', 'boundary-1', {
        componentId: 'csp-1',
        source: 'CSP',
        isInScope: true,
      });
    });
  });
});