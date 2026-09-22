import { beforeEach, describe, expect, it, vi } from 'vitest';
import { enqueuePackage, getPackageStatus } from '../../api/packages';
import { generatePackage, getPackageDetail } from '../../api/package';

vi.mock('../../api/client', () => ({
  default: { get: vi.fn().mockResolvedValue({ data: {} }), post: vi.fn().mockResolvedValue({ data: {} }) },
}));
vi.mock('../../api/package', () => ({
  generatePackage: vi.fn(), getPackageDetail: vi.fn(), downloadPackageUrl: vi.fn(),
}));
beforeEach(() => vi.clearAllMocks());

describe('package shortcut progress contract', () => {
  it.each([['inline', 'Embedded'], ['full', 'Embedded'], ['linked', 'ManifestOnly']] as const)(
    'uses the existing authenticated v1 package API for %s mode', async (input, mode) => {
      // Arrange
      const signal = new AbortController().signal;
      vi.mocked(generatePackage).mockResolvedValue({ packageId: 'package-a', status: 'Pending', message: 'Queued' });
      // Act
      await enqueuePackage('system-a', input, signal);
      // Assert
      expect(generatePackage).toHaveBeenCalledExactlyOnceWith('system-a', mode, signal);
    },
  );

  it('uses the existing authorized package detail endpoint with cancellation', async () => {
    // Arrange
    const signal = new AbortController().signal;
    // Act
    await getPackageStatus('system-a', 'package-a', signal);
    // Assert
    expect(getPackageDetail).toHaveBeenCalledExactlyOnceWith('system-a', 'package-a', signal);
  });
});
