import { useState } from 'react';
import { FileUp, ArrowRight } from 'lucide-react';
import { useNavigate } from '../workspaces/workspaceNavigation';
import { PackageUpload } from '../package-imports/PackageUpload';
import { PackageReceiptCard, packageIsProcessing } from '../package-imports/PackageReceipts';
import { getPackageCandidates, receivePackage } from '../package-imports/api';
import type { PackageCandidate, PackageStatus } from '../package-imports/types';
import { Pager, Status, secondaryButtonClass, surfaceClass, useRemote, warningClass } from '../workspace-operations/workspaceUi';
import { OfferingIntake } from './OfferingIntake';
import { authorizationHref, importHref } from './api';
import type { BoundaryInput, Offering } from './types';

export function FileFirstImport({ offering }: { offering?: Offering }) {
  const navigate = useNavigate();
  return <section aria-label="Import authorization package" className="mx-auto max-w-3xl space-y-5">
    <div className={`${surfaceClass} space-y-3 rounded-xl p-6`}>
      <FileUp size={28} aria-hidden="true" className="text-indigo-600" />
      <h2 className="text-xl font-semibold">Start with your authorization package</h2>
      <p className="text-sm text-slate-600 dark:text-gray-300">Upload the decision letter, SSP, and supporting documents. We’ll analyze the sources before {offering ? 'you confirm this offering’s boundary.' : 'you choose or create an offering and confirm its boundary.'}</p>
      {offering && <p className="text-sm font-medium">Selected offering: {offering.name}. Confirm its boundary after analysis; upload alone does not associate the package.</p>}
      <ol className="flex flex-wrap gap-3 text-xs text-slate-500 dark:text-gray-400"><li>1. Upload files</li><li aria-hidden="true"><ArrowRight size={14} /></li><li>2. Review extracted scope</li><li aria-hidden="true"><ArrowRight size={14} /></li><li>3. Confirm offering & boundary</li></ol>
    </div>
    <PackageUpload upload={async (files, key) => {
      const receipt = await receivePackage(files, key);
      const destination = offering ? authorizationHref(offering.offeringId, 'import') : importHref;
      navigate(`${destination}?packageId=${encodeURIComponent(receipt.packageId)}`, { replace: true });
    }} />
    <p className="text-xs text-slate-500 dark:text-gray-400">Receipt is saved before analysis. Upload does not create an offering, record an authorization decision, or publish capabilities.</p>
  </section>;
}

export function PackagePreparation({ item, initialOfferingId }: { item: PackageStatus; initialOfferingId?: string }) {
  return <section aria-label="Analyze and confirm imported package" className="space-y-5">
    <PackageReceiptCard item={item} />
    {packageIsProcessing(item) ? <div className={`${surfaceClass} p-5`} role="status"><h2 className="font-semibold">Analyzing your package</h2><p className="mt-2 text-sm text-slate-600 dark:text-gray-300">Keep this receipt URL to return later. Offering and boundary confirmation will follow analysis; nothing is published.</p></div>
      : <ScopeSuggestions key={`${item.packageId}:${item.revision}:${initialOfferingId ?? ''}`} item={item} initialOfferingId={initialOfferingId} />}
  </section>;
}

function boundarySuggestion(item: PackageStatus, candidate: PackageCandidate): BoundaryInput {
  return {
    name: candidate.claim?.boundary?.subject || candidate.name,
    scopeStatement: candidate.claim?.boundary?.scope ?? '',
    services: [], componentSnapshotIds: [], includedScopes: [], exclusions: [],
    providerResponsibilities: [], customerResponsibilities: [],
    citations: candidate.citations.map(citation => ({ ...citation, packageId: item.packageId })),
  };
}

function ScopeSuggestions({ item, initialOfferingId }: { item: PackageStatus; initialOfferingId?: string }) {
  const [page, setPage] = useState(1);
  const [choice, setChoice] = useState<{ boundary?: BoundaryInput; name?: string } | null>(null);
  const remote = useRemote(signal => getPackageCandidates(item.packageId, { page, pageSize: 25, type: 'BoundaryClaim' }, signal), [item.packageId, page]);
  if (choice) return <div className="space-y-4">
    <p className="text-sm text-slate-600 dark:text-gray-300">{initialOfferingId ? 'Confirm the selected offering’s boundary.' : 'Confirm an existing offering or create a new one; select the cloud environment explicitly.'} Suggested fields are editable source claims; verify the scope before saving.</p>
    <OfferingIntake initialOfferingId={initialOfferingId} existingPackageId={item.packageId} suggestedName={choice.name} suggestedBoundary={choice.boundary} />
  </div>;
  return <div className="space-y-4">
    <h2 className="text-xl font-semibold">Review extracted scope</h2>
    <p className="text-sm text-slate-600 dark:text-gray-300">Choose a source statement to prefill your offering and boundary, or enter them manually. No resource assignment or authorization is inferred.</p>
    {(item.processingState === 'Failed' || item.processingState === 'NeedsAttention') && <p className={warningClass}>Analysis needs attention. You can associate the retained sources, but unresolved processing and review requirements still apply before publication.</p>}
    <Status loading={remote.loading} error={remote.error} retry={remote.retry} />
    {remote.data && <>
      {!remote.data.items.length && <p className={`${surfaceClass} p-4`}>No boundary claims were extracted. Enter the offering and explicit scope from your source documents.</p>}
      {remote.data.items.map(candidate => <article key={candidate.candidateId} className={`${surfaceClass} space-y-3 p-5`}>
        <h3 className="font-semibold">{candidate.claim?.boundary?.subject || candidate.name}</h3>
        <p className="whitespace-pre-wrap break-words text-sm">{candidate.claim?.boundary?.scope || 'No explicit scope statement extracted.'}</p>
        <p className="text-xs text-slate-500">{candidate.claim?.boundary?.relationship || 'Undetermined'} source claim · requires confirmation</p>
        {candidate.claim?.boundary?.relationship !== 'Included' && <p className={warningClass}>This statement does not explicitly identify included scope. Review it as source context or enter the boundary manually.</p>}
        {candidate.citations.map((citation, index) => <blockquote key={index} className="break-words border-l-2 border-indigo-200 pl-3 text-xs text-slate-600 dark:text-gray-300"><p>{citation.archivePath} · {citation.locator}</p><p className="mt-1 whitespace-pre-wrap">{citation.quote}</p></blockquote>)}
        <button type="button" className={secondaryButtonClass} disabled={candidate.claim?.boundary?.relationship !== 'Included' || !candidate.claim.boundary.scope || !candidate.citations.length || candidate.reviewState === 'Rejected'} onClick={() => setChoice({ boundary: boundarySuggestion(item, candidate), name: candidate.claim?.boundary?.subject || undefined })}>Use this scope as a starting point</button>
      </article>)}
      {remote.data.total > remote.data.pageSize && <Pager {...remote.data} onPage={setPage} />}
    </>}
    {!remote.loading && <button type="button" className={secondaryButtonClass} onClick={() => setChoice({})}>Enter offering and boundary manually</button>}
  </div>;
}
