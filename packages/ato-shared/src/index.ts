/**
 * @ato-copilot/shared — public API (#2683)
 *
 * Shared types and utilities for ATO Copilot TypeScript projects.
 * Import from this package instead of duplicating types locally.
 *
 * @example
 *   import type { SseEvent, McpResponse } from '@ato-copilot/shared';
 *   import type { SourceRecord, DraftClaim } from '@ato-copilot/shared';
 *   import { parseSseChunk } from '@ato-copilot/shared';
 */
export type { SseEvent, SseEventHandler } from './sse';
export { parseSseChunk } from './sse';
export type { ToolExecution, ErrorDetail, McpResponse } from './mcp';
export { generateConversationId } from './mcp';
export type {
  NonEmptyArray,
  SourceRecord,
  RetrievedPassage,
  DraftClaim,
  CitationString,
  CitationStyle,
  CitationOutput,
  EditorSuggestion,
  // Claim↔Evidence Ledger — v2 grounding contract (#a493ec1c)
  VerificationStatus,
  ClaimNode,
  EvidenceBinding,
} from './research-workflow';
// GroundingPort + state machine + migration helpers
export type { GroundingPort, LegacyCitation, MigrationResult, MigrationReport } from './grounding';
export type { VerificationEvent } from './grounding/verificationStateMachine';
export {
  transitionVerificationStatus,
  isTerminalStatus,
  validateEvidenceBinding,
} from './grounding';
export {
  backfillLegacyCitation,
  buildMigrationReport,
  isMigrationPending,
  meetsLegacyRemovalGate,
} from './grounding/migration';
