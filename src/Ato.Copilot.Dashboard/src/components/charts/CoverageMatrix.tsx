import { useState } from 'react';
import type { GapAnalysisResponse } from '../../types/dashboard';

interface CoverageMatrixProps {
  data: GapAnalysisResponse;
}

export function CoverageMatrix({ data }: CoverageMatrixProps) {
  const [expandedFamily, setExpandedFamily] = useState<string | null>(null);

  return (
    <div className="overflow-x-auto">
      <table className="w-full text-sm">
        <thead>
          <tr className="border-b text-left text-xs text-gray-500 uppercase tracking-wider">
            <th className="py-2 px-3">Family</th>
            <th className="py-2 px-3 text-right">Total</th>
            <th className="py-2 px-3 text-right">Covered</th>
            <th className="py-2 px-3 text-right">Gaps</th>
            <th className="py-2 px-3 w-48">Coverage</th>
          </tr>
        </thead>
        <tbody>
          {/* Summary row */}
          <tr className="bg-gray-50 font-semibold border-b">
            <td className="py-2 px-3">All Families</td>
            <td className="py-2 px-3 text-right">{data.totalBaselineControls}</td>
            <td className="py-2 px-3 text-right">{data.coveredControls}</td>
            <td className="py-2 px-3 text-right">{data.gapCount}</td>
            <td className="py-2 px-3">
              <CoverageBar percent={data.coveragePercent} isBelow50={data.coveragePercent < 50} />
            </td>
          </tr>
          {data.familyBreakdown.map((f) => (
            <>
              <tr
                key={f.familyCode}
                className={`border-b cursor-pointer hover:bg-gray-50 ${f.isBelow50 ? 'bg-red-50' : ''}`}
                onClick={() => setExpandedFamily(expandedFamily === f.familyCode ? null : f.familyCode)}
              >
                <td className="py-2 px-3">
                  <div className="flex items-center gap-2">
                    <svg
                      className={`w-3 h-3 text-gray-400 transition-transform ${expandedFamily === f.familyCode ? 'rotate-90' : ''}`}
                      fill="currentColor" viewBox="0 0 20 20"
                    >
                      <path d="M6 4l8 6-8 6V4z" />
                    </svg>
                    <span className="font-mono text-xs font-medium">{f.familyCode}</span>
                    <span className="text-gray-600">{f.familyName}</span>
                  </div>
                </td>
                <td className="py-2 px-3 text-right">{f.totalControls}</td>
                <td className="py-2 px-3 text-right">{f.coveredControls}</td>
                <td className="py-2 px-3 text-right font-medium">
                  {f.gapCount > 0 && <span className={f.isBelow50 ? 'text-red-600' : 'text-yellow-600'}>{f.gapCount}</span>}
                  {f.gapCount === 0 && <span className="text-green-600">0</span>}
                </td>
                <td className="py-2 px-3">
                  <CoverageBar percent={f.coveragePercent} isBelow50={f.isBelow50} />
                </td>
              </tr>
              {expandedFamily === f.familyCode && f.unmappedControls.length > 0 && (
                <tr key={`${f.familyCode}-detail`}>
                  <td colSpan={5} className="bg-gray-50 px-8 py-3">
                    <p className="text-xs font-semibold text-gray-500 mb-2">
                      Unmapped Controls ({f.unmappedControls.length})
                    </p>
                    <div className="space-y-1">
                      {f.unmappedControls.map((c) => (
                        <div key={c.controlId} className="flex items-center gap-2 text-sm">
                          <span className="font-mono text-xs text-red-600">{c.controlId}</span>
                          <span className="text-gray-600">{c.controlTitle}</span>
                        </div>
                      ))}
                    </div>
                  </td>
                </tr>
              )}
            </>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function CoverageBar({ percent, isBelow50 }: { percent: number; isBelow50: boolean }) {
  const color = isBelow50 ? 'bg-red-500' : percent >= 80 ? 'bg-green-500' : 'bg-yellow-500';
  return (
    <div className="flex items-center gap-2">
      <div className="flex-1 bg-gray-200 rounded-full h-2">
        <div className={`${color} rounded-full h-2 transition-all`} style={{ width: `${percent}%` }} />
      </div>
      <span className="text-xs text-gray-500 w-10 text-right">{percent}%</span>
    </div>
  );
}
