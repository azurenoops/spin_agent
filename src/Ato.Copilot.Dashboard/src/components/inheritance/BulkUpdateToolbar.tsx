import { KNOWN_PROVIDERS } from './constants';

interface BulkUpdateToolbarProps {
  selectedCount: number;
  onApply: (inheritanceType: string, provider?: string, customerResponsibility?: string) => void;
  onClearSelection: () => void;
}

export default function BulkUpdateToolbar({ selectedCount, onApply, onClearSelection }: BulkUpdateToolbarProps) {
  if (selectedCount === 0) return null;

  return (
    <div className="flex items-center gap-3 rounded-xl border border-indigo-200 bg-indigo-50 px-4 py-3">
      <span className="text-sm font-medium text-indigo-700">{selectedCount} control{selectedCount !== 1 ? 's' : ''} selected</span>

      <form
        className="flex items-center gap-3"
        onSubmit={e => {
          e.preventDefault();
          const form = e.currentTarget;
          const type = (form.elements.namedItem('bulkType') as HTMLSelectElement).value;
          const prov = (form.elements.namedItem('bulkProvider') as HTMLInputElement).value || undefined;
          const resp = (form.elements.namedItem('bulkResp') as HTMLInputElement).value || undefined;
          onApply(type, prov, resp);
        }}
      >
        <select
          name="bulkType"
          className="rounded-lg border border-gray-300 px-3 py-1.5 text-sm"
          required
        >
          <option value="">Set type...</option>
          <option value="Inherited">Inherited</option>
          <option value="Shared">Shared</option>
          <option value="Customer">Customer</option>
        </select>

        <input
          name="bulkProvider"
          list="bulk-known-providers"
          className="w-60 rounded-lg border border-gray-300 px-3 py-1.5 text-sm"
          placeholder="Provider (for Inherited/Shared)"
        />
        <datalist id="bulk-known-providers">
          {KNOWN_PROVIDERS.map(p => <option key={p} value={p} />)}
        </datalist>

        <input
          name="bulkResp"
          className="w-60 rounded-lg border border-gray-300 px-3 py-1.5 text-sm"
          placeholder="Customer responsibility (optional)"
        />

        <button type="submit" className="rounded-lg bg-indigo-600 px-4 py-1.5 text-sm font-medium text-white hover:bg-indigo-700">
          Apply
        </button>
      </form>

      <button
        onClick={onClearSelection}
        className="ml-auto rounded-lg border border-gray-300 px-3 py-1.5 text-sm text-gray-600 hover:bg-gray-100"
      >
        Clear
      </button>
    </div>
  );
}
