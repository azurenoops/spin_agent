import { useState } from 'react';
import {
  createPta,
  addInterconnection,
  certifyNoInterconnections,
  generateAndApprovePia,
  setCategorization,
  selectBaseline,
} from '../../api/systemDetail';
import type {
  CreatePtaRequest,
  AddInterconnectionRequest,
  InfoTypeInput,
} from '../../api/systemDetail';

export type GateAction = 'pta' | 'pia' | 'interconnection' | 'certify-none' | 'categorization' | 'baseline';

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
};

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
      };
      return labels[action];
    }
    const labels: Record<GateAction, string> = {
      pta: 'Create PTA', pia: 'Generate & Approve PIA', interconnection: 'Add Interconnection',
      'certify-none': 'Certify', categorization: `Save Categorization (${catInfoTypes.length} type${catInfoTypes.length !== 1 ? 's' : ''})`,
      baseline: 'Select Baseline',
    };
    return labels[action];
  };

  const isSubmitDisabled = loading || (action === 'categorization' && catInfoTypes.length === 0);

  const submitColor = action === 'certify-none'
    ? 'bg-amber-600 hover:bg-amber-700'
    : 'bg-blue-600 hover:bg-blue-700';

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
                        ptaCategories.includes(cat) ? 'border-blue-500 bg-blue-50 text-blue-700' : 'border-gray-200 bg-white text-gray-600 hover:border-gray-300'
                      }`}>{ptaCategories.includes(cat) ? '✓ ' : ''}{cat}</button>
                  ))}
                </div>
              </div>
              <div>
                <label className="text-xs font-medium text-gray-700">Purpose</label>
                <input type="text" value={ptaPurpose} onChange={(e) => setPtaPurpose(e.target.value)}
                  placeholder="e.g., Personnel records and access management"
                  className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-1.5 text-sm focus:border-blue-500 focus:ring-1 focus:ring-blue-500" />
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
                    className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-1.5 text-sm focus:border-blue-500 focus:ring-1 focus:ring-blue-500" />
                </div>
                <div>
                  <label className="text-xs font-medium text-gray-700">Hostname</label>
                  <input type="text" value={icHostname} onChange={(e) => setIcHostname(e.target.value)}
                    placeholder="e.g., smtp.dee.disa.mil"
                    className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-1.5 text-sm focus:border-blue-500 focus:ring-1 focus:ring-blue-500" />
                </div>
              </div>
              <div className="grid grid-cols-3 gap-3">
                <div>
                  <label className="text-xs font-medium text-gray-700">Direction</label>
                  <select value={icDirection} onChange={(e) => setIcDirection(e.target.value)}
                    className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-1.5 text-sm focus:border-blue-500 focus:ring-1 focus:ring-blue-500">
                    <option value="Outbound">Outbound</option>
                    <option value="Inbound">Inbound</option>
                    <option value="Bidirectional">Bidirectional</option>
                  </select>
                </div>
                <div>
                  <label className="text-xs font-medium text-gray-700">Protocol</label>
                  <input type="text" value={icProtocol} onChange={(e) => setIcProtocol(e.target.value)}
                    placeholder="e.g., SMTP/TLS"
                    className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-1.5 text-sm focus:border-blue-500 focus:ring-1 focus:ring-blue-500" />
                </div>
                <div>
                  <label className="text-xs font-medium text-gray-700">Port</label>
                  <input type="text" value={icPort} onChange={(e) => setIcPort(e.target.value)}
                    placeholder="e.g., 587"
                    className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-1.5 text-sm focus:border-blue-500 focus:ring-1 focus:ring-blue-500" />
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
                    className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-1.5 text-sm focus:border-blue-500 focus:ring-1 focus:ring-blue-500" />
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
                          : 'border-gray-200 bg-white text-gray-600 hover:border-blue-300 hover:bg-blue-50'
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
                  className="flex-1 rounded-md border border-gray-300 px-2 py-1 text-xs focus:border-blue-500 focus:ring-1 focus:ring-blue-500" />
              </div>
            </div>
          )}

          {error && <p className="text-xs text-red-600 mt-3">{error}</p>}
        </div>

        {/* Footer */}
        <div className="border-t border-gray-100 px-6 py-3 bg-gray-50 flex justify-end gap-2 flex-shrink-0">
          <button type="button" onClick={onClose}
            className="rounded-lg px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-200 transition-colors">
            Cancel
          </button>
          <button type="button" onClick={() => void handleSubmit()} disabled={isSubmitDisabled}
            className={`rounded-lg px-4 py-2 text-sm font-medium text-white ${submitColor} disabled:opacity-50 transition-colors`}>
            {submitLabel()}
          </button>
        </div>
      </div>
    </div>
  );
}
