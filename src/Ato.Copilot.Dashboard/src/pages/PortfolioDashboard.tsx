import { useState, useCallback } from 'react';
import PageLayout from '../components/layout/PageLayout';
import PageHero from '../components/layout/PageHero';
import SystemSummaryRow from '../components/cards/SystemSummaryRow';
import IntakeWizard from '../components/wizard/IntakeWizard';
import { usePolling } from '../hooks/usePolling';
import { useIntakeWizard } from '../hooks/useIntakeWizard';
import { getPortfolio, getPortfolioLegacy, updateSystem, generateSystemDescription } from '../api/portfolio';
import type { UpdateSystemBody } from '../api/portfolio';
import type { PortfolioSystemSummary } from '../types/dashboard';

const SORT_COLUMNS = [
  { key: 'name', label: 'System Name' },
  { key: 'impactLevel', label: 'Impact Level' },
  { key: 'rmfPhase', label: 'RMF Phase' },
  { key: 'complianceScore', label: 'Compliance' },
  { key: 'atoExpiration', label: 'ATO' },
  { key: 'openPoamCount', label: 'POA&Ms' },
] as const;

export default function PortfolioDashboard() {
  const [systems, setSystems] = useState<PortfolioSystemSummary[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState('');
  const [sortBy, setSortBy] = useState('name');
  const [sortDir, setSortDir] = useState<'asc' | 'desc'>('asc');
  const [impactFilter, setImpactFilter] = useState('');
  const [rmfFilter, setRmfFilter] = useState('');

  // Intake wizard
  const wizard = useIntakeWizard();

  // Edit system dialog state
  const [editDialogOpen, setEditDialogOpen] = useState(false);
  const [editSystemId, setEditSystemId] = useState('');
  const [editSaving, setEditSaving] = useState(false);
  const [editError, setEditError] = useState('');
  const [editGeneratingDesc, setEditGeneratingDesc] = useState(false);
  const [editForm, setEditForm] = useState<UpdateSystemBody>({
    name: '',
    systemType: 'MajorApplication',
    missionCriticality: 'MissionEssential',
    hostingEnvironment: 'AzureGovernment',
    acronym: '',
    description: '',
  });

  const fetchPortfolio = useCallback(async () => {
    try {
      const query = {
        sortBy,
        sortDir,
        impactLevel: impactFilter || undefined,
        rmfPhase: rmfFilter || undefined,
      };

      let result;
      try {
        result = await getPortfolio(query);
      } catch {
        result = await getPortfolioLegacy(query);
      }

      setSystems(result.items);
      setTotalCount(result.totalCount);
      setLoadError('');
    } catch {
      setSystems([]);
      setTotalCount(0);
      setLoadError('Unable to load systems right now. Please refresh, or verify dashboard-to-MCP connectivity.');
    } finally {
      setLoading(false);
    }
  }, [sortBy, sortDir, impactFilter, rmfFilter]);

  usePolling(fetchPortfolio);

  const toggleSort = (column: string) => {
    if (sortBy === column) {
      setSortDir((d) => (d === 'asc' ? 'desc' : 'asc'));
    } else {
      setSortBy(column);
      setSortDir('asc');
    }
  };

  const openEditDialog = (system: PortfolioSystemSummary) => {
    setEditSystemId(system.systemId);
    setEditForm({
      name: system.name,
      acronym: system.acronym ?? '',
      systemType: system.systemType,
      missionCriticality: system.missionCriticality,
      hostingEnvironment: system.hostingEnvironment,
      description: system.description ?? '',
    });
    setEditError('');
    setEditDialogOpen(true);
  };

  const handleEditSystem = async () => {
    if (!editForm.name?.trim()) return;
    setEditSaving(true);
    setEditError('');
    try {
      await updateSystem(editSystemId, editForm);
      setEditDialogOpen(false);
      await fetchPortfolio();
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to update system';
      setEditError(msg);
    } finally {
      setEditSaving(false);
    }
  };

  return (
    <PageLayout title="Systems">
      <PageHero
        eyebrow="Portfolio"
        title="Systems"
        description="Register, manage, and monitor all information systems in your portfolio."
      />

      {/* Filters */}
      <div className="mb-4 flex flex-wrap items-center gap-3">
        <select
          value={impactFilter}
          onChange={(e) => setImpactFilter(e.target.value)}
          className="rounded-md border border-gray-300 px-3 py-1.5 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
        >
          <option value="">All Impact Levels</option>
          <option value="Low">Low</option>
          <option value="Moderate">Moderate</option>
          <option value="High">High</option>
        </select>
        <select
          value={rmfFilter}
          onChange={(e) => setRmfFilter(e.target.value)}
          className="rounded-md border border-gray-300 px-3 py-1.5 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
        >
          <option value="">All RMF Phases</option>
          <option value="Prepare">Prepare</option>
          <option value="Categorize">Categorize</option>
          <option value="Select">Select</option>
          <option value="Implement">Implement</option>
          <option value="Assess">Assess</option>
          <option value="Authorize">Authorize</option>
          <option value="Monitor">Monitor</option>
        </select>
        <span className="self-center text-sm text-gray-500">
          {totalCount} system{totalCount !== 1 ? 's' : ''}
        </span>
        <div className="ml-auto flex flex-wrap items-center gap-2">
          <button
            type="button"
            onClick={() => wizard.open()}
            className="inline-flex items-center rounded-md bg-indigo-600 px-4 py-1.5 text-sm font-medium text-white hover:bg-indigo-700"
          >
            + Add System
          </button>
        </div>
      </div>

      {/* Intake Wizard */}
      {wizard.state.isOpen && (
        <IntakeWizard
          state={wizard.state}
          onNext={wizard.nextStep}
          onPrev={wizard.prevStep}
          onSkip={wizard.skipStep}
          onGoToStep={wizard.goToStep}
          onCancel={() => { wizard.cancel(); fetchPortfolio(); }}
          onFinish={wizard.finish}
          onSystemId={wizard.setSystemId}
          onValidationErrors={wizard.setValidationErrors}
          onClearErrors={wizard.clearValidationErrors}
        />
      )}

      {/* Edit System Dialog */}
      {editDialogOpen && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40">
          <div className="w-full max-w-lg rounded-lg bg-white p-6 shadow-xl">
            <h2 className="mb-4 text-lg font-semibold">Edit System</h2>

            {editError && (
              <div className="mb-3 rounded bg-red-50 p-2 text-sm text-red-700">{editError}</div>
            )}

            <div className="space-y-3">
              <div>
                <label className="mb-1 block text-sm font-medium text-gray-700">System Name *</label>
                <input
                  value={editForm.name ?? ''}
                  onChange={(e) => setEditForm({ ...editForm, name: e.target.value })}
                  className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm"
                />
              </div>
              <div>
                <label className="mb-1 block text-sm font-medium text-gray-700">Acronym</label>
                <input
                  value={editForm.acronym ?? ''}
                  onChange={(e) => setEditForm({ ...editForm, acronym: e.target.value })}
                  className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm"
                />
              </div>
              <div className="grid grid-cols-2 gap-3">
                <div>
                  <label className="mb-1 block text-sm font-medium text-gray-700">System Type *</label>
                  <select
                    value={editForm.systemType ?? 'MajorApplication'}
                    onChange={(e) => setEditForm({ ...editForm, systemType: e.target.value })}
                    className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm"
                  >
                    <option value="MajorApplication">Major Application</option>
                    <option value="Enclave">Enclave</option>
                    <option value="PlatformIt">Platform IT</option>
                  </select>
                </div>
                <div>
                  <label className="mb-1 block text-sm font-medium text-gray-700">Mission Criticality *</label>
                  <select
                    value={editForm.missionCriticality ?? 'MissionEssential'}
                    onChange={(e) => setEditForm({ ...editForm, missionCriticality: e.target.value })}
                    className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm"
                  >
                    <option value="MissionCritical">Mission Critical</option>
                    <option value="MissionEssential">Mission Essential</option>
                    <option value="MissionSupport">Mission Support</option>
                  </select>
                </div>
              </div>
              <div>
                <label className="mb-1 block text-sm font-medium text-gray-700">Hosting Environment</label>
                <select
                  value={editForm.hostingEnvironment ?? 'AzureGovernment'}
                  onChange={(e) => setEditForm({ ...editForm, hostingEnvironment: e.target.value })}
                  className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm"
                >
                  <option value="AzureGovernment">Azure Government</option>
                  <option value="AzureCommercial">Azure Commercial</option>
                  <option value="OnPremises">On-Premises</option>
                  <option value="Hybrid">Hybrid</option>
                </select>
              </div>
              <div>
                <div className="mb-1 flex items-center justify-between">
                  <label className="block text-sm font-medium text-gray-700">Description</label>
                  <button
                    type="button"
                    onClick={async () => {
                      if (!editForm.name?.trim()) return;
                      setEditGeneratingDesc(true);
                      try {
                        const desc = await generateSystemDescription(editForm.name!, editForm.systemType ?? 'MajorApplication', editForm.missionCriticality ?? 'MissionEssential', editForm.hostingEnvironment ?? 'AzureGovernment');
                        setEditForm({ ...editForm, description: desc });
                      } catch (err: unknown) {
                        const msg = err instanceof Error ? err.message : 'Failed to generate description';
                        if (msg.includes('503')) {
                          alert('AI service is not configured. Contact administrator to enable Azure OpenAI integration.');
                        } else {
                          alert(`Error generating description: ${msg}`);
                        }
                      } finally {
                        setEditGeneratingDesc(false);
                      }
                    }}
                    disabled={!editForm.name?.trim() || editGeneratingDesc}
                    className="inline-flex items-center gap-1.5 rounded-md bg-purple-50 px-2.5 py-1 text-xs font-medium text-purple-700 hover:bg-purple-100 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
                  >
                    {editGeneratingDesc ? (
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
                  value={editForm.description ?? ''}
                  onChange={(e) => setEditForm({ ...editForm, description: e.target.value })}
                  rows={2}
                  className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm"
                />
              </div>
            </div>

            <div className="mt-5 flex justify-end gap-2">
              <button
                onClick={() => setEditDialogOpen(false)}
                className="rounded-md border border-gray-300 px-4 py-2 text-sm text-gray-700 hover:bg-gray-50"
                disabled={editSaving}
              >
                Cancel
              </button>
              <button
                onClick={handleEditSystem}
                disabled={editSaving || !editForm.name?.trim()}
                className="rounded-md bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-700 disabled:opacity-50"
              >
                {editSaving ? 'Saving...' : 'Save Changes'}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Table */}
      {loading ? (
        <p className="text-gray-500">Loading portfolio...</p>
      ) : loadError ? (
        <div className="rounded-lg border border-red-200 bg-red-50 p-6 text-center">
          <p className="text-sm font-medium text-red-800">Systems could not be loaded</p>
          <p className="mt-1 text-sm text-red-700">{loadError}</p>
        </div>
      ) : systems.length === 0 ? (
        <div className="rounded-lg border border-dashed border-gray-300 p-12 text-center">
          <p className="text-gray-500">No systems registered</p>
        </div>
      ) : (
        <div className="overflow-x-auto rounded-lg border border-gray-200 bg-white shadow-sm">
          <table className="min-w-full divide-y divide-gray-200">
            <thead className="bg-gray-50">
              <tr>
                {SORT_COLUMNS.map((col) => (
                  <th
                    key={col.key}
                    onClick={() => toggleSort(col.key)}
                    className="cursor-pointer px-3 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500 hover:text-gray-700"
                  >
                    {col.label}
                    {sortBy === col.key && (
                      <span className="ml-1">{sortDir === 'asc' ? '↑' : '↓'}</span>
                    )}
                  </th>
                ))}
                <th className="w-10 px-3 py-3" />
              </tr>
            </thead>
            <tbody>
              {systems.map((system) => (
                <SystemSummaryRow key={system.systemId} system={system} onEdit={openEditDialog} />
              ))}
            </tbody>
          </table>
        </div>
      )}
    </PageLayout>
  );
}
