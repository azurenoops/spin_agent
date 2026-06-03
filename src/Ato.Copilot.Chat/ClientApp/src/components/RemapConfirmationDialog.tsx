import React from 'react';

/**
 * Props for the RemapConfirmationDialog component.
 */
interface RemapConfirmationDialogProps {
  /** Whether the dialog is visible. */
  open: boolean;
  /** Name of the capability being remapped. */
  capabilityName: string;
  /** Display name of the new parent capability, or null if making it a root. */
  newParentName: string | null;
  /** Called when the user confirms the remap action. */
  onConfirm: () => void;
  /** Called when the user cancels. */
  onCancel: () => void;
}

/**
 * RemapConfirmationDialog
 *
 * Shows a confirmation modal before remapping a CSP capability's parent.
 * The action is recorded in the capability history upon confirmation (#161).
 *
 * @example
 * <RemapConfirmationDialog
 *   open={isOpen}
 *   capabilityName="Identity Management"
 *   newParentName="Security Controls"
 *   onConfirm={handleConfirm}
 *   onCancel={() => setIsOpen(false)}
 * />
 */
const RemapConfirmationDialog: React.FC<RemapConfirmationDialogProps> = ({
  open,
  capabilityName,
  newParentName,
  onConfirm,
  onCancel,
}) => {
  if (!open) return null;

  const targetDescription = newParentName
    ? <><strong>{newParentName}</strong></>
    : <em>root (no parent)</em>;

  return (
    /* Backdrop */
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black bg-opacity-40">
      {/* Dialog panel */}
      <div
        role="dialog"
        aria-modal="true"
        aria-labelledby="remap-dialog-title"
        aria-describedby="remap-dialog-desc"
        className="bg-white rounded-lg shadow-xl max-w-md w-full mx-4 p-6"
      >
        {/* Icon + Title */}
        <div className="flex items-center gap-3 mb-4">
          <div className="flex-shrink-0 w-10 h-10 rounded-full bg-amber-100 flex items-center justify-center">
            <svg
              className="w-5 h-5 text-amber-600"
              fill="none"
              stroke="currentColor"
              viewBox="0 0 24 24"
              aria-hidden="true"
            >
              <path
                strokeLinecap="round"
                strokeLinejoin="round"
                strokeWidth={2}
                d="M13 16h-1v-4h-1m1-4h.01M12 2a10 10 0 100 20A10 10 0 0012 2z"
              />
            </svg>
          </div>
          <h2 id="remap-dialog-title" className="text-lg font-semibold text-gray-900">
            Confirm Capability Remap
          </h2>
        </div>

        {/* Body */}
        <p id="remap-dialog-desc" className="text-sm text-gray-600 mb-6">
          Remap <strong>{capabilityName}</strong> to {targetDescription}? This action will be
          recorded in the capability history.
        </p>

        {/* Actions */}
        <div className="flex justify-end gap-3">
          <button
            type="button"
            onClick={onCancel}
            className="px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300
                       rounded-md hover:bg-gray-50 focus:outline-none focus:ring-2
                       focus:ring-offset-2 focus:ring-gray-400 transition-colors"
          >
            Cancel
          </button>
          <button
            type="button"
            onClick={onConfirm}
            className="px-4 py-2 text-sm font-medium text-white bg-blue-600 border border-transparent
                       rounded-md hover:bg-blue-700 focus:outline-none focus:ring-2
                       focus:ring-offset-2 focus:ring-blue-500 transition-colors"
          >
            Confirm Remap
          </button>
        </div>
      </div>
    </div>
  );
};

export default RemapConfirmationDialog;
