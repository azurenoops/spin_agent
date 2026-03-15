import { useCallback } from 'react';
import { useParams, Link } from 'react-router-dom';
import PageLayout from '../components/layout/PageLayout';
import MetricCard from '../components/cards/MetricCard';
import { CoverageMatrix } from '../components/charts/CoverageMatrix';
import { usePolling } from '../hooks/usePolling';
import { getGapAnalysis } from '../api/gapAnalysis';
import type { GapAnalysisResponse } from '../types/dashboard';

export default function GapAnalysis() {
  const { id } = useParams<{ id: string }>();
  const fetcher = useCallback(() => getGapAnalysis(id!), [id]);
  const { data, loading, error } = usePolling<GapAnalysisResponse>(fetcher, 30000);

  if (loading) {
    return (
      <PageLayout title="Gap Analysis">
        <p className="text-gray-400">Loading gap analysis...</p>
      </PageLayout>
    );
  }

  if (error || !data) {
    return (
      <PageLayout title="Gap Analysis">
        <nav className="text-sm text-gray-500 mb-4">
          <Link to="/" className="hover:text-blue-600">Portfolio</Link>
          <span className="mx-1">/</span>
          <Link to={`/systems/${id}`} className="hover:text-blue-600">System Detail</Link>
          <span className="mx-1">/</span>
          <span className="text-gray-800">Gap Analysis</span>
        </nav>
        <div className="bg-yellow-50 border border-yellow-200 rounded-lg p-4">
          <p className="text-yellow-800 font-medium">Gap analysis unavailable</p>
          <p className="text-yellow-600 text-sm mt-1">This system may not have a control baseline configured. Select a baseline first, then map capabilities to controls.</p>
        </div>
      </PageLayout>
    );
  }

  const criticalFamilies = data.familyBreakdown.filter((f) => f.isBelow50).length;

  return (
    <PageLayout title="Gap Analysis">
      {/* Breadcrumb */}
      <nav className="text-sm text-gray-500 mb-4">
        <Link to="/" className="hover:text-blue-600">Portfolio</Link>
        <span className="mx-1">/</span>
        <Link to={`/systems/${id}`} className="hover:text-blue-600">System Detail</Link>
        <span className="mx-1">/</span>
        <span className="text-gray-800">Gap Analysis</span>
      </nav>

      {/* Summary metrics */}
      <div className="grid grid-cols-2 md:grid-cols-4 gap-4 mb-6">
        <MetricCard title="Total Controls" value={data.totalBaselineControls} subtitle={`${data.baselineLevel} baseline`} />
        <MetricCard title="Covered" value={data.coveredControls} subtitle="With capability mapping" />
        <MetricCard title="Gaps" value={data.gapCount} subtitle="Unmapped controls" />
        <MetricCard title="Coverage" value={`${data.coveragePercent}%`} subtitle={criticalFamilies > 0 ? `${criticalFamilies} families below 50%` : 'All families above 50%'} />
      </div>

      {/* Coverage matrix */}
      <div className="bg-white border rounded-lg p-4">
        <h2 className="text-sm font-semibold text-gray-700 mb-3">Coverage by Control Family</h2>
        <CoverageMatrix data={data} />
      </div>
    </PageLayout>
  );
}
