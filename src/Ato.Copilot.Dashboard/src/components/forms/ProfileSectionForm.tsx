import { Fragment, useState, useEffect, useCallback, useRef, useId, type ReactNode } from 'react';
import EnvironmentAssociations from './EnvironmentAssociations';
import SetupDialog from '../../features/workspace-operations/SetupDialog';
import type {
  ProfileSectionType,
  GovernanceStatus,
  UserCategoryItem,
  DataTypeItem,
  PpsItem,
  LeveragedAuthItem,
  UserCategoryReviewRequest,
} from '../../types/dashboard';

// ─── Section field configuration ────────────────────────────────────────────

interface FieldDef {
  key: string;
  label: string;
  type: 'text' | 'textarea' | 'select' | 'multiselect';
  maxLength?: number;
  required?: boolean;
  placeholder?: string;
  options?: string[];
  rows?: number;
}

function FieldGroup({ label, children, className, preparation, contextDialog }: {
  label?: string; children: ReactNode; className?: string;
  preparation?: { recorded: number; total: number; guidance: string };
  contextDialog?: { open: boolean; busy: boolean; readOnly: boolean; error: string | null; onClose: () => void; onSave: () => void };
}) {
  if (contextDialog) return contextDialog.open ? <SetupDialog title="System-wide information handling context"
    description="This context describes the whole data profile, not one data type. Saving persists context and any pending information-type drafts together. Cancel discards only context edits made in this dialog."
    busy={contextDialog.busy} onClose={contextDialog.onClose}>
    <form className="space-y-4" onSubmit={event => {
      event.preventDefault(); event.stopPropagation();
      if (!contextDialog.busy && !contextDialog.readOnly) contextDialog.onSave();
    }}>
      {contextDialog.error && <p role="alert" className="text-sm text-red-700">{contextDialog.error}</p>}
      {children}
      <p className="text-xs text-slate-500">The saved context and information types are reviewed together. Recording a sensitivity label does not approve categorization or a privacy determination.</p>
      <div className="flex flex-wrap justify-end gap-3">
        <button type="button" disabled={contextDialog.busy} className="rounded border px-3 py-2 text-sm" onClick={contextDialog.onClose}>Cancel</button>
        {!contextDialog.readOnly && <button type="submit" disabled={contextDialog.busy} className="rounded bg-indigo-600 px-3 py-2 text-sm text-white disabled:opacity-50">Save information context</button>}
      </div>
    </form>
  </SetupDialog> : null;
  return label
    ? <details open={preparation ? true : undefined} className={`rounded-lg border border-gray-200 p-4 dark:border-gray-700${preparation ? ' bg-white dark:bg-slate-900' : ''}`}>
        <summary className="cursor-pointer font-medium">
          <span>{label}</span>
          {preparation && <span className="mt-2 flex flex-wrap gap-2 text-xs font-normal">
            <span className="rounded bg-indigo-50 px-2 py-1 text-indigo-800 dark:bg-indigo-950 dark:text-indigo-200">ATO preparation</span>
            <span className="rounded bg-amber-50 px-2 py-1 text-amber-900 dark:bg-amber-950 dark:text-amber-200">
              {preparation.recorded} of {preparation.total} fields recorded
            </span>
          </span>}
        </summary>
        {preparation && <p className="mt-3 text-sm text-slate-600 dark:text-slate-300">{preparation.guidance}</p>}
        <div className="mt-4 space-y-4">{children}</div>
      </details>
    : <div className={className ?? 'space-y-4'}>{children}</div>;
}

const sectionFields: Record<ProfileSectionType, FieldDef[]> = {
  MissionAndPurpose: [
    { key: 'missionStatement', label: 'Mission Statement', type: 'textarea', maxLength: 4000, required: true, rows: 4, placeholder: 'Describe the system\'s mission...' },
    { key: 'businessPurpose', label: 'Business Purpose', type: 'textarea', maxLength: 4000, required: true, rows: 4, placeholder: 'Describe the business purpose...' },
    { key: 'operationalJustification', label: 'Operational Justification', type: 'textarea', maxLength: 2000, rows: 3, placeholder: 'Justify operational need...' },
    { key: 'businessFunctions', label: 'Business Functions', type: 'textarea', maxLength: 2000, rows: 3, placeholder: 'List key business functions...' },
  ],
  UsersAndAccess: [
    { key: 'accessOverview', label: 'Access Overview', type: 'textarea', maxLength: 4000, rows: 4, placeholder: 'Describe overall access model...' },
    { key: 'authenticationMethod', label: 'Authentication Methods', type: 'multiselect', options: [
      'CAC/PIV', 'MFA', 'SAML', 'OAuth 2.0', 'OpenID Connect', 'Kerberos',
      'LDAP', 'Active Directory', 'Certificate-Based', 'FIDO2/WebAuthn',
      'Username/Password', 'Smart Card', 'Biometric', 'SSO', 'RADIUS',
    ] },
  ],
  EnvironmentAndDeployment: [
    { key: 'hostingModel', label: 'Hosting model', type: 'select', required: true, options: ['CSP-hosted', 'Organization-managed cloud', 'On-Premises', 'Hybrid'] },
    { key: 'cloudProvider', label: 'Cloud environment', type: 'multiselect', options: [
      'AWS', 'AWS GovCloud', 'Azure', 'Azure Government', 'Google Cloud', 'Oracle Cloud',
      'IBM Cloud', 'DISA milCloud', 'On-Premises / N/A',
    ] },
    { key: 'networkZones', label: 'Network Zones', type: 'multiselect', options: [
      'DMZ', 'Internal / Trusted', 'Management', 'Database Tier', 'Application Tier',
      'Web Tier', 'External / Untrusted', 'Restricted', 'Enclave',
    ] },
    { key: 'geographicLocations', label: 'Geographic Locations', type: 'multiselect', options: [
      'CONUS (Continental US)', 'OCONUS (Outside CONUS)', 'US East', 'US West',
      'US Central', 'Europe', 'Pacific', 'Multiple Regions', 'Classified Location',
    ] },
    { key: 'availabilityTier', label: 'Availability Tier', type: 'select', options: [
      '99.999% (Five 9s)', '99.99% (Four 9s)', '99.9% (Three 9s)',
      '99% (Two 9s)', 'Best Effort', 'Mission Critical — Zero Downtime',
    ] },
    { key: 'disasterRecoveryPosture', label: 'Disaster Recovery Strategy', type: 'select', options: [
      'Hot Standby (Active-Active)', 'Warm Standby (Active-Passive)',
      'Cold Standby (Backup Only)', 'Pilot Light', 'Multi-Region Failover',
      'No DR Plan', 'Under Development',
    ] },
    { key: 'rtoRpo', label: 'RTO / RPO Targets', type: 'select', options: [
      'RTO < 1hr / RPO < 15min', 'RTO < 4hr / RPO < 1hr', 'RTO < 24hr / RPO < 4hr',
      'RTO < 72hr / RPO < 24hr', 'RTO > 72hr / Best Effort', 'Not Defined',
    ] },
    { key: 'maintenanceWindows', label: 'Maintenance Windows', type: 'select', options: [
      'Weekdays 0200-0600 ET', 'Weekends 0200-0600 ET', 'Sundays 0000-0600 ET',
      '24/7 Rolling Updates', 'Quarterly Scheduled', 'Ad-Hoc / As Needed',
    ] },
    { key: 'operatingSystem', label: 'Operating Systems', type: 'multiselect', options: [
      'Windows Server 2022', 'Windows Server 2019', 'RHEL 8', 'RHEL 9',
      'Ubuntu 22.04 LTS', 'Ubuntu 24.04 LTS', 'Amazon Linux 2', 'CentOS Stream',
      'SUSE Linux', 'Container-Based (No Host OS)', 'Other',
    ] },
    { key: 'additionalDetails', label: 'Deployment description', type: 'textarea', maxLength: 4000, rows: 3, placeholder: 'Briefly describe where this system runs and which services it uses.' },
  ],
  DataTypes: [
    { key: 'dataOverview', label: 'Data Overview', type: 'textarea', maxLength: 4000, rows: 4, placeholder: 'Describe data processed by the system...' },
    { key: 'highestSensitivityLevel', label: 'Highest Sensitivity Level', type: 'select', options: [
      'Public', 'FOUO', 'CUI', 'PII', 'PHI', 'PCI', 'Classified', 'Top Secret',
    ] },
  ],
  PortsProtocolsAndServices: [
    { key: 'ppsOverview', label: 'PPS Overview', type: 'textarea', maxLength: 4000, rows: 4, placeholder: 'Describe network services...' },
  ],
  LeveragedAuthorizations: [
    { key: 'leveragedAuthOverview', label: 'Overview', type: 'textarea', maxLength: 4000, rows: 4, placeholder: 'Describe leveraged authorizations...' },
  ],
};

// ─── Child entity columns ───────────────────────────────────────────────────

type ChildType = 'userCategories' | 'dataTypeEntries' | 'ppsEntries' | 'leveragedAuthorizations';

interface ColDef {
  key: string;
  label: string;
  type: 'text' | 'number' | 'select';
  required?: boolean;
  maxLength?: number;
  options?: string[];
  width?: string;
}

const childConfig: Partial<Record<ProfileSectionType, { childKey: ChildType; columns: ColDef[] }>> = {
  UsersAndAccess: {
    childKey: 'userCategories',
    columns: [
      { key: 'categoryName', label: 'Category', type: 'text', required: true, maxLength: 200, options: [
        'Privileged Administrators', 'System Administrators', 'Database Administrators',
        'Network Administrators', 'Security Administrators', 'Application Users',
        'Power Users', 'Read-Only Users', 'Service Accounts', 'External Partners',
        'Auditors / Assessors', 'Help Desk / Support', 'Developers', 'Other',
      ], width: 'w-44' },
      { key: 'description', label: 'Description', type: 'text', maxLength: 2000, width: 'w-48' },
      { key: 'approximateCount', label: 'Count', type: 'number', width: 'w-20' },
      { key: 'accessMethod', label: 'Access Method', type: 'select', options: [
        'CAC/PIV', 'VPN + MFA', 'Direct Console', 'SSH Key', 'Web Portal (SSO)',
        'API Token', 'RDP', 'Citrix / VDI', 'Badge + Escort', 'Other',
      ], width: 'w-36' },
      { key: 'dataSensitivityLevel', label: 'Sensitivity', type: 'select', options: [
        'Public', 'CUI', 'PII', 'PHI', 'Classified', 'FOUO', 'SBU',
      ], width: 'w-28' },
    ],
  },
  DataTypes: {
    childKey: 'dataTypeEntries',
    columns: [
      { key: 'dataTypeName', label: 'Data Type', type: 'text', required: true, maxLength: 200, options: [
        'PII — Full Name', 'PII — SSN', 'PII — Date of Birth', 'PII — Address',
        'PII — Phone/Email', 'PHI — Medical Records', 'PHI — Insurance Data',
        'Financial — Payment Card (PCI)', 'Financial — Banking', 'CUI — ITAR',
        'CUI — Export Controlled', 'CUI — Law Enforcement', 'CUI — Privacy',
        'Authentication Credentials', 'Audit Logs', 'System Configuration',
        'Operational Data', 'Public / Open Data', 'Other',
      ], width: 'w-44' },
      { key: 'description', label: 'Description', type: 'text', maxLength: 2000, width: 'w-44' },
      { key: 'sensitivityClassification', label: 'Classification', type: 'select', required: true, options: [
        'Public', 'FOUO', 'CUI', 'PII', 'PHI', 'PCI', 'Classified', 'Top Secret',
      ], width: 'w-28' },
      { key: 'source', label: 'Source', type: 'select', options: [
        'User Input', 'External API', 'Database', 'File Upload', 'Partner Feed',
        'Sensor / IoT', 'Internal System', 'Manual Entry', 'Other',
      ], width: 'w-28' },
      { key: 'destination', label: 'Destination', type: 'select', options: [
        'Internal Database', 'External API', 'Backup Storage', 'Analytics / SIEM',
        'Archive', 'Partner System', 'User Display', 'Report Export', 'Other',
      ], width: 'w-28' },
      { key: 'applicableRegulations', label: 'Regulations', type: 'select', options: [
        'FISMA', 'HIPAA', 'PCI-DSS', 'Privacy Act', 'ITAR', 'EAR',
        'GDPR', 'CCPA', 'FERPA', 'SOX', 'CJIS', 'None / N/A',
      ], width: 'w-28' },
    ],
  },
  PortsProtocolsAndServices: {
    childKey: 'ppsEntries',
    columns: [
      { key: 'portOrRange', label: 'Port/Range', type: 'select', required: true, options: [
        '22 (SSH)', '25 (SMTP)', '53 (DNS)', '80 (HTTP)', '443 (HTTPS)',
        '389 (LDAP)', '636 (LDAPS)', '1433 (MSSQL)', '1521 (Oracle)',
        '3306 (MySQL)', '3389 (RDP)', '5432 (PostgreSQL)', '5671 (AMQP/TLS)',
        '8080 (HTTP Alt)', '8443 (HTTPS Alt)', '9443', 'Custom Range',
      ], width: 'w-32' },
      { key: 'protocol', label: 'Protocol', type: 'select', required: true, options: ['TCP', 'UDP', 'TCP and UDP', 'TLS', 'ICMP'], width: 'w-28' },
      { key: 'serviceName', label: 'Service', type: 'select', required: true, options: [
        'Web Application', 'REST API', 'Database', 'Email (SMTP)',
        'DNS', 'LDAP / AD', 'SSH Remote Admin', 'RDP Remote Admin',
        'Message Queue', 'File Transfer (SFTP)', 'Load Balancer',
        'Monitoring / SIEM', 'Certificate Services', 'Other',
      ], width: 'w-36' },
      { key: 'direction', label: 'Direction', type: 'select', required: true, options: ['Inbound', 'Outbound', 'Both'], width: 'w-24' },
      { key: 'justification', label: 'Justification', type: 'text', maxLength: 2000, width: 'w-44' },
    ],
  },
  LeveragedAuthorizations: {
    childKey: 'leveragedAuthorizations',
    columns: [
      { key: 'providerName', label: 'Provider', type: 'select', required: true, options: [
        'AWS GovCloud', 'Azure Government', 'Google Cloud', 'Oracle Cloud',
        'Microsoft 365 GCC/GCC High', 'Salesforce Government Cloud',
        'ServiceNow FedRAMP', 'Splunk Cloud (FedRAMP)', 'Palo Alto Prisma',
        'Okta (FedRAMP)', 'CrowdStrike (FedRAMP)', 'Other CSP',
      ], width: 'w-40' },
      { key: 'authorizationType', label: 'Auth Type', type: 'select', required: true, options: [
        'FedRAMP High', 'FedRAMP Moderate', 'FedRAMP Low', 'FedRAMP Li-SaaS',
        'DoD PA (IL2)', 'DoD PA (IL4)', 'DoD PA (IL5)', 'DoD PA (IL6)',
        'Agency ATO', 'JAB P-ATO', 'DISA STIG', 'Other',
      ], width: 'w-36' },
      { key: 'authorizationDate', label: 'Date', type: 'text', width: 'w-28' },
      { key: 'coveredControlFamilies', label: 'Control Families', type: 'select', options: [
        'AC — Access Control', 'AU — Audit', 'AT — Awareness & Training',
        'CM — Configuration Mgmt', 'CP — Contingency Planning',
        'IA — Identification & Auth', 'IR — Incident Response',
        'MA — Maintenance', 'MP — Media Protection', 'PE — Physical',
        'PL — Planning', 'PS — Personnel Security', 'RA — Risk Assessment',
        'SA — System Acquisition', 'SC — System Communications',
        'SI — System Integrity', 'PM — Program Management',
        'Multiple / All', 'See Provider Documentation',
      ], width: 'w-44' },
    ],
  },
};

const childTaskLabels: Partial<Record<ProfileSectionType, { title: string; add: string; context: string }>> = {
  UsersAndAccess: { title: 'User categories', add: 'Add user category', context: 'Access context' },
  DataTypes: { title: 'Information types', add: 'Add data type', context: 'Information handling context' },
  PortsProtocolsAndServices: { title: 'Ports and services', add: 'Add port / service', context: 'Communication context' },
};

// ─── Helper types ───────────────────────────────────────────────────────────

// eslint-disable-next-line @typescript-eslint/no-explicit-any
type ChildRow = Record<string, any>;

// ─── Props ──────────────────────────────────────────────────────────────────

/** Subset of SystemDetailResponse used for AI pre-fill */
export interface SystemContextForPrefill {
  hostingEnvironment?: string;
  systemType?: string;
  missionCriticality?: string;
  impactLevel?: string;
  baselineLevel?: string;
  categorization?: {
    confidentiality: string;
    integrity: string;
    availability: string;
    overall: string;
  } | null;
}

interface ProfileSectionFormProps {
  hideChildItems?: boolean;
  onHostingStatusChange?: (status: string) => void;
  formId?: string;
  addEntryOpen?: boolean;
  onAddEntryClose?: () => void;
  contextDialogOpen?: boolean;
  onContextDialogClose?: () => void;
  onReviewUserCategory?: (id: string, request: UserCategoryReviewRequest) => Promise<boolean>;
  systemId?: string;
  sectionType: ProfileSectionType;
  governanceStatus: GovernanceStatus;
  initialContent: string | null;
  initialChildItems?: UserCategoryItem[] | DataTypeItem[] | PpsItem[] | LeveragedAuthItem[];
  reviewerComments: string | null;
  isReadOnly: boolean;
  userRole: string;
  effectiveRoles?: readonly string[];
  isSubmitting: boolean;
  error: string | null;
  systemContext?: SystemContextForPrefill;
  onSave: (content: string, childItems?: ChildRow[]) => void;
  onSubmit: () => void;
  onWithdraw: () => void;
  onApprove?: () => void;
  onRequestRevision?: () => void;
}

// ─── Component ──────────────────────────────────────────────────────────────

/** Build pre-fill values from existing system registration data */
function buildPrefill(sectionType: ProfileSectionType, ctx?: SystemContextForPrefill): Record<string, string> {
  if (!ctx) return {};
  const p: Record<string, string> = {};
  if (sectionType === 'EnvironmentAndDeployment') {
    // Map hosting environment to hosting model
    const h = (ctx.hostingEnvironment ?? '').toLowerCase();
    if (h.includes('cloud') && h.includes('gov')) p.hostingModel = 'Government Cloud (GovCloud)';
    else if (h.includes('hybrid')) p.hostingModel = 'Hybrid';
    else if (h.includes('on-prem') || h.includes('onprem')) p.hostingModel = 'On-Premises';
    else if (h.includes('cloud')) p.hostingModel = 'Cloud (IaaS)';
    // Map cloud provider from hosting string
    if (h.includes('aws') || h.includes('amazon')) {
      p.cloudProvider = h.includes('gov') ? JSON.stringify(['AWS GovCloud']) : JSON.stringify(['AWS']);
    } else if (h.includes('azure')) {
      p.cloudProvider = h.includes('gov') ? JSON.stringify(['Azure Government']) : JSON.stringify(['Azure']);
    } else if (h.includes('gcp') || h.includes('google')) p.cloudProvider = JSON.stringify(['Google Cloud']);
    // Infer availability from categorization
    if (ctx.categorization) {
      const avail = ctx.categorization.availability;
      if (avail === 'High') p.availabilityTier = '99.99% (Four 9s)';
      else if (avail === 'Moderate') p.availabilityTier = '99.9% (Three 9s)';
      else if (avail === 'Low') p.availabilityTier = '99% (Two 9s)';
    }
    // Infer DR from mission criticality
    const mc = (ctx.missionCriticality ?? '').toLowerCase();
    if (mc.includes('essential') || mc.includes('critical')) p.disasterRecoveryPosture = 'Hot Standby (Active-Active)';
    else if (mc.includes('important')) p.disasterRecoveryPosture = 'Warm Standby (Active-Passive)';
  }
  if (sectionType === 'MissionAndPurpose') {
    if (ctx.systemType) p.businessFunctions = `System type: ${ctx.systemType}`;
  }
  return p;
}

function readSavedContent(content: string | null): { values: Record<string, string>; error: string | null } {
  const error = 'Saved section content could not be read. Reload or repair the saved content before editing.';
  try {
    const values = content ? JSON.parse(content) : {};
    return values && typeof values === 'object' && !Array.isArray(values)
      ? { values, error: null }
      : { values: {}, error };
  } catch {
    return { values: {}, error };
  }
}

export default function ProfileSectionForm({
  hideChildItems = false,
  onHostingStatusChange,
  formId,
  addEntryOpen,
  onAddEntryClose,
  contextDialogOpen = false,
  onContextDialogClose,
  onReviewUserCategory,
  systemId,
  sectionType,
  governanceStatus,
  initialContent,
  initialChildItems,
  reviewerComments,
  isReadOnly,
  userRole,
  effectiveRoles,
  isSubmitting,
  error,
  systemContext,
  onSave,
  onSubmit,
  onWithdraw,
  onApprove,
  onRequestRevision,
}: ProfileSectionFormProps) {
  const fields = sectionFields[sectionType] ?? [];
  const child = childConfig[sectionType];
  const childTask = childTaskLabels[sectionType];
  const [prefilled, setPrefilled] = useState(false);
  const editingLocked = isReadOnly || isSubmitting;

  // ─── Scalar field state ─────────────────────────────────────────────
  const [values, setValues] = useState<Record<string, string>>(() => readSavedContent(initialContent).values);

  // ─── Child entity state ─────────────────────────────────────────────
  const [rows, setRows] = useState<ChildRow[]>(() =>
    initialChildItems ? [...(initialChildItems as ChildRow[])] : [],
  );
  // Equivalent refetch objects must not erase an unsaved draft (including after a failed save).
  const sourceKey = JSON.stringify([systemId, sectionType, initialContent, initialChildItems ?? []]);
  const sourceInput = useRef(sourceKey);
  const contextSnapshot = useRef(values);
  const contextWasOpen = useRef(false);

  useEffect(() => {
    if (contextDialogOpen && (!contextWasOpen.current || sourceInput.current !== sourceKey)) {
      contextSnapshot.current = sourceInput.current === sourceKey ? values : readSavedContent(initialContent).values;
    }
    contextWasOpen.current = contextDialogOpen;
  }, [contextDialogOpen, sourceKey, initialContent, values]);

  useEffect(() => {
    if (sourceInput.current === sourceKey) return;
    sourceInput.current = sourceKey;
    setValues(readSavedContent(initialContent).values);
    setRows(initialChildItems ? [...(initialChildItems as ChildRow[])] : []);
    setPrefilled(false);
  }, [sourceKey, initialContent, initialChildItems]);

  const { values: savedValues, error: contentError } = readSavedContent(initialContent);
  const dirty = JSON.stringify(values) !== JSON.stringify(savedValues)
    || JSON.stringify(rows) !== JSON.stringify(initialChildItems ?? []);

  // AI pre-fill: when section is NotStarted and no content exists, populate from system data
  useEffect(() => {
    if (sectionType === 'EnvironmentAndDeployment' || prefilled || initialContent || governanceStatus !== 'NotStarted' || !systemContext) return;
    const pre = buildPrefill(sectionType, systemContext);
    if (Object.keys(pre).length > 0) {
      setValues((prev) => {
        const merged = { ...pre };
        // Don't overwrite any existing user values
        for (const [k, v] of Object.entries(prev)) { if (v) merged[k] = v; }
        return merged;
      });
      setPrefilled(true);
    }
  }, [sectionType, systemContext, initialContent, governanceStatus, prefilled]);

  const handleFieldChange = useCallback((key: string, value: string) => {
    setValues((prev) => ({ ...prev, [key]: value }));
  }, []);

  const handleSave = () => {
    if (editingLocked || contentError) return;
    const content = JSON.stringify(values);
    onSave(content, child ? rows : undefined);
  };

  const isOptionalSection = sectionType === 'LeveragedAuthorizations';
  const individualReviews = sectionType === 'UsersAndAccess';
  const roles = effectiveRoles ?? [userRole];
  const canSubmit = (!individualReviews || roles.includes('MissionOwner')) && (governanceStatus === 'Draft' || governanceStatus === 'NeedsRevision');
  const canWithdraw = governanceStatus === 'UnderReview' && roles.includes('MissionOwner');
  const canReview = governanceStatus === 'UnderReview' && roles.includes('ISSM');
  const scalarEditingLocked = editingLocked || individualReviews && governanceStatus === 'UnderReview';
  const closeContextDialog = () => {
    if (isSubmitting) return;
    setValues(current => {
      const restored = { ...current };
      for (const field of sectionFields[sectionType]) {
        if (Object.prototype.hasOwnProperty.call(contextSnapshot.current, field.key)) restored[field.key] = contextSnapshot.current[field.key]!;
        else delete restored[field.key];
      }
      return restored;
    });
    onContextDialogClose?.();
  };
  const groups: { label?: string; keys: string[] }[] = sectionType === 'EnvironmentAndDeployment' ? [
    { keys: ['hostingModel', 'cloudProvider', 'additionalDetails'] },
    { label: 'Network zones & deployment locations', keys: ['networkZones', 'geographicLocations'] },
    { label: 'Recovery, availability & operating details', keys: ['availabilityTier', 'disasterRecoveryPosture', 'rtoRpo', 'maintenanceWindows', 'operatingSystem'] },
  ] : sectionType === 'MissionAndPurpose' ? [
    { keys: ['missionStatement', 'businessPurpose'] },
    { label: 'Additional mission details', keys: ['operationalJustification', 'businessFunctions'] },
  ] : [{ keys: fields.map(field => field.key) }];
  const fieldRecorded = (key: string) => fields.find(field => field.key === key)?.type === 'multiselect'
    ? readSelection(values[key]).some(value => value.trim().length > 0)
    : !!values[key]?.trim();

  const sectionContext = <>
      {sectionType === 'EnvironmentAndDeployment' && <p className="rounded-lg border border-indigo-200 bg-indigo-50 p-3 text-sm text-indigo-900 dark:border-indigo-900 dark:bg-indigo-950 dark:text-indigo-200">
        Prepare for ATO review: complete the applicable deployment, network/location and recovery/operating details below.
        Expanded sections show which fields are still unrecorded.
      </p>}
      {/* Optional section label */}
      {isOptionalSection && (
        <div className="text-xs text-gray-400 italic">
          Optional — does not affect profile completeness
        </div>
      )}

      {/* NeedsRevision feedback */}
      {governanceStatus === 'NeedsRevision' && reviewerComments && (
        <div className="rounded-lg border border-amber-200 bg-amber-50 p-4">
          <p className="text-sm font-medium text-amber-800">Revision Requested</p>
          <p className="text-sm text-amber-700 mt-1">{reviewerComments}</p>
        </div>
      )}

      {/* Error */}
      {error && (
        <div role={individualReviews ? 'alert' : undefined} className="rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-700">
          {error}
        </div>
      )}
      {contentError && <p role="alert" className="text-sm text-red-700">{contentError}</p>}

      {/* Under Review indicator */}
      {!individualReviews && governanceStatus === 'UnderReview' && (
        <div className="rounded-lg border border-indigo-200 bg-indigo-50 p-3 text-sm text-indigo-700 font-medium">
          This section is under ISSM review — content is read-only.
        </div>
      )}

      {/* AI pre-fill notice */}
      {prefilled && (
        <div className="rounded-lg border border-indigo-200 bg-indigo-50 p-3 text-sm text-indigo-700 flex items-center gap-2">
          <svg className="h-4 w-4 flex-shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M9.813 15.904L9 18.75l-.813-2.846a4.5 4.5 0 00-3.09-3.09L2.25 12l2.846-.813a4.5 4.5 0 003.09-3.09L9 5.25l.813 2.846a4.5 4.5 0 003.09 3.09L15.75 12l-2.846.813a4.5 4.5 0 00-3.09 3.09zM18.259 8.715L18 9.75l-.259-1.035a3.375 3.375 0 00-2.455-2.456L14.25 6l1.036-.259a3.375 3.375 0 002.455-2.456L18 2.25l.259 1.035a3.375 3.375 0 002.455 2.456L21.75 6l-1.036.259a3.375 3.375 0 00-2.455 2.456z" />
          </svg>
          <span>Some fields were pre-filled from existing system registration data. Review and adjust before saving.</span>
        </div>
      )}

      {!hideChildItems && !individualReviews && child && childTask && <ChildEntityTable key={sourceKey} columns={child.columns} rows={rows} onChange={setRows}
        isReadOnly={isReadOnly} disabled={isSubmitting} title={childTask.title} addLabel={childTask.add}
        userCategories={individualReviews} addEntryOpen={addEntryOpen} onAddEntryClose={onAddEntryClose}
        dataTypes={sectionType === 'DataTypes'} sectionGovernanceStatus={governanceStatus}
        onReviewUserCategory={onReviewUserCategory} dirty={dirty} reviewError={error} savedRows={initialChildItems} />}

      {/* Scalar fields */}
      {groups.map(group => <Fragment key={group.label ?? 'primary'}><FieldGroup label={individualReviews || hideChildItems ? undefined : group.label ?? childTask?.context}
        contextDialog={sectionType === 'DataTypes' ? {
          open: contextDialogOpen, busy: isSubmitting, readOnly: isReadOnly,
          error: error ?? contentError, onClose: closeContextDialog, onSave: handleSave,
        } : undefined}
        preparation={sectionType === 'EnvironmentAndDeployment' && group.label ? {
          recorded: group.keys.filter(fieldRecorded).length, total: group.keys.length,
          guidance: group.keys.includes('networkZones')
            ? 'Document the trust zones and deployment locations that support the SSP boundary and environment description. Record the actual system design, including provider-managed dependencies.'
            : 'Document availability, recovery strategy, recovery time/data-loss targets (RTO/RPO), maintenance windows and operating platforms. These support contingency planning and operating procedures.',
        } : undefined}
        className={sectionType === 'EnvironmentAndDeployment' && !group.label ? 'grid min-w-0 gap-[18px] rounded-[10px] border border-slate-200 bg-white p-4 sm:grid-cols-2 min-[651px]:p-[22px] dark:border-slate-700 dark:bg-slate-900' : undefined}>
        {sectionType === 'EnvironmentAndDeployment' && !group.label && <h2 className="text-lg font-semibold sm:col-span-2">Deployment description</h2>}
        {group.keys.flatMap(key => fields.filter(field => field.key === key)).map((field) => (
          <div key={field.key} className={sectionType === 'EnvironmentAndDeployment' && field.key === 'additionalDetails' ? 'min-w-0 sm:col-span-2' : 'min-w-0'}>
            <label htmlFor={field.type === 'multiselect' ? undefined : `profile-${field.key}`} className="block text-sm font-medium text-gray-700 mb-1 dark:text-gray-200">
              {field.label}
              {field.required && <span aria-hidden="true" className="text-red-500 ml-0.5">*</span>}
              {sectionType === 'EnvironmentAndDeployment' && group.label && !fieldRecorded(field.key) &&
                <span aria-hidden="true" className="ml-2 text-xs font-normal text-amber-700 dark:text-amber-300">Not recorded</span>}
            </label>
            {field.type === 'textarea' ? (
              <>
                <textarea
                  id={`profile-${field.key}`}
                  value={values[field.key] ?? ''}
                  onChange={(e) => handleFieldChange(field.key, e.target.value)}
                  disabled={scalarEditingLocked}
                  rows={field.rows ?? 3}
                  maxLength={field.maxLength}
                  className="w-full rounded-lg border border-gray-300 px-4 py-2.5 text-sm text-gray-900 placeholder-gray-400 focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500 disabled:bg-gray-50 disabled:text-gray-500 dark:border-gray-600 dark:bg-gray-800 dark:text-gray-100 dark:disabled:bg-gray-900"
                  placeholder={field.placeholder}
                />
                {field.maxLength && (
                  <div className="text-xs text-gray-400 text-right mt-0.5">
                    {(values[field.key] ?? '').length.toLocaleString()} / {field.maxLength.toLocaleString()}
                  </div>
                )}
              </>
            ) : field.type === 'multiselect' ? (
              <MultiSelectField
                label={field.label}
                options={field.options ?? []}
                selected={readSelection(values[field.key])}
                onChange={(sel) => handleFieldChange(field.key, JSON.stringify(sel))}
                disabled={scalarEditingLocked}
                placeholder={field.placeholder}
              />
            ) : field.type === 'select' ? (
              <select
                id={`profile-${field.key}`}
                value={values[field.key] ?? ''}
                onChange={(e) => handleFieldChange(field.key, e.target.value)}
                disabled={scalarEditingLocked}
                className="w-full rounded-lg border border-gray-300 px-4 py-2.5 text-sm text-gray-900 focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500 disabled:bg-gray-50 disabled:text-gray-500 dark:border-gray-600 dark:bg-gray-800 dark:text-gray-100 dark:disabled:bg-gray-900"
              >
                <option value="">— Select —</option>
                {values[field.key] && !field.options?.includes(values[field.key]!) &&
                  <option value={values[field.key]}>{values[field.key]} (previously recorded)</option>}
                {field.options?.map((opt) => (
                  <option key={opt} value={opt}>{field.key === 'hostingModel' && opt === 'CSP-hosted' ? 'Provider-managed cloud' : opt}</option>
                ))}
              </select>
            ) : (
              <input
                id={`profile-${field.key}`}
                type="text"
                value={values[field.key] ?? ''}
                onChange={(e) => handleFieldChange(field.key, e.target.value)}
                disabled={scalarEditingLocked}
                maxLength={field.maxLength}
                className="w-full rounded-lg border border-gray-300 px-4 py-2.5 text-sm text-gray-900 placeholder-gray-400 focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500 disabled:bg-gray-50 disabled:text-gray-500"
                placeholder={field.placeholder}
              />
            )}
          </div>
        ))}
      </FieldGroup>
        {!group.label && sectionType === 'EnvironmentAndDeployment' && systemId &&
          <EnvironmentAssociations systemId={systemId} hostingModel={values.hostingModel ?? ''}
            description={values.additionalDetails ?? ''} readOnly={isReadOnly || isSubmitting} busy={isSubmitting}
            onHostingStatusChange={onHostingStatusChange}
            onPrefill={suggested => setValues(current => ({ ...current, ...suggested }))} />}
      </Fragment>)}
      {sectionType === 'EnvironmentAndDeployment' && <p className="rounded-lg border border-indigo-200 bg-indigo-50 p-4 text-sm text-indigo-900 dark:border-indigo-900 dark:bg-indigo-950 dark:text-indigo-200">
        Complete applicable details before submitting the environment for review. If a detail is provider-managed or not applicable, explain why and cite the source in Deployment description.
        {' '}Recorded values still require review; these counts are not an ATO-readiness score. Your ISSM determines adequacy against the applicable controls.
      </p>}

      {/* Child entity CRUD table */}
      {child && !childTask && (
        <ChildEntityTable
          key={sourceKey}
          columns={child.columns}
          rows={rows}
          onChange={setRows}
          isReadOnly={isReadOnly}
          disabled={isSubmitting}
        />
      )}

      {/* Action buttons */}
      {!isReadOnly && canSubmit && dirty && (
        <p role="status" className="text-sm text-amber-700 dark:text-amber-300">
          {individualReviews ? 'Save draft changes before submitting access context for review.' : 'Save draft changes before submitting for review.'}
        </p>
      )}
      <div className="flex flex-wrap items-center gap-3 border-t border-slate-100 pt-5 dark:border-slate-700">
        {individualReviews && <p className="w-full text-xs text-slate-500">Access-context review does not review the individual categories. Open a category for its review actions.</p>}
        {!isReadOnly && (
          <>
            {(individualReviews || !formId) && <button
              type="submit"
              disabled={scalarEditingLocked || !!contentError}
              className="rounded-lg bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-700 disabled:opacity-50"
            >
              {isSubmitting ? 'Saving...' : individualReviews ? 'Save access context' : 'Save Draft'}
            </button>}
            {canSubmit && (
              <button
                type="button"
                onClick={() => { if (!editingLocked && !dirty && !contentError) onSubmit(); }}
                disabled={isSubmitting || dirty || !!contentError}
                className="rounded-lg bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-700 disabled:opacity-50"
              >
                {individualReviews ? 'Submit access context' : 'Submit for Review'}
              </button>
            )}
          </>
        )}
        {canWithdraw && (
          <button
            type="button"
            onClick={onWithdraw}
            disabled={isSubmitting || individualReviews && dirty}
            className="rounded-lg border border-gray-300 bg-white px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 disabled:opacity-50"
          >
            {individualReviews ? 'Withdraw access context' : 'Withdraw'}
          </button>
        )}
        {canReview && onApprove && (
          <button
            type="button"
            onClick={onApprove}
            disabled={isSubmitting || individualReviews && dirty}
            className="rounded-lg bg-green-600 px-4 py-2 text-sm font-medium text-white hover:bg-green-700 disabled:opacity-50"
          >
            {individualReviews ? 'Approve access context' : 'Approve'}
          </button>
        )}
        {canReview && onRequestRevision && (
          <button
            type="button"
            onClick={onRequestRevision}
            disabled={isSubmitting || individualReviews && dirty}
            className="rounded-lg bg-amber-600 px-4 py-2 text-sm font-medium text-white hover:bg-amber-700 disabled:opacity-50"
          >
            {individualReviews ? 'Request access-context revision' : 'Request Revision'}
          </button>
        )}
        {individualReviews && <button type="button" onClick={closeContextDialog} disabled={isSubmitting}
          className="rounded-lg border border-gray-300 px-4 py-2 text-sm font-medium disabled:opacity-50">
          {isReadOnly ? 'Close' : 'Cancel'}
        </button>}
      </div>
  </>;

  return (
    <>
      <form id={formId} className="space-y-5" onSubmit={event => {
        event.preventDefault();
        if (event.target === event.currentTarget) handleSave();
      }}>
        {individualReviews ? <>
          {!contextDialogOpen && error && <p role="alert" className="text-sm text-red-700">{error}</p>}
          {!contextDialogOpen && contentError && <p role="alert" className="text-sm text-red-700">{contentError}</p>}
          {child && childTask && <ChildEntityTable key={sourceKey} columns={child.columns} rows={rows} onChange={setRows}
            isReadOnly={isReadOnly} disabled={isSubmitting} title={childTask.title} addLabel={childTask.add}
            userCategories addEntryOpen={addEntryOpen} onAddEntryClose={onAddEntryClose}
            onReviewUserCategory={onReviewUserCategory} dirty={dirty} reviewError={error} savedRows={initialChildItems} />}
          {dirty && <p role="status" className="text-sm text-amber-700">
            Save draft changes before reviewing an individual user category.
          </p>}
          {!isReadOnly && !formId && <button type="submit" disabled={isSubmitting || !!contentError}
            className="rounded-lg bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-700 disabled:opacity-50">
            {isSubmitting ? 'Saving...' : 'Save Draft'}
          </button>}
        </> : sectionContext}
      </form>
      {individualReviews && contextDialogOpen && <SetupDialog title="System-wide access context"
        description="Manage and review the system-wide access model independently of individual user categories. Saving persists the access context and any pending category drafts together. Cancel discards only context edits made in this dialog."
        busy={isSubmitting} onClose={closeContextDialog}>
        <form className="space-y-5" onSubmit={event => {
          event.preventDefault();
          event.stopPropagation();
          if (!scalarEditingLocked) handleSave();
        }}>{sectionContext}</form>
      </SetupDialog>}
    </>
  );
}

// ─── Multi-Select Field ─────────────────────────────────────────────────────

interface MultiSelectFieldProps {
  label: string;
  options: string[];
  selected: string[];
  onChange: (selected: string[]) => void;
  disabled?: boolean;
  placeholder?: string;
}

function readSelection(value?: string): string[] {
  if (!value) return [];
  let parsed: unknown;
  try { parsed = JSON.parse(value); }
  catch (error) {
    if (error instanceof SyntaxError) return [value];
    throw error;
  }
  if (Array.isArray(parsed) && parsed.every(item => typeof item === 'string')) return parsed;
  return [typeof parsed === 'string' ? parsed : value];
}

function MultiSelectField({ label, options, selected, onChange, disabled, placeholder }: MultiSelectFieldProps) {
  const [open, setOpen] = useState(false);
  const [filter, setFilter] = useState('');
  const ref = useRef<HTMLDivElement>(null);
  const menuId = useId();

  useEffect(() => {
    if (!open) return;
    const handleClick = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false);
    };
    document.addEventListener('mousedown', handleClick);
    return () => document.removeEventListener('mousedown', handleClick);
  }, [open]);

  const toggle = (opt: string) => {
    if (disabled) return;
    onChange(selected.includes(opt) ? selected.filter((s) => s !== opt) : [...selected, opt]);
  };

  const filtered = options.filter((o) => o.toLowerCase().includes(filter.toLowerCase()));

  return (
    <div ref={ref} className="relative min-w-0" onKeyDown={event => {
      if (disabled || !(event.target instanceof HTMLElement)) return;
      const trigger = ref.current?.querySelector<HTMLElement>('[role="combobox"]');
      if (event.key === 'Escape' && open) {
        event.preventDefault(); event.stopPropagation(); setOpen(false); trigger?.focus(); return;
      }
      if (event.key === 'Enter' && open && event.target instanceof HTMLInputElement) {
        event.preventDefault(); event.stopPropagation();
        if (filtered[0]) toggle(filtered[0]);
        setFilter(''); return;
      }
      if (['Enter', ' '].includes(event.key) && event.target === trigger) {
        event.preventDefault(); setOpen(true); return;
      }
      if (!['ArrowDown', 'ArrowUp'].includes(event.key)) return;
      event.preventDefault();
      if (!open) { setOpen(true); return; }
      const choices = [...(ref.current?.querySelectorAll<HTMLButtonElement>('[role="option"]:not(:disabled)') ?? [])];
      if (!choices.length) return;
      const index = choices.findIndex(choice => choice === event.target);
      const next = index < 0 ? event.key === 'ArrowDown' ? 0 : choices.length - 1
        : (index + (event.key === 'ArrowDown' ? 1 : choices.length - 1)) % choices.length;
      choices[next]?.focus();
    }}>
      {/* Selected tags */}
      <div
        role="combobox" aria-label={label} aria-expanded={open} aria-haspopup="listbox"
        aria-controls={open ? menuId : undefined} aria-disabled={disabled === true} tabIndex={disabled ? -1 : 0}
        className={`min-h-[42px] w-full rounded-lg border border-gray-300 px-3 py-2 flex flex-wrap gap-1.5 items-center cursor-text ${
          disabled ? 'bg-gray-50' : 'bg-white hover:border-gray-400'
        } ${open ? 'border-indigo-500 ring-1 ring-indigo-500' : ''}`}
        onClick={() => { if (!disabled) setOpen(true); }}
      >
        {selected.length === 0 && !open && (
          <span className="text-sm text-gray-400">{placeholder ?? 'Select options...'}</span>
        )}
        {selected.map((s) => (
          <span
            key={s}
            className="inline-flex items-center gap-1 rounded-md bg-indigo-100 px-2 py-0.5 text-xs font-medium text-indigo-700"
          >
            {s}
            {!disabled && (
              <button
                type="button"
                aria-label={`Remove ${s}`}
                onClick={(e) => { e.stopPropagation(); toggle(s); }}
                className="text-indigo-500 hover:text-indigo-800"
              >
                <svg className="h-3 w-3" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2.5}>
                  <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" />
                </svg>
              </button>
            )}
          </span>
        ))}
        {open && (
          <input
            autoFocus
            disabled={disabled}
            value={filter}
            onChange={(e) => setFilter(e.target.value)}
            className="flex-1 min-w-[120px] outline-none text-sm text-gray-900 bg-transparent"
            placeholder="Type to filter..."
          />
        )}
      </div>

      {/* Dropdown */}
      {open && (
        <div id={menuId} role="listbox" aria-label={`${label} choices`} aria-multiselectable="true"
          className="absolute z-50 mt-1 w-full max-h-48 overflow-y-auto rounded-lg border border-gray-200 bg-white shadow-lg">
          {filtered.length === 0 && (
            <div className="px-3 py-2 text-sm text-gray-400">No matching options</div>
          )}
          {filtered.map((opt) => {
            const isSelected = selected.includes(opt);
            return (
              <button
                key={opt}
                type="button"
                role="option" aria-selected={isSelected}
                disabled={disabled}
                onClick={() => { toggle(opt); setFilter(''); }}
                className={`flex w-full items-center gap-2 px-3 py-2 text-sm text-left transition-colors ${
                  isSelected ? 'bg-indigo-50 text-indigo-700' : 'text-gray-700 hover:bg-gray-50'
                }`}
              >
                <span className={`flex h-4 w-4 items-center justify-center rounded border ${
                  isSelected ? 'border-indigo-600 bg-indigo-600' : 'border-gray-300'
                }`}>
                  {isSelected && (
                    <svg className="h-3 w-3 text-white" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={3}>
                      <path strokeLinecap="round" strokeLinejoin="round" d="M4.5 12.75l6 6 9-13.5" />
                    </svg>
                  )}
                </span>
                {opt}
              </button>
            );
          })}
        </div>
      )}
    </div>
  );
}

// ─── Child Entity Table ─────────────────────────────────────────────────────

interface ChildEntityTableProps {
  columns: ColDef[];
  rows: ChildRow[];
  onChange: (rows: ChildRow[]) => void;
  isReadOnly: boolean;
  title?: string;
  addLabel?: string;
  disabled?: boolean;
  userCategories?: boolean;
  dataTypes?: boolean;
  sectionGovernanceStatus?: GovernanceStatus;
  addEntryOpen?: boolean;
  onAddEntryClose?: () => void;
  onReviewUserCategory?: (id: string, request: UserCategoryReviewRequest) => Promise<boolean>;
  dirty?: boolean;
  reviewError?: string | null;
  savedRows?: readonly ChildRow[];
}

function ChildEntityTable({ columns, rows, onChange, isReadOnly, title, addLabel = 'Add Row', disabled = false,
  userCategories = false, dataTypes = false, sectionGovernanceStatus, addEntryOpen = false, onAddEntryClose, onReviewUserCategory, dirty = false, reviewError, savedRows }: ChildEntityTableProps) {
  const [editor, setEditor] = useState<{ index: number | null; row: ChildRow; invalidNumbers?: string[]; inspect?: boolean; confirmRemoval?: boolean;
    reviewAction?: UserCategoryReviewRequest['action']; comments?: string } | null>(null);
  const [removing, setRemoving] = useState<number | null>(null);
  const [validationError, setValidationError] = useState<string | null>(null);
  const dataTable = useRef<HTMLDivElement>(null);
  const dataDialogContent = useRef<HTMLDivElement>(null);
  const restoreAfterRemoval = useRef(false);
  const externalAddWasOpen = useRef(false);
  const locked = isReadOnly || disabled;
  const dataReviewState = dirty ? 'Unsaved section changes' : `Section: ${sectionGovernanceStatus ?? 'Unavailable'}`;
  const rowReviewLocked = editor?.row.governanceStatus === 'UnderReview';
  const reviewAvailable = onReviewUserCategory && typeof editor?.row.id === 'string' && Number.isInteger(editor?.row.revision);
  const reviewLabels = { submit: 'Confirm submission', withdraw: 'Confirm withdrawal', approve: editor?.row.pendingDeletion ? 'Confirm removal approval' : 'Confirm approval', request_revision: 'Confirm revision request' };
  const hasUnsavedRow = (row: ChildRow) => {
    const source = savedRows?.find(saved => saved.id === row.id);
    return !source || JSON.stringify(source) !== JSON.stringify(row);
  };
  const closeEditor = () => { if (disabled) return; setEditor(null); onAddEntryClose?.(); };

  useEffect(() => {
    if (dataTypes) dataDialogContent.current?.querySelector<HTMLElement>('input, select, button')?.focus();
  }, [dataTypes, editor?.inspect, editor?.confirmRemoval, editor?.index]);

  useEffect(() => {
    if (!dataTypes || !restoreAfterRemoval.current) return;
    restoreAfterRemoval.current = false;
    const target = dataTable.current?.querySelector<HTMLElement>('table button:not(:disabled)')
      ?? dataTable.current?.querySelector<HTMLElement>('button:not(:disabled)')
      ?? dataTable.current?.querySelector<HTMLElement>('table');
    target?.focus();
  }, [dataTypes, rows]);

  const addRow = useCallback(() => {
    if (locked) return;
    const newRow: ChildRow = { _tempId: crypto.randomUUID(), sortOrder: userCategories
      ? Math.max(-1, ...rows.map(row => typeof row.sortOrder === 'number' ? row.sortOrder : -1)) + 1 : rows.length };
    columns.forEach((col) => {
      newRow[col.key] = col.type === 'number' ? null : '';
    });
    setValidationError(null);
    setEditor({ index: null, row: newRow });
  }, [locked, columns, rows, userCategories]);
  useEffect(() => {
    if (addEntryOpen && (!dataTypes || !externalAddWasOpen.current)) addRow();
    externalAddWasOpen.current = addEntryOpen;
  }, [addEntryOpen, addRow, dataTypes]);

  const applyRow = () => {
    if (!editor || locked) return;
    const row = { ...editor.row };
    for (const col of columns) {
      const value = row[col.key];
      if (col.required && (value == null || String(value).trim() === '')) {
        setValidationError(`${col.label} is required.`);
        return;
      }
      if (dataTypes && col.maxLength && String(value ?? '').length > col.maxLength) {
        setValidationError(`${col.label} must be ${col.maxLength} characters or fewer.`);
        return;
      }
      if (editor.invalidNumbers?.includes(col.key)) {
        setValidationError(`${col.label} must be a finite, nonnegative number.`);
        return;
      }
      if (col.type === 'number' && value != null && value !== '') {
        const number = Number(value);
        if (!Number.isFinite(number) || number < 0) {
          setValidationError(`${col.label} must be a finite, nonnegative number.`);
          return;
        }
        if (col.key === 'approximateCount' && (!Number.isInteger(number) || number > 2147483647)) {
          setValidationError('Count must be a whole number between 0 and 2147483647.');
          return;
        }
        row[col.key] = number;
      }
    }
    onChange(editor.index === null ? [...rows, row]
      : rows.map((current, index) => index === editor.index ? row : current));
    closeEditor();
  };

  const removeRow = () => {
    if (removing === null || locked) return;
    onChange(rows.filter((_, index) => index !== removing).map((row, sortOrder) => ({ ...row, sortOrder })));
    setRemoving(null);
  };

  const moveRow = (from: number, to: number) => {
    if (locked || to < 0 || to >= rows.length) return;
    const updated = [...rows];
    const moved = updated.splice(from, 1)[0];
    if (!moved) return;
    updated.splice(to, 0, moved);
    onChange(updated.map((r, i) => ({ ...r, sortOrder: i })));
  };

  return (
    <div ref={dataTypes ? dataTable : undefined} className="space-y-2">
      {title && <div className={(userCategories || dataTypes) && onAddEntryClose ? 'sr-only' : 'mb-4 flex flex-wrap items-center justify-between gap-3'}>
        <h2 className="text-lg font-semibold">{title}</h2>
        {!isReadOnly && !onAddEntryClose && <button type="button" onClick={addRow} disabled={disabled}
          className="rounded-[7px] bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-700 disabled:opacity-50">{addLabel}</button>}
      </div>}
      {dataTypes ? <div className="relative overflow-x-auto">
        <table aria-label="Information types" tabIndex={-1} className="w-full min-w-[520px] table-fixed text-left text-xs">
          <thead><tr className="border-b border-slate-200">
            {['Data type', 'Context', 'Sensitivity', 'Review state', 'Open'].map((label, index) => <th key={label} scope="col"
              className={`px-2.5 py-3 text-[10px] font-semibold uppercase tracking-wide text-slate-500 ${index === 4 ? 'w-20' : ''}`}>
              {label}
            </th>)}
          </tr></thead>
          <tbody className="divide-y divide-slate-100">
            {rows.length === 0 && <tr><td colSpan={5} className="px-2.5 py-10 text-slate-500">
              No information types are recorded.{!isReadOnly && ' Choose Add data type to describe the information handled by this system.'}
            </td></tr>}
            {rows.map((row, index) => <tr key={row.id ?? row._tempId ?? index}>
              <td className="break-words px-2.5 py-4 font-semibold">{row.dataTypeName}</td>
              <td className="px-2.5 py-4"><span className="line-clamp-2 break-words">{row.description || 'Not recorded'}</span></td>
              <td className="break-words px-2.5 py-4">{row.sensitivityClassification || 'Not recorded'}</td>
              <td className="break-words px-2.5 py-4 text-slate-500">{dataReviewState}</td>
              <td className="px-2.5 py-4"><button type="button" disabled={disabled}
                aria-label={`Open data type ${row.dataTypeName}`}
                onClick={() => { setValidationError(null); setEditor({ index, row: { ...row }, inspect: true }); }}
                className="rounded-md border border-slate-200 px-2 py-1 text-[11px] text-indigo-700 disabled:opacity-50">Open →</button></td>
            </tr>)}
          </tbody>
        </table>
      </div> : userCategories ? <div className="relative overflow-x-auto">
        <table aria-label="User categories" className="w-full min-w-[520px] table-fixed text-left text-xs">
          <thead><tr className="border-b border-slate-200">
            {['Category', 'Context', 'Count', 'Access method', ''].map((label, index) => <th key={index} scope="col"
              className={`px-2.5 py-3 text-[10px] font-semibold uppercase tracking-wide text-slate-500 ${index === 2 ? 'w-16' : index === 4 ? 'w-20' : ''}`}>
              {label || <span className="sr-only">Actions</span>}
            </th>)}
          </tr></thead>
          <tbody className="divide-y divide-slate-100">
            {rows.length === 0 && <tr><td colSpan={5} className="px-2.5 py-10 text-slate-500">
              No user categories are recorded.{!isReadOnly && ' Choose Add user category to describe a population.'}
            </td></tr>}
            {rows.map((row, index) => <tr key={row.id ?? row._tempId ?? index}>
              <td className="break-words px-2.5 py-4 font-semibold">{row.categoryName}
                <span className="mt-1 block text-[10px] font-normal text-slate-500">{hasUnsavedRow(row) ? 'Unsaved changes' : row.governanceStatus ?? 'Review status unavailable'}</span>
                {row.pendingDeletion && <span className="mt-1 block text-[10px] font-normal text-amber-700">Removal requested</span>}
              </td>
              <td className="px-2.5 py-4"><span className="line-clamp-2 break-words">{row.description || 'Not recorded'}</span></td>
              <td className="px-2.5 py-4">{row.approximateCount ?? 'Not recorded'}</td>
              <td className="break-words px-2.5 py-4">{row.accessMethod || 'Not recorded'}</td>
              <td className="px-2.5 py-4"><button type="button" disabled={disabled}
                aria-label={`Open user category ${row.categoryName}`}
                onClick={() => { setValidationError(null); setEditor({ index, row: { ...row }, inspect: true }); }}
                className="rounded-md border border-slate-200 px-2 py-1 text-[11px] text-indigo-700 disabled:opacity-50">Open →</button></td>
            </tr>)}
          </tbody>
        </table>
      </div> : <div className="relative overflow-x-auto rounded-lg border border-gray-200">
        <table className="w-full min-w-[720px] text-sm">
          <thead className="bg-gray-50 text-left">
            <tr>
              {!isReadOnly && <th className="px-2 py-2 w-16"><span className="sr-only">Order</span></th>}
              {columns.map((col) => (
                <th key={col.key} className={`px-3 py-2 font-medium text-gray-600 ${col.width ?? ''}`}>
                  {col.label}
                  {col.required && <span className="text-red-500 ml-0.5">*</span>}
                </th>
              ))}
              {!isReadOnly && <th className="px-2 py-2"><span className="sr-only">Actions</span></th>}
            </tr>
          </thead>
          <tbody className="divide-y divide-gray-100">
            {rows.length === 0 && (
              <tr>
                <td colSpan={columns.length + (isReadOnly ? 0 : 2)} className="px-3 py-6 text-center text-gray-400">
                  No entries yet.{!isReadOnly && ` Choose "${addLabel}" to begin.`}
                </td>
              </tr>
            )}
            {rows.map((row, ri) => (
              <tr key={row.id ?? row._tempId ?? ri} className="hover:bg-gray-50">
                {!isReadOnly && (
                  <td className="px-2 py-1.5">
                    <div className="flex flex-col items-center gap-0.5">
                      <button type="button" onClick={() => moveRow(ri, ri - 1)} disabled={disabled || ri === 0}
                        className="text-gray-400 hover:text-gray-600 disabled:opacity-30" title="Move up">
                        <svg className="h-3.5 w-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                          <path strokeLinecap="round" strokeLinejoin="round" d="M4.5 15.75l7.5-7.5 7.5 7.5" />
                        </svg>
                      </button>
                      <button type="button" onClick={() => moveRow(ri, ri + 1)} disabled={disabled || ri === rows.length - 1}
                        className="text-gray-400 hover:text-gray-600 disabled:opacity-30" title="Move down">
                        <svg className="h-3.5 w-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                          <path strokeLinecap="round" strokeLinejoin="round" d="M19.5 8.25l-7.5 7.5-7.5-7.5" />
                        </svg>
                      </button>
                    </div>
                  </td>
                )}
                {columns.map((col) => (
                  <td key={col.key} className="px-3 py-1.5">
                    <span className="block min-w-24 py-2 text-slate-700 dark:text-slate-200">{row[col.key] ?? '—'}</span>
                  </td>
                ))}
                {!isReadOnly && (
                  <td className="px-2 py-1.5">
                    <div className="flex gap-3">
                      <button type="button" aria-label={`Edit row ${ri + 1}`} disabled={disabled}
                        onClick={() => { setValidationError(null); setEditor({ index: ri, row: { ...row } }); }}
                        className="text-indigo-600 hover:underline disabled:opacity-50">Edit</button>
                      <button type="button" aria-label={`Remove row ${ri + 1}`} disabled={disabled} onClick={() => setRemoving(ri)}
                        className="text-red-600 hover:underline disabled:opacity-50" title="Remove row">Remove</button>
                    </div>
                  </td>
                )}
              </tr>
            ))}
          </tbody>
        </table>
      </div>}
      {!isReadOnly && !title && (
        <button
          type="button"
          onClick={addRow}
          disabled={disabled}
          className="inline-flex items-center gap-1.5 rounded-lg border border-dashed border-gray-300 px-3 py-1.5 text-sm text-gray-600 hover:border-gray-400 hover:text-gray-800 disabled:opacity-50"
        >
          <svg className="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M12 4.5v15m7.5-7.5h-15" />
          </svg>
          {addLabel}
        </button>
      )}
      {editor && (!isReadOnly || userCategories || dataTypes) && <SetupDialog busy={disabled} onClose={closeEditor}
        title={dataTypes ? editor.inspect ? `Data type: ${editor.row.dataTypeName}`
          : editor.confirmRemoval ? 'Remove data type' : editor.index === null ? addLabel : 'Edit data type'
          : editor.reviewAction ? `${reviewLabels[editor.reviewAction]}: ${editor.row.categoryName}`
          : editor.inspect ? `User category: ${editor.row.categoryName}` : editor.confirmRemoval ? 'Remove user category'
          : editor.index === null ? addLabel : userCategories ? 'Edit user category' : `Edit ${title?.toLowerCase() ?? 'entry'}`}
        description={editor.inspect ? dataTypes ? 'Information handled by this system. Data types are reviewed with the section, not individually.'
          : 'Documented population and access needs. This record does not grant accounts, roles or application access.'
          : 'Apply changes to the local draft, then use Save Draft to persist them.'}>
        <div ref={dataTypes ? dataDialogContent : undefined}>
        {dataTypes && editor.inspect ? <div className="space-y-5">
          <dl className="grid gap-4 sm:grid-cols-2">{columns.map(col => <div key={col.key} className={col.key === 'description' ? 'sm:col-span-2' : ''}>
            <dt className="text-xs text-slate-500">{col.label}</dt>
            <dd className="mt-1 whitespace-pre-wrap break-words text-sm">{editor.row[col.key] || 'Not recorded'}</dd>
          </div>)}</dl>
          <p className="text-xs text-slate-500">{dataReviewState}</p>
          <div className="flex flex-wrap gap-3">
            {!isReadOnly && <>
              <button type="button" disabled={disabled} className="rounded border px-3 py-2 text-sm disabled:opacity-50"
                onClick={() => setEditor({ ...editor, inspect: false })}>Edit data type</button>
              <button type="button" disabled={disabled} className="rounded border px-3 py-2 text-sm text-red-700 disabled:opacity-50"
                onClick={() => setEditor({ ...editor, inspect: false, confirmRemoval: true })}>Remove data type</button>
              <button type="button" disabled={disabled || editor.index === 0} className="rounded border px-3 py-2 text-sm disabled:opacity-50"
                onClick={() => { const index = editor.index!; moveRow(index, index - 1); setEditor({ ...editor, index: index - 1, row: { ...editor.row, sortOrder: index - 1 } }); }}>Move up</button>
              <button type="button" disabled={disabled || editor.index === rows.length - 1} className="rounded border px-3 py-2 text-sm disabled:opacity-50"
                onClick={() => { const index = editor.index!; moveRow(index, index + 1); setEditor({ ...editor, index: index + 1, row: { ...editor.row, sortOrder: index + 1 } }); }}>Move down</button>
            </>}
            <button type="button" disabled={disabled} onClick={closeEditor} className="rounded border px-3 py-2 text-sm">Close</button>
          </div>
        </div> : editor.reviewAction ? <form className="space-y-4" onSubmit={async event => {
          event.preventDefault(); event.stopPropagation();
          if (!onReviewUserCategory || !reviewAvailable || disabled || dirty) return;
          const comments = editor.comments?.trim();
          if (editor.reviewAction === 'request_revision' && !comments) { setValidationError('Enter review comments.'); return; }
          const confirmed = await onReviewUserCategory(editor.row.id, { action: editor.reviewAction!,
            expectedRevision: editor.row.revision, ...(comments ? { comments } : {}) });
          if (confirmed) closeEditor();
        }}>
          <p className="text-sm">This action applies only to {editor.row.categoryName}, revision {editor.row.revision}. It does not approve other categories or grant application access.</p>
          {editor.row.pendingDeletion && <p className="text-sm text-amber-800">This is a removal request. Approval removes this category from current approved document sources; its history remains retained.</p>}
          {(reviewError || validationError) && <p role="alert" className="text-sm text-red-700">{reviewError || validationError}</p>}
          <label className="block text-sm">Review comments
            <textarea className="mt-2 block w-full rounded border p-3" maxLength={4000} disabled={disabled}
              required={editor.reviewAction === 'request_revision'} value={editor.comments ?? ''}
              onChange={event => setEditor({ ...editor, comments: event.target.value })} />
          </label>
          <div className="flex flex-wrap gap-3">
            <button type="button" disabled={disabled} className="rounded border px-3 py-2"
              onClick={() => setEditor({ ...editor, reviewAction: undefined, inspect: true })}>Cancel</button>
            <button type="submit" disabled={disabled || dirty || !reviewAvailable} className="rounded bg-indigo-600 px-3 py-2 text-white disabled:opacity-50">
              {reviewLabels[editor.reviewAction]}
            </button>
          </div>
        </form> : editor.inspect ? <div className="space-y-5">
          <dl className="grid gap-4 sm:grid-cols-2">{columns.map(col => <div key={col.key} className={col.key === 'description' ? 'sm:col-span-2' : ''}>
            <dt className="text-xs text-slate-500">{col.label}</dt>
            <dd className="mt-1 whitespace-pre-wrap break-words text-sm">{editor.row[col.key] ?? 'Not recorded'}</dd>
          </div>)}</dl>
          <div className="space-y-1 text-xs text-slate-500">
            {hasUnsavedRow(editor.row) && <p>Unsaved changes. The recorded review status does not approve these edits.</p>}
            <p>Review status: {editor.row.governanceStatus ?? 'Unavailable'} · Revision: {editor.row.revision ?? (editor.row.id ? 'Unavailable' : 'Not saved')}</p>
            <p>Order: {(editor.index ?? 0) + 1} of {rows.length}. This category is reviewed independently.</p>
            {editor.row.reviewerComments && <p className="whitespace-pre-wrap">Reviewer comments: {editor.row.reviewerComments}</p>}
            {dirty && <p>Save draft changes before reviewing this category.</p>}
            {rowReviewLocked && <p>This category is under review. Withdraw it before editing.</p>}
            {editor.row.pendingDeletion && <p>Removal requested. The previously approved version remains in document sources until this removal is approved.</p>}
            {!reviewAvailable && <p>{editor.row.id ? 'Individual review metadata is unavailable from the server.' : 'Save this new category before requesting review.'}</p>}
            {reviewAvailable && ![editor.row.canSubmit, editor.row.canWithdraw, editor.row.canReview].includes(true)
              && <p>No review action is currently available for this category and your role.</p>}
          </div>
          <div className="flex flex-wrap gap-3">
            {!isReadOnly && <>
              <button type="button" disabled={disabled || rowReviewLocked || editor.row.pendingDeletion} className="rounded border px-3 py-2 text-sm disabled:opacity-50"
                onClick={() => setEditor({ ...editor, inspect: false })}>Edit category</button>
              <button type="button" disabled={disabled || rowReviewLocked || editor.row.pendingDeletion} className="rounded border px-3 py-2 text-sm text-red-700 disabled:opacity-50"
                onClick={() => setEditor({ ...editor, inspect: false, confirmRemoval: true })}>Remove category</button>
              {editor.row.pendingDeletion && <button type="button" disabled={disabled || rowReviewLocked}
                className="rounded border px-3 py-2 text-sm disabled:opacity-50"
                onClick={() => { onChange(rows.map((row, index) => index === editor.index ? { ...row, pendingDeletion: false } : row)); closeEditor(); }}>
                Cancel removal request
              </button>}
              <button type="button" disabled={disabled || rows.some(row => row.governanceStatus === 'UnderReview') || editor.index === 0} className="rounded border px-3 py-2 text-sm disabled:opacity-50"
                onClick={() => { const index = editor.index!; moveRow(index, index - 1); setEditor({ ...editor, index: index - 1, row: { ...editor.row, sortOrder: index - 1 } }); }}>Move up</button>
              <button type="button" disabled={disabled || rows.some(row => row.governanceStatus === 'UnderReview') || editor.index === rows.length - 1} className="rounded border px-3 py-2 text-sm disabled:opacity-50"
                onClick={() => { const index = editor.index!; moveRow(index, index + 1); setEditor({ ...editor, index: index + 1, row: { ...editor.row, sortOrder: index + 1 } }); }}>Move down</button>
            </>}
            {reviewAvailable && ([
              ['submit', 'Submit category for review', editor.row.canSubmit],
              ['withdraw', 'Withdraw category', editor.row.canWithdraw],
              ['approve', editor.row.pendingDeletion ? 'Approve removal' : 'Approve category', editor.row.canReview],
              ['request_revision', 'Request category revision', editor.row.canReview],
            ] as const).filter(([, , allowed]) => allowed === true).map(([action, label]) =>
              <button key={action} type="button" disabled={disabled || dirty}
                className="rounded border border-indigo-300 px-3 py-2 text-sm text-indigo-700 disabled:opacity-50"
                onClick={() => { setValidationError(null); setEditor({ ...editor, inspect: false, reviewAction: action, comments: '' }); }}>{label}</button>)}
            <button type="button" disabled={disabled} onClick={closeEditor} className="rounded border px-3 py-2 text-sm">Close</button>
          </div>
        </div> : editor.confirmRemoval ? <div className="space-y-4">
          <p className="text-sm">{dataTypes ? `Remove ${editor.row.dataTypeName} from the draft? The saved record is unchanged until Save Draft.`
            : editor.row.approvedSnapshotId
            ? `Request removal of ${editor.row.categoryName}? Save Draft records the request; individual review must approve removal before the approved document source is removed.`
            : `Remove ${editor.row.categoryName} from the draft? The saved record is unchanged until Save Draft.`}</p>
          <button type="button" disabled={disabled} onClick={() => setEditor({ ...editor, confirmRemoval: false, inspect: true })}
            className="mr-3 rounded border px-3 py-2 text-sm">Cancel</button>
          <button type="button" disabled={locked} className="rounded bg-red-700 px-3 py-2 text-sm text-white"
            onClick={() => {
              if (locked) return;
              const remaining = rows.filter((_, index) => index !== editor.index);
              if (dataTypes) restoreAfterRemoval.current = true;
              onChange(dataTypes ? remaining.map((row, sortOrder) => ({ ...row, sortOrder })) : remaining);
              closeEditor();
            }}>
            {!dataTypes && editor.row.approvedSnapshotId ? 'Request removal' : 'Remove from draft'}
          </button>
        </div> : !isReadOnly && <form noValidate onSubmit={event => { event.preventDefault(); event.stopPropagation(); applyRow(); }} className="space-y-4">
          {validationError && <p role="alert" className="text-sm text-red-700">{validationError}</p>}
          {columns.map(col => <div key={col.key}>
            <label htmlFor={`child-${col.key}`} className="mb-1 block text-sm font-medium">
              {col.label}{col.required && <span aria-hidden="true" className="ml-1 text-red-500">*</span>}
            </label>
            {col.type === 'select' ? <select id={`child-${col.key}`} aria-label={col.label}
              required={col.required} disabled={disabled} value={editor.row[col.key] ?? ''}
              onChange={event => setEditor({ ...editor, row: { ...editor.row, [col.key]: event.target.value } })}
              className="w-full rounded-lg border border-gray-300 bg-white px-3 py-2 text-sm dark:border-gray-600 dark:bg-gray-800">
              <option value="">— Select —</option>
              {editor.row[col.key] && !col.options?.includes(editor.row[col.key]) &&
                <option value={editor.row[col.key]}>{editor.row[col.key]} (previously recorded)</option>}
              {col.options?.map(option => <option key={option} value={option}>{option}</option>)}
            </select> : <input id={`child-${col.key}`} aria-label={col.label}
              list={col.type === 'text' && col.options ? `child-${col.key}-suggestions` : undefined}
              type={col.type === 'number' ? 'number' : 'text'} min={col.type === 'number' ? 0 : undefined}
              step={col.type === 'number' ? col.key === 'approximateCount' ? 1 : 'any' : undefined}
              required={col.required} disabled={disabled} maxLength={col.maxLength} value={editor.row[col.key] ?? ''}
              onChange={event => setEditor({
                ...editor,
                invalidNumbers: [
                  ...(editor.invalidNumbers ?? []).filter(key => key !== col.key),
                  ...(col.type === 'number' && event.target.validity.badInput ? [col.key] : []),
                ],
                row: { ...editor.row, [col.key]:
                  col.type === 'number' && event.target.value === '' ? null : event.target.value },
              })}
              className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm dark:border-gray-600 dark:bg-gray-800" />}
            {col.type === 'text' && col.options && <datalist id={`child-${col.key}-suggestions`}>
              {col.options.map(option => <option key={option} value={option} />)}
            </datalist>}
          </div>)}
          <div className="flex justify-end gap-3 border-t pt-4">
            <button type="button" disabled={disabled} onClick={() => {
              if (dataTypes && editor.index !== null) {
                setValidationError(null);
                setEditor({ index: editor.index, row: { ...rows[editor.index] }, inspect: true });
              } else closeEditor();
            }}
              className="rounded-lg border px-4 py-2 text-sm disabled:opacity-50">Cancel</button>
            <button type="submit" disabled={disabled}
              className="rounded-lg bg-indigo-600 px-4 py-2 text-sm font-medium text-white disabled:opacity-50">Apply to draft</button>
          </div>
        </form>}
        </div>
      </SetupDialog>}
      {removing !== null && !isReadOnly && <SetupDialog busy={disabled} onClose={() => setRemoving(null)}
        title="Remove entry?" description="Removal only changes the local draft until you save it.">
        <p className="text-sm">Remove {String(rows[removing]?.[columns[0]?.key ?? ''] ?? 'this entry')} from the draft?</p>
        <div className="mt-5 flex justify-end gap-3">
          <button type="button" disabled={disabled} onClick={() => setRemoving(null)}
            className="rounded-lg border px-4 py-2 text-sm disabled:opacity-50">Cancel</button>
          <button type="button" disabled={disabled} onClick={removeRow}
            className="rounded-lg bg-red-600 px-4 py-2 text-sm font-medium text-white disabled:opacity-50">Remove from draft</button>
        </div>
      </SetupDialog>}
    </div>
  );
}
