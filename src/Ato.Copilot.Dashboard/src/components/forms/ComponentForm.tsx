import { useState, useEffect } from 'react';
import type { CreateComponentRequest, SystemComponentDto, ComponentType, ComponentStatus, SecurityCapabilityDto } from '../../types/dashboard';
import { getCapabilities } from '../../api/capabilities';

const TYPE_OPTIONS: ComponentType[] = ['Person', 'Place', 'Thing'];
const STATUS_OPTIONS: ComponentStatus[] = ['Active', 'Planned', 'Decommissioned'];

interface ComponentFormProps {
  initial?: SystemComponentDto;
  onSubmit: (data: CreateComponentRequest) => void;
  onCancel: () => void;
  isSubmitting?: boolean;
  error?: string | null;
}

export function ComponentForm({ initial, onSubmit, onCancel, isSubmitting, error }: ComponentFormProps) {
  const [name, setName] = useState(initial?.name ?? '');
  const [componentType, setComponentType] = useState<ComponentType>(initial?.componentType ?? 'Thing');
  const [subType, setSubType] = useState(initial?.subType ?? '');
  const [description, setDescription] = useState(initial?.description ?? '');
  const [owner, setOwner] = useState(initial?.owner ?? '');
  const [status, setStatus] = useState<ComponentStatus>(initial?.status ?? 'Active');
  const [selectedCapIds, setSelectedCapIds] = useState<string[]>(
    initial?.linkedCapabilities.map((lc) => lc.capabilityId) ?? [],
  );
  const [capabilities, setCapabilities] = useState<SecurityCapabilityDto[]>([]);

  useEffect(() => {
    let cancelled = false;
    getCapabilities({ pageSize: 200 }).then((res) => {
      if (!cancelled) setCapabilities(res.items);
    });
    return () => { cancelled = true; };
  }, []);

  useEffect(() => {
    if (initial) {
      setName(initial.name);
      setComponentType(initial.componentType);
      setSubType(initial.subType ?? '');
      setDescription(initial.description ?? '');
      setOwner(initial.owner ?? '');
      setStatus(initial.status);
      setSelectedCapIds(initial.linkedCapabilities.map((lc) => lc.capabilityId));
    }
  }, [initial]);

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    onSubmit({
      name,
      componentType,
      subType: subType || undefined,
      description: description || undefined,
      owner: owner || undefined,
      status,
      linkedCapabilityIds: selectedCapIds,
    });
  };

  const toggleCap = (id: string) => {
    setSelectedCapIds((prev) => (prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id]));
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
          className="w-full border rounded px-3 py-2 text-sm focus:ring-2 focus:ring-blue-300 focus:outline-none"
          placeholder="e.g., Microsoft Entra ID"
        />
      </div>

      <div>
        <label className="block text-sm font-medium text-gray-700 mb-2">Type *</label>
        <div className="flex gap-4">
          {TYPE_OPTIONS.map((t) => (
            <label key={t} className="inline-flex items-center gap-1.5 text-sm cursor-pointer">
              <input
                type="radio"
                name="componentType"
                value={t}
                checked={componentType === t}
                onChange={() => setComponentType(t)}
                className="text-blue-600"
              />
              {t}
            </label>
          ))}
        </div>
      </div>

      <div>
        <label className="block text-sm font-medium text-gray-700 mb-1">Sub-Type</label>
        <input
          type="text"
          value={subType}
          onChange={(e) => setSubType(e.target.value)}
          maxLength={200}
          className="w-full border rounded px-3 py-2 text-sm focus:ring-2 focus:ring-blue-300 focus:outline-none"
          placeholder="e.g., Cloud Service, Network Device"
        />
      </div>

      <div>
        <label className="block text-sm font-medium text-gray-700 mb-1">Description</label>
        <textarea
          value={description}
          onChange={(e) => setDescription(e.target.value)}
          maxLength={4000}
          rows={3}
          className="w-full border rounded px-3 py-2 text-sm focus:ring-2 focus:ring-blue-300 focus:outline-none"
          placeholder="Describe this component..."
        />
      </div>

      <div className="grid grid-cols-2 gap-4">
        <div>
          <label className="block text-sm font-medium text-gray-700 mb-1">Owner</label>
          <input
            type="text"
            value={owner}
            onChange={(e) => setOwner(e.target.value)}
            maxLength={200}
            className="w-full border rounded px-3 py-2 text-sm focus:ring-2 focus:ring-blue-300 focus:outline-none"
            placeholder="e.g., Platform Team"
          />
        </div>
        <div>
          <label className="block text-sm font-medium text-gray-700 mb-1">Status</label>
          <select
            value={status}
            onChange={(e) => setStatus(e.target.value as ComponentStatus)}
            className="w-full border rounded px-3 py-2 text-sm focus:ring-2 focus:ring-blue-300 focus:outline-none"
          >
            {STATUS_OPTIONS.map((s) => (
              <option key={s} value={s}>{s}</option>
            ))}
          </select>
        </div>
      </div>

      {capabilities.length > 0 && (
        <div>
          <label className="block text-sm font-medium text-gray-700 mb-1">Linked Capabilities</label>
          <div className="border rounded max-h-40 overflow-y-auto p-2 space-y-1">
            {capabilities.map((cap) => (
              <label key={cap.id} className="flex items-center gap-2 text-sm cursor-pointer hover:bg-gray-50 rounded px-1 py-0.5">
                <input
                  type="checkbox"
                  checked={selectedCapIds.includes(cap.id)}
                  onChange={() => toggleCap(cap.id)}
                  className="text-blue-600"
                />
                <span>{cap.name}</span>
                <span className="text-xs text-gray-400">({cap.provider})</span>
              </label>
            ))}
          </div>
        </div>
      )}

      <div className="flex justify-end gap-3 pt-2">
        <button
          type="button"
          onClick={onCancel}
          className="px-4 py-2 text-sm text-gray-600 hover:text-gray-800"
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
