import { expect, it, vi } from 'vitest';
import { readAllPages, sourceReviewLabel } from '../../features/provider-authorizations/providerReadModels';
import { offeringOverview } from './overviewFixtures';

it('uses every retained page rather than presenting a first-page subtotal as a global count', async () => {
  // Arrange
  const read = vi.fn(async (page: number) => ({ page, pageSize: 1, total: 2, items: [page] }));
  // Act
  const items = await readAllPages(read);
  // Assert
  expect(items).toEqual([1, 2]);
  expect(read).toHaveBeenCalledTimes(2);
});

it('does not turn a missing page or mismatched page into a complete total', async () => {
  // Arrange
  const missing = async (page: number) => ({ page, pageSize: 1, total: 2, items: [] as number[] });
  const wrong = async () => ({ page: 2, pageSize: 1, total: 1, items: [1] });
  // Act / Assert
  await expect(readAllPages(missing)).rejects.toThrow('complete retained record list');
  await expect(readAllPages(wrong)).rejects.toThrow('requested retained-record page');
});

it('does not continue fetching after navigation cancels the read', async () => {
  // Arrange
  const controller = new AbortController();
  controller.abort();
  const read = vi.fn();
  // Act / Assert
  await expect(readAllPages(read, controller.signal)).rejects.toThrow();
  expect(read).not.toHaveBeenCalled();
});

it.each([
  [0, 0, 0, 0, 'No sources recorded'],
  [1, 1, 0, 0, 'Analysis in progress'],
  [1, 0, 1, 0, 'Source action needed'],
  [1, 0, 0, 1, 'Review required'],
  [1, 0, 0, 0, 'No pending source reviews'],
])('describes source review without promoting it to an authorization claim', (total, processing, needsAttention, awaitingReview, expected) => {
  // Arrange
  const data = offeringOverview();
  data.packages = { ...data.packages, total: Number(total), processing: Number(processing), needsAttention: Number(needsAttention), awaitingReview: Number(awaitingReview) };
  // Act / Assert
  expect(sourceReviewLabel(data)).toBe(expected);
});
