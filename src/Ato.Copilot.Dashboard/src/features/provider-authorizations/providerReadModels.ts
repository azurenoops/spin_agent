import type { OfferingOverviewData, Page } from './types';

export async function readAllPages<T>(read: (page: number) => Promise<Page<T>>, signal?: AbortSignal): Promise<T[]> {
  const items: T[] = [];
  for (let page = 1; ; page++) {
    signal?.throwIfAborted();
    const result = await read(page);
    if (result.page !== page || !Number.isSafeInteger(result.total) || result.total < 0)
      throw new Error('The server did not return the requested retained-record page.');
    items.push(...result.items);
    if (items.length >= result.total) return items;
    if (!result.items.length)
      throw new Error('The complete retained record list could not be loaded. Refresh before using its totals.');
  }
}

export function sourceReviewLabel(data: OfferingOverviewData): string {
  if (!data.packages.total) return 'No sources recorded';
  if (data.packages.processing) return 'Analysis in progress';
  if (data.packages.needsAttention) return 'Source action needed';
  if (data.packages.awaitingReview) return 'Review required';
  return 'No pending source reviews';
}

export function publishedReleaseLabel(data: OfferingOverviewData): string | null {
  const revisions = data.capabilities.publishedReleaseRevisions;
  if (!revisions?.length) return null;
  const ordered = [...revisions].sort((left, right) => left - right);
  return `${ordered.length === 1 ? 'Revision' : 'Revisions'} ${ordered.join(', ')}`;
}
