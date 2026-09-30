import type { ReactNode } from 'react';
import type { BoundaryDefinitionDto } from '../../types/dashboard';
import { systemPanel } from './SystemTaskPresentation';

export default function SystemBoundaryInventory({ boundaries, onOpenBoundary, emptyAction }: {
  boundaries: BoundaryDefinitionDto[];
  onOpenBoundary: (boundaryId: string) => void;
  emptyAction?: ReactNode;
}) {
  return <section className={`${systemPanel} boundary-inventory-card`} aria-label="Recorded boundary inventory">
    <h2 className="mb-3 text-lg font-semibold">Mission system boundary</h2>
    <p className="mb-4 text-sm text-slate-500">Recorded boundaries define the system scope. Open a boundary to manage its components.</p>
    <div className="overflow-x-auto">
      <table className="w-full text-left text-xs">
        <thead><tr>
          {['Boundary', 'Type', 'Description', 'Role', 'Open'].map(label => <th key={label} scope="col"
            className="border-b border-[#dfe4ed] px-2.5 py-2.5 text-[10px] font-semibold uppercase tracking-[0.6px] text-slate-500 dark:border-slate-700">
            {label === 'Open' ? <span className="sr-only">Open</span> : label}
          </th>)}
        </tr></thead>
        <tbody className="divide-y divide-slate-100 dark:divide-slate-700">
          {boundaries.length === 0 && <tr><td colSpan={5} className="px-2.5 py-[15px] text-sm text-slate-500">
            <p>No boundaries defined yet.</p>
            {emptyAction && <div className="mt-3">{emptyAction}</div>}
          </td></tr>}
          {boundaries.map(boundary => <tr key={boundary.id}>
            <td className="px-2.5 py-[15px] font-semibold">{boundary.name}</td>
            <td className="px-2.5 py-[15px]">{boundary.boundaryType}</td>
            <td className="px-2.5 py-[15px]">{boundary.description?.trim() || 'No description recorded'}</td>
            <td className="px-2.5 py-[15px]">
              <span className="whitespace-nowrap rounded-[5px] bg-[#f0f2f6] px-2 py-1 text-[11px] text-[#657088]">
                {boundary.isPrimary ? 'Primary' : 'Additional'}
              </span>
            </td>
            <td className="px-2.5 py-[15px]">
              <button type="button" aria-label={`Open boundary ${boundary.name}`}
                className="whitespace-nowrap rounded-[7px] border border-[#dce1ec] px-[9px] py-[5px] text-[11px] text-[#5143d7] dark:border-slate-600 dark:text-indigo-300"
                onClick={() => onOpenBoundary(boundary.id)}>Open →</button>
            </td>
          </tr>)}
        </tbody>
      </table>
    </div>
    <p className="mt-4 rounded-lg bg-[#f4f5f9] p-4 text-xs leading-relaxed text-slate-600 dark:bg-slate-800 dark:text-slate-300">
      Changes to included resources should identify affected inventory, assessment scope and monitoring rules.
    </p>
  </section>;
}
