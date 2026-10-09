import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, expect, it, vi } from 'vitest';
import * as api from '../../api/systemEnvironments';
import ProviderOfferingWorkflowContext from '../../features/systems/ProviderOfferingWorkflowContext';
vi.mock('../../api/systemEnvironments', () => ({ getSystemEnvironments: vi.fn() }));
const scope: api.SystemProviderScope = {
  assignmentId: 'assignment', assignmentVersion: 3, relationshipId: null, offeringId: 'offering',
  offeringName: 'Recorded service', providerName: 'Recorded provider', hostingScopeRevisionId: 'pinned',
  hostingScopeRevision: 8, hostingScopeName: 'Scope', state: 'Active', relationshipState: 'Undetermined',
  reviewRequired: true, assignedScopes: [], selectionVersion: 1,
};
const data: api.SystemEnvironmentsResponse = {
  systemId: 'system', version: 2, attachments: [], legacyReferences: [], providerScopes: [scope],
  permissions: { canManageEnvironments: false, canCheckAccess: false, canRunAssessments: false,
    canManageMonitoring: false, canRegisterSubscriptions: false },
};
const renderContext = (query = '?offeringId=offering&assignmentId=assignment&hostingScopeRevisionId=pinned') =>
  render(<MemoryRouter initialEntries={[`/systems/system/next${query}`]}>
    <ProviderOfferingWorkflowContext systemId="system">{item =>
      <p>{item ? `Verified ${item.offeringName}` : 'Ordinary workflow'}</p>}
    </ProviderOfferingWorkflowContext>
  </MemoryRouter>);
beforeEach(() => { vi.resetAllMocks(); vi.mocked(api.getSystemEnvironments).mockResolvedValue(data); });
it('retains and verifies the exact offering and captured release without selecting adoption or filtering the baseline', async () => {
  // Arrange / Act
  renderContext();
  // Assert
  expect(await screen.findByText('Verified Recorded service')).toBeVisible();
  expect(screen.getByText(/Captured scope release 8/)).toBeVisible();
  expect(api.getSystemEnvironments).toHaveBeenCalledWith('system', expect.any(AbortSignal));
});
it.each([
  { ...data, systemId: 'other-system' },
  { ...data, providerScopes: [{ ...scope, hostingScopeRevisionId: 'new-release' }] },
  { ...data, providerScopes: [{ ...scope, state: 'Removed' as const }] },
])('does not silently replace unavailable or changed route context', async response => {
  // Arrange
  vi.mocked(api.getSystemEnvironments).mockResolvedValue(response);
  // Act
  renderContext();
  // Assert
  expect(await screen.findByRole('alert')).toHaveTextContent('captured offering');
  expect(screen.queryByText(/Verified/)).not.toBeInTheDocument();
});
it('leaves ordinary workflow bookmarks unchanged', () => {
  // Arrange / Act
  renderContext('');
  // Assert
  expect(screen.getByText('Ordinary workflow')).toBeVisible();
  expect(api.getSystemEnvironments).not.toHaveBeenCalled();
});
it('surfaces failed source reads and permits an explicit retry', async () => {
  // Arrange
  vi.mocked(api.getSystemEnvironments).mockRejectedValueOnce(new Error('Context denied')).mockResolvedValue(data);
  // Act
  renderContext();
  // Assert
  expect(await screen.findByRole('alert')).toHaveTextContent('Context denied');
  expect(screen.queryByText(/Verified/)).not.toBeInTheDocument();
  // Act
  fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
  // Assert
  expect(await screen.findByText('Verified Recorded service')).toBeVisible();
});
