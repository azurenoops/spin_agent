import { act,fireEvent,render,screen,waitFor,within } from '@testing-library/react';
import type { ReactNode } from 'react';
import { createPortal } from 'react-dom';
import { beforeEach,describe,expect,it,vi } from 'vitest';
import SystemProfile,{ computeIsReadOnly } from '../../pages/SystemProfile';
import type { ProfileSectionDetail,ProfileSectionType } from '../../types/dashboard';
vi.mock('../../features/system-design/UnsavedDesignGuard', () => ({ default: () => null }));
vi.mock('../../features/systems/SystemOperationalStatus', () => ({
  default: () => <p>Operational source status</p>,
}));
vi.mock('../../features/systems/ConnectedSystemEnvironments', () => ({
  default: () => <><h2>Provider services &amp; scopes</h2><h2>System subscriptions</h2></>,
}));

const state=vi.hoisted(() => ({ systemId: 'system-a',sectionType: 'MissionAndPurpose',role: '' }));
const workspace = vi.hoisted(() => ({ value: null as { roles: string[] } | null }));
vi.mock('../../features/workspaces/WorkspaceBoundary', () => ({ useWorkspaceSession: () => workspace.value }));
const api=vi.hoisted(() => ({
  getProfileSection: vi.fn(),getProfileCompleteness: vi.fn(),saveProfileSection: vi.fn(),
  submitSections: vi.fn(),withdrawSections: vi.fn(),reviewSection: vi.fn(),
}));
const getSystemRoles = vi.hoisted(() => vi.fn());
vi.mock('../../api/roles', () => ({ rolesApi: { getSystemRoles } }));
vi.mock('../../features/workspace-operations/SetupDialog', () => ({
  default: ({ title, description, children }: { title: string; description?: string; children: ReactNode }) =>
    createPortal(<div role="dialog" aria-label={title}>{description && <p>{description}</p>}{children}</div>, document.body),
}));
vi.mock('react-router-dom',() => ({
  useParams: () => ({ sectionType: state.sectionType, id: state.systemId }),
  useLocation: () => ({ pathname: `/systems/${state.systemId}/profile/${state.sectionType}`, hash: '', search: '' }),
  Link: ({ to, children, ...props }: { to: string; children: ReactNode }) => <a href={to} {...props}>{children}</a>,
}));
vi.mock('../../components/layout/SystemLayout',() => ({ useSystemContext: () => ({ detail: { systemId: state.systemId } }) }));
vi.mock('../../hooks/useSettings',() => ({ useSettings: () => ({ settings: { role: state.role } }) }));
vi.mock('../../api/systemProfile',() => api);
vi.mock('../../api/documents', () => ({ getSystemDocuments: async () => ({ systemId: state.systemId, interconnections: [] }) }));
vi.mock('../../api/interconnections', () => ({
  listSystemInterconnections: async () => ({ items: [], total: 0, page: 1, pageSize: 50, canManageInterconnections: false }),
  getSystemInterconnection: vi.fn(), createSystemInterconnection: vi.fn(), updateSystemInterconnection: vi.fn(),
}));

function section(canEditProfile?: boolean,governanceStatus='NotStarted') {
  return {
    id: 'section-a',sectionType: state.sectionType,governanceStatus,canEditProfile,
    draftContent: null,userCategories: [],dataTypeEntries: [],ppsEntries: [],leveragedAuthorizations: [],
  } as unknown as ProfileSectionDetail;
}

beforeEach(() => {
  vi.resetAllMocks();
  Object.assign(state,{ systemId: 'system-a',sectionType: 'MissionAndPurpose',role: '' });
  workspace.value = null;
  api.getProfileSection.mockResolvedValue(section(false));
  api.getProfileCompleteness.mockResolvedValue({ statusCounts: {},totalSections: 5,approvedPercentage: 0 });
  getSystemRoles.mockResolvedValue({ systemId: state.systemId, roles: [{ role: 'SystemOwner', person: { id: 'owner-a', displayName: 'Recorded Owner' }, source: 'override' }] });
});

describe('server-authoritative profile editing (#968)',() => {
  it('opens Data context separately from individual information types', async () => {
    // Arrange
    state.sectionType = 'DataTypes';
    api.getProfileSection.mockResolvedValue(section(true, 'Draft'));
    render(<SystemProfile />);
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Manage information handling context' }));
    // Assert
    const dialog = screen.getByRole('dialog', { name: 'System-wide information handling context' });
    expect(within(dialog).getByLabelText('Data Overview')).toBeVisible();
    expect(within(dialog).getByLabelText('Highest Sensitivity Level')).toBeVisible();
    expect(screen.queryByText('Information handling context')).not.toBeInTheDocument();
    expect(api.saveProfileSection).not.toHaveBeenCalled();
  });
  it('places Data Save Draft at the top and opens Add data type beside the table without saving', async () => {
    // Arrange
    state.sectionType = 'DataTypes';
    api.getProfileSection.mockResolvedValue(section(true, 'Draft'));
    render(<SystemProfile />);
    await screen.findByRole('heading', { name: 'Data types & sensitivity' });
    // Act
    const header = screen.getByRole('banner');
    expect(within(header).getByRole('button', { name: 'Save Draft' })).toHaveAttribute('form', 'system-profile-editor');
    expect(within(header).getByRole('link', { name: 'Preview SSP contribution' })).toHaveAttribute('href',
      '/systems/system-a/documents/preview?contribution=DataTypes');
    fireEvent.click(screen.getByRole('button', { name: 'Add data type' }));
    // Assert
    expect(screen.getAllByRole('button', { name: 'Add data type' })).toHaveLength(1);
    expect(screen.getAllByRole('button', { name: 'Save Draft' })).toHaveLength(1);
    expect(screen.getByRole('dialog', { name: 'Add data type' })).toBeVisible();
    expect(api.saveProfileSection).not.toHaveBeenCalled();
    expect(screen.getByRole('link', { name: 'Preview contribution' })).toHaveAttribute('href', '/systems/system-a/documents/preview?contribution=DataTypes');
  });
  it('uses the shared Environment documentation sidebar below both page banners', async () => {
    // Arrange
    state.sectionType = 'EnvironmentAndDeployment';
    api.getProfileSection.mockResolvedValue(section(true, 'Draft'));
    // Act
    render(<SystemProfile />);
    await screen.findByRole('heading', { name: 'Environment & hosting' });
    // Assert
    expect(screen.queryByRole('region', { name: 'Documentation & review' })).not.toBeInTheDocument();
    const support = screen.getByRole('complementary', { name: 'Document contribution and next tasks' });
    expect(within(support).getByText('Used in your package')).toBeVisible();
    const guidance = screen.getByText(/Prepare for review: complete the applicable deployment/);
    expect(guidance.parentElement).toBe(support.parentElement!.parentElement);
    expect(guidance).not.toHaveTextContent('Expand Documentation details');
    expect(screen.getByRole('link', { name: 'Preview contribution' })).toBeVisible();
    expect(screen.getByRole('link', { name: 'View package readiness' })).toBeVisible();
  });
  it.each([true, false])('shows one Environment header save only with edit capability %s', async canEdit => {
    // Arrange
    state.sectionType = 'EnvironmentAndDeployment';
    api.getProfileSection.mockResolvedValue(section(canEdit, 'Draft'));
    // Act
    render(<SystemProfile />);
    await screen.findByRole('heading', { name: 'Environment & hosting' });
    // Assert
    expect(within(screen.getByRole('banner')).queryByRole('link', { name: 'Review hosting scope' })).not.toBeInTheDocument();
    const saves = screen.queryAllByRole('button', { name: 'Save Draft' });
    expect(saves).toHaveLength(canEdit ? 1 : 0);
    if (canEdit) expect(within(screen.getByRole('banner')).getByRole('button', { name: 'Save Draft' }))
      .toHaveAttribute('form', 'system-profile-editor');
    expect(screen.getAllByRole('heading', { name: 'Provider services & scopes' })).toHaveLength(1);
  });
  it.each(['Draft', 'NeedsRevision'])('omits Environment profile submission in %s without removing header save', async status => {
    // Arrange
    state.sectionType = 'EnvironmentAndDeployment';
    state.role = 'MissionOwner';
    api.getProfileSection.mockResolvedValue(section(true, status));
    // Act
    render(<SystemProfile />);
    await screen.findByRole('heading', { name: 'Environment & hosting' });
    // Assert
    expect(screen.queryByRole('button', { name: 'Submit for Review' })).not.toBeInTheDocument();
    expect(within(screen.getByRole('banner')).getByRole('button', { name: 'Save Draft' })).toBeEnabled();
    expect(api.submitSections).not.toHaveBeenCalled();
  });
  it('retains Mission profile submission for an editable draft', async () => {
    // Arrange
    state.role = 'MissionOwner';
    api.getProfileSection.mockResolvedValue(section(true, 'Draft'));
    api.submitSections.mockResolvedValue({ submittedSections: ['MissionAndPurpose'], skippedSections: [] });
    // Act
    render(<SystemProfile />);
    fireEvent.click(await screen.findByRole('button', { name: 'Submit for Review' }));
    // Assert
    await waitFor(() => expect(api.submitSections).toHaveBeenCalledWith('system-a', { action: 'submit', sectionTypes: ['MissionAndPurpose'] }));
  });
  it.each([
    ['Draft', false],
    ['UnderReview', true],
  ])('keeps Environment %s fields locked with edit capability %s', async (status, canEdit) => {
    // Arrange
    state.sectionType = 'EnvironmentAndDeployment';
    state.role = 'MissionOwner';
    api.getProfileSection.mockResolvedValue(section(canEdit, status));
    // Act
    render(<SystemProfile />);
    await screen.findByRole('heading', { name: 'Environment & hosting' });
    // Assert
    expect(screen.getByRole('textbox', { name: 'Deployment description' })).toBeDisabled();
    expect(screen.queryByRole('button', { name: 'Save Draft' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Submit for Review' })).not.toBeInTheDocument();
    if (status === 'UnderReview') expect(screen.getByRole('button', { name: 'Withdraw' })).toBeEnabled();
    expect(api.saveProfileSection).not.toHaveBeenCalled();
    expect(api.submitSections).not.toHaveBeenCalled();
  });
  it('saves visible Environment fields together from the header with unknown values retained', async () => {
    // Arrange
    state.sectionType = 'EnvironmentAndDeployment';
    const original = { hostingModel: 'Hybrid', additionalDetails: 'Reviewed description',
      networkZones: '["DMZ"]', rtoRpo: 'Custom retained target', customSource: 'retain' };
    api.getProfileSection.mockResolvedValue({ ...section(true, 'Draft'), draftContent: JSON.stringify(original) });
    api.saveProfileSection.mockResolvedValue(section(true, 'Draft'));
    render(<SystemProfile />);
    await screen.findByRole('heading', { name: 'Environment & hosting' });
    // Act
    fireEvent.change(screen.getByRole('textbox', { name: 'Deployment description' }), { target: { value: 'Working description' } });
    fireEvent.change(screen.getByRole('combobox', { name: 'Availability Tier' }), { target: { value: 'Best Effort' } });
    fireEvent.click(within(screen.getByRole('banner')).getByRole('button', { name: 'Save Draft' }));
    // Assert
    await waitFor(() => expect(api.saveProfileSection).toHaveBeenCalledWith('system-a', 'EnvironmentAndDeployment', {
      content: JSON.stringify({ ...original, additionalDetails: 'Working description', availabilityTier: 'Best Effort' }), childItems: undefined,
    }));
  });
  it('keeps unified Environment dirty values after a failed header save and retains them on save/reload', async () => {
    // Arrange
    state.sectionType = 'EnvironmentAndDeployment';
    const original = { hostingModel: 'Legacy hosting', networkZones: '["Custom zone"]',
      geographicLocations: 'Legacy site', rtoRpo: 'Custom target', operatingSystem: '["Legacy OS"]',
      additionalDetails: 'Saved description', customSource: { reference: 'retain' } };
    api.getProfileSection.mockResolvedValue({ ...section(true, 'Draft'), draftContent: JSON.stringify(original) });
    api.saveProfileSection.mockRejectedValueOnce(new Error('Synthetic save rejected'));
    const view = render(<SystemProfile />);
    await screen.findByRole('textbox', { name: 'Deployment description' });
    // Act
    fireEvent.change(screen.getByRole('textbox', { name: 'Deployment description' }), { target: { value: 'Dirty description' } });
    fireEvent.change(screen.getByRole('combobox', { name: 'Maintenance Windows' }), { target: { value: 'Quarterly Scheduled' } });
    fireEvent.click(within(screen.getByRole('banner')).getByRole('button', { name: 'Save Draft' }));
    // Assert
    await screen.findAllByText('Synthetic save rejected');
    expect(screen.getByRole('textbox', { name: 'Deployment description' })).toHaveValue('Dirty description');
    expect(screen.getByRole('combobox', { name: 'Maintenance Windows' })).toHaveValue('Quarterly Scheduled');
    const saved = { ...original, additionalDetails: 'Dirty description', maintenanceWindows: 'Quarterly Scheduled' };
    api.saveProfileSection.mockResolvedValue({ ...section(true, 'Draft'), draftContent: JSON.stringify(saved) });
    api.getProfileSection.mockResolvedValue({ ...section(true, 'Draft'), draftContent: JSON.stringify(saved) });
    // Act
    fireEvent.click(within(screen.getByRole('banner')).getByRole('button', { name: 'Save Draft' }));
    await waitFor(() => expect(api.saveProfileSection).toHaveBeenLastCalledWith('system-a', 'EnvironmentAndDeployment', {
      content: JSON.stringify(saved), childItems: undefined,
    }));
    view.unmount();
    render(<SystemProfile />);
    // Assert
    expect(await screen.findByRole('textbox', { name: 'Deployment description' })).toHaveValue('Dirty description');
    expect(screen.getByRole('combobox', { name: 'RTO / RPO Targets' })).toHaveValue('Custom target');
    expect(screen.getByRole('combobox', { name: 'Network Zones' })).toHaveTextContent('Custom zone');
    expect(screen.getByRole('combobox', { name: 'Geographic Locations' })).toHaveTextContent('Legacy site');
    expect(screen.getByRole('combobox', { name: 'Operating Systems' })).toHaveTextContent('Legacy OS');
  });
  it('labels Users access-context approval separately from individual category review', async () => {
    // Arrange
    state.sectionType = 'UsersAndAccess';
    workspace.value = { roles: ['Issm'] };
    api.getProfileSection.mockResolvedValueOnce(section(false, 'UnderReview')).mockResolvedValue(section(false, 'Approved'));
    api.reviewSection.mockResolvedValue(section(false, 'Approved'));
    render(<SystemProfile />);
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Manage access context' }));
    fireEvent.click(screen.getByRole('button', { name: 'Approve access context' }));
    const dialog = screen.getByRole('dialog');
    // Assert
    expect(dialog).toHaveTextContent('Individual user-category reviews are unchanged.');
    // Act
    fireEvent.click(within(dialog).getByRole('button', { name: 'Approve access context' }));
    // Assert
    expect(await screen.findByText('Access context approved. Category reviews are unchanged.')).toBeVisible();
    expect(api.reviewSection).toHaveBeenCalledWith('system-a', 'UsersAndAccess', { decision: 'approve' });
  });

  it('places Users Save Draft at the top and keeps one table Add user category action', async () => {
    // Arrange
    state.sectionType = 'UsersAndAccess';
    api.getProfileSection.mockResolvedValue(section(true, 'Draft'));
    render(<SystemProfile />);
    // Act
    await screen.findByRole('heading', { name: 'Users & access' });
    const header = screen.getByRole('banner');
    expect(within(header).getByRole('button', { name: 'Save Draft' })).toHaveAttribute('form', 'system-profile-editor');
    expect(within(header).getByRole('link', { name: 'Preview SSP contribution' })).toHaveAttribute('href', '/systems/system-a/documents/preview?contribution=UsersAndAccess');
    fireEvent.click(screen.getByRole('button', { name: 'Add user category' }));
    // Assert
    expect(screen.getAllByRole('button', { name: 'Add user category' })).toHaveLength(1);
    expect(screen.getAllByRole('button', { name: 'Save Draft' })).toHaveLength(1);
    expect(screen.getByRole('dialog')).toBeVisible();
    expect(api.saveProfileSection).not.toHaveBeenCalled();
  });

  it('places Mission heading and save before six tabs, with real owner and document actions', async () => {
    // Arrange
    api.getProfileSection.mockResolvedValue(section(true, 'Draft'));
    // Act
    render(<SystemProfile />);
    const heading = await screen.findByRole('heading', { name: 'Mission & purpose' });
    const nav = screen.getByRole('navigation', { name: 'System task views' });
    // Assert
    expect(heading.compareDocumentPosition(nav) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(within(nav).getAllByRole('link')).toHaveLength(7);
    expect(await screen.findByDisplayValue('Recorded Owner')).toBeVisible();
    expect(screen.getByRole('button', { name: 'Save Draft' })).toHaveAttribute('form', 'system-profile-editor');
    expect(screen.getByRole('link', { name: 'Preview contribution' })).toHaveAttribute('href', '/systems/system-a/documents/preview?contribution=MissionAndPurpose');
    expect(screen.getByRole('link', { name: 'View package readiness' })).toHaveAttribute('href', '/systems/system-a/documents?purpose=InitialSubmission');
  });

  it('does not claim success when the server skips submission', async () => {
    // Arrange
    api.getProfileSection.mockResolvedValue(section(true, 'Draft'));
    api.submitSections.mockResolvedValue({ submittedSections: [], skippedSections: [{ sectionType: 'MissionAndPurpose', reason: 'Required content is missing.' }] });
    render(<SystemProfile />);
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Submit for Review' }));
    // Assert
    expect(await screen.findByText('Required content is missing.')).toBeVisible();
    expect(screen.queryByText('Section submitted for review.')).not.toBeInTheDocument();
  });

  it('requests revision in a dialog and retains comments after a rejected write', async () => {
    // Arrange
    workspace.value = { roles: ['Issm'] };
    api.getProfileSection.mockResolvedValue(section(false, 'UnderReview'));
    api.reviewSection.mockRejectedValue(new Error('Review write unavailable'));
    render(<SystemProfile />);
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Request Revision' }));
    const dialog = screen.getByRole('dialog');
    fireEvent.change(within(dialog).getByLabelText('Revision comments'), { target: { value: 'Clarify mission dependencies.' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Request revision' }));
    // Assert
    await waitFor(() => expect(api.reviewSection).toHaveBeenCalledWith('system-a', 'MissionAndPurpose',
      { decision: 'request_revision', comments: 'Clarify mission dependencies.' }));
    expect(within(dialog).getByLabelText('Revision comments')).toHaveValue('Clarify mission dependencies.');
    expect(within(dialog).getByRole('alert')).toHaveTextContent('Review write unavailable');
  });

  it('does not claim withdrawal when the server skips the selected section', async () => {
    // Arrange
    workspace.value = { roles: ['MissionOwner'] };
    api.getProfileSection.mockResolvedValue(section(false, 'UnderReview'));
    api.withdrawSections.mockResolvedValue({ withdrawnSections: [], skippedSections: [{ sectionType: 'MissionAndPurpose', reason: 'Review already completed.' }] });
    render(<SystemProfile />);
    // Act
    fireEvent.click(await screen.findByRole('button', { name: /^Withdraw$/ }));
    expect(api.withdrawSections).not.toHaveBeenCalled();
    fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Withdraw section' }));
    // Assert
    expect(await within(screen.getByRole('dialog')).findByRole('alert')).toHaveTextContent('Review already completed.');
    expect(screen.queryByText('Section withdrawn from review.')).not.toBeInTheDocument();
  });

  it('requires confirmation before approving the saved profile and accepts only confirmed approval', async () => {
    // Arrange
    workspace.value = { roles: ['Issm'] };
    api.getProfileSection.mockResolvedValue(section(false, 'UnderReview'));
    api.reviewSection.mockResolvedValue(section(false, 'UnderReview'));
    render(<SystemProfile />);
    // Act
    fireEvent.click(await screen.findByRole('button', { name: /^Approve$/ }));
    expect(api.reviewSection).not.toHaveBeenCalled();
    fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Approve section' }));
    // Assert
    expect(await within(screen.getByRole('dialog')).findByRole('alert')).toHaveTextContent('The server did not confirm section approval.');
    expect(screen.queryByText('Section approved.')).not.toBeInTheDocument();
  });

  it.each([
    ['MissionAndPurpose', 'Mission & purpose', 'System record'],
    ['UsersAndAccess', 'Users & access', 'User categories'],
    ['DataTypes', 'Data types & sensitivity', 'Information types and handling'],
    ['PortsProtocolsAndServices', 'Ports & interconnections', 'Ports and services'],
  ])('uses the task-specific record composition for %s', async (type, title, recordTitle) => {
    // Arrange
    state.sectionType = type;
    api.getProfileSection.mockResolvedValue(section(true, 'Draft'));
    // Act
    render(<SystemProfile />);
    // Assert
    expect(await screen.findByRole('heading', { name: title })).toBeVisible();
    if (type === 'PortsProtocolsAndServices') {
      expect(screen.getByRole('table', { name: 'Network interfaces and interconnections' })).toBeVisible();
      await waitFor(() => expect(screen.getByRole('button', { name: 'Add connection' })).toBeEnabled());
      expect(screen.queryByRole('button', { name: 'Save Draft' })).not.toBeInTheDocument();
    } else {
      expect(screen.getByRole('heading', { name: recordTitle })).toBeVisible();
      expect(screen.getAllByRole('button', { name: 'Save Draft' })).toHaveLength(1);
    }
    if (type === 'MissionAndPurpose') {
      const sidebar = screen.getByRole('complementary', { name: 'Document contribution and next tasks' });
      expect(sidebar).toBeVisible();
      expect(within(sidebar).getByText('Used in your package')).toBeVisible();
      expect(within(sidebar).getByText('Review & ownership')).toBeVisible();
      expect(within(sidebar).getByText('Related work')).toBeVisible();
      expect(within(sidebar).getByText('SSP · System description')).toBeVisible();
      expect(within(sidebar).getByText('Last edited: Not recorded')).toBeVisible();
      expect(within(sidebar).getByText('Approved snapshot: Not recorded')).toBeVisible();
      expect(within(sidebar).getByRole('link', { name: 'Review System team' })).toHaveAttribute('href', '/systems/system-a/roles');
      expect(within(sidebar).getByRole('link', { name: 'View activity history' })).toHaveAttribute('href', '/systems/system-a/history');
      expect(within(sidebar).getByRole('link', { name: 'Users & access' })).toHaveAttribute('href', '/systems/system-a/profile/UsersAndAccess');
      expect(screen.getAllByRole('link', { name: 'Preview contribution' })).toHaveLength(1);
      expect(screen.getAllByRole('link', { name: 'View package readiness' })).toHaveLength(1);
      expect(within(sidebar).queryByRole('button', { name: 'Manage access context' })).not.toBeInTheDocument();
      expect(screen.queryByRole('region', { name: 'Documentation & review' })).not.toBeInTheDocument();
      expect(screen.getAllByRole('region', { name: 'System record' })).toHaveLength(1);
    } else {
      expect(screen.getByRole('complementary', { name: 'Document contribution and next tasks' })).toBeVisible();
    }
  });

  it('adds and saves a real user-category row through the existing single editor', async () => {
    // Arrange
    state.sectionType = 'UsersAndAccess';
    api.getProfileSection.mockResolvedValue(section(true, 'Draft'));
    api.saveProfileSection.mockResolvedValue(section(true, 'Draft'));
    render(<SystemProfile />);
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Add user category' }));
    fireEvent.change(within(screen.getByRole('dialog')).getByLabelText('Category'), { target: { value: 'Application Users' } });
    fireEvent.change(within(screen.getByRole('dialog')).getByLabelText('Count'), { target: { value: '12' } });
    fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Apply to draft' }));
    fireEvent.click(screen.getByRole('button', { name: 'Save Draft' }));
    // Assert
    await waitFor(() => expect(api.saveProfileSection).toHaveBeenCalledWith('system-a', 'UsersAndAccess',
      expect.objectContaining({ childItems: [expect.objectContaining({ categoryName: 'Application Users', approximateCount: 12 })] })));
  });

  it('does not infer profile approval from a browser ISSM preference in a workspace', async () => {
    // Arrange
    state.role = 'ISSM';
    workspace.value = { roles: ['MissionOwner'] };
    api.getProfileSection.mockResolvedValue(section(false, 'UnderReview'));

    // Act
    render(<SystemProfile />);

    // Assert
    expect(await screen.findByText('This section is under ISSM review — content is read-only.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Approve' })).not.toBeInTheDocument();
  });

  it('renders exactly one profile form after workspace navigation (#1017)', async () => {
    // Arrange
    api.getProfileSection.mockResolvedValue(section(true));

    // Act
    render(<SystemProfile />);
    await screen.findByRole('button', { name: 'Save Draft' });

    // Assert
    expect(screen.getAllByPlaceholderText("Describe the system's mission...")).toHaveLength(1);
    expect(screen.getAllByRole('button', { name: /Save Draft/i })).toHaveLength(1);
  });

  it('does not render a profile form before scoped permissions load or when they fail (#1017)', async () => {
    // Arrange
    let rejectLoad!: (error: Error) => void;
    api.getProfileSection.mockReturnValue(new Promise((_, reject) => { rejectLoad = reject; }));
    render(<SystemProfile />);
    expect(screen.queryByPlaceholderText("Describe the system's mission...")).not.toBeInTheDocument();

    // Act
    await act(async () => rejectLoad(new Error('Permission response unavailable')));

    // Assert
    expect(await screen.findByText('Unable to load profile section.')).toBeInTheDocument();
    expect(screen.queryByPlaceholderText("Describe the system's mission...")).not.toBeInTheDocument();
  });

  it.each(['NotStarted','Draft','NeedsRevision','Approved','UnderReview',undefined])(
    'fails closed without capability for %s',governanceStatus => {
      // Arrange
      const missingCapability=undefined;
      // Act
      const readOnly=computeIsReadOnly(governanceStatus,missingCapability);
      // Assert
      expect(readOnly).toBe(true);
    });
  it.each(['NotStarted','Draft','NeedsRevision','Approved','UnderReview'])(
    'applies review lock independently for %s',governanceStatus => {
      // Arrange
      const canEditProfile=true;
      // Act
      const readOnly=computeIsReadOnly(governanceStatus,canEditProfile);
      // Assert
      expect(readOnly).toBe(governanceStatus==='UnderReview');
    });
  it.each(['','ISSO','ISSM','MissionOwner','SystemOwner','Engineer','AO'])(
    'local persona %s cannot grant authoring',async role => {
      // Arrange
      state.role=role;
      // Act
      render(<SystemProfile />);
      // Assert
      expect(await screen.findByText('Read-only')).toBeInTheDocument();
      expect(screen.queryByRole('button',{ name: /Save Draft/i })).not.toBeInTheDocument();
      expect(api.saveProfileSection).not.toHaveBeenCalled();
    });

  it.each<ProfileSectionType>(['MissionAndPurpose','UsersAndAccess','EnvironmentAndDeployment','DataTypes','PortsProtocolsAndServices','LeveragedAuthorizations'])(
    'allows the server-authorized author on %s regardless of persona',async sectionType => {
      // Arrange
      state.sectionType=sectionType;
      state.role='Engineer';
      api.getProfileSection.mockResolvedValue(section(true));
      // Act
      render(<SystemProfile />);
      if (sectionType === 'PortsProtocolsAndServices')
        fireEvent.click(await screen.findByRole('button', { name: 'Manage communication context & review' }));
      // Assert
      expect(await screen.findByRole('button',{ name: /Save Draft/i })).toBeInTheDocument();
      expect(screen.queryByText('Read-only')).not.toBeInTheDocument();
    });

  it('retains authoring capability after save and surfaces safe server errors',async () => {
    // Arrange
    api.getProfileSection.mockResolvedValue(section(true));
    api.saveProfileSection.mockResolvedValueOnce(section(true,'Draft'))
      .mockRejectedValueOnce({ error: 'Your profile author assignment was removed.',errorCode: 'UNAUTHORIZED' });
    render(<SystemProfile />);
    // Act
    fireEvent.click(await screen.findByRole('button',{ name: /Save Draft/i }));
    await screen.findByText('Section saved as Draft.');
    fireEvent.click(screen.getByRole('button',{ name: /Save Draft/i }));
    // Assert
    expect(await screen.findByText('Your profile author assignment was removed.')).toBeInTheDocument();
    expect(screen.queryByText('Section saved as Draft.')).not.toBeInTheDocument();
  });

  it('stays closed while loading and after a failed permission response',async () => {
    // Arrange
    let rejectLoad!: (error: Error) => void;
    api.getProfileSection.mockReturnValue(new Promise((_,reject) => { rejectLoad=reject; }));
    // Act
    render(<SystemProfile />);
    // Assert
    expect(screen.queryByRole('button',{ name: /Save Draft/i })).not.toBeInTheDocument();
    await act(async () => rejectLoad(new Error('Unavailable')));
    expect(await screen.findByText('Unable to load profile section.')).toBeInTheDocument();
    expect(screen.queryByRole('button',{ name: /Save Draft/i })).not.toBeInTheDocument();
  });

  it('ignores an old system permission response after navigation',async () => {
    // Arrange
    let finishOldRequest!: (value: ProfileSectionDetail) => void;
    api.getProfileSection.mockReturnValueOnce(new Promise(resolve => { finishOldRequest=resolve; }))
      .mockResolvedValueOnce(section(false));
    const page=render(<SystemProfile />);
    // Act
    state.systemId='system-b';
    page.rerender(<SystemProfile />);
    await screen.findByText('Read-only');
    await act(async () => finishOldRequest(section(true)));
    // Assert
    await waitFor(() => expect(screen.queryByRole('button',{ name: /Save Draft/i })).not.toBeInTheDocument());
  });

  it.each([
    [new Error('Connection unavailable'),'Connection unavailable'],
    [{ error: '' },'Save failed'],
    [{ error: 42 },'Save failed'],
    [null,'Save failed'],
  ])('uses the safe fallback for rejected saves (%s)',async (failure,message) => {
    // Arrange
    api.getProfileSection.mockResolvedValue(section(true));
    api.saveProfileSection.mockRejectedValue(failure);
    render(<SystemProfile />);
    // Act
    fireEvent.click(await screen.findByRole('button',{ name: /Save Draft/i }));
    // Assert
    expect(await screen.findByText(message as string)).toBeInTheDocument();
  });

  it('ignores rejection from an obsolete system request',async () => {
    // Arrange
    let rejectOldRequest!: (error: Error) => void;
    api.getProfileSection.mockReturnValueOnce(new Promise((_,reject) => { rejectOldRequest=reject; }))
      .mockResolvedValueOnce(section(false));
    const page=render(<SystemProfile />);
    // Act
    state.systemId='system-b';
    page.rerender(<SystemProfile />);
    await screen.findByText('Read-only');
    await act(async () => rejectOldRequest(new Error('Obsolete request')));
    // Assert
    expect(screen.getByText('Read-only')).toBeInTheDocument();
    expect(screen.queryByText('Unable to load profile section.')).not.toBeInTheDocument();
  });
});
