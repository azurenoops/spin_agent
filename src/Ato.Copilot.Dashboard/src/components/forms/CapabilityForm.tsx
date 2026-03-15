import { useState, useEffect } from 'react';
import type { CreateCapabilityRequest, SecurityCapabilityDto } from '../../types/dashboard';

const NIST_FAMILIES: Record<string, string> = {
  AC: 'Access Control',
  AT: 'Awareness and Training',
  AU: 'Audit and Accountability',
  CA: 'Assessment, Authorization, and Monitoring',
  CM: 'Configuration Management',
  CP: 'Contingency Planning',
  IA: 'Identification and Authentication',
  IR: 'Incident Response',
  MA: 'Maintenance',
  MP: 'Media Protection',
  PE: 'Physical and Environmental Protection',
  PL: 'Planning',
  PM: 'Program Management',
  PS: 'Personnel Security',
  PT: 'PII Processing and Transparency',
  RA: 'Risk Assessment',
  SA: 'System and Services Acquisition',
  SC: 'System and Communications Protection',
  SI: 'System and Information Integrity',
  SR: 'Supply Chain Risk Management',
};

const STATUS_OPTIONS = ['Planned', 'InProgress', 'Implemented', 'Deprecated'] as const;
type CapabilityStatusOption = typeof STATUS_OPTIONS[number];

interface CapabilityFormProps {
  initial?: SecurityCapabilityDto;
  onSubmit: (data: CreateCapabilityRequest) => void;
  onCancel: () => void;
  isSubmitting?: boolean;
  error?: string | null;
}

export function CapabilityForm({ initial, onSubmit, onCancel, isSubmitting, error }: CapabilityFormProps) {
  const [name, setName] = useState(initial?.name ?? '');
  const [provider, setProvider] = useState(initial?.provider ?? '');
  const [category, setCategory] = useState(initial?.category ?? '');
  const [description, setDescription] = useState(initial?.description ?? '');
  const [implementationStatus, setImplementationStatus] = useState<CapabilityStatusOption>(initial?.implementationStatus as CapabilityStatusOption ?? 'Planned');
  const [owner, setOwner] = useState(initial?.owner ?? '');

  useEffect(() => {
    if (initial) {
      setName(initial.name);
      setProvider(initial.provider);
      setCategory(initial.category);
      setDescription(initial.description);
      setImplementationStatus(initial.implementationStatus);
      setOwner(initial.owner);
    }
  }, [initial]);

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    onSubmit({ name, provider, category, description, implementationStatus, owner });
  };

  const isValid = name.trim() && provider.trim() && category && description.trim() && owner.trim();

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
          className="w-full border rounded px-3 py-2 text-sm focus:ring-2 focus:ring-blue-300 focus:outline-none"
          placeholder="e.g., Multi-Factor Authentication"
        />
      </div>

      <div>
        <label className="block text-sm font-medium text-gray-700 mb-1">Provider *</label>
        <input
          type="text"
          value={provider}
          onChange={(e) => setProvider(e.target.value)}
          maxLength={200}
          className="w-full border rounded px-3 py-2 text-sm focus:ring-2 focus:ring-blue-300 focus:outline-none"
          placeholder="e.g., Microsoft Entra ID"
        />
      </div>

      <div>
        <label className="block text-sm font-medium text-gray-700 mb-1">Category *</label>
        <select
          value={category}
          onChange={(e) => setCategory(e.target.value)}
          className="w-full border rounded px-3 py-2 text-sm focus:ring-2 focus:ring-blue-300 focus:outline-none"
        >
          <option value="">Select a NIST family...</option>
          {Object.entries(NIST_FAMILIES).map(([code, label]) => (
            <option key={code} value={code}>{code} — {label}</option>
          ))}
        </select>
      </div>

      <div>
        <label className="block text-sm font-medium text-gray-700 mb-1">Description *</label>
        <textarea
          value={description}
          onChange={(e) => setDescription(e.target.value)}
          maxLength={8000}
          rows={4}
          className="w-full border rounded px-3 py-2 text-sm focus:ring-2 focus:ring-blue-300 focus:outline-none"
          placeholder="Describe how this capability works..."
        />
      </div>

      <div className="grid grid-cols-2 gap-4">
        <div>
          <label className="block text-sm font-medium text-gray-700 mb-1">Status *</label>
          <select
            value={implementationStatus}
            onChange={(e) => setImplementationStatus(e.target.value as CapabilityStatusOption)}
            className="w-full border rounded px-3 py-2 text-sm focus:ring-2 focus:ring-blue-300 focus:outline-none"
          >
            {STATUS_OPTIONS.map((s) => (
              <option key={s} value={s}>{s}</option>
            ))}
          </select>
        </div>
        <div>
          <label className="block text-sm font-medium text-gray-700 mb-1">Owner *</label>
          <input
            type="text"
            value={owner}
            onChange={(e) => setOwner(e.target.value)}
            maxLength={200}
            className="w-full border rounded px-3 py-2 text-sm focus:ring-2 focus:ring-blue-300 focus:outline-none"
            placeholder="e.g., Identity Team"
          />
        </div>
      </div>

      <div className="flex justify-end gap-2 pt-2">
        <button
          type="button"
          onClick={onCancel}
          className="px-4 py-2 text-sm text-gray-600 border rounded hover:bg-gray-50"
        >
          Cancel
        </button>
        <button
          type="submit"
          disabled={!isValid || isSubmitting}
          className="px-4 py-2 text-sm bg-blue-600 text-white rounded hover:bg-blue-700 disabled:opacity-50 disabled:cursor-not-allowed"
        >
          {isSubmitting ? 'Saving...' : initial ? 'Update' : 'Create'}
        </button>
      </div>
    </form>
  );
}
