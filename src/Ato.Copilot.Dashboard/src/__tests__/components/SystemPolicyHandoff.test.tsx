import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import SystemComponents from '../../components/wizard/steps/SystemComponents';
import * as api from '../../api/components';

vi.mock('../../api/components', () => ({ getComponents: vi.fn(), createComponent: vi.fn(), listComponents: vi.fn(), assignToSystem: vi.fn() }));
const source = { id: 'policy-a', name: 'Access policy', componentType: 'Policy', status: 'Active',
  createdAt: '2026-09-28', systemAssignments: [], capabilityLinks: [] };
beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(api.getComponents).mockResolvedValue({ systemId: 'system-a', items: [], totalCount: 0, nextCursor: null,
    summary: { personCount: 0, placeCount: 0, thingCount: 0, policyCount: 0, totalCount: 0 } });
  vi.mocked(api.listComponents).mockResolvedValue({ items: [source], totalCount: 1, page: 1, pageSize: 50 });
});
afterEach(cleanup);
const mount = () => render(<MemoryRouter><SystemComponents systemId="system-a" onNext={vi.fn()} onErrors={vi.fn()} /></MemoryRouter>);
it('hands policy assignment to the rationale workflow without a legacy write', async () => {
  // Arrange / Act
  mount();
  // Assert
  const link = await screen.findByRole('link', { name: /Add policy reference/ });
  expect(link).toHaveAttribute('href', '/systems/system-a/legal?policyAction=add&policySource=policy-a');
  expect(link).toHaveAttribute('target', '_blank');
  expect(api.assignToSystem).not.toHaveBeenCalled();
});
it('opens an existing policy reference rather than another assignment form', async () => {
  // Arrange
  vi.mocked(api.listComponents).mockResolvedValue({ items: [{ ...source, systemAssignments: [{ id: 'ref-a', registeredSystemId: 'system-a' }] }],
    totalCount: 1, page: 1, pageSize: 50 });
  // Act
  mount();
  // Assert
  expect(await screen.findByRole('link', { name: /Review policy reference/ })).toHaveAttribute('href', '/systems/system-a/legal?reference=ref-a');
});
it('preserves ordinary non-policy assignment behavior', async () => {
  // Arrange
  vi.mocked(api.listComponents).mockResolvedValue({ items: [{ ...source, id: 'thing-a', name: 'Firewall', componentType: 'Thing' }],
    totalCount: 1, page: 1, pageSize: 50 });
  mount();
  // Act
  fireEvent.click(await screen.findByRole('button', { name: /^Add$/ }));
  // Assert
  await waitFor(() => expect(api.assignToSystem).toHaveBeenCalledWith('thing-a', { registeredSystemId: 'system-a' }));
});
