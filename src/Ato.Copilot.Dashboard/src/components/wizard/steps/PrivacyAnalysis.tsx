import { useState } from 'react';
import {
  createPta,
  generateAndApprovePia,
  certifyNoInterconnections,
  addInterconnection,
} from '../../../api/systemDetail';
import type {
  CreatePtaResponse,
  GenerateApprovePiaResponse,
  AddInterconnectionRequest,
  AddInterconnectionResponse,
} from '../../../api/systemDetail';

const PII_CATEGORIES = [
  'Full Name',
  'Social Security Number',
  'Date of Birth',
  'Email Address',
  'Phone Number',
  'Home Address',
  'Financial Information',
  'Medical Records',
  'Biometric Data',
  'Employment Records',
  'Education Records',
  'Military Service Records',
  'Citizenship/Immigration Status',
  'Driver License Number',
  'Passport Number',
];

const DIRECTION_OPTIONS = ['Inbound', 'Outbound', 'Bidirectional'];

interface PrivacyAnalysisProps {
  systemId: string;
  onFinish: () => void;
  onBack: () => void;
  onCancel: () => void;
  onErrors: (errors: Record<string, string[]>) => void;
}

export default function PrivacyAnalysis({ systemId, onFinish, onBack, onCancel, onErrors }: PrivacyAnalysisProps) {
  // PTA state
  const [collectsPii, setCollectsPii] = useState(false);
  const [maintainsPii, setMaintainsPii] = useState(false);
  const [disseminatesPii, setDisseminatesPii] = useState(false);
  const [piiCategories, setPiiCategories] = useState<string[]>([]);
  const [estimatedRecordCount, setEstimatedRecordCount] = useState('');
  const [purpose, setPurpose] = useState('');
  const [ptaResult, setPtaResult] = useState<CreatePtaResponse | null>(null);
  const [piaResult, setPiaResult] = useState<GenerateApprovePiaResponse | null>(null);
  const [savingPta, setSavingPta] = useState(false);
  const [savingPia, setSavingPia] = useState(false);

  // Interconnection state
  const [interconnectionMode, setInterconnectionMode] = useState<'none' | 'certify' | 'add' | null>(null);
  const [certifyDone, setCertifyDone] = useState(false);
  const [certifying, setCertifying] = useState(false);
  const [addedInterconnections, setAddedInterconnections] = useState<AddInterconnectionResponse[]>([]);
  const [ixnForm, setIxnForm] = useState<AddInterconnectionRequest>({
    remoteSystem: '',
    hostname: '',
    direction: 'Bidirectional',
    type: '',
    protocol: '',
    port: '',
    dataClassification: '',
  });
  const [addingIxn, setAddingIxn] = useState(false);

  const handleCategoryToggle = (cat: string) => {
    setPiiCategories((prev) =>
      prev.includes(cat) ? prev.filter((c) => c !== cat) : [...prev, cat],
    );
  };

  const handlesPii = collectsPii || maintainsPii || disseminatesPii;

  const handleSubmitPta = async () => {
    setSavingPta(true);
    try {
      const result = await createPta(systemId, {
        collectsPii,
        maintainsPii,
        disseminatesPii,
        piiCategories: handlesPii ? piiCategories : [],
        estimatedRecordCount: estimatedRecordCount ? parseInt(estimatedRecordCount, 10) : undefined,
        purpose: purpose.trim() || undefined,
      });
      setPtaResult(result);
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to submit PTA';
      onErrors({ _form: [msg] });
    } finally {
      setSavingPta(false);
    }
  };

  const handleGeneratePia = async () => {
    setSavingPia(true);
    try {
      const result = await generateAndApprovePia(systemId);
      setPiaResult(result);
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to generate PIA';
      onErrors({ _form: [msg] });
    } finally {
      setSavingPia(false);
    }
  };

  const handleCertifyNoInterconnections = async () => {
    setCertifying(true);
    try {
      await certifyNoInterconnections(systemId);
      setCertifyDone(true);
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to certify no interconnections';
      onErrors({ _form: [msg] });
    } finally {
      setCertifying(false);
    }
  };

  const handleAddInterconnection = async () => {
    if (!ixnForm.remoteSystem.trim()) return;
    setAddingIxn(true);
    try {
      const result = await addInterconnection(systemId, {
        ...ixnForm,
        remoteSystem: ixnForm.remoteSystem.trim(),
      });
      setAddedInterconnections((prev) => [...prev, result]);
      setIxnForm({
        remoteSystem: '',
        hostname: '',
        direction: 'Bidirectional',
        type: '',
        protocol: '',
        port: '',
        dataClassification: '',
      });
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to add interconnection';
      onErrors({ _form: [msg] });
    } finally {
      setAddingIxn(false);
    }
  };

  const ptaDetermination = ptaResult?.determination;
  const piaRequired = ptaDetermination === 'PiaRequired';
  const ptaComplete = ptaResult !== null;
  const piaComplete = piaResult !== null;

  const interconnectionsDone =
    certifyDone || addedInterconnections.length > 0;

  const canFinish =
    ptaComplete && (!piaRequired || piaComplete) && interconnectionsDone;

  return (
    <div>
      <h2 className="text-xl font-semibold text-gray-900 mb-1">Step 8: Privacy &amp; Interconnections</h2>
      <p className="text-sm text-gray-500 mb-6">
        Complete the Privacy Threshold Analysis (PTA), PIA if required, and document system interconnections.
      </p>

      {/* ── Section 1: Privacy ── */}
      <h3 className="text-base font-semibold text-gray-800 mb-3">Privacy Analysis</h3>

      {/* PTA Result banner */}
      {ptaResult && (
        <div className={`mb-6 rounded-md border p-4 ${
          ptaDetermination === 'PiaNotRequired' || ptaDetermination === 'Exempt'
            ? 'border-green-200 bg-green-50'
            : ptaDetermination === 'PiaRequired'
              ? 'border-amber-200 bg-amber-50'
              : 'border-gray-200 bg-gray-50'
        }`}>
          <h3 className="text-sm font-medium mb-1">
            PTA Determination: <span className="font-bold">{ptaDetermination}</span>
          </h3>
          <p className="text-sm text-gray-600">{ptaResult.rationale}</p>
          {ptaResult.piiCategories.length > 0 && (
            <p className="text-xs text-gray-500 mt-1">
              PII Categories: {ptaResult.piiCategories.join(', ')}
            </p>
          )}
        </div>
      )}

      {/* PIA Result banner */}
      {piaResult && (
        <div className="mb-6 rounded-md border border-green-200 bg-green-50 p-4">
          <h3 className="text-sm font-medium text-green-800 mb-1">
            PIA Status: <span className="font-bold">{piaResult.status}</span>
          </h3>
          {piaResult.expirationDate && (
            <p className="text-xs text-gray-500">Expires: {new Date(piaResult.expirationDate).toLocaleDateString()}</p>
          )}
        </div>
      )}

      {/* PTA form */}
      {!ptaResult && (
        <div className="rounded-md border border-gray-200 p-4 space-y-4">
          <h3 className="text-sm font-medium text-gray-700">Privacy Threshold Analysis (PTA)</h3>

          <div className="space-y-2">
            <label className="flex items-center gap-2 text-sm">
              <input type="checkbox" checked={collectsPii} onChange={(e) => setCollectsPii(e.target.checked)} className="rounded" />
              System collects PII
            </label>
            <label className="flex items-center gap-2 text-sm">
              <input type="checkbox" checked={maintainsPii} onChange={(e) => setMaintainsPii(e.target.checked)} className="rounded" />
              System maintains PII
            </label>
            <label className="flex items-center gap-2 text-sm">
              <input type="checkbox" checked={disseminatesPii} onChange={(e) => setDisseminatesPii(e.target.checked)} className="rounded" />
              System disseminates PII
            </label>
          </div>

          {handlesPii && (
            <>
              <div>
                <label className="mb-2 block text-xs font-medium text-gray-600">PII Categories</label>
                <div className="grid grid-cols-2 sm:grid-cols-3 gap-1.5 max-h-40 overflow-y-auto border border-gray-200 rounded-md p-2">
                  {PII_CATEGORIES.map((cat) => (
                    <label key={cat} className="flex items-center gap-1.5 text-xs">
                      <input
                        type="checkbox"
                        checked={piiCategories.includes(cat)}
                        onChange={() => handleCategoryToggle(cat)}
                        className="rounded"
                      />
                      {cat}
                    </label>
                  ))}
                </div>
              </div>

              <div className="grid grid-cols-2 gap-3">
                <div>
                  <label className="mb-1 block text-xs font-medium text-gray-600">Estimated Record Count</label>
                  <input
                    type="number"
                    value={estimatedRecordCount}
                    onChange={(e) => setEstimatedRecordCount(e.target.value)}
                    className="w-full rounded-md border border-gray-300 px-3 py-1.5 text-sm"
                    placeholder="e.g. 10000"
                    min={0}
                  />
                </div>
                <div>
                  <label className="mb-1 block text-xs font-medium text-gray-600">Purpose</label>
                  <input
                    value={purpose}
                    onChange={(e) => setPurpose(e.target.value)}
                    className="w-full rounded-md border border-gray-300 px-3 py-1.5 text-sm"
                    placeholder="Reason for PII collection"
                  />
                </div>
              </div>
            </>
          )}

          <button
            onClick={handleSubmitPta}
            disabled={savingPta}
            className="rounded-md bg-blue-600 px-4 py-1.5 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50"
          >
            {savingPta ? 'Submitting...' : 'Submit PTA'}
          </button>
        </div>
      )}

      {/* PIA generation (shown when PTA says PIA is required) */}
      {piaRequired && !piaResult && (
        <div className="mt-4 rounded-md border border-amber-200 bg-amber-50 p-4">
          <h3 className="text-sm font-medium text-amber-800 mb-2">Privacy Impact Assessment Required</h3>
          <p className="text-sm text-gray-600 mb-3">
            The PTA determined a PIA is required. Generate and approve the PIA to proceed.
          </p>
          <button
            onClick={handleGeneratePia}
            disabled={savingPia}
            className="rounded-md bg-amber-600 px-4 py-1.5 text-sm font-medium text-white hover:bg-amber-700 disabled:opacity-50"
          >
            {savingPia ? 'Generating...' : 'Generate & Approve PIA'}
          </button>
        </div>
      )}

      {/* ── Section 2: Interconnections ── */}
      <div className="mt-8 border-t border-gray-200 pt-6">
        <h3 className="text-base font-semibold text-gray-800 mb-2">Interconnection Documentation</h3>
        <p className="text-sm text-gray-500 mb-4">
          Does this system have external interconnections with other systems?
        </p>

        {/* Certify done banner */}
        {certifyDone && (
          <div className="mb-4 rounded-md border border-green-200 bg-green-50 p-3">
            <p className="text-sm font-medium text-green-800">
              &#10003; Certified: No external interconnections
            </p>
          </div>
        )}

        {/* Added interconnections list */}
        {addedInterconnections.length > 0 && (
          <div className="mb-4 rounded-md border border-green-200 bg-green-50 p-3">
            <p className="text-sm font-medium text-green-800 mb-2">
              &#10003; {addedInterconnections.length} interconnection{addedInterconnections.length > 1 ? 's' : ''} documented
            </p>
            <ul className="space-y-1">
              {addedInterconnections.map((ixn) => (
                <li key={ixn.interconnectionId} className="text-xs text-gray-600">
                  {ixn.targetSystemName} &mdash; {ixn.direction} ({ixn.status})
                </li>
              ))}
            </ul>
          </div>
        )}

        {/* Choice buttons (only if not yet handled) */}
        {!certifyDone && addedInterconnections.length === 0 && interconnectionMode === null && (
          <div className="flex gap-3">
            <button
              onClick={() => setInterconnectionMode('certify')}
              className="rounded-md border border-gray-300 px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50"
            >
              No External Interconnections
            </button>
            <button
              onClick={() => setInterconnectionMode('add')}
              className="rounded-md border border-gray-300 px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50"
            >
              Add Interconnections
            </button>
          </div>
        )}

        {/* Certify confirmation */}
        {interconnectionMode === 'certify' && !certifyDone && (
          <div className="rounded-md border border-gray-200 p-4">
            <p className="text-sm text-gray-600 mb-3">
              By certifying, you confirm this system has no external interconnections with other information systems.
            </p>
            <div className="flex gap-2">
              <button
                onClick={handleCertifyNoInterconnections}
                disabled={certifying}
                className="rounded-md bg-blue-600 px-4 py-1.5 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50"
              >
                {certifying ? 'Certifying...' : 'Certify No Interconnections'}
              </button>
              <button
                onClick={() => setInterconnectionMode(null)}
                className="rounded-md border border-gray-300 px-4 py-1.5 text-sm text-gray-700 hover:bg-gray-50"
              >
                Cancel
              </button>
            </div>
          </div>
        )}

        {/* Add interconnection form */}
        {interconnectionMode === 'add' && !certifyDone && (
          <div className="rounded-md border border-gray-200 p-4 space-y-3">
            <div className="grid grid-cols-2 gap-3">
              <div>
                <label className="mb-1 block text-xs font-medium text-gray-600">Remote System *</label>
                <input
                  value={ixnForm.remoteSystem}
                  onChange={(e) => setIxnForm((p) => ({ ...p, remoteSystem: e.target.value }))}
                  className="w-full rounded-md border border-gray-300 px-3 py-1.5 text-sm"
                  placeholder="e.g. DISA SIPRNet Gateway"
                />
              </div>
              <div>
                <label className="mb-1 block text-xs font-medium text-gray-600">Hostname</label>
                <input
                  value={ixnForm.hostname}
                  onChange={(e) => setIxnForm((p) => ({ ...p, hostname: e.target.value }))}
                  className="w-full rounded-md border border-gray-300 px-3 py-1.5 text-sm"
                  placeholder="e.g. gw01.sipr.mil"
                />
              </div>
              <div>
                <label className="mb-1 block text-xs font-medium text-gray-600">Direction *</label>
                <select
                  value={ixnForm.direction}
                  onChange={(e) => setIxnForm((p) => ({ ...p, direction: e.target.value }))}
                  className="w-full rounded-md border border-gray-300 px-3 py-1.5 text-sm"
                >
                  {DIRECTION_OPTIONS.map((d) => (
                    <option key={d} value={d}>{d}</option>
                  ))}
                </select>
              </div>
              <div>
                <label className="mb-1 block text-xs font-medium text-gray-600">Protocol</label>
                <input
                  value={ixnForm.protocol}
                  onChange={(e) => setIxnForm((p) => ({ ...p, protocol: e.target.value }))}
                  className="w-full rounded-md border border-gray-300 px-3 py-1.5 text-sm"
                  placeholder="e.g. HTTPS, SSH"
                />
              </div>
              <div>
                <label className="mb-1 block text-xs font-medium text-gray-600">Port</label>
                <input
                  value={ixnForm.port}
                  onChange={(e) => setIxnForm((p) => ({ ...p, port: e.target.value }))}
                  className="w-full rounded-md border border-gray-300 px-3 py-1.5 text-sm"
                  placeholder="e.g. 443"
                />
              </div>
              <div>
                <label className="mb-1 block text-xs font-medium text-gray-600">Data Classification</label>
                <input
                  value={ixnForm.dataClassification}
                  onChange={(e) => setIxnForm((p) => ({ ...p, dataClassification: e.target.value }))}
                  className="w-full rounded-md border border-gray-300 px-3 py-1.5 text-sm"
                  placeholder="e.g. CUI, FOUO"
                />
              </div>
            </div>
            <div className="flex gap-2">
              <button
                onClick={handleAddInterconnection}
                disabled={addingIxn || !ixnForm.remoteSystem.trim()}
                className="rounded-md bg-blue-600 px-4 py-1.5 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50"
              >
                {addingIxn ? 'Adding...' : 'Add Interconnection'}
              </button>
              {addedInterconnections.length === 0 && (
                <button
                  onClick={() => setInterconnectionMode(null)}
                  className="rounded-md border border-gray-300 px-4 py-1.5 text-sm text-gray-700 hover:bg-gray-50"
                >
                  Back
                </button>
              )}
            </div>
          </div>
        )}
      </div>

      {/* ── Footer: Back / Finish / Cancel ── */}
      <div className="mt-8 flex items-center justify-between border-t border-gray-200 pt-4">
        <div className="flex gap-2">
          <button
            onClick={onBack}
            className="rounded-md border border-gray-300 px-4 py-2 text-sm text-gray-700 hover:bg-gray-50"
          >
            Back
          </button>
          <button
            onClick={onCancel}
            className="rounded-md border border-gray-300 px-4 py-2 text-sm text-gray-700 hover:bg-gray-50"
          >
            Cancel
          </button>
        </div>
        <button
          onClick={onFinish}
          disabled={!canFinish}
          className="rounded-md bg-green-600 px-6 py-2 text-sm font-medium text-white hover:bg-green-700 disabled:opacity-50"
          title={!canFinish ? 'Complete PTA, PIA (if required), and interconnection documentation before finishing' : undefined}
        >
          Finish
        </button>
      </div>
    </div>
  );
}
