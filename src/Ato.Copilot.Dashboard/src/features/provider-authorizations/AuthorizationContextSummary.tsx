export function AuthorizationContextSummary({ contextSnapshotHash, impactReviewIds }: {
  contextSnapshotHash?: string | null; impactReviewIds?: string[] | null;
}) {
  if (!contextSnapshotHash && !impactReviewIds?.length) return null;
  return <section className="space-y-2 rounded border border-indigo-200 bg-indigo-50 p-3">
    <h3 className="font-semibold">Publication review context</h3>
    <p className="text-xs text-slate-600">Bound by the server to this publication preview. This does not issue authorization or confirm workload coverage.</p>
    <p className="text-sm">{impactReviewIds?.length ?? 0} impact reviews bound to this preview.</p>
    <details className="text-xs"><summary className="cursor-pointer font-medium">Details</summary>
      {contextSnapshotHash && <p className="mt-2 break-all font-mono">{contextSnapshotHash}</p>}
      <ul className="space-y-1 break-all">{impactReviewIds?.map(id => <li key={id}>{id}</li>)}</ul>
    </details>
  </section>;
}
