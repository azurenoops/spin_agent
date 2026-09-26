export const PACKAGE_FILE_ACCEPT = '.pdf,.docx,.json,.xlsx,.zip,.xml,.csv,.txt';
const MAX_UPLOAD_BYTES = 50 * 1024 * 1024;
const extensions = new Set(PACKAGE_FILE_ACCEPT.split(','));

export function validatePackageFiles(files: File[]): string[] {
  if (!files.length) return ['Choose at least one source file.'];
  const errors: string[] = [];
  if (files.length > 1000) errors.push('Select no more than 1000 source files per package.');
  for (const file of files) {
    const extension = file.name.slice(file.name.lastIndexOf('.')).toLowerCase();
    if (!extensions.has(extension)) errors.push(`${file.name}: unsupported source format.`);
    if (file.size === 0) errors.push(`${file.name}: the source file is empty.`);
  }
  if (files.reduce((total, file) => total + file.size, 0) > MAX_UPLOAD_BYTES) {
    errors.push('The selected package exceeds the 50 MiB total upload limit.');
  }
  return errors;
}
