import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import PoamManagement from '../../pages/PoamManagement';
import DeviationsPage from '../../pages/DeviationsPage';
import * as deviations from '../../api/deviations';
import type { CreatePoamRequest } from '../../types/poam';

const calls = vi.hoisted(() => ({ create: vi.fn(), refreshList: vi.fn(), refreshMetrics: vi.fn() }));
vi.mock('../../components/layout/SystemLayout', () => ({ useSystemContext: () => ({ detail: { systemId: 'a' } }) }));
vi.mock('../../components/permissions/useSystemMutationPermission', () => ({ useSystemMutationPermission: () => true }));
vi.mock('../../hooks/usePoam', () => ({
  usePoamList: () => ({ data: { items: [], totalCount: 0 }, loading: false, error: null, refresh: calls.refreshList }),
  usePoamMetrics: () => ({ data: null, loading: false, error: null, refresh: calls.refreshMetrics }),
  useCreatePoam: () => ({ create: calls.create, loading: false }),
}));
vi.mock('../../hooks/usePoamQueue', () => ({
  usePoamQueue: () => ({ data: { items: [], totalCount: 0, counts: { all: 0, overdue: 0, readyToVerify: 0, closed: 0 } }, loading: false, error: null, refresh: calls.refreshList }),
}));
vi.mock('../../components/poam/PoamCreateForm', () => ({
  default: ({ onSubmit }: { onSubmit: (request: CreatePoamRequest) => Promise<void> }) =>
    <button onClick={() => void onSubmit({ weakness: 'Recorded weakness', controlId: 'AC-2' } as CreatePoamRequest)}>Submit existing POAM form</button>,
}));
vi.mock('../../components/poam/PoamDetailDrawer', () => ({ default: () => <div>Created commitment detail</div> }));
vi.mock('../../api/deviations', () => ({ getDeviations: vi.fn(), getDeviationSummary: vi.fn() }));
vi.mock('../../components/AddDeviationDialog', () => ({ default: ({ systemId }: { systemId: string }) => <div role="dialog">Exception request for {systemId}</div> }));

describe('System risk tasks', () => {
  beforeEach(() => { vi.clearAllMocks(); calls.create.mockResolvedValue({ id: 'poam-a' }); });
  it('refreshes the complete POAM queue after an accepted creation', async () => {
    // Arrange
    render(<MemoryRouter><PoamManagement /></MemoryRouter>);
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Add POA&M' }));
    fireEvent.click(screen.getByRole('button', { name: 'Submit existing POAM form' }));
    // Assert
    await waitFor(() => expect(calls.refreshList).toHaveBeenCalled());
    expect(calls.create).toHaveBeenCalledWith('a', expect.objectContaining({ controlId: 'AC-2' }));
    expect(screen.getByRole('heading', { name: 'Track remediation commitments' })).toBeVisible();
  });
  it('shows failed exception retrieval instead of an empty successful register', async () => {
    // Arrange
    vi.mocked(deviations.getDeviations).mockRejectedValue(new Error('Exception service unavailable'));
    vi.mocked(deviations.getDeviationSummary).mockResolvedValue({} as Awaited<ReturnType<typeof deviations.getDeviationSummary>>);
    // Act
    render(<MemoryRouter initialEntries={['/systems/a/deviations']}><Routes><Route path="/systems/:id/deviations" element={<DeviationsPage />} /></Routes></MemoryRouter>);
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Exception service unavailable');
    expect(screen.getByRole('button', { name: 'Retry exceptions' })).toBeVisible();
    fireEvent.click(screen.getByRole('button', { name: 'Request exception' }));
    expect(screen.getByRole('dialog')).toHaveTextContent('Exception request for a');
  });
});
