import { useCallback, useState } from 'react';
import { usePolling } from '../../hooks/usePolling';
import type { TodoList, TodoItem } from '../../types/dashboard';
import apiClient from '../../api/client';
import TodoActionDialog from './TodoActionDialog';
import HelpTooltip from '../help/HelpTooltip';

interface TodoPanelProps {
  systemId: string;
}

export default function TodoPanel({ systemId }: TodoPanelProps) {
  const [selectedItem, setSelectedItem] = useState<TodoItem | null>(null);
  const fetcher = useCallback(
    () => apiClient.get<TodoList>(`/systems/${systemId}/todos`).then((r) => r.data),
    [systemId],
  );
  const { data, loading, error } = usePolling(fetcher, 30000);

  if (loading) {
    return (
      <div className="rounded-xl border border-gray-200 bg-white px-5 py-5">
        <div className="h-4 w-16 animate-pulse rounded bg-gray-200" />
      </div>
    );
  }

  if (error || !data || data.items.length === 0) return null;

  const arrow = (
    <svg className="h-4 w-4 flex-shrink-0 text-gray-400" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
      <path strokeLinecap="round" strokeLinejoin="round" d="M8.25 4.5l7.5 7.5-7.5 7.5" />
    </svg>
  );

  return (
    <>
      <div className="rounded-xl border border-gray-200 bg-white">
        <div className="px-5 pt-5 pb-1">
          <div className="flex items-center">
            <h2 className="text-lg font-semibold text-gray-900">To do</h2>
            <HelpTooltip helpKey="todo" />
          </div>
          <p className="text-xs text-gray-500 mt-0.5">
            Phase: {data.currentPhase}{data.nextPhase ? ` → ${data.nextPhase}` : ''}
          </p>
        </div>

        <div className="divide-y divide-gray-100">
          {data.items.map((item) => (
            <button
              key={item.id}
              type="button"
              onClick={() => setSelectedItem(item)}
              className="flex w-full items-center justify-between gap-4 px-5 py-4 hover:bg-gray-50 transition-colors cursor-pointer text-left"
            >
              <div className="min-w-0">
                <p className="text-sm font-medium text-gray-900">{item.label}</p>
                <p className="text-sm text-gray-500 mt-0.5">{item.detail}</p>
              </div>
              {arrow}
            </button>
          ))}
        </div>
      </div>

      {selectedItem && (
        <TodoActionDialog
          item={selectedItem}
          systemName={data.systemName}
          onClose={() => setSelectedItem(null)}
        />
      )}
    </>
  );
}
