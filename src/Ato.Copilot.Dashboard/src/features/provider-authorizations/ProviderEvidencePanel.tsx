import { useState } from 'react';
import AuthenticatedDownload from '../../components/AuthenticatedDownload';
import { Pager, Status, useRemote } from '../workspace-operations/workspaceUi';
import * as api from './evidenceSharingApi';

export function ProviderEvidencePanel({ systemId }: { systemId: string }) {
  const [page, setPage] = useState(1);
  const remote = useRemote(signal => api.listMissionEvidence(systemId, page, signal), [systemId, page]);
  return <section aria-label="Provider-approved evidence" className="min-w-0 rounded-xl border border-slate-200 bg-white p-5 dark:border-slate-700 dark:bg-slate-900">
    <h2 className="text-lg font-semibold">Provider-approved evidence</h2>
    <p className="mt-2 text-sm text-slate-600">Only explicit approvals for this tenant and system appear here. Private attachments remain restricted. A summary is not a provider package or mission approval.</p>
    <button type="button" className="mt-3 text-sm font-semibold text-indigo-700" disabled={remote.loading} onClick={remote.retry}>Refresh evidence access</button>
    <Status loading={remote.loading} error={remote.error ? `Provider evidence is unavailable. ${remote.error}` : null} retry={remote.retry} />
    {remote.data && !remote.loading && !remote.error && <>
      {!remote.data.items.length && <p className="mt-4 text-sm">No approved provider evidence is available for this system. Resolve sharing with the provider; private references do not count as accessible attachments.</p>}
      {remote.data.items.length > 0 && <div className="mt-4 overflow-x-auto rounded-lg border border-slate-200 dark:border-slate-700">
        <table aria-label="Provider-approved summaries" className="w-full min-w-[640px] table-fixed text-left text-sm">
          <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-500 dark:bg-slate-800">
            <tr><th scope="col" className="w-[30%] px-4 py-3">Artifact</th>
              <th scope="col" className="w-[28%] px-4 py-3">Source</th>
              <th scope="col" className="w-[22%] px-4 py-3">Availability</th>
              <th scope="col" className="px-4 py-3">Actions</th></tr>
          </thead>
          <tbody className="divide-y divide-slate-200 dark:divide-slate-700">
            {remote.data.items.map(item => <tr key={item.shareId}>
              <td className="space-y-3 break-words px-4 py-4 align-top">
                <h3 className="font-semibold">Approved summary · version {item.version}</h3>
                <details><summary className="cursor-pointer font-medium text-indigo-700 dark:text-indigo-300">Preview approved summary</summary>
                  <p className="mt-2 whitespace-pre-wrap">{item.summary}</p>
                </details>
                <p className="text-xs text-slate-600 dark:text-slate-300">Approved by {item.approvedBy} · {new Date(item.approvedAt).toLocaleString()}</p>
              </td>
              <td className="space-y-3 break-words px-4 py-4 align-top">
                <dl className="space-y-1 break-all"><dt className="text-xs text-slate-500">Provider / offering</dt><dd>{item.providerId} / {item.offeringId}</dd>
                  <dt className="text-xs text-slate-500">Source artifact reference</dt><dd>{item.evidenceId}</dd></dl>
                <details><summary className="cursor-pointer font-medium">Retained provenance and permitted use</summary>
                  <dl className="mt-2 space-y-1 break-all">
                    <dt>Permission</dt><dd>Approved summary only; no private attachment access</dd>
                    <dt>Source SHA-256</dt><dd>{item.sourceSha256}</dd>
                    <dt>Summary SHA-256</dt><dd>{item.contentHash}</dd>
                    <dt>Assignment / approval</dt><dd>{item.assignmentId} / {item.shareId}</dd>
                  </dl>
                </details>
              </td>
              <td className="space-y-3 px-4 py-4 align-top">
                <p className="rounded bg-emerald-50 px-2 py-1 text-xs font-medium text-emerald-900">Approved summary only</p>
                <p className="text-xs text-amber-900 dark:text-amber-200">Private attachment restricted</p>
              </td>
              <td className="px-4 py-4 align-top">
                <AuthenticatedDownload className="font-semibold text-indigo-700 underline dark:text-indigo-300" url={api.summaryUrl(systemId, item.shareId)}
                  fileName={`provider-summary-${item.shareId}.json`}>Download approved summary</AuthenticatedDownload>
              </td>
            </tr>)}
          </tbody>
        </table>
      </div>}
      <Pager {...remote.data} onPage={setPage} />
    </>}
  </section>;
}
