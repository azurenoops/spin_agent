import { useCallback, useRef, useState, type DragEvent } from 'react';
import type { FileAttachment as FileAttachmentType, FileAttachmentType as AttachmentExt } from '../../types/chat';

const ALLOWED_EXTENSIONS = ['.ckl', '.xccdf', '.xml', '.csv', '.nessus'];
const MAX_FILE_SIZE = 10 * 1024 * 1024; // 10MB

function detectFileType(name: string): AttachmentExt {
  const lower = name.toLowerCase();
  if (lower.endsWith('.ckl')) return 'stig-ckl';
  if (lower.endsWith('.xccdf')) return 'stig-xccdf';
  if (lower.endsWith('.csv')) return 'prisma-csv';
  if (lower.endsWith('.nessus')) return 'nessus';
  return 'unknown';
}

function isAllowedExtension(name: string): boolean {
  const lower = name.toLowerCase();
  return ALLOWED_EXTENSIONS.some((ext) => lower.endsWith(ext));
}

export interface FileAttachmentProps {
  attachments: FileAttachmentType[];
  onAdd: (attachment: FileAttachmentType) => void;
  onRemove: (name: string) => void;
  disabled: boolean;
}

export default function FileAttachment({ attachments, onAdd, onRemove, disabled }: FileAttachmentProps) {
  const fileInputRef = useRef<HTMLInputElement>(null);
  const [isDragOver, setIsDragOver] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const processFile = useCallback(
    (file: File) => {
      setError(null);

      if (!isAllowedExtension(file.name)) {
        setError(`Unsupported file type. Allowed: ${ALLOWED_EXTENSIONS.join(', ')}`);
        return;
      }

      if (file.size > MAX_FILE_SIZE) {
        setError('File too large. Maximum size is 10MB.');
        return;
      }

      const reader = new FileReader();
      reader.onload = () => {
        onAdd({
          name: file.name,
          size: file.size,
          type: detectFileType(file.name),
          content: reader.result as string,
        });
      };
      reader.readAsText(file);
    },
    [onAdd],
  );

  const handleDrop = useCallback(
    (e: DragEvent) => {
      e.preventDefault();
      setIsDragOver(false);
      if (disabled) return;
      const files = Array.from(e.dataTransfer.files);
      files.forEach(processFile);
    },
    [disabled, processFile],
  );

  const handleDragOver = useCallback((e: DragEvent) => {
    e.preventDefault();
    setIsDragOver(true);
  }, []);

  const handleDragLeave = useCallback(() => {
    setIsDragOver(false);
  }, []);

  const handleFileSelect = useCallback(() => {
    const files = fileInputRef.current?.files;
    if (!files) return;
    Array.from(files).forEach(processFile);
    if (fileInputRef.current) fileInputRef.current.value = '';
  }, [processFile]);

  return (
    <div>
      {attachments.length > 0 && (
        <div className="flex flex-wrap gap-1.5 pb-2">
          {attachments.map((a) => (
            <span key={a.name} className="flex items-center gap-1 rounded-full bg-gray-100 px-2 py-0.5 text-xs text-gray-600">
              <svg className="h-3 w-3" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M18.375 12.739l-7.693 7.693a4.5 4.5 0 01-6.364-6.364l10.94-10.94A3 3 0 1119.5 7.372L8.552 18.32m.009-.01l-.01.01m5.699-9.941l-7.81 7.81a1.5 1.5 0 002.112 2.13" />
              </svg>
              {a.name}
              <button
                type="button"
                onClick={() => onRemove(a.name)}
                className="ml-0.5 text-gray-400 hover:text-red-500"
                aria-label={`Remove ${a.name}`}
              >
                <svg className="h-3 w-3" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                  <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" />
                </svg>
              </button>
            </span>
          ))}
        </div>
      )}

      <div
        onDrop={handleDrop}
        onDragOver={handleDragOver}
        onDragLeave={handleDragLeave}
        className={`rounded-lg border-2 border-dashed p-2 text-center text-xs transition-colors ${
          isDragOver
            ? 'border-blue-400 bg-blue-50 text-blue-600'
            : 'border-gray-200 text-gray-400'
        }`}
      >
        <p>Drop files here or{' '}
          <button
            type="button"
            onClick={() => fileInputRef.current?.click()}
            disabled={disabled}
            className="font-medium text-blue-600 hover:text-blue-800 disabled:text-gray-400"
          >
            browse
          </button>
        </p>
        <p className="mt-0.5 text-gray-300">.ckl, .xccdf, .xml, .csv, .nessus (max 10MB)</p>
      </div>

      {error && (
        <p className="mt-1 text-xs text-red-500">{error}</p>
      )}

      <input
        ref={fileInputRef}
        type="file"
        onChange={handleFileSelect}
        accept={ALLOWED_EXTENSIONS.join(',')}
        className="hidden"
        multiple
      />
    </div>
  );
}
