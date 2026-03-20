import { useState, useEffect, useRef, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import {
  generatePackage,
  getPackageDetail,
  validatePackage,
  downloadPackageUrl,
} from '../api/package';
import type { PackageDetail, ValidationFinding } from '../api/package';
import * as signalR from '@microsoft/signalr';

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
            className="inline-flex items-center gap-0.5 text-blue-600 hover:text-blue-800 underline font-medium"
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

export default function PackageGenerationDialog({
  systemId,
  onClose,
  onPackageComplete,
}: PackageGenerationDialogProps) {
  const [phase, setPhase] = useState<DialogPhase>('readiness');
  const [evidenceMode, setEvidenceMode] = useState<'Embedded' | 'ManifestOnly'>('Embedded');
  const [readinessFindings, setReadinessFindings] = useState<ValidationFinding[]>([]);
  const [readinessValid, setReadinessValid] = useState<boolean | null>(null);
  const [readinessLoading, setReadinessLoading] = useState(true);
  const [packageId, setPackageId] = useState<string | null>(null);
  const [packageDetail, setPackageDetail] = useState<PackageDetail | null>(null);
  const [artifactProgress, setArtifactProgress] = useState<ArtifactProgress[]>(
    ARTIFACT_SEQUENCE.map((a) => ({ type: a.type, status: 'pending' })),
  );
  const [statusMessage, setStatusMessage] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [failedArtifact, setFailedArtifact] = useState<string | null>(null);
  const connectionRef = useRef<signalR.HubConnection | null>(null);
  const navigate = useNavigate();

  const handleNavigate = useCallback(
    (path: string) => {
      onClose();
      navigate(path);
    },
    [onClose, navigate],
  );

  // Close on Escape (unless generating)
  useEffect(() => {
    const handleKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape' && phase !== 'generating') onClose();
    };
    document.addEventListener('keydown', handleKey);
    return () => document.removeEventListener('keydown', handleKey);
  }, [onClose, phase]);

  // Run readiness check on mount
  useEffect(() => {
    let cancelled = false;
    setReadinessLoading(true);
    validatePackage(systemId)
      .then((result) => {
        if (cancelled) return;
        setReadinessValid(result.isValid);
        setReadinessFindings(result.findings);
        setReadinessLoading(false);
        if (result.isValid) setPhase('configure');
      })
      .catch((err) => {
        if (cancelled) return;
        setReadinessValid(false);
        setReadinessFindings([]);
        setError(err instanceof Error ? err.message : 'Readiness check failed');
        setReadinessLoading(false);
      });
    return () => { cancelled = true; };
  }, [systemId]);

  // Clean up SignalR on unmount
  useEffect(() => {
    return () => {
      connectionRef.current?.stop();
    };
  }, []);

  const setupSignalR = useCallback(
    (pkgId: string) => {
      const hubUrl =
        (import.meta.env.VITE_API_BASE_URL || '').replace('/api/dashboard', '') +
        '/hubs/package';

      const connection = new signalR.HubConnectionBuilder()
        .withUrl(hubUrl, {
          accessTokenFactory: () => localStorage.getItem('auth_token') ?? '',
        })
        .withAutomaticReconnect()
        .build();

      connection.on('PackageStatusChanged', (payload: { packageId: string; status: string }) => {
        if (payload.packageId === pkgId) {
          setStatusMessage(`Status: ${payload.status}`);
        }
      });

      connection.on(
        'PackageArtifactGenerated',
        (payload: { packageId: string; artifactType: string; fileName: string }) => {
          if (payload.packageId === pkgId) {
            setArtifactProgress((prev) =>
              prev.map((a) =>
                a.type === payload.artifactType ? { ...a, status: 'done' } : a,
              ),
            );
          }
        },
      );

      connection.on('PackageValidationComplete', (payload: { packageId: string; isValid: boolean }) => {
        if (payload.packageId === pkgId) {
          setStatusMessage(payload.isValid ? 'Validation passed' : 'Validation completed with issues');
        }
      });

      connection.on(
        'PackageComplete',
        (payload: { packageId: string; filePath: string; fileSize: number; contentHash: string }) => {
          if (payload.packageId === pkgId) {
            setPhase('completed');
            setStatusMessage('Package ready for download');
            // Refresh package detail for download link
            getPackageDetail(systemId, pkgId)
              .then(setPackageDetail)
              .catch(() => {});
            onPackageComplete?.();
          }
        },
      );

      connection.on(
        'PackageFailed',
        (payload: { packageId: string; failureReason: string; failedArtifactType: string }) => {
          if (payload.packageId === pkgId) {
            setPhase('failed');
            setError(payload.failureReason);
            setFailedArtifact(payload.failedArtifactType);
            setArtifactProgress((prev) =>
              prev.map((a) =>
                a.type === payload.failedArtifactType ? { ...a, status: 'failed' } : a,
              ),
            );
          }
        },
      );

      connection
        .start()
        .then(() => connection.invoke('SubscribeToPackage', pkgId))
        .catch(() => {});

      connectionRef.current = connection;
    },
    [systemId, onPackageComplete],
  );

  const handleGenerate = async () => {
    setPhase('generating');
    setError(null);
    setStatusMessage('Submitting...');
    setArtifactProgress(ARTIFACT_SEQUENCE.map((a) => ({ type: a.type, status: 'pending' })));

    try {
      const result = await generatePackage(systemId, evidenceMode);
      setPackageId(result.packageId);
      setStatusMessage('Queued — waiting for background generation');
      // Mark first artifact as generating
      setArtifactProgress((prev) =>
        prev.map((a, i) => (i === 0 ? { ...a, status: 'generating' } : a)),
      );
      setupSignalR(result.packageId);
    } catch (err: unknown) {
      setPhase('failed');
      setError(err instanceof Error ? err.message : 'Failed to start package generation');
    }
  };

  const handleBackdrop = (e: React.MouseEvent) => {
    if (e.target === e.currentTarget && phase !== 'generating') onClose();
  };

  const errors = readinessFindings.filter((f) => f.severity === 'error');
  const warnings = readinessFindings.filter((f) => f.severity === 'warning');

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 backdrop-blur-sm"
      onClick={handleBackdrop}
    >
      <div
        className="w-full max-w-lg rounded-xl bg-white shadow-2xl border border-gray-200 overflow-hidden"
        role="dialog"
        aria-labelledby="pkg-dialog-title"
      >
        {/* Header */}
        <div className="flex items-center justify-between px-5 py-4 bg-gray-50 border-b border-gray-200">
          <h2 id="pkg-dialog-title" className="text-base font-semibold text-gray-900">
            Generate Authorization Package
          </h2>
          {phase !== 'generating' && (
            <button
              onClick={onClose}
              className="text-gray-400 hover:text-gray-600 transition-colors"
              aria-label="Close"
            >
              <svg className="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" />
              </svg>
            </button>
          )}
        </div>

        {/* Body */}
        <div className="px-5 py-4 space-y-4 max-h-[70vh] overflow-y-auto">
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
                                      className="inline-flex items-center gap-0.5 text-blue-600 hover:text-blue-800 underline font-medium"
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
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-2">Evidence Bundling</label>
                <div className="space-y-2">
                  <label
                    className={`flex items-center gap-3 p-3 rounded-lg border cursor-pointer transition-colors ${
                      evidenceMode === 'Embedded' ? 'border-blue-500 bg-blue-50' : 'border-gray-200 hover:bg-gray-50'
                    }`}
                  >
                    <input
                      type="radio"
                      name="evidenceMode"
                      value="Embedded"
                      checked={evidenceMode === 'Embedded'}
                      onChange={() => setEvidenceMode('Embedded')}
                      className="text-blue-600 focus:ring-blue-500"
                    />
                    <div>
                      <span className="text-sm font-medium text-gray-900">Embedded</span>
                      <p className="text-xs text-gray-500">Include evidence files in the ZIP archive</p>
                    </div>
                  </label>
                  <label
                    className={`flex items-center gap-3 p-3 rounded-lg border cursor-pointer transition-colors ${
                      evidenceMode === 'ManifestOnly' ? 'border-blue-500 bg-blue-50' : 'border-gray-200 hover:bg-gray-50'
                    }`}
                  >
                    <input
                      type="radio"
                      name="evidenceMode"
                      value="ManifestOnly"
                      checked={evidenceMode === 'ManifestOnly'}
                      onChange={() => setEvidenceMode('ManifestOnly')}
                      className="text-blue-600 focus:ring-blue-500"
                    />
                    <div>
                      <span className="text-sm font-medium text-gray-900">Manifest Only</span>
                      <p className="text-xs text-gray-500">Include evidence manifest (references only, smaller file)</p>
                    </div>
                  </label>
                </div>
              </div>
            </div>
          )}

          {/* ─── Generation Progress ─── */}
          {(phase === 'generating' || phase === 'completed' || phase === 'failed') && (
            <div>
              <h3 className="text-sm font-medium text-gray-700 mb-3">Artifact Generation</h3>
              <div className="space-y-2">
                {artifactProgress.map((a) => {
                  const info = ARTIFACT_SEQUENCE.find((s) => s.type === a.type);
                  return (
                    <div
                      key={a.type}
                      className={`flex items-center gap-3 p-2 rounded-lg text-sm ${
                        a.status === 'done'
                          ? 'bg-green-50 text-green-800'
                          : a.status === 'generating'
                            ? 'bg-blue-50 text-blue-800'
                            : a.status === 'failed'
                              ? 'bg-red-50 text-red-800'
                              : 'bg-gray-50 text-gray-500'
                      }`}
                    >
                      {a.status === 'done' && <span className="text-green-600">&#10003;</span>}
                      {a.status === 'generating' && (
                        <svg className="animate-spin h-4 w-4 text-blue-600" viewBox="0 0 24 24">
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
            <div className="p-3 rounded-lg bg-red-50 border border-red-200">
              <p className="text-sm font-medium text-red-800">Generation Failed</p>
              {failedArtifact && (
                <p className="text-xs text-red-700 mt-1">
                  Failed artifact: {ARTIFACT_SEQUENCE.find((a) => a.type === failedArtifact)?.label ?? failedArtifact}
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
                className="px-4 py-2 text-sm font-medium text-white bg-blue-600 rounded-lg hover:bg-blue-700"
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
              <a
                href={downloadPackageUrl(systemId, packageId)}
                download
                className="px-4 py-2 text-sm font-medium text-white bg-green-600 rounded-lg hover:bg-green-700 inline-flex items-center gap-2"
              >
                <svg className="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                  <path strokeLinecap="round" strokeLinejoin="round" d="M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-4l-4 4m0 0l-4-4m4 4V4" />
                </svg>
                Download ZIP
              </a>
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
                  setPhase('configure');
                  setError(null);
                  setFailedArtifact(null);
                }}
                className="px-4 py-2 text-sm font-medium text-white bg-blue-600 rounded-lg hover:bg-blue-700"
              >
                Retry
              </button>
            </>
          )}
        </div>
      </div>
    </div>
  );
}
