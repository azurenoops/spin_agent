import { useState, useEffect } from 'react';
import type { CreateBoundaryDefinitionRequest, BoundaryDefinitionDto, BoundaryDefinitionType } from '../../types/dashboard';

const TYPE_OPTIONS: BoundaryDefinitionType[] = ['Physical', 'Logical', 'Hybrid'];

interface BoundaryFormProps {
  initial?: BoundaryDefinitionDto;
  onSubmit: (data: CreateBoundaryDefinitionRequest) => void;
  onCancel: () => void;
  isSubmitting?: boolean;
  error?: string | null;
}

export function BoundaryForm({ initial, onSubmit, onCancel, isSubmitting, error }: BoundaryFormProps) {
  const [name, setName] = useState(initial?.name ?? '');
  const [boundaryType, setBoundaryType] = useState<BoundaryDefinitionType>(initial?.boundaryType ?? 'Logical');
  const [description, setDescription] = useState(initial?.description ?? '');

  useEffect(() => {
    if (initial) {
      setName(initial.name);
      setBoundaryType(initial.boundaryType);
      setDescription(initial.description ?? '');
    }
  }, [initial]);

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    onSubmit({
      name,
      boundaryType,
      description: description || undefined,
    });
  };

  const isValid = name.trim().length > 0;

  return (
    <form onSubmit={handleSubmit} className="space-y-4">
      {error && (
        <div className="bg-red-50 text-red-700 p-3 rounded text-sm">{error}</div>
      )}

      <div>
        <label className="block text-sm font-medium text-gray-700 mb-1">Name *</label>
        <input
          type="text"
          value={name}
          onChange={(e) => setName(e.target.value)}
          maxLength={200}
          className="w-full border rounded px-3 py-2 text-sm focus:ring-2 focus:ring-indigo-300 focus:outline-none"
          placeholder="e.g., Production Environment"
        />
      </div>

      <div>
        <label className="block text-sm font-medium text-gray-700 mb-2">Boundary Type *</label>
        <div className="flex gap-4">
          {TYPE_OPTIONS.map((t) => (
            <label key={t} className="inline-flex items-center gap-1.5 text-sm cursor-pointer">
              <input
                type="radio"
                name="boundaryType"
                value={t}
                checked={boundaryType === t}
                onChange={() => setBoundaryType(t)}
                className="text-indigo-600"
              />
              {t}
            </label>
          ))}
        </div>
      </div>

      <div>
        <label className="block text-sm font-medium text-gray-700 mb-1">Description</label>
        <textarea
          value={description}
          onChange={(e) => setDescription(e.target.value)}
          maxLength={2000}
          rows={3}
          className="w-full border rounded px-3 py-2 text-sm focus:ring-2 focus:ring-indigo-300 focus:outline-none resize-y"
          placeholder="Describe the purpose and scope of this boundary..."
        />
      </div>

      <div className="flex gap-2 pt-2">
        <button
          type="submit"
          disabled={!isValid || isSubmitting}
          className="px-4 py-2 text-sm bg-indigo-600 text-white rounded hover:bg-indigo-700 disabled:opacity-50"
        >
          {isSubmitting ? 'Saving...' : initial ? 'Update Boundary' : 'Create Boundary'}
        </button>
        <button
          type="button"
          onClick={onCancel}
          className="px-4 py-2 text-sm bg-gray-100 text-gray-700 rounded hover:bg-gray-200"
        >
          Cancel
        </button>
      </div>
    </form>
  );
}
