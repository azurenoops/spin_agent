import {
  AreaChart,
  Area,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
  ResponsiveContainer,
  Legend,
} from 'recharts';
import type { RiskCurvePoint } from '../../types/dashboard';

interface Props {
  projected: RiskCurvePoint[];
  actual?: RiskCurvePoint[];
}

export default function RiskReductionCurve({ projected, actual }: Props) {
  // Merge projected and actual data by week
  const merged = projected.map((p) => {
    const a = actual?.find((x) => x.week === p.week);
    return {
      week: `Wk ${p.week}`,
      projected: Math.round(p.riskReductionPercent * 10) / 10,
      actual: a ? Math.round(a.riskReductionPercent * 10) / 10 : undefined,
    };
  });

  return (
    <ResponsiveContainer width="100%" height={280}>
      <AreaChart data={merged} margin={{ top: 10, right: 30, left: 0, bottom: 0 }}>
        <CartesianGrid strokeDasharray="3 3" stroke="#e5e7eb" />
        <XAxis dataKey="week" tick={{ fontSize: 12 }} />
        <YAxis
          domain={[0, 100]}
          tickFormatter={(v: number) => `${v}%`}
          tick={{ fontSize: 12 }}
        />
        <Tooltip formatter={(value: number) => `${value}%`} />
        <Legend />
        <Area
          type="monotone"
          dataKey="projected"
          stroke="#6366f1"
          fill="#6366f1"
          fillOpacity={0.1}
          strokeWidth={2}
          name="Projected Risk Reduction"
        />
        {actual && actual.length > 0 && (
          <Area
            type="monotone"
            dataKey="actual"
            stroke="#10b981"
            fill="#10b981"
            fillOpacity={0.1}
            strokeWidth={2}
            strokeDasharray="5 5"
            name="Actual Risk Reduction"
          />
        )}
      </AreaChart>
    </ResponsiveContainer>
  );
}
