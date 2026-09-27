import { beforeEach, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import type { ReactNode } from 'react';
import { AuthorizationsPage } from '../../features/provider-authorizations/AuthorizationsPage';
import { WorkspaceNavigationProvider } from '../../features/workspaces/workspaceNavigation';
import * as api from '../../features/provider-authorizations/api';
import * as hostingApi from '../../features/provider-authorizations/hostingApi';
import { offering } from './testData';
import { recordedAuthorization } from './overviewFixtures';
import { page } from '../package-imports/fixtures';

vi.mock('../../components/layout/PageLayout', () => ({ default: ({ children }: { children: ReactNode }) => <main>{children}</main> }));
vi.mock('../../features/workspaces/WorkspaceBoundary', () => ({ useWorkspaceSession: () => ({
  target: { kind: 'csp' }, workspace: { permissions: { canAccessCsp: true } },
}) }));
vi.mock('../../features/provider-authorizations/api', async original => ({
  ...await original<typeof api>(), getOffering: vi.fn(), getDecision: vi.fn(), listDecisions: vi.fn(),
}));
vi.mock('../../features/provider-authorizations/hostingApi', async original => ({
  ...await original<typeof hostingApi>(), getHostingScope: vi.fn(), listHostingScopes: vi.fn(),
}));
const mount = (route: string) => render(<MemoryRouter initialEntries={[api.authorizationHref(offering.offeringId, route)]}>
  <WorkspaceNavigationProvider workspace={{ kind: 'csp' }}><AuthorizationsPage /></WorkspaceNavigationProvider>
</MemoryRouter>);
beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(api.getOffering).mockResolvedValue(offering);
  vi.mocked(api.getDecision).mockResolvedValue(recordedAuthorization);
  vi.mocked(hostingApi.listHostingScopes).mockResolvedValue(page([]));
});
it('opens the exact decision snapshot at its direct route without a substitute record', async () => {
  // Arrange
  mount('decisions/decision-1');
  // Act
  await screen.findByRole('region', { name: 'Selected decision' });
  // Assert
  expect(screen.getByText(recordedAuthorization.snapshotHash)).toBeVisible();
  expect(screen.getByRole('button', { name: 'Revise draft' })).toBeInTheDocument();
  expect(api.getDecision).toHaveBeenCalledWith(offering.offeringId, 'decision-1', expect.any(AbortSignal));
  expect(api.listDecisions).not.toHaveBeenCalled();
});
it('shows missing decision identity as unavailable instead of rendering a different decision', async () => {
  // Arrange
  vi.mocked(api.getDecision).mockResolvedValue(null);
  // Act
  mount('decisions/missing');
  // Assert
  expect(await screen.findByRole('alert')).toHaveTextContent('Decision unavailable in this offering');
  expect(screen.queryByRole('button', { name: 'Revise draft' })).not.toBeInTheDocument();
});
it('loads the exact prior scope into the direct scope-proposal form', async () => {
  // Arrange
  vi.mocked(api.getOffering).mockResolvedValue({ ...offering, currentHostingScopeRevisionId: 'scope-1' });
  vi.mocked(hostingApi.getHostingScope).mockResolvedValue({
    offeringId: offering.offeringId, offeringRevision: offering.revision,
    snapshot: { revisionId: 'scope-1', revision: 2, snapshotHash: 'prior-hash' },
    impactReviewId: null, predecessorRevisionId: null, name: 'Recorded service scope',
    permittedScopes: [], exclusions: [], citations: [], purpose: 'Shared production resources', changeRationale: 'Prior reason',
  });
  // Act
  mount('inherited-coverage/propose');
  // Assert
  expect(await screen.findByRole('heading', { name: 'Propose a scope update', level: 1 })).toBeInTheDocument();
  expect(await screen.findByLabelText('Hosting scope name')).toHaveValue('Recorded service scope');
  expect(screen.getByLabelText('Purpose')).toHaveValue('Shared production resources');
  expect(screen.getByLabelText('Change rationale')).toHaveValue('');
  expect(hostingApi.getHostingScope).toHaveBeenCalledWith(offering.offeringId, 'scope-1', expect.any(AbortSignal));
});
