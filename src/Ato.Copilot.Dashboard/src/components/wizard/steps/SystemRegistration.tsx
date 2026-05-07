import { useState, useCallback } from 'react';
import { registerSystem, generateSystemDescription } from '../../../api/portfolio';
import type { RegisterSystemBody } from '../../../api/portfolio';

interface RegistrationData {
  name: string;
  acronym: string;
  systemType: string;
  missionCriticality: string;
  hostingEnvironment: string;
  description: string;
}

interface SystemRegistrationProps {
  data: RegistrationData;
  errors: Record<string, string[]>;
  onNext: (data: RegistrationData) => void;
  onSystemId: (id: string) => void;
  onErrors: (errors: Record<string, string[]>) => void;
  onClearErrors: () => void;
}

export default function SystemRegistration({
  data,
  errors,
  onNext,
  onSystemId,
  onErrors,
  onClearErrors,
}: SystemRegistrationProps) {
  const [form, setForm] = useState<RegistrationData>(data);
  const [saving, setSaving] = useState(false);
  const [generatingDesc, setGeneratingDesc] = useState(false);

  const validate = (): Record<string, string[]> => {
    const errs: Record<string, string[]> = {};
    if (!form.name.trim()) errs.name = ['System name is required'];
    else if (form.name.length > 200) errs.name = ['System name cannot exceed 200 characters'];
    if (form.acronym && form.acronym.length > 20) errs.acronym = ['Acronym cannot exceed 20 characters'];
    if (!form.systemType) errs.systemType = ['System type is required'];
    if (!form.missionCriticality) errs.missionCriticality = ['Mission criticality is required'];
    if (!form.hostingEnvironment) errs.hostingEnvironment = ['Hosting environment is required'];
    if (form.description && form.description.length > 2000) errs.description = ['Description cannot exceed 2000 characters'];
    return errs;
  };

  const handleNext = useCallback(async () => {
    onClearErrors();
    const errs = validate();
    if (Object.keys(errs).length > 0) {
      onErrors(errs);
      return;
    }

    setSaving(true);
    try {
      const body: RegisterSystemBody = {
        name: form.name.trim(),
        systemType: form.systemType,
        missionCriticality: form.missionCriticality,
        hostingEnvironment: form.hostingEnvironment,
        acronym: form.acronym.trim() || undefined,
        description: form.description.trim() || undefined,
      };
      const result = await registerSystem(body);
      onSystemId(result.id);
      onNext(form);
    } catch (err: unknown) {
      if (err && typeof err === 'object' && 'errorCode' in err && (err as { errorCode: string }).errorCode === 'DUPLICATE_NAME') {
        onErrors({ name: ['A system with this name already exists'] });
      } else {
        const msg = err instanceof Error ? err.message : 'Failed to register system';
        onErrors({ _form: [msg] });
      }
    } finally {
      setSaving(false);
    }
  }, [form, onNext, onSystemId, onErrors, onClearErrors]);

  const handleGenerateDescription = async () => {
    if (!form.name.trim()) return;
    setGeneratingDesc(true);
    try {
      const desc = await generateSystemDescription(
        form.name,
        form.systemType,
        form.missionCriticality,
        form.hostingEnvironment,
      );
      setForm((f) => ({ ...f, description: desc }));
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to generate description';
      if (msg.includes('503')) {
        onErrors({ description: ['AI service is not configured. Contact administrator to enable Azure OpenAI integration.'] });
      } else {
        onErrors({ description: [msg] });
      }
    } finally {
      setGeneratingDesc(false);
    }
  };

  const fieldError = (field: string) => errors[field]?.[0];

  return (
    <div>
      <h2 className="text-xl font-semibold text-gray-900 mb-1">Step 1: System Registration</h2>
      <p className="text-sm text-gray-500 mb-6">Provide basic information about the information system.</p>

      {errors._form && (
        <div className="mb-4 rounded-md bg-red-50 p-3 text-sm text-red-700">{errors._form[0]}</div>
      )}

      <div className="space-y-4">
        {/* Name */}
        <div>
          <label className="mb-1 block text-sm font-medium text-gray-700">System Name *</label>
          <input
            value={form.name}
            onChange={(e) => setForm({ ...form, name: e.target.value })}
            maxLength={200}
            className={`w-full rounded-md border px-3 py-2 text-sm ${
              fieldError('name') ? 'border-red-300 bg-red-50' : 'border-gray-300'
            }`}
            placeholder="e.g. ACME Portal"
          />
          {fieldError('name') && <p className="mt-1 text-xs text-red-600">{fieldError('name')}</p>}
        </div>

        {/* Acronym */}
        <div>
          <label className="mb-1 block text-sm font-medium text-gray-700">Acronym</label>
          <input
            value={form.acronym}
            onChange={(e) => setForm({ ...form, acronym: e.target.value })}
            maxLength={20}
            className={`w-full rounded-md border px-3 py-2 text-sm ${
              fieldError('acronym') ? 'border-red-300 bg-red-50' : 'border-gray-300'
            }`}
            placeholder="e.g. AP"
          />
          {fieldError('acronym') && <p className="mt-1 text-xs text-red-600">{fieldError('acronym')}</p>}
        </div>

        {/* Type + Criticality */}
        <div className="grid grid-cols-2 gap-4">
          <div>
            <label className="mb-1 block text-sm font-medium text-gray-700">System Type *</label>
            <select
              value={form.systemType}
              onChange={(e) => setForm({ ...form, systemType: e.target.value })}
              className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm"
            >
              <option value="MajorApplication">Major Application</option>
              <option value="Enclave">Enclave</option>
              <option value="PlatformIt">Platform IT</option>
            </select>
            {fieldError('systemType') && <p className="mt-1 text-xs text-red-600">{fieldError('systemType')}</p>}
          </div>
          <div>
            <label className="mb-1 block text-sm font-medium text-gray-700">Mission Criticality *</label>
            <select
              value={form.missionCriticality}
              onChange={(e) => setForm({ ...form, missionCriticality: e.target.value })}
              className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm"
            >
              <option value="MissionCritical">Mission Critical</option>
              <option value="MissionEssential">Mission Essential</option>
              <option value="MissionSupport">Mission Support</option>
            </select>
            {fieldError('missionCriticality') && <p className="mt-1 text-xs text-red-600">{fieldError('missionCriticality')}</p>}
          </div>
        </div>

        {/* Hosting Environment */}
        <div>
          <label className="mb-1 block text-sm font-medium text-gray-700">Hosting Environment *</label>
          <select
            value={form.hostingEnvironment}
            onChange={(e) => setForm({ ...form, hostingEnvironment: e.target.value })}
            className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm"
          >
            <option value="AzureGovernment">Azure Government</option>
            <option value="AzureCommercial">Azure Commercial</option>
            <option value="OnPremises">On-Premises</option>
            <option value="Hybrid">Hybrid</option>
          </select>
          {fieldError('hostingEnvironment') && <p className="mt-1 text-xs text-red-600">{fieldError('hostingEnvironment')}</p>}
        </div>

        {/* Description */}
        <div>
          <div className="mb-1 flex items-center justify-between">
            <label className="block text-sm font-medium text-gray-700">Description</label>
            <button
              type="button"
              onClick={handleGenerateDescription}
              disabled={!form.name.trim() || generatingDesc}
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
            value={form.description}
            onChange={(e) => setForm({ ...form, description: e.target.value })}
            maxLength={2000}
            rows={3}
            className={`w-full rounded-md border px-3 py-2 text-sm ${
              fieldError('description') ? 'border-red-300 bg-red-50' : 'border-gray-300'
            }`}
            placeholder="Brief system description"
          />
          {fieldError('description') && <p className="mt-1 text-xs text-red-600">{fieldError('description')}</p>}
        </div>
      </div>

      {/* Step 1 handles its own Next button since it creates the system */}
      <div className="mt-6 flex justify-end gap-3">
        <button
          onClick={handleNext}
          disabled={saving || !form.name.trim()}
          className="rounded-md bg-indigo-600 px-6 py-2 text-sm font-medium text-white hover:bg-indigo-700 disabled:opacity-50"
        >
          {saving ? 'Creating System...' : 'Next'}
        </button>
      </div>
    </div>
  );
}
