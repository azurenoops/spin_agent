import { cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import ComponentLibrary from '../../pages/ComponentLibrary';
import * as api from '../../api/components';

vi.mock('../../components/layout/PageLayout', () => ({ default: ({ children }: { children: React.ReactNode }) => <div>{children}</div> }));
vi.mock('../../components/layout/PageHero', () => ({ default: ({ title }: { title: string }) => <h1>{title}</h1> }));
vi.mock('../../api/components', () => ({
  listComponents: vi.fn(), createOrgComponent: vi.fn(), updateOrgComponent: vi.fn(),
  deleteOrgComponent: vi.fn(), getComponentImpactPreview: vi.fn(),
}));
vi.mock('../../features/onboarding/api/onboardingApi', () => ({ onboarding: { listAzureRegistrations: vi.fn().mockResolvedValue([]) } }));
vi.mock('../../features/csp-inherited-components/api', () => ({
  listCspInheritedComponents: vi.fn().mockResolvedValue({ items: [], totalCount: 0 }),
  isUnavailable: () => false,
}));
beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(api.listComponents).mockResolvedValue({ items: [{ id: 'source-a', name: 'Access policy', componentType: 'Policy',
    status: 'Active', createdAt: '2026-09-28', capabilityLinks: [], systemAssignments: [] }], totalCount: 1, page: 1, pageSize: 25 });
  vi.mocked(api.getComponentImpactPreview).mockResolvedValue({ totalNarratives: 0, totalSystems: 0, customSkipped: 0, bySystem: [] });
});
afterEach(cleanup);
it('opens the organization policy library with its Policy filter', async () => {
  // Arrange / Act
  render(<MemoryRouter initialEntries={['/components?type=Policy']}><ComponentLibrary /></MemoryRouter>);
  // Assert
  expect(await screen.findByRole('heading', { name: 'Access policy' })).toBeVisible();
  expect(api.listComponents).toHaveBeenCalledWith(expect.objectContaining({ type: 'Policy' }));
});
it('shows the server protection reason instead of silently ignoring shared-policy deletion', async () => {
  // Arrange
  vi.mocked(api.deleteOrgComponent).mockRejectedValue({ error: 'Unlink retained system references before deleting the shared policy.' });
  render(<MemoryRouter initialEntries={['/components?type=Policy']}><ComponentLibrary /></MemoryRouter>);
  fireEvent.click(await screen.findByTitle('Delete'));
  // Act
  const confirmation = screen.getByRole('heading', { name: 'Delete Component?' }).parentElement!;
  fireEvent.click(within(confirmation).getByRole('button', { name: 'Delete' }));
  // Assert
  expect(await screen.findByRole('alert')).toHaveTextContent('Unlink retained system references');
  expect(screen.getByRole('heading', { name: 'Access policy' })).toBeVisible();
});
