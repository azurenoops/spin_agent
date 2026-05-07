import { useState, useEffect } from 'react';
import {
  createPta,
  addInterconnection,
  certifyNoInterconnections,
  generateAndApprovePia,
  setCategorization,
  selectBaseline,
} from '../../api/systemDetail';
import { assignRole, fetchRoles, deleteRole } from '../../api/roles';
import type { RoleAssignment } from '../../api/roles';
import { listComponents } from '../../api/components';
import type { OrgComponentDto } from '../../api/components';
import type {
  CreatePtaRequest,
  AddInterconnectionRequest,
  InfoTypeInput,
} from '../../api/systemDetail';

export type GateAction = 'pta' | 'pia' | 'interconnection' | 'certify-none' | 'categorization' | 'baseline' | 'roles';

interface GateActionDialogProps {
  action: GateAction;
  systemId: string;
  onClose: () => void;
  onSuccess: () => void;
}

const PII_CATEGORIES = ['Name', 'SSN', 'Email', 'Phone', 'Address', 'Financial', 'Medical', 'Biometric'];
const IMPACT_LEVELS = ['Low', 'Moderate', 'High'] as const;

const COMMON_INFO_TYPES: { sp80060Id: string; name: string; category: string; c: string; i: string; a: string }[] = [
  { sp80060Id: 'C.2.8.12', name: 'System Development', category: 'Management & Support', c: 'Low', i: 'Low', a: 'Low' },
  { sp80060Id: 'C.3.5.1', name: 'Access Control', category: 'Services Delivery', c: 'Moderate', i: 'Moderate', a: 'Moderate' },
  { sp80060Id: 'D.3.5.3', name: 'Internal Logistics', category: 'Services Delivery', c: 'Low', i: 'Low', a: 'Low' },
  { sp80060Id: 'C.2.1.2', name: 'Strategic Planning', category: 'Management & Support', c: 'Low', i: 'Low', a: 'Low' },
  { sp80060Id: 'D.2.2', name: 'Human Resources', category: 'Management & Support', c: 'Moderate', i: 'Moderate', a: 'Low' },
  { sp80060Id: 'C.3.5.8', name: 'Information Management', category: 'Services Delivery', c: 'Moderate', i: 'Moderate', a: 'Moderate' },
];

const DIALOG_TITLES: Record<GateAction, string> = {
  pta: 'Create Privacy Threshold Analysis',
  pia: 'Generate & Approve PIA',
  interconnection: 'Add Interconnection',
  'certify-none': 'Certify No External Interconnections',
  categorization: 'FIPS 199 Security Categorization',
  baseline: 'Select Control Baseline',
  roles: 'Assign RMF Roles',
};

const ROLE_OPTIONS = [
  { value: 'AuthorizingOfficial', label: 'Authorizing Official (AO)' },
  { value: 'Issm', label: 'ISSM' },
  { value: 'Isso', label: 'ISSO' },
  { value: 'Sca', label: 'SCA' },
  { value: 'SystemOwner', label: 'System Owner' },
];

function roleBadgeColor(role: string): string {
  switch (role) {
    case 'AuthorizingOfficial': return 'bg-purple-100 text-purple-800';
    case 'Issm': return 'bg-indigo-100 text-indigo-800';
    case 'Isso': return 'bg-green-100 text-green-800';
    case 'Sca': return 'bg-amber-100 text-amber-800';
    case 'SystemOwner': return 'bg-indigo-100 text-indigo-800';
    default: return 'bg-gray-100 text-gray-800';
  }
}

function roleLabel(role: string): string {
  return ROLE_OPTIONS.find((r) => r.value === role)?.label ?? role;
}

export default function GateActionDialog({ action, systemId, onClose, onSuccess }: GateActionDialogProps) {
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // PTA state
  const [ptaCollects, setPtaCollects] = useState(true);
  const [ptaMaintains, setPtaMaintains] = useState(true);
  const [ptaDisseminates, setPtaDisseminates] = useState(false);
  const [ptaCategories, setPtaCategories] = useState<string[]>([]);
  const [ptaPurpose, setPtaPurpose] = useState('');

  // Interconnection state
  const [icRemoteSystem, setIcRemoteSystem] = useState('');
  const [icHostname, setIcHostname] = useState('');
  const [icDirection, setIcDirection] = useState('Outbound');
  const [icProtocol, setIcProtocol] = useState('');
  const [icPort, setIcPort] = useState('');

  // Categorization state
  const [catInfoTypes, setCatInfoTypes] = useState<InfoTypeInput[]>([]);
  const [catJustification, setCatJustification] = useState('');
  const [catNss, setCatNss] = useState(false);

  // Baseline state
  const [blApplyOverlay, setBlApplyOverlay] = useState(true);
  const [blOverlayName, setBlOverlayName] = useState('');

  // Roles state
  const [roleSelected, setRoleSelected] = useState(ROLE_OPTIONS[0]!.value);
  const [personComponents, setPersonComponents] = useState<OrgComponentDto[]>([]);
  const [personSearch, setPersonSearch] = useState('');
  const [selectedPerson, setSelectedPerson] = useState<OrgComponentDto | null>(null);
  const [existingRoles, setExistingRoles] = useState<RoleAssignment[]>([]);
  const [rolesLoading, setRolesLoading] = useState(false);
  const [deletingRoleId, setDeletingRoleId] = useState<string | null>(null);

  // Load Person components and existing roles for the roles action
  useEffect(() => {
    if (action !== 'roles') return;
    setRolesLoading(true);
    Promise.all([
      listComponents({ type: 'Person', pageSize: 200 }),
      fetchRoles(systemId),
    ]).then(([compResult, rolesResult]) => {
      setPersonComponents(compResult.items);
      setExistingRoles(rolesResult);
    }).finally(() => setRolesLoading(false));
  }, [action, systemId]);

  const filteredPersons = personComponents.filter(
    (c) => !personSearch || c.name.toLowerCase().includes(personSearch.toLowerCase()),
  );

  const handleDeleteRole = async (roleId: string) => {
    setDeletingRoleId(roleId);
    try {
      await deleteRole(systemId, roleId);
      const updated = await fetchRoles(systemId);
      setExistingRoles(updated);
    } finally {
      setDeletingRoleId(null);
    }
  };

  const togglePiiCategory = (cat: string) => {
    setPtaCategories((prev) => prev.includes(cat) ? prev.filter((c) => c !== cat) : [...prev, cat]);
  };

  const addCommonInfoType = (t: typeof COMMON_INFO_TYPES[number]) => {
    if (catInfoTypes.some(it => it.sp80060Id === t.sp80060Id)) return;
    setCatInfoTypes(prev => [...prev, {
      sp80060Id: t.sp80060Id, name: t.name, category: t.category,
      confidentialityImpact: t.c, integrityImpact: t.i, availabilityImpact: t.a,
    }]);
  };

  const removeInfoType = (sp80060Id: string) => {
    setCatInfoTypes(prev => prev.filter(it => it.sp80060Id !== sp80060Id));
  };

  const updateInfoTypeImpact = (sp80060Id: string, field: 'confidentialityImpact' | 'integrityImpact' | 'availabilityImpact', value: string) => {
    setCatInfoTypes(prev => prev.map(it => it.sp80060Id === sp80060Id ? { ...it, [field]: value } : it));
  };

  const handleSubmit = async () => {
    setLoading(true);
    setError(null);
    try {
      switch (action) {
        case 'pta': {
          const body: CreatePtaRequest = {
            collectsPii: ptaCollects, maintainsPii: ptaMaintains, disseminatesPii: ptaDisseminates,
            piiCategories: ptaCategories.length > 0 ? ptaCategories : undefined,
            purpose: ptaPurpose || undefined,
          };
          await createPta(systemId, body);
          break;
        }
        case 'pia':
          await generateAndApprovePia(systemId);
          break;
        case 'interconnection': {
          if (!icRemoteSystem.trim()) { setError('Remote system name is required'); setLoading(false); return; }
          const body: AddInterconnectionRequest = {
            remoteSystem: icRemoteSystem, hostname: icHostname || undefined,
            direction: icDirection, protocol: icProtocol || undefined, port: icPort || undefined,
          };
          await addInterconnection(systemId, body);
          break;
        }
        case 'certify-none':
          await certifyNoInterconnections(systemId);
          break;
        case 'categorization': {
          if (catInfoTypes.length === 0) { setError('Add at least one information type'); setLoading(false); return; }
          await setCategorization(systemId, {
            informationTypes: catInfoTypes,
            justification: catJustification || undefined,
            isNationalSecuritySystem: catNss,
          });
          break;
        }
        case 'baseline':
          await selectBaseline(systemId, {
            applyOverlay: blApplyOverlay,
            overlayName: blOverlayName || undefined,
          });
          break;
        case 'roles': {
          if (!selectedPerson) { setError('Select a person to assign'); setLoading(false); return; }
          await assignRole(systemId, {
            role: roleSelected,
            userDisplayName: selectedPerson.name,
          });
          // Refresh existing roles list
          const updated = await fetchRoles(systemId);
          setExistingRoles(updated);
          setSelectedPerson(null);
          setLoading(false);
          return; // Don't close — let user assign more roles
        }
      }
      onSuccess();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Operation failed');
    } finally {
      setLoading(false);
    }
  };

  const submitLabel = (): string => {
    if (loading) {
      const labels: Record<GateAction, string> = {
        pta: 'Creating...', pia: 'Processing...', interconnection: 'Adding...',
        'certify-none': 'Certifying...', categorization: 'Saving...', baseline: 'Selecting...',
        roles: 'Assigning...',
      };
      return labels[action];
    }
    const labels: Record<GateAction, string> = {
      pta: 'Create PTA', pia: 'Generate & Approve PIA', interconnection: 'Add Interconnection',
      'certify-none': 'Certify', categorization: `Save Categorization (${catInfoTypes.length} type${catInfoTypes.length !== 1 ? 's' : ''})`,
      baseline: 'Select Baseline',
      roles: 'Assign Role',
    };
    return labels[action];
  };

  const isSubmitDisabled = loading || (action === 'categorization' && catInfoTypes.length === 0) || (action === 'roles' && !selectedPerson);

  const submitColor = action === 'certify-none'
    ? 'bg-amber-600 hover:bg-amber-700'
    : 'bg-indigo-600 hover:bg-indigo-700';

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 backdrop-blur-sm"
      onClick={(e) => { if (e.target === e.currentTarget) onClose(); }}
    >
      <div className="w-full max-w-lg rounded-xl bg-white shadow-2xl border border-gray-200 overflow-hidden max-h-[85vh] flex flex-col">
        {/* Header */}
        <div className="flex items-start justify-between px-6 pt-5 pb-3 flex-shrink-0">
          <h3 className="text-lg font-semibold text-gray-900">{DIALOG_TITLES[action]}</h3>
          <button
            type="button"
            onClick={onClose}
            className="ml-4 rounded-lg p-1 text-gray-400 hover:bg-gray-100 hover:text-gray-600 transition-colors"
            aria-label="Close"
          >
            <svg className="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" />
            </svg>
          </button>
        </div>

        <div className="border-t border-gray-100" />

        {/* Body */}
        <div className="px-6 py-4 overflow-y-auto flex-1">
          {action === 'pta' && (
            <div className="space-y-4">
              <div className="flex flex-wrap gap-4">
                <label className="flex items-center gap-2 text-sm">
                  <input type="checkbox" checked={ptaCollects} onChange={(e) => setPtaCollects(e.target.checked)} className="rounded border-gray-300" />
                  Collects PII
                </label>
                <label className="flex items-center gap-2 text-sm">
                  <input type="checkbox" checked={ptaMaintains} onChange={(e) => setPtaMaintains(e.target.checked)} className="rounded border-gray-300" />
                  Maintains PII
                </label>
                <label className="flex items-center gap-2 text-sm">
                  <input type="checkbox" checked={ptaDisseminates} onChange={(e) => setPtaDisseminates(e.target.checked)} className="rounded border-gray-300" />
                  Disseminates PII
                </label>
              </div>
              <div>
                <p className="text-xs font-medium text-gray-700 mb-1.5">PII Categories</p>
                <div className="flex flex-wrap gap-2">
                  {PII_CATEGORIES.map((cat) => (
                    <button key={cat} onClick={() => togglePiiCategory(cat)}
                      className={`rounded-md border px-2.5 py-1 text-xs font-medium transition-colors ${
                        ptaCategories.includes(cat) ? 'border-indigo-500 bg-indigo-50 text-indigo-700' : 'border-gray-200 bg-white text-gray-600 hover:border-gray-300'
                      }`}>{ptaCategories.includes(cat) ? '✓ ' : ''}{cat}</button>
                  ))}
                </div>
              </div>
              <div>
                <label className="text-xs font-medium text-gray-700">Purpose</label>
                <input type="text" value={ptaPurpose} onChange={(e) => setPtaPurpose(e.target.value)}
                  placeholder="e.g., Personnel records and access management"
                  className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-1.5 text-sm focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500" />
              </div>
            </div>
          )}

          {action === 'pia' && (
            <p className="text-sm text-gray-600">
              The PTA determined a Privacy Impact Assessment is required. This will auto-generate
              the PIA from the PTA and approve it.
            </p>
          )}

          {action === 'interconnection' && (
            <div className="space-y-4">
              <div className="grid grid-cols-2 gap-3">
                <div>
                  <label className="text-xs font-medium text-gray-700">Remote System *</label>
                  <input type="text" value={icRemoteSystem} onChange={(e) => setIcRemoteSystem(e.target.value)}
                    placeholder="e.g., DISA DEE"
                    className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-1.5 text-sm focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500" />
                </div>
                <div>
                  <label className="text-xs font-medium text-gray-700">Hostname</label>
                  <input type="text" value={icHostname} onChange={(e) => setIcHostname(e.target.value)}
                    placeholder="e.g., smtp.dee.disa.mil"
                    className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-1.5 text-sm focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500" />
                </div>
              </div>
              <div className="grid grid-cols-3 gap-3">
                <div>
                  <label className="text-xs font-medium text-gray-700">Direction</label>
                  <select value={icDirection} onChange={(e) => setIcDirection(e.target.value)}
                    className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-1.5 text-sm focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500">
                    <option value="Outbound">Outbound</option>
                    <option value="Inbound">Inbound</option>
                    <option value="Bidirectional">Bidirectional</option>
                  </select>
                </div>
                <div>
                  <label className="text-xs font-medium text-gray-700">Protocol</label>
                  <input type="text" value={icProtocol} onChange={(e) => setIcProtocol(e.target.value)}
                    placeholder="e.g., SMTP/TLS"
                    className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-1.5 text-sm focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500" />
                </div>
                <div>
                  <label className="text-xs font-medium text-gray-700">Port</label>
                  <input type="text" value={icPort} onChange={(e) => setIcPort(e.target.value)}
                    placeholder="e.g., 587"
                    className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-1.5 text-sm focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500" />
                </div>
              </div>
            </div>
          )}

          {action === 'certify-none' && (
            <p className="text-sm text-gray-600">
              This certifies that this system has no external interconnections.
              This can be updated later if interconnections are added.
            </p>
          )}

          {action === 'baseline' && (
            <div className="space-y-4">
              <p className="text-sm text-gray-600">
                The baseline level (Low / Moderate / High) is automatically derived from your
                system's FIPS 199 security categorization. This will also populate NIST 800-53
                control implementations with narrative templates.
              </p>
              <label className="flex items-center gap-2 text-sm">
                <input type="checkbox" checked={blApplyOverlay} onChange={(e) => setBlApplyOverlay(e.target.checked)} className="rounded border-gray-300" />
                Apply CNSSI 1253 overlay (recommended for DoD systems)
              </label>
              {blApplyOverlay && (
                <div>
                  <label className="text-xs font-medium text-gray-700">Overlay Name (optional)</label>
                  <input type="text" value={blOverlayName} onChange={(e) => setBlOverlayName(e.target.value)}
                    placeholder="Auto-detected from DoD IL (e.g., CNSSI 1253 IL5)"
                    className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-1.5 text-sm focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500" />
                </div>
              )}
            </div>
          )}

          {action === 'categorization' && (
            <div className="space-y-4">
              <p className="text-sm text-gray-500">
                Select information types and set their C/I/A impact levels. The overall categorization
                is the high-water mark (maximum) across all types.
              </p>

              {/* Quick-add */}
              <div>
                <p className="text-xs font-medium text-gray-700 mb-1.5">Quick Add (SP 800-60)</p>
                <div className="flex flex-wrap gap-1.5">
                  {COMMON_INFO_TYPES.map((t) => (
                    <button key={t.sp80060Id} onClick={() => addCommonInfoType(t)}
                      disabled={catInfoTypes.some(it => it.sp80060Id === t.sp80060Id)}
                      className={`rounded-md border px-2 py-1 text-xs font-medium transition-colors ${
                        catInfoTypes.some(it => it.sp80060Id === t.sp80060Id)
                          ? 'border-green-300 bg-green-50 text-green-700'
                          : 'border-gray-200 bg-white text-gray-600 hover:border-indigo-300 hover:bg-indigo-50'
                      }`}>{catInfoTypes.some(it => it.sp80060Id === t.sp80060Id) ? '✓ ' : '+ '}{t.name}</button>
                  ))}
                </div>
              </div>

              {/* Info types table */}
              {catInfoTypes.length > 0 && (
                <div className="border border-gray-200 rounded-md overflow-hidden">
                  <table className="w-full text-xs">
                    <thead className="bg-gray-100">
                      <tr>
                        <th className="px-2 py-1.5 text-left font-medium text-gray-700">Info Type</th>
                        <th className="px-2 py-1.5 text-center font-medium text-gray-700">C</th>
                        <th className="px-2 py-1.5 text-center font-medium text-gray-700">I</th>
                        <th className="px-2 py-1.5 text-center font-medium text-gray-700">A</th>
                        <th className="px-2 py-1.5 w-8" />
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-gray-100">
                      {catInfoTypes.map((it) => (
                        <tr key={it.sp80060Id} className="bg-white">
                          <td className="px-2 py-1.5">
                            <span className="font-medium text-gray-800">{it.name}</span>
                            <span className="ml-1 text-gray-400">{it.sp80060Id}</span>
                          </td>
                          {(['confidentialityImpact', 'integrityImpact', 'availabilityImpact'] as const).map((field) => (
                            <td key={field} className="px-1 py-1.5 text-center">
                              <select value={it[field]} onChange={(e) => updateInfoTypeImpact(it.sp80060Id, field, e.target.value)}
                                className="rounded border border-gray-200 px-1 py-0.5 text-xs">
                                {IMPACT_LEVELS.map((l) => <option key={l} value={l}>{l[0]}</option>)}
                              </select>
                            </td>
                          ))}
                          <td className="px-1 py-1.5 text-center">
                            <button onClick={() => removeInfoType(it.sp80060Id)} className="text-red-400 hover:text-red-600" title="Remove">✕</button>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}

              {/* NSS + Justification */}
              <div className="flex items-center gap-4">
                <label className="flex items-center gap-2 text-xs">
                  <input type="checkbox" checked={catNss} onChange={(e) => setCatNss(e.target.checked)} className="rounded border-gray-300" />
                  National Security System (NSS)
                </label>
                <input type="text" value={catJustification} onChange={(e) => setCatJustification(e.target.value)}
                  placeholder="Justification (optional)"
                  className="flex-1 rounded-md border border-gray-300 px-2 py-1 text-xs focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500" />
              </div>
            </div>
          )}

          {action === 'roles' && (
            <div className="space-y-4">
              {/* Existing role assignments */}
              {existingRoles.length > 0 && (
                <div>
                  <p className="text-xs font-medium text-gray-700 mb-1.5">Current Assignments</p>
                  <div className="divide-y divide-gray-100 rounded-md border border-gray-200">
                    {existingRoles.map((r) => (
                      <div key={r.id} className="flex items-center justify-between px-3 py-2">
                        <div className="flex items-center gap-2 min-w-0">
                          <span className={`inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium ${roleBadgeColor(r.role)}`}>
                            {roleLabel(r.role)}
                          </span>
                          <span className="text-sm text-gray-900 truncate">{r.userDisplayName ?? r.userId}</span>
                        </div>
                        <button
                          type="button"
                          onClick={() => handleDeleteRole(r.id)}
                          disabled={deletingRoleId === r.id}
                          className="text-gray-400 hover:text-red-600 transition-colors disabled:opacity-50"
                          title="Remove"
                        >
                          <svg className="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
                            <path strokeLinecap="round" strokeLinejoin="round" d="M14.74 9l-.346 9m-4.788 0L9.26 9m9.968-3.21c.342.052.682.107 1.022.166m-1.022-.165L18.16 19.673a2.25 2.25 0 01-2.244 2.077H8.084a2.25 2.25 0 01-2.244-2.077L4.772 5.79m14.456 0a48.108 48.108 0 00-3.478-.397m-12 .562c.34-.059.68-.114 1.022-.165m0 0a48.11 48.11 0 013.478-.397m7.5 0v-.916c0-1.18-.91-2.164-2.09-2.201a51.964 51.964 0 00-3.32 0c-1.18.037-2.09 1.022-2.09 2.201v.916m7.5 0a48.667 48.667 0 00-7.5 0" />
                          </svg>
                        </button>
                      </div>
                    ))}
                  </div>
                </div>
              )}

              {/* Role selector */}
              <div>
                <label className="block text-xs font-medium text-gray-700 mb-1">Role</label>
                <select
                  value={roleSelected}
                  onChange={(e) => setRoleSelected(e.target.value)}
                  className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500"
                >
                  {ROLE_OPTIONS.map((o) => (
                    <option key={o.value} value={o.value}>{o.label}</option>
                  ))}
                </select>
              </div>

              {/* Person picker */}
              <div>
                <label className="block text-xs font-medium text-gray-700 mb-1">Assign Person (from Components)</label>
                <input
                  type="text"
                  value={personSearch}
                  onChange={(e) => setPersonSearch(e.target.value)}
                  placeholder="Search person components..."
                  className="w-full rounded-md border border-gray-300 px-3 py-1.5 text-sm mb-2 focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500"
                />
                {rolesLoading ? (
                  <p className="text-xs text-gray-500 py-2">Loading persons...</p>
                ) : filteredPersons.length === 0 ? (
                  <p className="text-xs text-gray-500 italic py-2 text-center">
                    {personComponents.length === 0 ? 'No Person components found in the Component Library.' : 'No matches.'}
                  </p>
                ) : (
                  <div className="max-h-48 overflow-y-auto space-y-1 rounded-md border border-gray-200 p-1">
                    {filteredPersons.map((p) => (
                      <button
                        key={p.id}
                        type="button"
                        onClick={() => setSelectedPerson(p)}
                        className={`w-full text-left flex items-center justify-between rounded-md px-3 py-2 text-sm transition-colors ${
                          selectedPerson?.id === p.id
                            ? 'bg-indigo-50 border border-indigo-300 text-indigo-800'
                            : 'bg-white hover:bg-gray-50 border border-transparent'
                        }`}
                      >
                        <div className="min-w-0">
                          <span className="font-medium">{p.name}</span>
                          {p.subType && <span className="ml-2 text-xs text-gray-500">{p.subType}</span>}
                        </div>
                        {selectedPerson?.id === p.id && (
                          <svg className="h-4 w-4 text-indigo-600 flex-shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                            <path strokeLinecap="round" strokeLinejoin="round" d="M5 13l4 4L19 7" />
                          </svg>
                        )}
                      </button>
                    ))}
                  </div>
                )}
              </div>
            </div>
          )}

          {error && <p className="text-xs text-red-600 mt-3">{error}</p>}
        </div>

        {/* Footer */}
        <div className="border-t border-gray-100 px-6 py-3 bg-gray-50 flex justify-end gap-2 flex-shrink-0">
          {action === 'roles' ? (
            <>
              <button type="button" onClick={() => void handleSubmit()} disabled={isSubmitDisabled}
                className={`rounded-lg px-4 py-2 text-sm font-medium text-white ${submitColor} disabled:opacity-50 transition-colors`}>
                {submitLabel()}
              </button>
              <button type="button" onClick={() => { onSuccess(); }}
                className="rounded-lg px-4 py-2 text-sm font-medium text-white bg-green-600 hover:bg-green-700 transition-colors">
                Done
              </button>
            </>
          ) : (
            <>
              <button type="button" onClick={onClose}
                className="rounded-lg px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-200 transition-colors">
                Cancel
              </button>
              <button type="button" onClick={() => void handleSubmit()} disabled={isSubmitDisabled}
                className={`rounded-lg px-4 py-2 text-sm font-medium text-white ${submitColor} disabled:opacity-50 transition-colors`}>
                {submitLabel()}
              </button>
            </>
          )}
        </div>
      </div>
    </div>
  );
}
