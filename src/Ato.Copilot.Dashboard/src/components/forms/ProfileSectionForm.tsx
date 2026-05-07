import { useState, useEffect, useCallback, useRef } from 'react';
import type {
  ProfileSectionType,
  GovernanceStatus,
  UserCategoryItem,
  DataTypeItem,
  PpsItem,
  LeveragedAuthItem,
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
    { key: 'hostingModel', label: 'Hosting Model', type: 'select', required: true, options: ['Cloud (IaaS)', 'Cloud (PaaS)', 'Cloud (SaaS)', 'On-Premises', 'Hybrid', 'Government Cloud (GovCloud)', 'Private Cloud'] },
    { key: 'cloudProvider', label: 'Cloud Service Provider', type: 'multiselect', options: [
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
    { key: 'additionalDetails', label: 'Additional Details', type: 'textarea', maxLength: 4000, rows: 3, placeholder: 'Any additional environment details not covered above...' },
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
      { key: 'categoryName', label: 'Category', type: 'select', required: true, options: [
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
      { key: 'dataTypeName', label: 'Data Type', type: 'select', required: true, options: [
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
  sectionType: ProfileSectionType;
  governanceStatus: GovernanceStatus;
  initialContent: string | null;
  initialChildItems?: UserCategoryItem[] | DataTypeItem[] | PpsItem[] | LeveragedAuthItem[];
  reviewerComments: string | null;
  isReadOnly: boolean;
  userRole: string;
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

export default function ProfileSectionForm({
  sectionType,
  governanceStatus,
  initialContent,
  initialChildItems,
  reviewerComments,
  isReadOnly,
  userRole,
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
  const [prefilled, setPrefilled] = useState(false);

  // ─── Scalar field state ─────────────────────────────────────────────
  const [values, setValues] = useState<Record<string, string>>(() => {
    try {
      return initialContent ? JSON.parse(initialContent) : {};
    } catch {
      return {};
    }
  });

  // ─── Child entity state ─────────────────────────────────────────────
  const [rows, setRows] = useState<ChildRow[]>(() =>
    initialChildItems ? [...(initialChildItems as ChildRow[])] : [],
  );

  useEffect(() => {
    try {
      setValues(initialContent ? JSON.parse(initialContent) : {});
    } catch {
      setValues({});
    }
    setRows(initialChildItems ? [...(initialChildItems as ChildRow[])] : []);
  }, [initialContent, initialChildItems]);

  // AI pre-fill: when section is NotStarted and no content exists, populate from system data
  useEffect(() => {
    if (prefilled || initialContent || governanceStatus !== 'NotStarted' || !systemContext) return;
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
    const content = JSON.stringify(values);
    onSave(content, child ? rows : undefined);
  };

  const isOptionalSection = sectionType === 'LeveragedAuthorizations';
  const canSubmit = governanceStatus === 'Draft' || governanceStatus === 'NeedsRevision';
  const canWithdraw = governanceStatus === 'UnderReview' && userRole === 'MissionOwner';
  const canReview = governanceStatus === 'UnderReview' && userRole === 'ISSM';

  return (
    <div className="space-y-5">
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
        <div className="rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-700">
          {error}
        </div>
      )}

      {/* Under Review indicator */}
      {governanceStatus === 'UnderReview' && (
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

      {/* Scalar fields */}
      <div className="space-y-4">
        {fields.map((field) => (
          <div key={field.key}>
            <label className="block text-sm font-medium text-gray-700 mb-1">
              {field.label}
              {field.required && <span className="text-red-500 ml-0.5">*</span>}
            </label>
            {field.type === 'textarea' ? (
              <>
                <textarea
                  value={values[field.key] ?? ''}
                  onChange={(e) => handleFieldChange(field.key, e.target.value)}
                  disabled={isReadOnly}
                  rows={field.rows ?? 3}
                  maxLength={field.maxLength}
                  className="w-full rounded-lg border border-gray-300 px-4 py-2.5 text-sm text-gray-900 placeholder-gray-400 focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500 disabled:bg-gray-50 disabled:text-gray-500"
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
                options={field.options ?? []}
                selected={(() => { try { const v = values[field.key]; return v ? JSON.parse(v) : []; } catch { return []; } })()}
                onChange={(sel) => handleFieldChange(field.key, JSON.stringify(sel))}
                disabled={isReadOnly}
                placeholder={field.placeholder}
              />
            ) : field.type === 'select' ? (
              <select
                value={values[field.key] ?? ''}
                onChange={(e) => handleFieldChange(field.key, e.target.value)}
                disabled={isReadOnly}
                className="w-full rounded-lg border border-gray-300 px-4 py-2.5 text-sm text-gray-900 focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500 disabled:bg-gray-50 disabled:text-gray-500"
              >
                <option value="">— Select —</option>
                {field.options?.map((opt) => (
                  <option key={opt} value={opt}>{opt}</option>
                ))}
              </select>
            ) : (
              <input
                type="text"
                value={values[field.key] ?? ''}
                onChange={(e) => handleFieldChange(field.key, e.target.value)}
                disabled={isReadOnly}
                maxLength={field.maxLength}
                className="w-full rounded-lg border border-gray-300 px-4 py-2.5 text-sm text-gray-900 placeholder-gray-400 focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500 disabled:bg-gray-50 disabled:text-gray-500"
                placeholder={field.placeholder}
              />
            )}
          </div>
        ))}
      </div>

      {/* Child entity CRUD table */}
      {child && (
        <ChildEntityTable
          columns={child.columns}
          rows={rows}
          onChange={setRows}
          isReadOnly={isReadOnly}
        />
      )}

      {/* Action buttons */}
      <div className="flex items-center gap-3 pt-2">
        {!isReadOnly && (
          <>
            <button
              type="button"
              onClick={handleSave}
              disabled={isSubmitting}
              className="rounded-lg bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-700 disabled:opacity-50"
            >
              {isSubmitting ? 'Saving...' : 'Save Draft'}
            </button>
            {canSubmit && (
              <button
                type="button"
                onClick={onSubmit}
                disabled={isSubmitting}
                className="rounded-lg bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-700 disabled:opacity-50"
              >
                Submit for Review
              </button>
            )}
          </>
        )}
        {canWithdraw && (
          <button
            type="button"
            onClick={onWithdraw}
            disabled={isSubmitting}
            className="rounded-lg border border-gray-300 bg-white px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 disabled:opacity-50"
          >
            Withdraw
          </button>
        )}
        {canReview && onApprove && (
          <button
            type="button"
            onClick={onApprove}
            disabled={isSubmitting}
            className="rounded-lg bg-green-600 px-4 py-2 text-sm font-medium text-white hover:bg-green-700 disabled:opacity-50"
          >
            Approve
          </button>
        )}
        {canReview && onRequestRevision && (
          <button
            type="button"
            onClick={onRequestRevision}
            disabled={isSubmitting}
            className="rounded-lg bg-amber-600 px-4 py-2 text-sm font-medium text-white hover:bg-amber-700 disabled:opacity-50"
          >
            Request Revision
          </button>
        )}
      </div>
    </div>
  );
}

// ─── Multi-Select Field ─────────────────────────────────────────────────────

interface MultiSelectFieldProps {
  options: string[];
  selected: string[];
  onChange: (selected: string[]) => void;
  disabled?: boolean;
  placeholder?: string;
}

function MultiSelectField({ options, selected, onChange, disabled, placeholder }: MultiSelectFieldProps) {
  const [open, setOpen] = useState(false);
  const [filter, setFilter] = useState('');
  const ref = useRef<HTMLDivElement>(null);

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
    <div ref={ref} className="relative">
      {/* Selected tags */}
      <div
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
            value={filter}
            onChange={(e) => setFilter(e.target.value)}
            className="flex-1 min-w-[120px] outline-none text-sm text-gray-900 bg-transparent"
            placeholder="Type to filter..."
          />
        )}
      </div>

      {/* Dropdown */}
      {open && (
        <div className="absolute z-50 mt-1 w-full max-h-48 overflow-y-auto rounded-lg border border-gray-200 bg-white shadow-lg">
          {filtered.length === 0 && (
            <div className="px-3 py-2 text-sm text-gray-400">No matching options</div>
          )}
          {filtered.map((opt) => {
            const isSelected = selected.includes(opt);
            return (
              <button
                key={opt}
                type="button"
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
}

function ChildEntityTable({ columns, rows, onChange, isReadOnly }: ChildEntityTableProps) {
  const addRow = () => {
    const newRow: ChildRow = { _tempId: crypto.randomUUID(), sortOrder: rows.length };
    columns.forEach((col) => {
      newRow[col.key] = col.type === 'number' ? null : '';
    });
    onChange([...rows, newRow]);
  };

  const updateCell = (index: number, key: string, value: string | number | null) => {
    const updated = [...rows];
    updated[index] = { ...updated[index], [key]: value };
    onChange(updated);
  };

  const removeRow = (index: number) => {
    onChange(rows.filter((_, i) => i !== index));
  };

  const moveRow = (from: number, to: number) => {
    if (to < 0 || to >= rows.length) return;
    const updated = [...rows];
    const moved = updated.splice(from, 1)[0];
    if (!moved) return;
    updated.splice(to, 0, moved);
    onChange(updated.map((r, i) => ({ ...r, sortOrder: i })));
  };

  return (
    <div className="space-y-2">
      <div className="overflow-x-auto rounded-lg border border-gray-200">
        <table className="min-w-full text-sm">
          <thead className="bg-gray-50 text-left">
            <tr>
              {!isReadOnly && <th className="px-2 py-2 w-16" />}
              {columns.map((col) => (
                <th key={col.key} className={`px-3 py-2 font-medium text-gray-600 ${col.width ?? ''}`}>
                  {col.label}
                  {col.required && <span className="text-red-500 ml-0.5">*</span>}
                </th>
              ))}
              {!isReadOnly && <th className="px-2 py-2 w-10" />}
            </tr>
          </thead>
          <tbody className="divide-y divide-gray-100">
            {rows.length === 0 && (
              <tr>
                <td colSpan={columns.length + (isReadOnly ? 0 : 2)} className="px-3 py-6 text-center text-gray-400">
                  No entries yet.{!isReadOnly && ' Click "Add Row" to begin.'}
                </td>
              </tr>
            )}
            {rows.map((row, ri) => (
              <tr key={row.id ?? row._tempId ?? ri} className="hover:bg-gray-50">
                {!isReadOnly && (
                  <td className="px-2 py-1.5">
                    <div className="flex flex-col items-center gap-0.5">
                      <button type="button" onClick={() => moveRow(ri, ri - 1)} disabled={ri === 0}
                        className="text-gray-400 hover:text-gray-600 disabled:opacity-30" title="Move up">
                        <svg className="h-3.5 w-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                          <path strokeLinecap="round" strokeLinejoin="round" d="M4.5 15.75l7.5-7.5 7.5 7.5" />
                        </svg>
                      </button>
                      <button type="button" onClick={() => moveRow(ri, ri + 1)} disabled={ri === rows.length - 1}
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
                    {col.type === 'select' ? (
                      <select
                        value={row[col.key] ?? ''}
                        onChange={(e) => updateCell(ri, col.key, e.target.value)}
                        disabled={isReadOnly}
                        className="w-full rounded border border-gray-300 px-2 py-1.5 text-sm focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500 disabled:bg-gray-50"
                      >
                        <option value="">—</option>
                        {col.options?.map((opt) => (
                          <option key={opt} value={opt}>{opt}</option>
                        ))}
                      </select>
                    ) : col.type === 'number' ? (
                      <input
                        type="number"
                        value={row[col.key] ?? ''}
                        onChange={(e) => updateCell(ri, col.key, e.target.value ? Number(e.target.value) : null)}
                        disabled={isReadOnly}
                        className="w-full rounded border border-gray-300 px-2 py-1.5 text-sm focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500 disabled:bg-gray-50"
                      />
                    ) : (
                      <input
                        type="text"
                        value={row[col.key] ?? ''}
                        onChange={(e) => updateCell(ri, col.key, e.target.value)}
                        disabled={isReadOnly}
                        maxLength={col.maxLength}
                        className="w-full rounded border border-gray-300 px-2 py-1.5 text-sm focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500 disabled:bg-gray-50"
                      />
                    )}
                  </td>
                ))}
                {!isReadOnly && (
                  <td className="px-2 py-1.5">
                    <button type="button" onClick={() => removeRow(ri)}
                      className="text-red-400 hover:text-red-600" title="Remove row">
                      <svg className="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                        <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" />
                      </svg>
                    </button>
                  </td>
                )}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {!isReadOnly && (
        <button
          type="button"
          onClick={addRow}
          className="inline-flex items-center gap-1.5 rounded-lg border border-dashed border-gray-300 px-3 py-1.5 text-sm text-gray-600 hover:border-gray-400 hover:text-gray-800"
        >
          <svg className="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M12 4.5v15m7.5-7.5h-15" />
          </svg>
          Add Row
        </button>
      )}
    </div>
  );
}
