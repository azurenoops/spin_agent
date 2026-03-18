import { useCallback, useEffect, useState } from 'react';
import { useParams } from 'react-router-dom';
import MetricCard from '../components/cards/MetricCard';
import { CoverageMatrix } from '../components/charts/CoverageMatrix';
import { BoundaryComparisonTable } from '../components/cards/BoundaryComparisonTable';
import { usePolling } from '../hooks/usePolling';
import { getGapAnalysis } from '../api/gapAnalysis';
import { fetchBoundaryDefinitions } from '../api/boundaries';
import type { BoundaryDefinitionDto, GapAnalysisResponse } from '../types/dashboard';

export default function GapAnalysis() {
  const { id } = useParams<{ id: string }>();
  const [boundaries, setBoundaries] = useState<BoundaryDefinitionDto[]>([]);
  const [boundaryFilter, setBoundaryFilter] = useState<string>('');

  useEffect(() => {
    if (!id) return;
    fetchBoundaryDefinitions(id).then(setBoundaries).catch(() => {});
  }, [id]);

  const fetcher = useCallback(
    () => getGapAnalysis(id!, boundaryFilter || undefined),
    [id, boundaryFilter],
  );
  const { data, loading, error } = usePolling<GapAnalysisResponse>(fetcher, 30000);

  if (loading) {
    return <p className="text-gray-400">Loading gap analysis...</p>;
  }

  if (error || !data) {
    return (
      <div className="bg-yellow-50 border border-yellow-200 rounded-lg p-4">
        <p className="text-yellow-800 font-medium">Gap analysis unavailable</p>
        <p className="text-yellow-600 text-sm mt-1">This system may not have a control baseline configured. Select a baseline first, then map capabilities to controls.</p>
      </div>
    );
  }

  const criticalFamilies = data.familyBreakdown.filter((f) => f.isBelow50).length;

  return (
    <>
      {/* Header */}
      <div className="mb-6">
        <h2 className="text-2xl font-bold text-gray-900">Gap Analysis</h2>
        <p className="mt-1 text-sm text-gray-500">
          Identifies unmapped controls and coverage gaps across the selected baseline, highlighting families that need attention.
        </p>
      </div>

      {/* Summary metrics */}
      <div className="grid grid-cols-2 md:grid-cols-5 gap-4 mb-6">
        <MetricCard title="Total Controls" value={data.totalBaselineControls} subtitle={`${data.baselineLevel} baseline`} />
        <MetricCard title="Covered" value={data.coveredControls} subtitle="With capability mapping" />
        {data.waivedControls > 0 && (
          <MetricCard title="Waived" value={data.waivedControls} subtitle="Excluded by waiver" />
        )}
        <MetricCard title="Gaps" value={data.gapCount} subtitle="Unmapped controls" />
        <MetricCard title="Coverage" value={`${data.coveragePercent}%`} subtitle={criticalFamilies > 0 ? `${criticalFamilies} families below 50%` : 'All families above 50%'} />
      </div>

      {/* Boundary selector */}
      {boundaries.length > 1 && (
        <div className="mb-4 flex items-center gap-2">
          <label htmlFor="boundary-filter" className="text-sm font-medium text-gray-700">
            Boundary:
          </label>
          <select
            id="boundary-filter"
            value={boundaryFilter}
            onChange={(e) => setBoundaryFilter(e.target.value)}
            className="border border-gray-300 rounded-md px-3 py-1.5 text-sm bg-white focus:ring-2 focus:ring-blue-500 focus:border-blue-500"
          >
            <option value="">All Boundaries</option>
            {boundaries.map((b) => (
              <option key={b.id} value={b.id}>
                {b.name}{b.isPrimary ? ' (Primary)' : ''}
              </option>
            ))}
          </select>
        </div>
      )}

      {/* Boundary comparison table — shown when "All Boundaries" is selected */}
      {!boundaryFilter && data.boundaryComparison && data.boundaryComparison.length > 0 && (
        <div className="bg-white border rounded-lg p-4 mb-6">
          <h2 className="text-sm font-semibold text-gray-700 mb-3">Coverage by Boundary</h2>
          <BoundaryComparisonTable items={data.boundaryComparison} />
        </div>
      )}

      {/* Coverage matrix */}
      <div className="bg-white border rounded-lg p-4">
        <h2 className="text-sm font-semibold text-gray-700 mb-3">Coverage by Control Family</h2>
        <CoverageMatrix data={data} />
      </div>
    </>
  );
}
