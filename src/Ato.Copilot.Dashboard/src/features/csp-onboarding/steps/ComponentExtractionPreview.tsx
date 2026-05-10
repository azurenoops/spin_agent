import type { ReactElement } from 'react';
import type { AtoUploadFileResult, AtoUploadResponse } from '../api';

interface Props {
  result: AtoUploadResponse;
}

/**
 * `ComponentExtractionPreview` — Feature 048 / US9 / T211.
 *
 * Renders the per-file extraction summary returned by
 * `POST /api/csp/onboarding/atos/upload`. Surfaces:
 *
 *  - The aggregate tally (documents accepted, components extracted,
 *    capabilities mapped vs needs-review).
 *  - An "AI mapping unavailable" banner (FR-102) when the AI capability
 *    mapper was unreachable; in that case capabilities default to zero
 *    and the operator is told they can re-run the mapper later from the
 *    `/csp/inherited-components` management page.
 *  - A per-file table with the parser used, success/failure, and the
 *    component/capability tallies for that file.
 */
export default function ComponentExtractionPreview({ result }: Props): ReactElement {
  return (
    <div className="space-y-3" data-testid="component-extraction-preview">
      {/* Aggregate tally */}
      <dl className="grid grid-cols-2 gap-3 sm:grid-cols-4">
        <Tally label="Documents accepted" value={result.documentsAccepted} />
        <Tally label="Components extracted" value={result.componentsExtracted} />
        <Tally label="Capabilities mapped" value={result.capabilitiesMapped} tone="success" />
        <Tally
          label="Needs review"
          value={result.capabilitiesNeedsReview}
          tone={result.capabilitiesNeedsReview > 0 ? 'warn' : 'neutral'}
        />
      </dl>

      {/* AI mapping unavailable banner */}
      {!result.aiMappingAvailable && (
        <div
          role="alert"
          className="rounded-md border border-amber-300 bg-amber-50 px-3 py-2 text-sm text-amber-800"
          data-testid="ai-mapping-unavailable-banner"
        >
          <p className="font-medium">AI capability mapping was unavailable.</p>
          <p className="mt-1">
            Components were imported but capabilities were not auto-mapped
            {result.aiMappingFailureReason ? ` (${result.aiMappingFailureReason})` : ''}.
            You can re-run mapping later from the <em>Inherited Components</em> page.
          </p>
        </div>
      )}

      {/* Per-file table */}
      {result.files.length > 0 && (
        <div className="overflow-x-auto rounded-md border border-gray-200">
          <table className="min-w-full divide-y divide-gray-200 text-sm">
            <thead className="bg-gray-50">
              <tr>
                <th scope="col" className="px-3 py-2 text-left font-medium text-gray-700">File</th>
                <th scope="col" className="px-3 py-2 text-left font-medium text-gray-700">Format</th>
                <th scope="col" className="px-3 py-2 text-left font-medium text-gray-700">Parser</th>
                <th scope="col" className="px-3 py-2 text-right font-medium text-gray-700">Components</th>
                <th scope="col" className="px-3 py-2 text-right font-medium text-gray-700">Mapped</th>
                <th scope="col" className="px-3 py-2 text-right font-medium text-gray-700">Needs review</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-100 bg-white">
              {result.files.map((f) => (
                <FileRow key={f.fileName} file={f} />
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

function Tally({
  label,
  value,
  tone = 'neutral',
}: {
  label: string;
  value: number;
  tone?: 'neutral' | 'success' | 'warn';
}): ReactElement {
  const palette =
    tone === 'success'
      ? 'border-emerald-200 bg-emerald-50 text-emerald-800'
      : tone === 'warn'
        ? 'border-amber-200 bg-amber-50 text-amber-800'
        : 'border-gray-200 bg-gray-50 text-gray-800';
  return (
    <div className={`rounded-md border ${palette} px-3 py-2`}>
      <dt className="text-xs font-medium uppercase tracking-wide opacity-80">{label}</dt>
      <dd className="mt-1 text-lg font-semibold">{value}</dd>
    </div>
  );
}

function FileRow({ file }: { file: AtoUploadFileResult }): ReactElement {
  return (
    <tr>
      <td className="px-3 py-2 text-gray-900">
        <span className="font-medium">{file.fileName}</span>
        {!file.parsedSuccessfully && file.parseError && (
          <p className="mt-1 text-xs text-red-700" role="alert">
            {file.parseError}
          </p>
        )}
      </td>
      <td className="px-3 py-2 text-gray-700">{file.sourceFormat}</td>
      <td className="px-3 py-2">
        {file.parsedSuccessfully ? (
          <span className="inline-flex items-center rounded-full bg-emerald-100 px-2 py-0.5 text-xs font-medium text-emerald-800">
            Parsed
          </span>
        ) : (
          <span className="inline-flex items-center rounded-full bg-red-100 px-2 py-0.5 text-xs font-medium text-red-800">
            Failed
          </span>
        )}
      </td>
      <td className="px-3 py-2 text-right text-gray-900">{file.componentsExtracted}</td>
      <td className="px-3 py-2 text-right text-gray-900">{file.capabilitiesMapped}</td>
      <td className="px-3 py-2 text-right text-gray-900">{file.capabilitiesNeedsReview}</td>
    </tr>
  );
}
