import { useState } from 'react';

interface CascadeConfirmDialogProps {
  message: string;
  detail?: string;
  confirmLabel?: string;
  onConfirm: () => void | Promise<void>;
  onDismiss: () => void;
}

export default function CascadeConfirmDialog({
  message,
  detail,
  confirmLabel = 'Apply Cascade',
  onConfirm,
  onDismiss,
}: CascadeConfirmDialogProps) {
  const [loading, setLoading] = useState(false);

  const handleConfirm = async () => {
    setLoading(true);
    try {
      await onConfirm();
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="fixed inset-0 z-[60] flex items-center justify-center bg-black/30" onClick={onDismiss}>
      <div className="w-full max-w-sm rounded-xl bg-white p-6 shadow-xl" onClick={e => e.stopPropagation()}>
        <div className="mb-1 flex items-center gap-2">
          <span className="flex h-8 w-8 items-center justify-center rounded-full bg-indigo-100 text-indigo-600">⟳</span>
          <h3 className="text-base font-bold text-gray-900">Cascade Change</h3>
        </div>
        <p className="mt-3 text-sm text-gray-700">{message}</p>
        {detail && <p className="mt-1 text-xs text-gray-500">{detail}</p>}

        <div className="mt-5 flex justify-end gap-2">
          <button
            onClick={onDismiss}
            className="rounded-lg bg-gray-100 px-4 py-2 text-sm text-gray-700 hover:bg-gray-200"
          >
            Skip
          </button>
          <button
            onClick={handleConfirm}
            disabled={loading}
            className="rounded-lg bg-indigo-600 px-4 py-2 text-sm text-white hover:bg-indigo-700 disabled:opacity-50"
          >
            {loading ? 'Applying...' : confirmLabel}
          </button>
        </div>
      </div>
    </div>
  );
}
