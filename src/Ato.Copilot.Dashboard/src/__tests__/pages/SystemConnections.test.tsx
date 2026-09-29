import { act, fireEvent, render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import '../helpers/dialog';
import SystemConnections from '../../features/systems/SystemConnections';
import InterconnectionDrawer from '../../features/systems/InterconnectionDrawer';
import * as api from '../../api/interconnections';
import type { ComponentProps } from 'react';

vi.mock('../../api/interconnections', () => ({
  listSystemInterconnections: vi.fn(), getSystemInterconnection: vi.fn(),
  createSystemInterconnection: vi.fn(), updateSystemInterconnection: vi.fn(),
}));
const external: api.SystemInterconnectionDetail = {
  id: 'connection-a', interconnectionId: 'connection-a', systemId: 'system-a',
  targetSystemName: 'Partner system', targetSystemOwner: 'Partner owner', targetSystemAcronym: 'PART',
  dataClassification: 'CUI', dataDescription: 'Mission records', authenticationMethod: 'mTLS',
  protocolsUsed: ['HTTPS'], portsUsed: ['443'], securityMeasures: ['Inspect TLS, retain logs'],
  interconnectionType: 'Api', dataFlowDirection: 'Bidirectional', canManageInterconnections: true,
  status: 'Proposed', statusReason: null, authorizationToConnect: false, hasAgreement: false, agreements: [],
  createdBy: 'Recorder', createdAt: '2026-09-28T12:00:00Z', modifiedAt: null,
};
const network = { id: 'pps-a', serviceName: 'User access', portOrRange: '443', protocol: 'TCP',
  direction: 'Inbound', justification: 'Mission access', sortOrder: 0 };
type Props = ComponentProps<typeof SystemConnections>;
const props = (overrides: Partial<Props> = {}): Props => ({
  systemId: 'system-a', profile: { draftContent: '{"ppsOverview":"Existing context","customSource":"retain"}',
    governanceStatus: 'Draft', ppsEntries: [network, { ...network, id: 'pps-b', serviceName: 'SSH', sortOrder: 1 }] },
  profileReadOnly: false, profileError: null, saving: false, addOpen: false,
  onAddClose: vi.fn(), onCanAddChange: vi.fn(), onSaveProfile: vi.fn().mockResolvedValue(true), ...overrides,
});
beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(api.listSystemInterconnections).mockResolvedValue({
    items: [external], total: 1, page: 1, pageSize: 50, canManageInterconnections: true,
  });
  vi.mocked(api.getSystemInterconnection).mockResolvedValue(external);
});

describe('Network interfaces and external connections', () => {
  it('shows one register with explicit record types and edits the exact profile row without changing siblings', async () => {
    // Arrange
    const input = props();
    render(<MemoryRouter><SystemConnections {...input} /></MemoryRouter>);
    await screen.findByRole('cell', { name: 'Partner system' });
    expect(screen.getAllByRole('table')).toHaveLength(1);
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Open network interface User access' }));
    fireEvent.change(screen.getByLabelText('Ports or range'), { target: { value: '8443' } });
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Save network interface' })); });
    // Assert
    await vi.waitFor(() => expect(input.onSaveProfile).toHaveBeenCalledExactlyOnceWith(input.profile.draftContent,
      [{ ...network, portOrRange: '8443' }, input.profile.ppsEntries[1]]));
    expect(api.updateSystemInterconnection).not.toHaveBeenCalled();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('reads every external list page instead of silently truncating the register', async () => {
    // Arrange
    vi.mocked(api.listSystemInterconnections)
      .mockResolvedValueOnce({ items: [external], total: 2, page: 1, pageSize: 1, canManageInterconnections: true })
      .mockResolvedValueOnce({ items: [{ ...external, id: 'connection-b', targetSystemName: 'Second partner' }],
        total: 2, page: 2, pageSize: 1, canManageInterconnections: true });
    // Act
    render(<MemoryRouter><SystemConnections {...props()} /></MemoryRouter>);
    // Assert
    expect(await screen.findByRole('cell', { name: 'Second partner' })).toBeVisible();
    expect(api.listSystemInterconnections).toHaveBeenLastCalledWith('system-a', expect.any(AbortSignal), 2, 1);
  });

  it('shows a source failure explicitly without declaring there are no connections', async () => {
    // Arrange
    vi.mocked(api.listSystemInterconnections).mockRejectedValue(new Error('External source unavailable'));
    // Act
    render(<MemoryRouter><SystemConnections {...props()} /></MemoryRouter>);
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('External source unavailable');
    expect(screen.getByRole('cell', { name: 'User access' })).toBeVisible();
    expect(screen.queryByText(/No network interfaces or interconnections are recorded/)).not.toBeInTheDocument();
  });

  it('keeps profile permission and external-management permission separate in the add drawer', async () => {
    // Arrange
    vi.mocked(api.listSystemInterconnections).mockResolvedValue({
      items: [external], total: 1, page: 1, pageSize: 50, canManageInterconnections: false,
    });
    const input = props({ addOpen: true, profileReadOnly: true });
    // Act
    render(<MemoryRouter><SystemConnections {...input} /></MemoryRouter>);
    // Assert
    await screen.findByRole('cell', { name: 'Partner system' });
    expect(screen.getByRole('button', { name: 'Add network interface' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Add interconnection' })).toBeDisabled();
    expect(input.onCanAddChange).toHaveBeenLastCalledWith(false);
  });
  it('can record an external connection without unlocking an under-review network profile', async () => {
    // Arrange
    const input = props({ addOpen: true, profileReadOnly: true });
    // Act
    render(<MemoryRouter><SystemConnections {...input} /></MemoryRouter>);
    await screen.findByRole('cell', { name: 'Partner system' });
    // Assert
    expect(screen.getByRole('button', { name: 'Add network interface' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Add interconnection' })).toBeEnabled();
    expect(input.onCanAddChange).toHaveBeenLastCalledWith(true);
  });
});

describe('External interconnection editor', () => {
  it('loads canonical details and updates only editable fields on the selected record', async () => {
    // Arrange
    const saved = vi.fn();
    const close = vi.fn();
    vi.mocked(api.getSystemInterconnection).mockResolvedValue({ ...external, targetSystemName: 'Current partner' });
    vi.mocked(api.updateSystemInterconnection).mockResolvedValue({ ...external, targetSystemName: 'Updated partner', portsUsed: ['443', '8443'] });
    render(<MemoryRouter><InterconnectionDrawer systemId="system-a" item={external} canCreate onSaved={saved} onClose={close} /></MemoryRouter>);
    await screen.findByDisplayValue('Current partner');
    // Act
    fireEvent.change(screen.getByLabelText('External system'), { target: { value: 'Updated partner' } });
    fireEvent.change(screen.getByLabelText('Ports (one per line)'), { target: { value: '443\n8443' } });
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Save interconnection' })); });
    // Assert
    await vi.waitFor(() => expect(api.updateSystemInterconnection).toHaveBeenCalledWith('system-a', 'connection-a', {
      targetSystemName: 'Updated partner', targetSystemOwner: 'Partner owner', targetSystemAcronym: 'PART',
      dataClassification: 'CUI', dataDescription: 'Mission records', authenticationMethod: 'mTLS',
      protocolsUsed: ['HTTPS'], portsUsed: ['443', '8443'], securityMeasures: ['Inspect TLS, retain logs'],
      interconnectionType: 'Api', dataFlowDirection: 'Bidirectional',
    }));
    expect(saved).toHaveBeenCalledOnce();
    expect(close).toHaveBeenCalledOnce();
  });

  it('uses freshly returned permissions for read-only inspection', async () => {
    // Arrange
    vi.mocked(api.getSystemInterconnection).mockResolvedValue({ ...external, canManageInterconnections: false });
    // Act
    render(<MemoryRouter><InterconnectionDrawer systemId="system-a" item={external} canCreate onSaved={vi.fn()} onClose={vi.fn()} /></MemoryRouter>);
    await screen.findByDisplayValue('Partner system');
    // Assert
    expect(screen.getByLabelText('External system')).toBeDisabled();
    expect(screen.queryByRole('button', { name: 'Save interconnection' })).not.toBeInTheDocument();
    expect(api.updateSystemInterconnection).not.toHaveBeenCalled();
  });

  it('retains failed input and blocks edits and Escape while a save is pending', async () => {
    // Arrange
    let reject!: (reason: Error) => void;
    vi.mocked(api.updateSystemInterconnection).mockReturnValue(new Promise((_resolve, fail) => { reject = fail; }));
    const close = vi.fn();
    render(<MemoryRouter><InterconnectionDrawer systemId="system-a" item={external} canCreate onSaved={vi.fn()} onClose={close} /></MemoryRouter>);
    await screen.findByDisplayValue('Partner system');
    fireEvent.change(screen.getByLabelText('System owner'), { target: { value: 'Retain my edit' } });
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Save interconnection' }));
    fireEvent(screen.getByRole('dialog'), new Event('cancel', { cancelable: true }));
    // Assert
    expect(screen.getByRole('dialog')).toHaveAttribute('aria-busy', 'true');
    expect(screen.getByLabelText('System owner')).toBeDisabled();
    expect(close).not.toHaveBeenCalled();
    // Act
    await act(async () => reject(new Error('Update rejected')));
    // Assert
    expect(screen.getByLabelText('System owner')).toHaveValue('Retain my edit');
    expect(screen.getByRole('alert')).toHaveTextContent('Update rejected');
    expect(close).not.toHaveBeenCalled();
  });

  it('does not offer an empty editor after a failed detail read', async () => {
    // Arrange
    vi.mocked(api.getSystemInterconnection).mockRejectedValue(new Error('Detail unavailable'));
    // Act
    render(<MemoryRouter><InterconnectionDrawer systemId="system-a" item={external} canCreate onSaved={vi.fn()} onClose={vi.fn()} /></MemoryRouter>);
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Detail unavailable');
    expect(within(screen.getByRole('dialog')).queryByRole('textbox')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Retry interconnection' })).toBeVisible();
  });
});
