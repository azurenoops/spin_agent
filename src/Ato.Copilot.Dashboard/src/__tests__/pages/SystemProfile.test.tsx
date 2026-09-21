import { act,fireEvent,render,screen,waitFor } from '@testing-library/react';
import { beforeEach,describe,expect,it,vi } from 'vitest';
import SystemProfile,{ computeIsReadOnly } from '../../pages/SystemProfile';
import type { ProfileSectionDetail,ProfileSectionType } from '../../types/dashboard';

const state=vi.hoisted(() => ({ systemId: 'system-a',sectionType: 'MissionAndPurpose',role: '' }));
const api=vi.hoisted(() => ({
  getProfileSection: vi.fn(),getProfileCompleteness: vi.fn(),saveProfileSection: vi.fn(),
  submitSections: vi.fn(),withdrawSections: vi.fn(),reviewSection: vi.fn(),
}));
vi.mock('react-router-dom',() => ({ useParams: () => ({ sectionType: state.sectionType }) }));
vi.mock('../../components/layout/SystemLayout',() => ({ useSystemContext: () => ({ detail: { systemId: state.systemId } }) }));
vi.mock('../../hooks/useSettings',() => ({ useSettings: () => ({ settings: { role: state.role } }) }));
vi.mock('../../api/systemProfile',() => api);
// Keep these tests focused on profile permissions, not Azure attachment requests.
vi.mock('../../components/AssessmentEnvironmentPanel', () => ({
  default: () => <div data-testid="assessment-environment" />,
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
  api.getProfileSection.mockResolvedValue(section(false));
  api.getProfileCompleteness.mockResolvedValue({ statusCounts: {},totalSections: 5,approvedPercentage: 0 });
});

describe('server-authoritative profile editing (#968)',() => {
  it('renders exactly one profile form after workspace navigation (#1017)', async () => {
    // Arrange
    api.getProfileSection.mockResolvedValue(section(true));

    // Act
    render(<SystemProfile />);
    await screen.findByText('Profile Completeness');

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
