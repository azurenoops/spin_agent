import { beforeEach, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { WorkspaceNavigationProvider } from '../../features/workspaces/workspaceNavigation';
import { SourceReviewRedirect } from '../../features/provider-authorizations/SourceReviewRedirect';
import { getPackageCandidates } from '../../features/package-imports/api';
import { candidate, page } from '../package-imports/fixtures';

vi.mock('../../features/package-imports/api', () => ({ getPackageCandidates: vi.fn() }));
function Probe() {
  const location = useLocation();
  return <output>{location.pathname}{location.search}</output>;
}
const mount = () => render(<MemoryRouter initialEntries={['/workspaces/csp/authorizations/offerings/offering/packages/package/candidates/wanted?type=Capability']}>
  <WorkspaceNavigationProvider workspace={{ kind: 'csp' }}>
    <Probe /><SourceReviewRedirect offeringId="offering" packageId="package" candidateId="wanted" />
  </WorkspaceNavigationProvider>
</MemoryRouter>);
beforeEach(() => vi.clearAllMocks());
it('opens the canonical reviewer at the actual candidate page, not an invented first-page match', async () => {
  // Arrange
  vi.mocked(getPackageCandidates).mockResolvedValueOnce(page([candidate()], 1, 26))
    .mockResolvedValueOnce(page([candidate({ candidateId: 'wanted' })], 2, 26));
  // Act
  mount();
  // Assert
  expect(await screen.findByText('/workspaces/csp/authorizations/offerings/offering/packages/package?page=2&candidate=wanted')).toBeInTheDocument();
  expect(getPackageCandidates).toHaveBeenLastCalledWith('package', { page: 2, pageSize: 25 }, expect.any(AbortSignal));
});
it('shows an unavailable candidate instead of opening another record', async () => {
  // Arrange
  vi.mocked(getPackageCandidates).mockResolvedValue(page([]));
  // Act
  mount();
  // Assert
  expect(await screen.findByRole('alert')).toHaveTextContent('This source record is unavailable in the retained package.');
});
