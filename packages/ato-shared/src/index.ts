/**
 * @ato-copilot/shared — public API (#2683)
 *
 * Shared types and utilities for ATO Copilot TypeScript projects.
 * Import from this package instead of duplicating types locally.
 *
 * @example
 *   import type { SseEvent, McpResponse } from '@ato-copilot/shared';
 *   import { parseSseChunk } from '@ato-copilot/shared';
 */
export type { SseEvent, SseEventHandler } from './sse';
export { parseSseChunk } from './sse';
export type { ToolExecution, ErrorDetail, McpResponse } from './mcp';
export { generateConversationId } from './mcp';
