// Issue #981: synthetic fixtures for the agreed assessment admission contract.
export const systemId = 'assessment-system-a';
export const otherSystemId = 'assessment-system-b';
export const subscriptionId = '11111111-1111-4111-8111-111111111111';
export const otherSubscriptionId = '22222222-2222-4222-8222-222222222222';
export const unavailableSubscriptionId = '33333333-3333-4333-8333-333333333333';
export const governmentSubscriptionId = '44444444-4444-4444-8444-444444444444';
export const legacySubscriptionId = '55555555-5555-4555-8555-555555555555';

export const configurationUrl = (id = systemId) =>
  `/systems/${id}/assessments/environment#azure-assessment-environment`;
export const readinessPath = (id = systemId) => `/systems/${id}/assessment-readiness`;
export const environmentPath = (id = systemId) => `/systems/${id}/assessment-environment`;

export function readiness(isReady = false, id = systemId) {
  return {
    systemId: id,
    isReady,
    errorCode: isReady ? null : 'ASSESSMENT_AZURE_ENVIRONMENT_REQUIRED',
    message: isReady ? 'Azure assessment prerequisites verified.' : 'No Azure environment is attached.',
    suggestion: isReady ? null : 'Attach an eligible subscription in Configure Environment.',
    configurationUrl: configurationUrl(id),
    deploymentCloud: 'Commercial' as 'Commercial' | 'Government' | null,
    cloudEnvironment: isReady ? 'Commercial' : null as string | null,
    subscriptions: isReady ? [{ subscriptionId, displayName: 'Synthetic Commercial Alpha' }] : [],
    checkedAt: '2026-09-21T12:00:00Z',
  };
}

export function environment(id = systemId) {
  return {
    systemId: id,
    deploymentCloud: 'Commercial' as 'Commercial' | 'Government',
    cloudEnvironment: null as string | null,
    subscriptionIds: [] as string[],
    availableSubscriptions: [
      { subscriptionId, displayName: 'Synthetic Commercial Alpha', cloudEnvironment: 'Commercial', isAvailable: true },
      { subscriptionId: otherSubscriptionId, displayName: 'Synthetic Commercial Beta', cloudEnvironment: 'Commercial', isAvailable: true },
      { subscriptionId: unavailableSubscriptionId, displayName: 'Synthetic Unavailable', cloudEnvironment: 'Commercial', isAvailable: false },
      { subscriptionId: governmentSubscriptionId, displayName: 'Synthetic Government', cloudEnvironment: 'Government', isAvailable: true },
      { subscriptionId: legacySubscriptionId, displayName: 'Synthetic Unknown Cloud', cloudEnvironment: 'Unknown', isAvailable: false },
    ],
  };
}

export const systemDetail = {
  systemId,
  name: 'Synthetic Assessment System',
  acronym: 'SAS',
  systemType: 'MajorApplication',
  missionCriticality: 'MissionSupport',
  hostingEnvironment: 'AzureCommercial',
  impactLevel: 'IL2',
  baselineLevel: 'Moderate',
  currentRmfPhase: 'Assess',
  rmfPhaseProgress: [],
  keyMetrics: {
    complianceScore: 75, priorScore: 70, totalOpenPoams: 0, overduePoams: 0,
    totalFindings: 1, narrativeCoverage: 0, activeDeviations: 0,
  },
  recentActivity: [],
  categorization: { confidentiality: 'Moderate', integrity: 'Moderate', availability: 'Moderate', overall: 'Moderate' },
};

export const historicalAssessment = {
  assessmentId: 'historical-manual-assessment',
  systemId,
  systemName: systemDetail.name,
  framework: 'NIST80053',
  status: 'Completed',
  scanType: 'Manual',
  complianceScore: 75,
  totalControls: 4,
  passedControls: 3,
  failedControls: 1,
  totalFindings: 1,
  assessedAt: '2025-01-02T12:00:00Z',
  initiatedBy: 'Synthetic assessor',
  hasCategorization: true,
};

export const profileSection = {
  systemId,
  sectionType: 'EnvironmentAndDeployment',
  governanceStatus: 'UnderReview',
  draftContent: '{}',
  approvedContent: null,
  reviewerComments: null,
};

export const profileCompleteness = {
  systemId,
  totalSections: 6,
  statusCounts: {},
  approvedPercentage: 0,
  isProfileComplete: false,
  incompleteSections: [],
  missionOwnerAssigned: true,
  missionOwnerName: 'Synthetic owner',
  daysSinceRegistration: 1,
};

export function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason: unknown) => void;
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise;
    reject = rejectPromise;
  });
  return { promise, resolve, reject };
}
