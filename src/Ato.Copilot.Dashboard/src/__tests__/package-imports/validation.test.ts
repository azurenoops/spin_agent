import { describe, expect, it } from 'vitest';
import { validatePackageFiles } from '../../features/package-imports/validation';

describe('package upload validation', () => {
  it('requires a nonempty selection', () => {
    // Arrange
    const files: File[] = [];
    // Act
    const errors = validatePackageFiles(files);
    // Assert
    expect(errors).toContain('Choose at least one source file.');
  });

  it.each(['pdf', 'docx', 'json', 'xlsx', 'zip', 'xml', 'csv', 'txt'])('accepts a supported .%s source', extension => {
    // Arrange
    const files = [new File(['source evidence'], `source.${extension.toUpperCase()}`)];
    // Act
    const errors = validatePackageFiles(files);
    // Assert
    expect(errors).toEqual([]);
  });

  it('rejects unsupported and empty source files instead of silently skipping them', () => {
    // Arrange
    const files = [new File(['executable'], 'unsafe.exe'), new File([], 'empty.pdf')];
    // Act
    const errors = validatePackageFiles(files);
    // Assert
    expect(errors).toEqual(expect.arrayContaining([
      expect.stringContaining('unsafe.exe'),
      expect.stringContaining('empty.pdf'),
    ]));
  });

  it('enforces the total upload budget, not only a per-file size', () => {
    // Arrange
    const files = ['one.pdf', 'two.pdf'].map(name => {
      const file = new File(['source'], name);
      Object.defineProperty(file, 'size', { value: 26 * 1024 * 1024 });
      return file;
    });
    // Act
    const errors = validatePackageFiles(files);
    // Assert
    expect(errors).toContain('The selected package exceeds the 50 MiB total upload limit.');
  });

  it('accepts exactly the byte limit', () => {
    // Arrange
    const file = new File(['source'], 'source.zip');
    Object.defineProperty(file, 'size', { value: 50 * 1024 * 1024 });
    // Act
    const errors = validatePackageFiles([file]);
    // Assert
    expect(errors).toEqual([]);
  });

  it('rejects more than 1000 original files', () => {
    // Arrange
    const files = Array.from({ length: 1001 }, (_, index) => new File(['source'], `${index}.txt`));
    // Act
    const errors = validatePackageFiles(files);
    // Assert
    expect(errors).toContain('Select no more than 1000 source files per package.');
  });
});
