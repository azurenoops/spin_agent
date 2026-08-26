/**
 * research-workflow — public API (#2683)
 *
 * Re-exports all package boundary contracts for the research-workflow slice.
 * Import from `@ato-copilot/shared` (not from this path directly).
 *
 * @example
 *   import type { SourceRecord, DraftClaim, CitationOutput } from '@ato-copilot/shared';
 */
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
} from './types';
