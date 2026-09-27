import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import '../helpers/dialog';
import SystemInterconnections from '../../features/systems/SystemInterconnections';
import { getSystemDocuments, type SystemDocumentsResponse } from '../../api/documents';
import { addInterconnection } from '../../api/systemDetail';
const permissions = vi.hoisted(() => ({ allowed: true }));
vi.mock('../../api/documents', () => ({ getSystemDocuments: vi.fn() }));
vi.mock('../../api/systemDetail', () => ({ addInterconnection: vi.fn() }));
vi.mock('../../components/permissions/useSystemMutationPermission', () => ({ useSystemMutationPermission: () => permissions.allowed }));
describe('System interconnection register', () => {
  beforeEach(() => {
    vi.clearAllMocks(); permissions.allowed = true;
    vi.mocked(getSystemDocuments).mockResolvedValue({ systemId: 'a', interconnections: [{
      interconnectionId: 'connection-a', targetSystem: 'Partner system', direction: 'Inbound', status: 'Proposed',
      hasAgreement: false, agreementType: null, agreementStatus: null,
    }] } as SystemDocumentsResponse);
  });
  it('lists recorded agreements and creates a distinct interconnection through the real system action', async () => {
    // Arrange
    vi.mocked(addInterconnection).mockResolvedValue({ interconnectionId: 'connection-b', targetSystemName: 'Log service', direction: 'Outbound', status: 'Proposed' });
    render(<MemoryRouter><SystemInterconnections systemId="a" /></MemoryRouter>);
    await screen.findByText('Partner system');
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Add interconnection' }));
    fireEvent.change(screen.getByLabelText('External system'), { target: { value: 'Log service' } });
    fireEvent.change(screen.getByLabelText('Data flow direction'), { target: { value: 'Outbound' } });
    fireEvent.click(screen.getByRole('button', { name: 'Record interconnection' }));
    // Assert
    await waitFor(() => expect(addInterconnection).toHaveBeenCalledWith('a', expect.objectContaining({ remoteSystem: 'Log service', direction: 'Outbound' })));
    expect(await screen.findByRole('status')).toHaveTextContent('Proposed');
  });
  it('keeps failed reads distinct from a certified absence of interconnections', async () => {
    // Arrange
    vi.mocked(getSystemDocuments).mockRejectedValue(new Error('Interconnection records unavailable'));
    // Act
    render(<MemoryRouter><SystemInterconnections systemId="a" /></MemoryRouter>);
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Interconnection records unavailable');
    expect(screen.queryByText('No interconnections are recorded.')).not.toBeInTheDocument();
  });
  it('does not grant interconnection management from profile editing alone', async () => {
    // Arrange
    permissions.allowed = false;
    // Act
    render(<MemoryRouter><SystemInterconnections systemId="a" /></MemoryRouter>);
    // Assert
    expect(await screen.findByRole('button', { name: 'Add interconnection' })).toBeDisabled();
    expect(addInterconnection).not.toHaveBeenCalled();
  });
});
