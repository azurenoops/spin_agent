// ─── POA&M TypeScript Interfaces (Feature 039) ─────────────────────────────

// ─── Enums ──────────────────────────────────────────────────────────────────

export type CatSeverity = 'I' | 'II' | 'III';
export type PoamStatus = 'Ongoing' | 'Completed' | 'Delayed' | 'RiskAccepted';
export type TicketSyncStatus = 'Synced' | 'Pending' | 'Conflict' | 'Error';
export type TrendPeriod = 'daily' | 'weekly' | 'monthly';
export type ExportFormat = 'emass_excel' | 'oscal_json' | 'csv';

// ─── List & Summary ─────────────────────────────────────────────────────────

export interface PoamListItem {
  id: string;
  controlId: string;
  weakness: string;
  catSeverity: CatSeverity;
  status: PoamStatus;
  components: { id: string; name: string; type: string }[];
  poc: string;
  dueDate: string;
  daysRemaining: number;
  milestoneProgress: { completed: number; total: number };
  deviationType: string | null;
  externalTicketRef: string | null;
  remediationTaskId: string | null;
  remediationTaskStatus: string | null;
  isOverdue: boolean;
  systemId: string;
  systemName: string;
}

export interface PaginatedPoamResponse {
  items: PoamListItem[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

// ─── Detail ─────────────────────────────────────────────────────────────────

export interface PoamMilestoneDto {
  id: string;
  description: string;
  targetDate: string;
  completedDate: string | null;
  sequence: number;
  isOverdue: boolean;
}

export interface PoamHistoryDto {
  id: string;
  eventType: string;
  oldValue: string | null;
  newValue: string | null;
  actingUserName: string;
  timestamp: string;
  details: string | null;
  cascadeOrigin: string | null;
}

export interface PoamTicketSyncDto {
  externalTicketId: string;
  externalTicketUrl: string | null;
  syncStatus: TicketSyncStatus;
  lastSyncAt: string;
  lastSyncError: string | null;
}

export interface PoamDetail extends PoamListItem {
  weaknessSource: string;
  pocEmail: string | null;
  resourcesRequired: string | null;
  costEstimate: number | null;
  scheduledCompletionDate: string;
  actualCompletionDate: string | null;
  comments: string | null;
  findingId: string | null;
  deviationId: string | null;
  createdAt: string;
  modifiedAt: string | null;
  createdBy: string | null;
  rowVersion: string;
  milestones: PoamMilestoneDto[];
  history: PoamHistoryDto[];
  ticketSync: PoamTicketSyncDto | null;
}

// ─── Create / Update ────────────────────────────────────────────────────────

export interface CreatePoamRequest {
  weakness: string;
  weaknessSource: string;
  controlId: string;
  catSeverity: CatSeverity;
  poc: string;
  pocEmail?: string;
  scheduledCompletionDate: string;
  resourcesRequired?: string;
  costEstimate?: number;
  comments?: string;
  findingId?: string;
  componentIds?: string[];
  milestones?: { description: string; targetDate: string }[];
}

export interface UpdatePoamStatusRequest {
  status: PoamStatus;
  comments?: string;
  delayReason?: string;
  revisedDate?: string;
  deviationId?: string;
  cascadeToTask?: boolean;
  rowVersion: string;
}

export interface UpdatePoamStatusResponse {
  poam: PoamDetail;
  cascadeResult?: {
    taskId: string;
    taskUpdated: boolean;
    newTaskStatus: string;
  };
}

// ─── Metrics ────────────────────────────────────────────────────────────────

export interface PoamMetrics {
  totalOpen: number;
  overdue: number;
  catICount: number;
  catIICount: number;
  catIIICount: number;
  expiringWithin30Days: number;
  avgDaysToClose: number;
  byStatus: { status: string; count: number }[];
}

// ─── Trend ──────────────────────────────────────────────────────────────────

export interface PoamTrendResponse {
  openOverTime: { date: string; count: number }[];
  closureRate: { period: string; closed: number; opened: number }[];
  agingBreakdown: { bucket: string; catI: number; catII: number; catIII: number }[];
  timeToClose: { bucket: string; count: number }[];
}

// ─── Component Linkage ──────────────────────────────────────────────────────

export interface LinkComponentsRequest {
  componentIds: string[];
}

export interface UnlinkComponentsRequest {
  componentIds: string[];
}

// ─── Remediation Task ───────────────────────────────────────────────────────

export interface CreateTaskFromPoamRequest {
  boardId: string;
  columnName?: string;
}

export interface LinkTaskRequest {
  taskId: string;
}

// ─── Bulk Operations ────────────────────────────────────────────────────────

export interface BulkPoamStatusRequest {
  poamIds: string[];
  status: PoamStatus;
  comments?: string;
  delayReason?: string;
  revisedDate?: string;
}

export interface BulkPoamStatusResponse {
  succeeded: number;
  failed: number;
  results: { poamId: string; success: boolean; error?: string }[];
}

export interface BulkCreateFromFindingsRequest {
  findingIds: string[];
  componentIds?: string[];
  linkRemediationTasks?: boolean;
}

export interface BulkCreateResponse {
  created: number;
  skippedDuplicates: number;
  results: { findingId: string; poamId?: string; status: 'created' | 'duplicate' | 'error' }[];
}

// ─── Ticketing ──────────────────────────────────────────────────────────────

export interface ConfigureTicketingRequest {
  provider: 'jira' | 'servicenow';
  baseUrl: string;
  projectKeyOrTableName: string;
  issueType?: string;
  authToken: string;
  fieldMapping: Record<string, string>;
  syncEnabled?: boolean;
}

export interface SyncTicketRequest {
  direction?: 'push' | 'pull' | 'bidirectional';
}

// ─── Query Params ───────────────────────────────────────────────────────────

export interface PoamListQuery {
  page?: number;
  pageSize?: number;
  sortBy?: string;
  sortDirection?: 'asc' | 'desc';
  status?: string;
  catSeverity?: string;
  overdue?: boolean;
  componentId?: string;
  search?: string;
  systemId?: string;
}
