import type { ImpactReview } from './types';

export const impactOutcome = (value: string) => ({
  PendingReview: 'Awaiting review',
  AcceptForPublication: 'Impact accepted for publication',
  RequestChanges: 'Changes requested',
  Reject: 'Change rejected',
}[value] ?? 'Review outcome unavailable');

export const impactTitle = (review: ImpactReview) => review.title || 'Offering change review';
export const impactNeedsAction = (review: ImpactReview) =>
  review.stale || review.disposition === 'PendingReview' || review.disposition === 'RequestChanges';
