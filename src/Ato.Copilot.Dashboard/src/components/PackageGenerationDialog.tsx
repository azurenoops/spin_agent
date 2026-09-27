import { useState, useEffect, useCallback } from 'react';
import { useNavigate } from '../features/workspaces/workspaceNavigation';
import {
  generatePackage,
  getPackageDetail,
  validatePackage,
  downloadPackageUrl,
} from '../api/package';
import type { PackageDetail, ValidationFinding, PackagePurpose, RetainedPackageSelection } from '../api/package';
import PackagePurposeSelect from './PackagePurposeSelect';
import AuthenticatedDownload from './AuthenticatedDownload';
import { isProgressEvent, progressError, useJobProgress, useProgressSession, type ProgressSession } from '../hooks/useJobProgress';
import ProgressTransportNotice from './ProgressTransportNotice';
import SetupDialog from '../features/workspace-operations/SetupDialog';
import RetainedPackageContext from './RetainedPackageContext';

interface PackageGenerationDialogProps {
  systemId: string;
  onClose: () => void;
  onPackageComplete?: () => void;
}

type DialogPhase = 'readiness' | 'configure' | 'generating' | 'completed' | 'failed';

interface ArtifactProgress {
  type: string;
  status: 'pending' | 'generating' | 'done' | 'failed';
}

const ARTIFACT_SEQUENCE = [
  { type: 'OscalSsp', label: 'OSCAL SSP' },
  { type: 'OscalPoam', label: 'OSCAL POA&M' },
  { type: 'OscalAssessmentResults', label: 'OSCAL Assessment Results' },
  { type: 'OscalAssessmentPlan', label: 'OSCAL Assessment Plan' },
  { type: 'Sar', label: 'Security Assessment Report' },
  { type: 'EvidenceManifest', label: 'Evidence Bundle' },
];

/** Maps validation finding category to a dashboard route suffix and label */
function getCategoryRoute(category: string, artifactType?: string | null): { path: string; label: string } | null {
  switch (category) {
    case 'authorization-decision':
      return { path: 'authorize', label: 'Authorize' };
    case 'boundary':
      return { path: 'boundaries', label: 'Boundaries' };
    case 'ssp':
      return { path: 'narratives', label: 'Narratives' };
    case 'sar':
      return { path: 'assessments', label: 'Assessments' };
    case 'sap':
      return { path: 'assessments', label: 'Assessments' };
    case 'poam':
    case 'cross-reference':
      return { path: 'poam', label: 'POA&M' };
    case 'schema':
      if (artifactType === 'ssp') return { path: 'narratives', label: 'Narratives' };
      if (artifactType === 'poam') return { path: 'poam', label: 'POA&M' };
      if (artifactType === 'assessment-results' || artifactType === 'assessment-plan')
        return { path: 'assessments', label: 'Assessments' };
      return null;
    case 'evidence':
      return { path: 'evidence', label: 'Evidence' };
    default:
      return null;
  }
}

function RemediationText({
  finding,
  systemId,
  onNavigate,
}: {
  finding: ValidationFinding;
  systemId: string;
  onNavigate: (path: string) => void;
}) {
  if (!finding.remediation) return null;

  const route = getCategoryRoute(finding.category, finding.artifactType);

  return (
    <p className="text-xs text-red-600 mt-1">
      {finding.remediation}
      {route && (
        <>
          {' '}
          <button
            type="button"
            onClick={() => onNavigate(`/systems/${systemId}/${route.path}`)}
            className="inline-flex items-center gap-0.5 text-indigo-600 hover:text-indigo-800 underline font-medium"
          >
            Open {route.label}
            <svg className="h-3 w-3" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M13.5 6H5.25A2.25 2.25 0 003 8.25v10.5A2.25 2.25 0 005.25 21h10.5A2.25 2.25 0 0018 18.75V10.5m-10.5 6L21 3m0 0h-5.25M21 3v5.25" />
            </svg>
          </button>
        </>
      )}
    </p>
  );
}

export default function PackageGenerationDialog(props: PackageGenerationDialogProps) {
  const session = useProgressSession(props.systemId);
  return <PackageGenerationContent key={session.key} {...props} session={session} />;
}

function PackageGenerationContent({
  systemId,
  onClose,
  onPackageComplete,
  session,
}: PackageGenerationDialogProps & { session: ProgressSession }) {
  const [phase, setPhase] = useState<DialogPhase>('readiness');
  const [purpose, setPurpose] = useState<PackagePurpose>('Legacy');
  const [retainedContext, setRetainedContext] = useState<RetainedPackageSelection | null>(null);
  const [validatedSourceHash, setValidatedSourceHash] = useState<string | null>(null);
  const requiresContext = purpose === 'AuthorizedBaselineArchive' || purpose === 'ChangeSubmission';
  const artifactSequence = requiresContext
    ? [{ type: 'RetainedBaseline', label: 'Retained baseline package' }, { type: 'PackageContext', label: 'Recorded decision and source manifest' },
      ...(purpose === 'ChangeSubmission' ? [{ type: 'ReviewedSspChange', label: 'Reviewed SSP change' }] : [])]
    : ARTIFACT_SEQUENCE;
  const [evidenceMode, setEvidenceMode] = useState<'Embedded' | 'ManifestOnly'>('Embedded');
  const [readinessFindings, setReadinessFindings] = useState<ValidationFinding[]>([]);
  const [readinessValid, setReadinessValid] = useState<boolean | null>(null);
  const [readinessLoading, setReadinessLoading] = useState(true);
  const [validationRevision, setValidationRevision] = useState(0);
  const [packageId, setPackageId] = useState<string | null>(null);
  const [packageDetail, setPackageDetail] = useState<PackageDetail | null>(null);
  const [artifactProgress, setArtifactProgress] = useState<ArtifactProgress[]>(
    ARTIFACT_SEQUENCE.map((a) => ({ type: a.type, status: 'pending' })),
  );
  const [statusMessage, setStatusMessage] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [failedArtifact, setFailedArtifact] = useState<string | null>(null);
  const navigate = useNavigate();
  const monitor = useJobProgress<PackageDetail>({
    session, jobId: packageId, hubPath: '/hubs/package',
    events: ['PackageStatusChanged', 'PackageArtifactGenerated', 'PackageValidationComplete', 'PackageComplete', 'PackageFailed'],
    matchesEvent: payload => isProgressEvent(payload, 'packageId', packageId),
    subscribe: connection => connection.invoke('SubscribeToPackage', packageId),
    poll: async signal => {
      if (!packageId) throw new Error('No package is selected.');
      const result = await getPackageDetail(systemId, packageId, signal);
      if (result.packageId !== packageId || result.systemId !== systemId || !Array.isArray(result.artifacts)
        || !['Pending', 'Generating', 'Validating', 'Completed', 'Failed'].includes(result.status)) {
        throw new Error('Unexpected package status response.');
      }
      return result;
    },
    isTerminal: result => result.status === 'Completed' || result.status === 'Failed',
    onStatus: result => {
      setPackageDetail(result);
      const generated = new Set(result.artifacts.map(artifact => artifact.type));
      const next = artifactSequence.find(artifact => !generated.has(artifact.type))?.type;
      setArtifactProgress(previous => previous.map(artifact => ({
        ...artifact,
        status: generated.has(artifact.type) ? 'done'
          : result.failedArtifactType === artifact.type ? 'failed'
          : result.status === 'Generating' && next === artifact.type ? 'generating' : 'pending',
      })));
      setStatusMessage(result.status === 'Completed' ? 'Package ready for download' : `Status: ${result.status}`);
      if (result.status === 'Completed') {
        setPhase('completed');
        onPackageComplete?.();
      } else if (result.status === 'Failed') {
        setPhase('failed');
        setError(result.failureReason ?? 'Package generation failed.');
        setFailedArtifact(result.failedArtifactType);
      }
    },
  });

  const handleNavigate = useCallback(
    (path: string) => {
      onClose();
      navigate(path);
    },
    [onClose, navigate],
  );

  // Run readiness check on mount
  useEffect(() => {
    if (!session.ready) { setReadinessLoading(false); return; }
    if (requiresContext && !retainedContext) { setReadinessLoading(false); setReadinessValid(false); setPhase('readiness'); return; }
    const request = session.request();
    setReadinessLoading(true);
    (purpose === 'Legacy' ? validatePackage(systemId, request.signal)
      : retainedContext ? validatePackage(systemId, request.signal, purpose, retainedContext)
        : validatePackage(systemId, request.signal, purpose))
      .then((result) => {
        if (!request.isCurrent()) return;
        if (requiresContext && result.isValid && !result.sourceContextHash) {
          setReadinessValid(false);
          setReadinessLoading(false);
          setError('The server did not return a validated source-context hash. Revalidate against the current API before generation.');
          return;
        }
        setValidatedSourceHash(result.sourceContextHash ?? null);
        setReadinessValid(result.isValid);
        setReadinessFindings(result.findings);
        setReadinessLoading(false);
        if (result.isValid) setPhase('configure');
      })
      .catch((err) => {
        if (!request.isCurrent()) return;
        setReadinessValid(false);
        setReadinessFindings([]);
        setError(progressError(err));
        setReadinessLoading(false);
      }).finally(request.complete);
    return () => { request.cancel(); request.complete(); };
  }, [systemId, session.key, session.ready, purpose, retainedContext, validationRevision]);

  const handleGenerate = async () => {
    if (!session.ready || !session.isCurrent()) { setError('Authenticated workspace context is required.'); return; }
    if (requiresContext && !retainedContext) { setError('Select a retained baseline and decision before generating this package.'); return; }
    if (requiresContext && !validatedSourceHash) { setError('Revalidate the selected source versions before generation.'); return; }
    const request = session.request();
    setPhase('generating');
    setError(null);
    setStatusMessage('Submitting...');
    setArtifactProgress(artifactSequence.map((a) => ({ type: a.type, status: 'pending' })));

    try {
      const result = purpose === 'Legacy'
        ? await generatePackage(systemId, evidenceMode, request.signal)
        : retainedContext ? await generatePackage(systemId, 'ManifestOnly', request.signal, purpose, {
          ...retainedContext, expectedSourceContextHash: validatedSourceHash!,
        })
          : await generatePackage(systemId, evidenceMode, request.signal, purpose);
      if (!request.isCurrent()) return;
      if (!result || typeof result.packageId !== 'string' || !result.packageId.trim()) {
        throw new Error('Unexpected package generation response.');
      }
      setPackageId(result.packageId);
      setStatusMessage('Queued — waiting for background generation');
    } catch (err: unknown) {
      if (!request.isCurrent()) return;
      setPhase('failed');
      setError(progressError(err));
    } finally {
      request.complete();
    }
  };

  const errors = readinessFindings.filter((f) => f.severity === 'error');
  const warnings = readinessFindings.filter((f) => f.severity === 'warning');

  return (
    <SetupDialog title="Generate Authorization Package" description="Choose a purpose, review validation, and preserve the generated artifacts."
      busy={phase === 'generating'} onClose={onClose}>

        {/* Body */}
        <div className="px-5 py-4 space-y-4 max-h-[70vh] overflow-y-auto">
          <ProgressTransportNotice monitor={monitor} onClose={onClose} />
          {(phase === 'readiness' || phase === 'configure') && <PackagePurposeSelect value={purpose}
            onChange={value => {
              setPurpose(value);
              setRetainedContext(null);
              setValidatedSourceHash(null);
              setPhase('readiness');
              setReadinessValid(null);
              setReadinessFindings([]);
              setReadinessLoading(true);
              setError(null);
            }} />}
          {requiresContext && (phase === 'readiness' || phase === 'configure') && <RetainedPackageContext key={purpose}
            systemId={systemId} purpose={purpose} disabled={readinessLoading} onChange={selection => {
              setRetainedContext(selection); setValidatedSourceHash(null); setPhase('readiness'); setReadinessValid(null);
              setReadinessFindings([]); setError(null);
            }} />}
          {error && phase !== 'failed' && <p role="alert">{error}</p>}
          {/* ─── Readiness Check ─── */}
          {phase === 'readiness' && (
            <div>
              <h3 className="text-sm font-medium text-gray-700 mb-3">Pre-Submission Readiness Check</h3>
              {readinessLoading ? (
                <div className="flex items-center gap-2 text-sm text-gray-500">
                  <svg className="animate-spin h-4 w-4" viewBox="0 0 24 24">
                    <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" fill="none" />
                    <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
                  </svg>
                  Running readiness checks...
                </div>
              ) : (
                <>
                  {readinessValid === false && errors.length > 0 && (
                    <div className="space-y-2 mb-3">
                      <p className="text-sm font-medium text-red-700">
                        {errors.length} blocking issue{errors.length > 1 ? 's' : ''} found:
                      </p>
                      {errors.map((f, i) => (
                        <div key={i} className="p-3 rounded-lg bg-red-50 border border-red-200">
                          <p className="text-sm text-red-800 font-medium">{f.description}</p>
                          <RemediationText finding={f} systemId={systemId} onNavigate={handleNavigate} />
                        </div>
                      ))}
                    </div>
                  )}
                  {warnings.length > 0 && (
                    <div className="space-y-2 mb-3">
                      <p className="text-sm font-medium text-amber-700">
                        {warnings.length} warning{warnings.length > 1 ? 's' : ''}:
                      </p>
                      {warnings.map((f, i) => (
                        <div key={i} className="p-2 rounded-lg bg-amber-50 border border-amber-200">
                          <p className="text-sm text-amber-800">{f.description}</p>
                          {f.remediation && (() => {
                            const route = getCategoryRoute(f.category, f.artifactType);
                            return (
                              <p className="text-xs text-amber-600 mt-1">
                                {f.remediation}
                                {route && (
                                  <>
                                    {' '}
                                    <button
                                      type="button"
                                      onClick={() => handleNavigate(`/systems/${systemId}/${route.path}`)}
                                      className="inline-flex items-center gap-0.5 text-indigo-600 hover:text-indigo-800 underline font-medium"
                                    >
                                      Open {route.label}
                                      <svg className="h-3 w-3" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                                        <path strokeLinecap="round" strokeLinejoin="round" d="M13.5 6H5.25A2.25 2.25 0 003 8.25v10.5A2.25 2.25 0 005.25 21h10.5A2.25 2.25 0 0018 18.75V10.5m-10.5 6L21 3m0 0h-5.25M21 3v5.25" />
                                      </svg>
                                    </button>
                                  </>
                                )}
                              </p>
                            );
                          })()}
                        </div>
                      ))}
                    </div>
                  )}
                  {readinessValid === false && errors.length > 0 && (
                    <p className="text-sm text-gray-500">
                      Resolve blocking issues before generating the package.
                    </p>
                  )}
                  {readinessValid && (
                    <div className="p-3 rounded-lg bg-green-50 border border-green-200">
                      <p className="text-sm text-green-800 font-medium">All readiness checks passed</p>
                    </div>
                  )}
                </>
              )}
            </div>
          )}

          {/* ─── Configuration ─── */}
          {phase === 'configure' && (
            <div>
              {warnings.length > 0 && (
                <div className="mb-4 space-y-1">
                  {warnings.map((f, i) => (
                    <div key={i} className="p-2 rounded-lg bg-amber-50 border border-amber-200">
                      <p className="text-xs text-amber-700">{f.description}</p>
                    </div>
                  ))}
                </div>
              )}
              {!requiresContext ? <div>
                <label className="block text-sm font-medium text-gray-700 mb-2">Evidence Bundling</label>
                <div className="space-y-2">
                  <label
                    className={`flex items-center gap-3 p-3 rounded-lg border cursor-pointer transition-colors ${
                      evidenceMode === 'Embedded' ? 'border-indigo-500 bg-indigo-50' : 'border-gray-200 hover:bg-gray-50'
                    }`}
                  >
                    <input
                      type="radio"
                      name="evidenceMode"
                      value="Embedded"
                      checked={evidenceMode === 'Embedded'}
                      onChange={() => setEvidenceMode('Embedded')}
                      className="text-indigo-600 focus:ring-indigo-500"
                    />
                    <div>
                      <span className="text-sm font-medium text-gray-900">Embedded</span>
                      <p className="text-xs text-gray-500">Include evidence files in the ZIP archive</p>
                    </div>
                  </label>
                  <label
                    className={`flex items-center gap-3 p-3 rounded-lg border cursor-pointer transition-colors ${
                      evidenceMode === 'ManifestOnly' ? 'border-indigo-500 bg-indigo-50' : 'border-gray-200 hover:bg-gray-50'
                    }`}
                  >
                    <input
                      type="radio"
                      name="evidenceMode"
                      value="ManifestOnly"
                      checked={evidenceMode === 'ManifestOnly'}
                      onChange={() => setEvidenceMode('ManifestOnly')}
                      className="text-indigo-600 focus:ring-indigo-500"
                    />
                    <div>
                      <span className="text-sm font-medium text-gray-900">Manifest Only</span>
                      <p className="text-xs text-gray-500">Include evidence manifest (references only, smaller file)</p>
                    </div>
                  </label>
                </div>
              </div> : <p className="text-sm text-slate-600">
                The retained baseline package is included unchanged, including its original evidence.
                No current evidence files are collected or removed by this archive/change operation.
              </p>}
            </div>
          )}

          {/* ─── Generation Progress ─── */}
          {(phase === 'generating' || phase === 'completed' || phase === 'failed') && (
            <div>
              <h3 className="text-sm font-medium text-gray-700 mb-3">Artifact Generation</h3>
              <div className="space-y-2">
                {artifactProgress.map((a) => {
                  const info = artifactSequence.find((s) => s.type === a.type);
                  return (
                    <div
                      key={a.type}
                      className={`flex items-center gap-3 p-2 rounded-lg text-sm ${
                        a.status === 'done'
                          ? 'bg-green-50 text-green-800'
                          : a.status === 'generating'
                            ? 'bg-indigo-50 text-indigo-800'
                            : a.status === 'failed'
                              ? 'bg-red-50 text-red-800'
                              : 'bg-gray-50 text-gray-500'
                      }`}
                    >
                      {a.status === 'done' && <span className="text-green-600">&#10003;</span>}
                      {a.status === 'generating' && (
                        <svg className="animate-spin h-4 w-4 text-indigo-600" viewBox="0 0 24 24">
                          <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" fill="none" />
                          <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
                        </svg>
                      )}
                      {a.status === 'failed' && <span className="text-red-600">&#10007;</span>}
                      {a.status === 'pending' && <span className="text-gray-400">&#9675;</span>}
                      <span>{info?.label ?? a.type}</span>
                    </div>
                  );
                })}
              </div>
              {statusMessage && (
                <p className="text-xs text-gray-500 mt-2">{statusMessage}</p>
              )}
            </div>
          )}

          {/* ─── Error Display ─── */}
          {phase === 'failed' && error && (
            <div role="alert" className="p-3 rounded-lg bg-red-50 border border-red-200">
              <p className="text-sm font-medium text-red-800">Generation Failed</p>
              {failedArtifact && (
                <p className="text-xs text-red-700 mt-1">
                  Failed artifact: {artifactSequence.find((a) => a.type === failedArtifact)?.label ?? failedArtifact}
                </p>
              )}
              <p className="text-xs text-red-600 mt-1">{error}</p>
            </div>
          )}

          {/* ─── Completed ─── */}
          {phase === 'completed' && packageId && (
            <div className="p-3 rounded-lg bg-green-50 border border-green-200">
              <p className="text-sm font-medium text-green-800">Package Generated Successfully</p>
              {packageDetail?.fileSize && (
                <p className="text-xs text-green-700 mt-1">
                  Size: {(packageDetail.fileSize / 1024 / 1024).toFixed(2)} MB
                </p>
              )}
            </div>
          )}
        </div>

        {/* Footer */}
        <div className="flex justify-end gap-3 px-5 py-4 bg-gray-50 border-t border-gray-200">
          {phase === 'readiness' && !readinessLoading && readinessValid === false && errors.length > 0 && (
            <button
              onClick={onClose}
              className="px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-lg hover:bg-gray-50"
            >
              Close
            </button>
          )}

          {phase === 'configure' && (
            <>
              <button
                onClick={onClose}
                className="px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-lg hover:bg-gray-50"
              >
                Cancel
              </button>
              <button
                onClick={handleGenerate}
                disabled={!session.ready}
                className="px-4 py-2 text-sm font-medium text-white bg-indigo-600 rounded-lg hover:bg-indigo-700"
              >
                Generate Package
              </button>
            </>
          )}

          {phase === 'generating' && (
            <p className="text-xs text-gray-500">
              Generation in progress — do not close this dialog.
            </p>
          )}

          {phase === 'completed' && packageId && (
            <>
              <button
                onClick={onClose}
                className="px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-lg hover:bg-gray-50"
              >
                Close
              </button>
              <AuthenticatedDownload
                url={downloadPackageUrl(systemId, packageId)}
                fileName={`ato-package-${packageId}.zip`}
                className="px-4 py-2 text-sm font-medium text-white bg-green-600 rounded-lg hover:bg-green-700 inline-flex items-center gap-2"
              >
                <svg className="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                  <path strokeLinecap="round" strokeLinejoin="round" d="M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-4l-4 4m0 0l-4-4m4 4V4" />
                </svg>
                Download ZIP
              </AuthenticatedDownload>
            </>
          )}

          {phase === 'failed' && (
            <>
              <button
                onClick={onClose}
                className="px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-lg hover:bg-gray-50"
              >
                Close
              </button>
              <button
                onClick={() => {
                  setPackageId(null);
                  setPackageDetail(null);
                  setRetainedContext(null);
                  setValidatedSourceHash(null);
                  setReadinessValid(null);
                  setReadinessFindings([]);
                  setReadinessLoading(true);
                  setValidationRevision(value => value + 1);
                  setPhase('readiness');
                  setError(null);
                  setFailedArtifact(null);
                }}
                className="px-4 py-2 text-sm font-medium text-white bg-indigo-600 rounded-lg hover:bg-indigo-700"
              >
                Retry
              </button>
            </>
          )}
        </div>
    </SetupDialog>
  );
}
