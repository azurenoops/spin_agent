import axios from 'axios';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import * as api from '../../features/provider-authorizations/api';
import { receipt } from './testData';

vi.mock('axios', () => ({ default: { request: vi.fn(), isAxiosError: vi.fn(() => false) } }));
beforeEach(() => vi.clearAllMocks());
describe('offering intake exact transport contract', () => {
  it('requests server-filtered Microsoft references with independently correct totals', async () => {
    // Arrange
    const data = { items: [], page: 2, pageSize: 25, total: 26 };
    vi.mocked(axios.request).mockResolvedValue({ status: 200, data: { status: 'success', data } });
    const signal = new AbortController().signal;
    // Act
    const result = await api.listMicrosoftReferences('offering-1', 2, signal);
    // Assert
    expect(result).toEqual(data);
    expect(axios.request).toHaveBeenCalledWith(expect.objectContaining({
      url: '/api/csp/offerings/offering-1/authorization-records',
      params: { page: 2, pageSize: 25, recordKind: 'InheritedMicrosoftReference' }, signal,
    }));
  });
  it('reads the exact current boundary rather than choosing a revision from a paged history', async () => {
    // Arrange
    const data = { offeringId: 'offering-1', boundaryRevisionId: 'boundary-1' };
    vi.mocked(axios.request).mockResolvedValue({ status: 200, data: { status: 'success', data } });
    const signal = new AbortController().signal;
    // Act
    const result = await api.getBoundary('offering-1', 'boundary-1', signal);
    // Assert
    expect(result).toEqual(data);
    expect(axios.request).toHaveBeenCalledWith(expect.objectContaining({
      url: '/api/csp/offerings/offering-1/boundary-revisions/boundary-1', signal,
    }));
  });
  it('rejects boundary data from a different offering or revision', async () => {
    // Arrange
    vi.mocked(axios.request).mockResolvedValue({ status: 200, data: { status: 'success', data: {
      offeringId: 'offering-elsewhere', boundaryRevisionId: 'boundary-elsewhere',
    } } });
    // Act
    const result = api.getBoundary('offering-1', 'boundary-1');
    // Assert
    await expect(result).rejects.toThrow(/selected offering and boundary/);
  });
  it('pages linked capabilities and hosting records independently using the existing protected client', async () => {
    // Arrange
    vi.mocked(axios.request).mockResolvedValue({ status: 200, data: { status: 'success', data: { offeringId: 'offering-1' } } });
    const signal = new AbortController().signal;
    // Act
    await api.getBoundaryOverview('offering-1', 2, 3, signal);
    // Assert
    expect(axios.request).toHaveBeenCalledWith(expect.objectContaining({
      url: '/api/csp/offerings/offering-1/boundary-overview',
      params: { capabilityPage: 2, missionPage: 3, pageSize: 10 }, signal,
    }));
  });
  it('binds a lifecycle event to its stable creation key', async () => {
    // Arrange
    vi.mocked(axios.request).mockResolvedValue({ status: 201, data: { status: 'success', data: { eventId: 'event-1' } } });
    // Act
    await api.lifecycleDecision('offering-1', 'decision-1', {
      expectedRevision: 4, kind: 'Withdrawn', effectiveOn: '2026-09-24', rationale: 'External withdrawal evidence.', citations: [],
    }, 'lifecycle-key');
    // Assert
    expect(axios.request).toHaveBeenCalledWith(expect.objectContaining({ headers: { 'Idempotency-Key': 'lifecycle-key' } }));
  });
  it('rejects missing association metadata instead of claiming an old package is unassociated', async () => {
    // Arrange
    const { association: _association, ...oldStatus } = receipt.package;
    vi.mocked(axios.request).mockResolvedValue({ status: 200, data: { status: 'success', data: oldStatus } });
    // Act
    const request = api.getAssociatedPackage('package-1');
    // Assert
    await expect(request).rejects.toThrow(/association metadata/i);
  });
  it('does not accept a receipt bound to a different offering or boundary', async () => {
    // Arrange
    vi.mocked(axios.request).mockResolvedValue({ status: 202, data: { status: 'success', data: receipt } });
    // Act
    const request = api.uploadPackage('different-offering', { name: 'Source', boundaryRevisionId: 'different-boundary', expectedOfferingRevision: 4 }, [new File(['original'], 'original.txt')], 'stable-key');
    // Assert
    await expect(request).rejects.toThrow(/receipt.*selected offering/i);
  });
  it('binds upload bytes, name, exact boundary, predecessor and asynchronous stable key without a bypass client', async () => {
    // Arrange
    vi.mocked(axios.request).mockResolvedValue({ status: 202, data: { status: 'success', data: receipt } });
    const source = new File(['original'], 'original.txt');
    // Act
    const result = await api.uploadPackage('offering-1', {
      name: 'Source', boundaryRevisionId: 'boundary-1', expectedOfferingRevision: 4, seriesId: 'series-1', previousVersionId: 'previous-version',
    }, [source], 'stable-key');
    // Assert
    expect(result).toEqual(receipt);
    expect(axios.request).toHaveBeenCalledWith(expect.objectContaining({
      method: 'POST', url: '/api/csp/offerings/offering-1/package-versions',
      headers: { 'Idempotency-Key': 'stable-key', Prefer: 'respond-async' },
    }));
    const form = vi.mocked(axios.request).mock.calls[0]?.[0]?.data;
    expect(form).toBeInstanceOf(FormData);
    if (!(form instanceof FormData)) throw new Error('Expected the versioned upload to transmit multipart data.');
    expect(form.get('expectedOfferingRevision')).toBe('4');
    expect(form.get('boundaryRevisionId')).toBe('boundary-1');
    expect(form.get('previousVersionId')).toBe('previous-version');
    expect(form.getAll('files')).toEqual([source]);
  });
});
