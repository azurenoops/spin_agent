import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import '../../helpers/dialog';
import AddDeviationDialog from '../../../components/AddDeviationDialog';
import { createDeviation } from '../../../api/deviations';
import { listPoamItems } from '../../../api/poam';
const access = vi.hoisted(() => ({ allowed: true }));
vi.mock('../../../components/permissions/useSystemMutationPermission', () => ({ useSystemMutationPermission: () => access.allowed }));
vi.mock('../../../api/deviations', () => ({ createDeviation: vi.fn() }));
vi.mock('../../../api/poam', () => ({ listPoamItems: vi.fn() }));

describe('POA&M exception request', () => {
  it('surfaces POA&M search errors instead of claiming there are no matching records', async () => {
    // Arrange
    access.allowed = true;
    vi.mocked(listPoamItems).mockRejectedValue({ error: 'POA&M search unavailable' });
    render(<MemoryRouter><AddDeviationDialog systemId="system-a" onCreated={vi.fn()} onClose={vi.fn()} /></MemoryRouter>);
    // Act
    fireEvent.change(screen.getByPlaceholderText('Search POA&M by control or weakness...'), { target: { value: 'AC' } });
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('POA&M search unavailable');
  });
  it('retains the POA&M and finding context in a real request without approving it', async () => {
    // Arrange
    access.allowed = true;
    vi.mocked(createDeviation).mockResolvedValue({ id: 'exception-a', status: 'Pending' });
    render(<MemoryRouter><AddDeviationDialog systemId="system-a" initialPoamEntryId="poam-a" initialFindingId="finding-a" initialControlId="AC-2" onCreated={vi.fn()} onClose={vi.fn()} /></MemoryRouter>);
    // Act
    fireEvent.click(screen.getByRole('button', { name: /Risk Acceptance/ }));
    fireEvent.change(screen.getByLabelText('Severity *'), { target: { value: 'CatII' } });
    fireEvent.change(screen.getByLabelText('Justification *'), { target: { value: 'Maintenance window needed' } });
    fireEvent.click(screen.getByRole('button', { name: 'Submit Deviation' }));
    // Assert
    await waitFor(() => expect(createDeviation).toHaveBeenCalledWith('system-a', expect.objectContaining({
      poamEntryId: 'poam-a', findingId: 'finding-a', deviationType: 'RiskAcceptance',
    })));
    expect(screen.getByRole('dialog', { name: 'Request exception' })).toBeInTheDocument();
    expect(screen.getByText(/does not accept risk or change deadlines/)).toBeInTheDocument();
  });
  it('disables an open request after permission revocation', () => {
    // Arrange
    access.allowed = true;
    const view = render(<MemoryRouter><AddDeviationDialog systemId="system-a" initialPoamEntryId="poam-a" onCreated={vi.fn()} onClose={vi.fn()} /></MemoryRouter>);
    // Act
    access.allowed = false;
    view.rerender(<MemoryRouter><AddDeviationDialog systemId="system-a" initialPoamEntryId="poam-a" onCreated={vi.fn()} onClose={vi.fn()} /></MemoryRouter>);
    // Assert
    expect(screen.getByRole('button', { name: 'Submit Deviation' })).toBeDisabled();
    expect(screen.getByRole('alert')).toHaveTextContent(/permission/i);
  });
});
