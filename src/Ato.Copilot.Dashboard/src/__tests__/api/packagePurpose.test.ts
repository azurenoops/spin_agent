import { beforeEach, describe, expect, it, vi } from 'vitest';
import { generatePackage, validatePackage } from '../../api/package';

const client = vi.hoisted(() => ({ post: vi.fn() }));
vi.mock('axios', () => ({ default: { create: () => client } }));
vi.mock('../../features/auth/interceptors', () => ({ attachAuthInterceptor: vi.fn() }));
vi.mock('../../features/auth/msalInstance', () => ({ getMsalInstance: vi.fn(), DEFAULT_API_SCOPES: [] }));

beforeEach(() => client.post.mockReset().mockResolvedValue({ data: {} }));

describe('explicit package-purpose wire contract', () => {
  it('preserves omitted-purpose requests for legacy callers', async () => {
    // Arrange
    const signal = new AbortController().signal;
    // Act
    await validatePackage('system-a', signal);
    await generatePackage('system-a', 'ManifestOnly', signal);
    // Assert
    expect(client.post).toHaveBeenNthCalledWith(1, '/systems/system-a/packages/validate', undefined, { signal });
    expect(client.post).toHaveBeenNthCalledWith(2, '/systems/system-a/packages',
      { evidenceMode: 'ManifestOnly', includeEvidence: true }, { signal });
  });

  it('sends the selected purpose to both validation and generation', async () => {
    // Arrange
    const signal = new AbortController().signal;
    client.post.mockResolvedValue({ data: { purpose: 'InitialSubmission' } });
    // Act
    await validatePackage('system-a', signal, 'InitialSubmission');
    await generatePackage('system-a', 'Embedded', signal, 'InitialSubmission');
    // Assert
    expect(client.post).toHaveBeenNthCalledWith(1, '/systems/system-a/packages/validate', undefined, { signal, params: { purpose: 'InitialSubmission' } });
    expect(client.post).toHaveBeenNthCalledWith(2, '/systems/system-a/packages',
      { evidenceMode: 'Embedded', includeEvidence: true, purpose: 'InitialSubmission' }, { signal });
  });

  it('does not label an older server response as an initial-submission result', async () => {
    // Arrange
    client.post.mockResolvedValue({ data: { isValid: true } });
    // Act / Assert
    await expect(validatePackage('system-a', undefined, 'InitialSubmission'))
      .rejects.toThrow('did not confirm the requested package purpose');
  });

  it('passes explicitly pinned archive context rather than reconstructing it from current state', async () => {
    // Arrange
    const context = { baselinePackageId: 'baseline-a', baselineContentHash: 'hash-a',
      authorizationDecisionId: 'decision-a', expectedDecisionSnapshotHash: 'decision-hash' };
    client.post.mockResolvedValue({ data: { purpose: 'AuthorizedBaselineArchive' } });
    // Act
    await validatePackage('system-a', undefined, 'AuthorizedBaselineArchive', context);
    await generatePackage('system-a', 'ManifestOnly', undefined, 'AuthorizedBaselineArchive', context);
    // Assert
    expect(client.post).toHaveBeenNthCalledWith(1, '/systems/system-a/packages/validate', context,
      { signal: undefined, params: { purpose: 'AuthorizedBaselineArchive' } });
    expect(client.post).toHaveBeenNthCalledWith(2, '/systems/system-a/packages',
      { evidenceMode: 'ManifestOnly', includeEvidence: true, purpose: 'AuthorizedBaselineArchive', retainedContext: context },
      { signal: undefined });
  });
});
