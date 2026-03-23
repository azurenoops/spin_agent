import { useState, useEffect } from 'react';
import type { CreateComponentRequest, SystemComponentDto, ComponentType, ComponentStatus } from '../../types/dashboard';
import { generateComponentDescription } from '../../api/components';

const TYPE_OPTIONS: ComponentType[] = ['Person', 'Place', 'Thing'];
const STATUS_OPTIONS: ComponentStatus[] = ['Active', 'Planned', 'Decommissioned'];
const RMF_ROLE_OPTIONS = [
  { value: '', label: 'None' },
  { value: 'AuthorizingOfficial', label: 'Authorizing Official (AO)' },
  { value: 'Issm', label: 'Information System Security Manager (ISSM)' },
  { value: 'Isso', label: 'Information System Security Officer (ISSO)' },
  { value: 'Sca', label: 'Security Control Assessor (SCA)' },
  { value: 'SystemOwner', label: 'System Owner' },
];

interface ComponentFormProps {
  initial?: SystemComponentDto;
  systemId: string;
  onSubmit: (data: CreateComponentRequest) => void;
  onCancel: () => void;
  isSubmitting?: boolean;
  error?: string | null;
}

export function ComponentForm({ initial, systemId: _systemId, onSubmit, onCancel, isSubmitting, error }: ComponentFormProps) {
  const [name, setName] = useState(initial?.name ?? '');
  const [componentType, setComponentType] = useState<ComponentType>(initial?.componentType ?? 'Thing');
  const [subType, setSubType] = useState(initial?.subType ?? '');
  const [description, setDescription] = useState(initial?.description ?? '');
  const [owner, setOwner] = useState(initial?.owner ?? '');
  const [status, setStatus] = useState<ComponentStatus>(initial?.status ?? 'Active');
  const [generatingDesc, setGeneratingDesc] = useState(false);
  const [rmfRole, setRmfRole] = useState('');
  const [personName, setPersonName] = useState(initial?.personName ?? '');
  const [email, setEmail] = useState(initial?.email ?? '');

  useEffect(() => {
    if (initial) {
      setName(initial.name);
      setComponentType(initial.componentType);
      setSubType(initial.subType ?? '');
      setDescription(initial.description ?? '');
      setOwner(initial.owner ?? '');
      setStatus(initial.status);
      setPersonName(initial.personName ?? '');
      setEmail(initial.email ?? '');
      setRmfRole(initial.rmfRole ?? '');
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
      personName: componentType === 'Person' && personName ? personName : undefined,
      email: componentType === 'Person' && email ? email : undefined,
      status,
      rmfRole: componentType === 'Person' && rmfRole ? rmfRole : undefined,
    });
  };

  const handleGenerateDescription = async () => {
    if (!name.trim()) return;
    setGeneratingDesc(true);
    try {
      const desc = await generateComponentDescription(name, componentType, subType || undefined);
      setDescription(desc);
    } finally {
      setGeneratingDesc(false);
    }
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
        <div className="flex items-center justify-between mb-1">
          <label className="block text-sm font-medium text-gray-700">Description</label>
          <button
            type="button"
            onClick={handleGenerateDescription}
            disabled={!name.trim() || generatingDesc}
            className="inline-flex items-center gap-1.5 rounded-md bg-purple-50 px-2.5 py-1 text-xs font-medium text-purple-700 hover:bg-purple-100 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
          >
            {generatingDesc ? (
              <>
                <svg className="h-3.5 w-3.5 animate-spin" viewBox="0 0 24 24" fill="none">
                  <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                  <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
                </svg>
                Generating…
              </>
            ) : (
              <>
                <svg className="h-3.5 w-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
                  <path strokeLinecap="round" strokeLinejoin="round" d="M9.813 15.904L9 18.75l-.813-2.846a4.5 4.5 0 00-3.09-3.09L2.25 12l2.846-.813a4.5 4.5 0 003.09-3.09L9 5.25l.813 2.846a4.5 4.5 0 003.09 3.09L15.75 12l-2.846.813a4.5 4.5 0 00-3.09 3.09zM18.259 8.715L18 9.75l-.259-1.035a3.375 3.375 0 00-2.455-2.456L14.25 6l1.036-.259a3.375 3.375 0 002.455-2.456L18 2.25l.259 1.035a3.375 3.375 0 002.455 2.456L21.75 6l-1.036.259a3.375 3.375 0 00-2.455 2.456z" />
                </svg>
                AI Summary
              </>
            )}
          </button>
        </div>
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

      {componentType === 'Person' && (
        <div className="grid grid-cols-2 gap-4">
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Person Name</label>
            <input
              type="text"
              value={personName}
              onChange={(e) => setPersonName(e.target.value)}
              maxLength={200}
              className="w-full border rounded px-3 py-2 text-sm focus:ring-2 focus:ring-blue-300 focus:outline-none"
              placeholder="e.g., Jane Smith"
            />
          </div>
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Email</label>
            <input
              type="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              maxLength={200}
              className="w-full border rounded px-3 py-2 text-sm focus:ring-2 focus:ring-blue-300 focus:outline-none"
              placeholder="e.g., jane.smith@agency.gov"
            />
          </div>
        </div>
      )}

      {componentType === 'Person' && (
        <div>
          <label className="block text-sm font-medium text-gray-700 mb-1">RMF Role</label>
          <select
            value={rmfRole}
            onChange={(e) => setRmfRole(e.target.value)}
            className="w-full border rounded px-3 py-2 text-sm focus:ring-2 focus:ring-blue-300 focus:outline-none"
          >
            {RMF_ROLE_OPTIONS.map((opt) => (
              <option key={opt.value} value={opt.value}>{opt.label}</option>
            ))}
          </select>
          <p className="text-xs text-gray-500 mt-1">Assigns this person an RMF role on the system per DoDI 8510.01</p>
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
