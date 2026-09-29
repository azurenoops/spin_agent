import { useEffect, useState } from 'react';
import Markdown from 'react-markdown';
import remarkGfm from 'remark-gfm';
import SetupDialog from '../workspace-operations/SetupDialog';
import AuthenticatedDownload from '../../components/AuthenticatedDownload';
import { assessmentWorkspaceError, getAssessmentReport, previewAssessmentPlan, type AssessmentPlanPreview, type AssessmentReport } from '../../api/assessmentWorkspace';

export function SavedPlanPreview({ systemId, planId, onClose }: { systemId: string; planId: string; onClose: () => void }) {
  const [data, setData] = useState<AssessmentPlanPreview | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [attempt, setAttempt] = useState(0);
  useEffect(() => {
    const controller = new AbortController(); setData(null); setError(null);
    previewAssessmentPlan(systemId, planId, controller.signal)
      .then(value => { if (!controller.signal.aborted) setData(value); })
      .catch(reason => { if (!controller.signal.aborted) setError(assessmentWorkspaceError(reason)); });
    return () => controller.abort();
  }, [systemId, planId, attempt]);
  return <SetupDialog placement="right" expanded title="Saved assessment plan" description={data ? `Retained revision ${data.revision} · No generation or edits performed` : 'Loading saved content'}
    busy={false} onClose={onClose}><div className="aw-dialog">
      {error && <div className="aw-error" role="alert"><p>{error}</p><button type="button" onClick={() => setAttempt(v => v + 1)}>Retry saved preview</button></div>}
      {!error && !data && <p role="status">Loading saved plan…</p>}
      {data && <div className="aw-markdown"><Markdown remarkPlugins={[remarkGfm]}>{data.content}</Markdown></div>}
      <footer className="aw-dialog-footer">
        {data && <button type="button" onClick={() => setAttempt(v => v + 1)}>Reload saved preview</button>}
        {data && ['docx', 'pdf'].map(format => <AuthenticatedDownload key={format}
          url={`/api/dashboard/systems/${encodeURIComponent(systemId)}/assessment-workspace/plans/${encodeURIComponent(planId)}/export?format=${format}`}
          fileName={`assessment-plan.${format}`}>Download current saved {format.toUpperCase()}</AuthenticatedDownload>)}
        <button type="button" onClick={onClose}>Close preview</button></footer>
    </div></SetupDialog>;
}

export function RetainedSarPreview({ systemId, reportId, onClose }: { systemId: string; reportId: string; onClose: () => void }) {
  const [data, setData] = useState<AssessmentReport | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [attempt, setAttempt] = useState(0);
  useEffect(() => {
    const controller = new AbortController(); setData(null); setError(null);
    getAssessmentReport(systemId, reportId, controller.signal)
      .then(value => { if (!controller.signal.aborted) setData(value); })
      .catch(reason => { if (!controller.signal.aborted) setError(assessmentWorkspaceError(reason)); });
    return () => controller.abort();
  }, [systemId, reportId, attempt]);
  return <SetupDialog placement="right" expanded title="Retained assessment report" description={data ? `${data.title} · ${data.status}` : 'Loading retained SAR'}
    busy={false} onClose={onClose}><div className="aw-dialog">
      {error && <div className="aw-error" role="alert"><p>{error}</p><button type="button" onClick={() => setAttempt(v => v + 1)}>Retry report</button></div>}
      {!error && !data && <p role="status">Loading retained report…</p>}
      {data && <>
        <p className="aw-info">Report generation is separate from report review, package export, eMASS submission and an authorization decision.</p>
        {data.warnings.length > 0 && <ul className="aw-warning">{data.warnings.map(w => <li key={w}>{w}</li>)}</ul>}
        <div className="aw-markdown">{data.sections.map((section, index) => <section key={index}><h3>{section.title}</h3>
          <Markdown remarkPlugins={[remarkGfm]}>{section.content}</Markdown></section>)}</div>
        <footer className="aw-dialog-footer">
          {data.downloadUrl && <AuthenticatedDownload url={data.downloadUrl} fileName={`${data.title}.docx`} onDownloadError={reason => { setData(null); setError(assessmentWorkspaceError(reason)); }}>
            Download retained SAR
          </AuthenticatedDownload>}
          <button type="button" onClick={onClose}>Close report</button>
        </footer>
      </>}
    </div></SetupDialog>;
}
