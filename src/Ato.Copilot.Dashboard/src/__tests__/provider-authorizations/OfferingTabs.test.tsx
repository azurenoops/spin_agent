import type { ReactNode } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { AuthorizationsPage } from '../../features/provider-authorizations/AuthorizationsPage';
import { WorkspaceNavigationProvider } from '../../features/workspaces/workspaceNavigation';
import * as api from '../../features/provider-authorizations/api';
import { offering } from './testData';
import { offeringOverview } from './overviewFixtures';
import { page } from '../package-imports/fixtures';

vi.mock('../../components/layout/PageLayout', () => ({ default: ({ children }: { children: ReactNode }) => <main>{children}</main> }));
vi.mock('../../features/workspaces/WorkspaceBoundary', () => ({ useWorkspaceSession: () => ({
  target: { kind: 'csp' }, workspace: { permissions: { canAccessCsp: true } },
}) }));
vi.mock('../../features/provider-authorizations/OfferingOverview', () => ({ OfferingOverview: () => <div>Overview records</div> }));
vi.mock('../../features/provider-authorizations/HostingSetupPage', () => ({ HostingSetupPage: () => <div>Hosting records</div> }));
vi.mock('../../features/provider-authorizations/FindingsPage', () => ({ FindingsPage: () => <div>Finding records</div> }));
vi.mock('../../features/provider-authorizations/api', async original => ({
  ...await original<typeof api>(), getOffering: vi.fn(), getOfferingOverview: vi.fn(), listPackageVersions: vi.fn(), getBoundaryOverview: vi.fn(),
}));

const sections = [
  ['', 'Overview'], ['inherited-coverage?task=capabilities', 'Capabilities'], ['boundary', 'Scope & duties'],
  ['packages', 'Sources & findings'], ['release', 'Release & changes'], ['mission-use', 'Mission use'],
] as const;

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(api.getOffering).mockResolvedValue(offering);
  vi.mocked(api.getOfferingOverview).mockResolvedValue({
    ...offeringOverview(), packages: { ...offeringOverview().packages, items: [], total: 0 },
  });
  vi.mocked(api.listPackageVersions).mockResolvedValue(page([]));
  vi.mocked(api.getBoundaryOverview).mockResolvedValue({ offeringId: offering.offeringId, offeringRevision: offering.revision,
    capabilities: { ...page([]), published: 0, awaitingReview: 0 }, missionSystems: page([]) });
});

describe('exact mock offering navigation', () => {
  it.each(sections)('renders the approved focused sections with the correct active link at %s', async (path, active) => {
    // Arrange / Act
    render(<MemoryRouter initialEntries={[api.authorizationHref(offering.offeringId, path)]}>
      <WorkspaceNavigationProvider workspace={{ kind: 'csp' }}><AuthorizationsPage /></WorkspaceNavigationProvider>
    </MemoryRouter>);
    const breadcrumb = await screen.findByRole('navigation', { name: 'Offering breadcrumb' });
    // Assert
    await waitFor(() => expect(breadcrumb).toHaveTextContent(`Provider / Offerings / ${offering.name}`));
    const links = within(screen.getByRole('navigation', { name: 'Offering sections' })).getAllByRole('link');
    expect(links.map(link => link.textContent)).toEqual(sections.map(([, label]) => label));
    expect(links.filter(link => link.getAttribute('aria-current') === 'page')).toHaveLength(1);
    expect(links.find(link => link.textContent === active)).toHaveAttribute('aria-current', 'page');
    for (const [index, [route]] of sections.entries()) expect(links[index]).toHaveAttribute('href', api.authorizationHref(offering.offeringId, route));
    expect(within(screen.getByRole('navigation', { name: 'Offering sections' })).queryByText('Change impact')).not.toBeInTheDocument();
    links[0]!.focus();
    fireEvent.keyDown(links[0]!, { key: 'ArrowRight' });
    expect(links[1]).toHaveFocus();
    fireEvent.keyDown(links[1]!, { key: 'ArrowLeft' });
    expect(links[0]).toHaveFocus();
    fireEvent.keyDown(links[0]!, { key: 'End' });
    expect(links[5]).toHaveFocus();
    fireEvent.keyDown(links[5]!, { key: 'Home' });
    expect(links[0]).toHaveFocus();
  });
});
