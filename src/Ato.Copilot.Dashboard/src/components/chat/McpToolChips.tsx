import { useChat } from '../../hooks/useChat';

/**
 * T270: MCP Tool Status Chips.
 *
 * Renders inline status chips for each active MCP tool invocation.
 * Chips appear when a tool_start SSE event arrives and disappear on tool_end.
 * Shown in the chat panel between the message list and the input area.
 */
export default function McpToolChips() {
  const { activeToolChips, isProcessing } = useChat() as {
    activeToolChips?: Map<string, number>;
    isProcessing: boolean;
  };

  if (!isProcessing || !activeToolChips || activeToolChips.size === 0) {
    return null;
  }

  return (
    <div className="flex flex-wrap gap-1.5 px-4 py-2 border-t border-gray-100 bg-gray-50">
      {Array.from(activeToolChips.entries()).map(([toolName]) => (
        <span
          key={toolName}
          className="inline-flex items-center gap-1.5 rounded-full bg-indigo-50 border border-indigo-200 px-2.5 py-1 text-xs font-medium text-indigo-700"
        >
          <span className="h-1.5 w-1.5 rounded-full bg-indigo-500 animate-pulse" aria-hidden="true" />
          {toolName}
        </span>
      ))}
    </div>
  );
}
