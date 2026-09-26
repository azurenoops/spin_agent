import { describe, expect, it } from 'vitest';
import './crypto';
import { preparePackageUpload } from '../../features/package-imports/uploadIdentity';

describe('upload identity across refresh', () => {
  it('recreates the same key and server file order after reselecting files in a different order', async () => {
    // Arrange
    const original = [new File(['second'], 'b.json', { type: 'application/json', lastModified: 1 }), new File(['first'], 'a.txt', { type: 'text/plain', lastModified: 2 })];
    const reselected = [new File(['first'], 'a.txt', { type: 'text/plain', lastModified: 300 }), new File(['second'], 'b.json', { type: 'application/json', lastModified: 400 })];
    // Act
    const beforeRefresh = await preparePackageUpload(original);
    const afterRefresh = await preparePackageUpload(reselected);
    // Assert
    expect(afterRefresh.key).toBe(beforeRefresh.key);
    expect(afterRefresh.key).toMatch(/^source-sha256-[a-f0-9]{64}$/);
    expect(afterRefresh.files.map(file => file.name)).toEqual(beforeRefresh.files.map(file => file.name));
  });

  it('never gives changed source bytes the same key even with identical file metadata', async () => {
    // Arrange
    const first = new File(['first'], 'source.txt', { type: 'text/plain', lastModified: 1 });
    const changed = new File(['other'], 'source.txt', { type: 'text/plain', lastModified: 1 });
    // Act
    const original = await preparePackageUpload([first]);
    const revised = await preparePackageUpload([changed]);
    // Assert
    expect(revised.key).not.toBe(original.key);
    expect(first.size).toBe(changed.size);
  });

  it('retains duplicate-name occurrences and makes their ordering deterministic', async () => {
    // Arrange
    const first = new File(['one'], 'duplicate.txt');
    const second = new File(['two'], 'duplicate.txt');
    // Act
    const original = await preparePackageUpload([first, second]);
    const reselected = await preparePackageUpload([second, first]);
    // Assert
    expect(reselected.key).toBe(original.key);
    expect(reselected.files).toEqual(original.files);
    expect(reselected.files).toHaveLength(2);
  });
});
