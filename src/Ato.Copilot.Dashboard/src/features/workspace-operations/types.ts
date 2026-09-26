export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  total: number;
  aggregateState?: string | null;
}

export interface CatalogQuery {
  page: number;
  pageSize: number;
  search?: string;
  grouping?: 'component' | 'capability';
  sort?: string;
  direction?: 'asc' | 'desc';
  lifecycle?: string;
  review?: string;
  source?: string;
  systemId?: string;
  componentId?: string;
}

export interface ProviderCatalogItem {
  source: string;
  componentId: string;
  capabilityId: string | null;
  name: string;
  description: string;
  componentName: string;
  componentType: string;
  lifecycle: string;
  reviewState: string;
  sourceFormat: string;
  sourceReference: string | null;
  distinctAdoptionCount: number | null;
  workingRevision: number | null;
  releasedRevision: number | null;
  supportingComponents?: SupportingComponent[];
  distinctOrganizationCount?: number | null;
  workingApprovalState?: string | null;
}

export interface ProviderSourceArtifact {
  componentId: string;
  componentName: string;
  sourceFormat: string;
  sourceFileName: string | null;
  sourceReference: string | null;
}

export interface ProviderCatalogOverview {
  providerName: string | null;
  sourceArtifacts: PagedResult<ProviderSourceArtifact>;
  authorizationRecord: null;
}

export interface ProviderCapabilityDetail {
  capability: ProviderCatalogItem;
  supportingComponents: SupportingComponent[];
  unresolvedContributorIds: string[];
  sourceArtifacts: ProviderSourceArtifact[];
  mappedControlIds: string[];
  sourceEvidenceReferences: null;
  implementationNarrative: null;
}

export interface WorkingRevision {
  capabilityId: string;
  revision: number;
  snapshotHash: string;
  approvedRevision: number | null;
  updatedAt: string;
  contributors: string[];
  controlDuties: Record<string, string>;
  classification: string;
  serviceCategory: string;
  approvalState: string;
  approvedPreviewId: string | null;
  approvedPreviewHash: string | null;
  approvedAt: string | null;
  approvedBy: string | null;
}

export interface PublicationPreview {
  previewId: string;
  capabilityId: string;
  revision: number;
  workingSnapshotHash: string;
  previewHash: string;
  generatedAt: string;
  expiresAt: string;
  isStale: boolean;
  contributorChanges: { value: string; changeKind: string }[];
  dutyChanges: { key: string; before: string | null; after: string | null; changeKind: string }[];
  referenceChanges: { value: string; changeKind: string }[];
  affectedOrganizations: string[];
  affectedSystems: { organizationId: string; systemId: string }[];
  delivery: { impactWrites: number; distinctOrganizations: number; distinctSystems: number };
  notifications: { recipientCount: number; distinctOrganizations: number };
  impactReviewIds?: string[] | null;
  contextSnapshotHash?: string | null;
}

export interface ProviderSubscriber {
  organizationId: string;
  organizationName: string;
  systemId: string;
  systemName: string;
  subscriptionId: string;
  sourceRevision: string | null;
  reviewState: string;
}

export interface PublicationResult {
  releaseId: string;
  capabilityId: string;
  revision: number;
  snapshotHash: string;
  publishedAt: string;
  impactCount: number;
  existing: boolean;
}

export interface OrganizationCatalogItem {
  id: string;
  displayName: string;
  lifecycle: string;
  onboarding: string;
  reviewState: string;
  systemCount: number;
  distinctAdoptionCount: number | null;
  setupState?: string;
  memberCount?: number;
}

export interface OrganizationDetail {
  id: string;
  displayName: string;
  lifecycle: string;
  onboarding: string;
  legalEntityName?: string | null;
  primaryPocName?: string | null;
  primaryPocEmail?: string | null;
  setupState?: string;
  memberCount?: number;
  systems: { id: string; name: string; rmfPhase: string; isActive: boolean }[];
  subscriptions: { id: string; systemId: string; capabilityId: string; sourceRevision: string | null; isActive: boolean }[];
  activity: { action: string; occurredAt: string; outcome: string }[];
}

export interface ProvisioningResult {
  operationId: string;
  tenantId: string;
  tenantState: string;
  administratorState: string;
  membershipState: string;
  lastError: string | null;
  idempotencyKey?: string;
  initialAdministrator?: InitialAdministrator | null;
  personState?: string;
  canEditAdministrator?: boolean;
}

export interface InitialAdministrator {
  directoryTenantId: string;
  objectId: string;
  personId?: string | null;
  newPerson?: { displayName: string; email: string } | null;
}

export interface OrganizationCreationRequest {
  displayName: string;
  legalEntityName?: string;
  primaryPocName?: string;
  primaryPocEmail?: string;
  initialAdministrator?: InitialAdministrator;
}

export interface CreateOrganizationResult {
  tenantId: string;
  operationId: string;
  displayName: string;
  status: string;
  onboardingState: string;
  existing: boolean;
}

export interface InlineLocalCapability {
  name: string;
  provider: string;
  category: string;
  description: string;
  implementationStatus: string;
  owner: string;
}

export interface OrganizationCapability {
  source: 'local' | 'provider';
  recordId: string;
  name: string;
  description: string;
  category: string;
  availability: string;
  isSubscribed: boolean;
  systemCount: number;
  mutationAuthority: string;
  recordType: 'capability' | 'component';
  supportingComponents?: SupportingComponent[] | null;
  controlCount?: number | null;
  reviewState?: string | null;
  responsibility?: string | null;
  sourceName?: string | null;
  organizationContribution?: string | null;
  organizationOwner?: string | null;
  isOrganizationAdopted?: boolean;
}

export interface OrganizationRecordReference {
  source: 'local' | 'provider';
  recordId: string;
}

export interface OrganizationComponentDraft {
  name: string;
  componentType: 'Person' | 'Place' | 'Thing' | 'Policy';
  description: string;
  owner: string;
}

export interface OrganizationCatalogAddition {
  idempotencyKey: string;
  source: 'local' | 'provider';
  recordType: 'capability' | 'component';
  recordId?: string;
  capability?: InlineLocalCapability;
  component?: OrganizationComponentDraft;
  components: OrganizationRecordReference[];
  newComponents: OrganizationComponentDraft[];
  organizationContribution: string;
  owner: string;
}

export interface OrganizationCatalogAdditionResult extends OrganizationRecordReference {
  recordType: 'capability' | 'component';
  name: string;
  existing: boolean;
}

export interface SupportingComponent {
  id: string;
  name: string;
  componentType: string;
  source: string;
  description?: string | null;
}

export interface ControlCoverage {
  controlId: string;
  designation: string;
  remainingDuty?: string | null;
  systemId?: string | null;
}

export interface OrganizationCapabilityDetail {
  capability: OrganizationCapability;
  supportingComponents?: SupportingComponent[] | null;
  controlCoverage?: ControlCoverage[] | null;
  sourceReference?: string | null;
  providerName?: string | null;
  responsibilities: {
    systemId: string; controlId: string; designation: string; confirmedBy: string | null;
    confirmedAt: string | null; sourceRevision: string | null;
  }[];
  narrativeReviews: {
    id: string; systemId: string; controlId: string; narrativeType: string; status: string;
    revision: number; provenance: unknown; createdAt: string; createdBy: string;
    reviewedAt: string | null; reviewedBy: string | null; reviewNote: string | null;
  }[];
}

export interface SetupResult {
  operationId: string;
  recordState: string;
  componentLinksState: string;
  subscriptionState: string;
  subscribeRequested?: boolean;
  outcomes: {
    writeKind: string; writeId: string; state: string; error: string | null; updatedAt: string;
  }[];
  lastError: string | null;
}

export interface CapabilitySetupOperation extends SetupResult {
  idempotencyKey: string;
  tenantId: string;
  systemId: string | null;
  source: string;
  recordId: string;
  componentIds: string[];
  inlineLocalCapability: InlineLocalCapability | null;
  subscribeRequested: boolean;
  createdAt: string;
  updatedAt: string;
}
