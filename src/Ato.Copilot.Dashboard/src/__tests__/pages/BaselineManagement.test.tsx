import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import BaselineManagement from '../../pages/BaselineManagement';
import * as systemDetailApi from '../../api/systemDetail';
import * as systemProfileApi from '../../api/systemProfile';
import { useWorkspaceSession, type WorkspaceSession } from '../../features/workspaces/WorkspaceBoundary';
import { WorkspaceNavigationProvider } from '../../features/workspaces/workspaceNavigation';
import type { BaselineDetailResponse } from '../../api/systemDetail';
import type { ProfileSectionDetail, SystemDetailResponse } from '../../types/dashboard';

vi.mock('../../api/systemDetail', () => ({
  getBaselineDetail: vi.fn(),
  getSystemDetail: vi.fn(),
  selectBaseline: vi.fn(),
  setCategorization: vi.fn(),
}));
vi.mock('../../api/systemProfile', () => ({ getProfileSection: vi.fn() }));
vi.mock('../../features/workspaces/WorkspaceBoundary', () => ({ useWorkspaceSession: vi.fn() }));
vi.mock('../../hooks/useSettings', () => ({
  useSettings: () => ({ settings: { activeFramework: 'NIST 800-53 Rev. 5' } }),
}));

const systemId = 'system-a';
const workspaceTarget = { kind: 'organization' as const, tenantId: 'tenant-a' };
const routeRoot = `/workspaces/organizations/tenant-a/systems/${systemId}`;

const baseline: BaselineDetailResponse = {
  baselineId: 'baseline-a',
  baselineLevel: 'Moderate',
  totalControls: 325,
  overlayApplied: 'CNSSI 1253 IL4',
  inheritedControls: 100,
  sharedControls: 25,
  customerControls: 175,
  tailoredInControls: 2,
  tailoredOutControls: 1,
  createdAt: '2026-09-01T00:00:00Z',
  createdBy: 'ISSM User',
  modifiedAt: null,
  familyBreakdown: [{ family: 'Access Control', count: 25 }],
  tailorings: [],
  controlIds: ['AC-1'],
};

const detail = {
  systemId,
  name: 'Mission System',
  categorization: {
    confidentiality: 'Moderate',
    integrity: 'Moderate',
    availability: 'Low',
    overall: 'Moderate',
    formalNotation: 'SC = {(confidentiality, Moderate), (integrity, Moderate), (availability, Low)}',
    dodImpactLevel: 'IL4',
    isNationalSecuritySystem: false,
    informationTypes: [{
      name: 'Mission support records',
      confidentiality: 'Moderate',
      integrity: 'Moderate',
      availability: 'Low',
    }],
  },
} as SystemDetailResponse;

const dataTypes: ProfileSectionDetail = {
  id: 'profile-data',
  sectionType: 'DataTypes',
  governanceStatus: 'UnderReview',
  draftContent: null,
  approvedContent: null,
  completionPercentage: 100,
  lastEditedBy: 'System Owner',
  lastEditedAt: '2026-09-02T00:00:00Z',
  submittedBy: 'System Owner',
  submittedAt: '2026-09-02T00:00:00Z',
  reviewedBy: null,
  reviewedAt: null,
  reviewerComments: null,
  userCategories: [],
  dataTypeEntries: [{
    id: 'data-a',
    dataTypeName: 'Mission support records',
    description: 'Operational mission support data.',
    sensitivityClassification: 'CUI',
    source: 'Mission owner inventory',
    destination: null,
    applicableRegulations: null,
    sortOrder: 0,
  }],
  ppsEntries: [],
  leveragedAuthorizations: [],
};

function session(canManageSystem = true): WorkspaceSession {
  return {
    identity: {} as WorkspaceSession['identity'],
    workspace: {
      kind: 'organization',
      tenantId: 'tenant-a',
      displayName: 'Demo Organization',
      mode: 'ordinary',
      personId: 'person-a',
      roles: ['ISSM'],
      permissions: {
        canManageMemberships: false,
        canManageOrganization: false,
        canAccessCsp: false,
      },
    },
    target: workspaceTarget,
    systemAccess: {
      systemId,
      roles: ['ISSM'],
      permissions: {
        canRead: true,
        canEditProfile: true,
        canManageSystem,
        canAuthorNarratives: true,
        canReviewNarratives: true,
        canManageEvidence: true,
        canRunAssessments: true,
        canManageRemediation: true,
        canDecideAuthorization: false,
      },
    },
    roles: ['ISSM'],
    refresh: vi.fn(),
  };
}

function renderPage() {
  return render(
    <MemoryRouter initialEntries={[`${routeRoot}/baseline`]}>
      <WorkspaceNavigationProvider workspace={workspaceTarget}>
        <Routes>
          <Route
            path="/workspaces/organizations/:tenantId/systems/:id/baseline"
            element={<BaselineManagement />}
          />
        </Routes>
      </WorkspaceNavigationProvider>
    </MemoryRouter>,
  );
}

beforeEach(() => {
  vi.resetAllMocks();
  vi.mocked(useWorkspaceSession).mockReturnValue(session());
  vi.mocked(systemDetailApi.getBaselineDetail).mockResolvedValue(baseline);
  vi.mocked(systemDetailApi.getSystemDetail).mockResolvedValue(detail);
  vi.mocked(systemProfileApi.getProfileSection).mockResolvedValue(dataTypes);
});

afterEach(cleanup);

describe('BaselineManagement design alignment', () => {
  it('renders real categorization metadata and working supporting actions', async () => {
    // Arrange
    renderPage();

    // Act
    await screen.findByRole('heading', { name: 'Categorization & control baseline' });

    // Assert
    expect(screen.getByText('Mission support records')).toBeInTheDocument();
    expect(screen.getByText('Mission owner inventory')).toBeInTheDocument();
    expect(screen.getByText('System Owner')).toBeInTheDocument();
    expect(screen.getByText('Under review')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Open Mission support records' })).toHaveAttribute(
      'href',
      `${routeRoot}/profile/DataTypes`,
    );
    expect(screen.getByRole('link', { name: 'Preview contribution' })).toHaveAttribute(
      'href',
      `${routeRoot}/documents#ssp-sections`,
    );
    expect(screen.getByRole('link', { name: 'View package readiness' })).toHaveAttribute('href', routeRoot);
    expect(screen.queryByRole('heading', { name: /baseline details/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: /control families/i })).not.toBeInTheDocument();
  });

  it('uses restrained neutral surfaces without the legacy detail panels', async () => {
    // Arrange
    renderPage();

    // Act
    await screen.findByTestId('categorization-summary');

    // Assert
    expect(screen.getByTestId('categorization-summary')).toHaveClass('border-gray-200', 'bg-white');
    expect(screen.getByTestId('information-type-panel')).not.toHaveClass('shadow-sm');
    expect(screen.queryByTestId('baseline-metric')).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Preview contribution' })).toHaveClass('border-gray-300', 'bg-white');
  });

  it('opens the existing categorization workflow from the primary action', async () => {
    // Arrange
    renderPage();
    const review = await screen.findByRole('button', { name: 'Review categorization' });

    // Act
    fireEvent.click(review);

    // Assert
    expect(screen.getByRole('heading', { name: 'Re-categorize System' })).toBeInTheDocument();
  });

  it('keeps categorization visible and reports unavailable Data Types metadata', async () => {
    // Arrange
    vi.mocked(systemProfileApi.getProfileSection).mockRejectedValue(new Error('profile unavailable'));
    renderPage();

    // Act
    await screen.findByText('Mission support records');

    // Assert
    expect(screen.getByRole('alert')).toHaveTextContent('Data Types review metadata is unavailable');
    expect(screen.getByText('SP 800-60')).toBeInTheDocument();
    expect(screen.getAllByText('Review unavailable').length).toBeGreaterThan(0);
  });
});
