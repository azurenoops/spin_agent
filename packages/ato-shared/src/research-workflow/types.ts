/**
 * Research-workflow package boundary contracts (#2683).
 *
 * These interfaces define the authoritative data shapes passed between the
 * five packages in the research-workflow slice of the starter lib:
 *
 *   ingestion → retrieval → synthesis → citation → editor-integration
 *
 * Ownership rules (enforced by the `citation-boundary` CI job):
 *   - Only the `citation` package may render citation strings.
 *   - Every `DraftClaim` MUST carry ≥1 `supportingPassageIds` — zero-length
 *     arrays are a type-level contract violation caught by `tsc --noEmit`.
 *   - `retrieval` outputs MUST include a `sourceId` on every passage — prose
 *     without provenance is forbidden.
 *
 * Hand-off routing:
 *   - retrieval / synthesis alignment → Banner
 *   - editor-integration conformance → Shuri / Hawkeye
 *   - CI boundary enforcement → Friday / Rocket
 */

// ---------------------------------------------------------------------------
// Utility
// ---------------------------------------------------------------------------

/**
 * A tuple type that requires at least one element.
 * Constructing `NonEmptyArray<string>` with `[]` is a compile-time error,
 * enforcing the "no orphaned assertion" invariant on DraftClaim.
 */
export type NonEmptyArray<T> = [T, ...T[]];

// ---------------------------------------------------------------------------
// ingestion package
// ---------------------------------------------------------------------------

/**
 * Normalized source document emitted by the `ingestion` package.
 *
 * The ingestion package owns nothing about citations or claims — it only
 * normalizes raw sources into a stable shape downstream packages can rely on.
 */
export interface SourceRecord {
  /** Stable UUID for this source within the session/document. */
  id: string;
  /** Canonical URL of the source (may be a DOI, permalink, or local file URI). */
  url: string;
  /** Full plain-text content of the source document. */
  text: string;
  /** Arbitrary key/value metadata (author, year, title, publisher, etc.). */
  metadata: Record<string, string>;
}

// ---------------------------------------------------------------------------
// retrieval package (Banner's RAG domain)
// ---------------------------------------------------------------------------

/**
 * A passage retrieved from a `SourceRecord` by the `retrieval` package.
 *
 * Every `RetrievedPassage` MUST carry a `sourceId` — the retrieval package
 * is prohibited from emitting prose without provenance. The CI lint rule
 * enforces this by flagging any construction site that omits `sourceId`.
 */
export interface RetrievedPassage {
  /** UUID of the parent `SourceRecord` this passage was extracted from. */
  sourceId: string;
  /** Character-offset span [start, end) within the source document's `text`. */
  span: [start: number, end: number];
  /** Cosine similarity score from the embedding retrieval step (0–1). */
  embeddingScore: number;
  /** Optional verbatim excerpt cached at retrieval time for latency reduction. */
  excerpt?: string;
}

// ---------------------------------------------------------------------------
// synthesis package
// ---------------------------------------------------------------------------

/**
 * A claim produced by the `synthesis` package from one or more passages.
 *
 * The non-empty `supportingPassageIds` field is the core "no orphaned
 * assertion" invariant: every claim MUST trace back to at least one retrieved
 * passage. Using `NonEmptyArray<string>` makes an empty array a compile error,
 * so the invariant is checked by `tsc --noEmit` in CI with zero runtime cost.
 */
export interface DraftClaim {
  /** AI-generated claim text to be inserted into the draft. */
  text: string;
  /**
   * IDs of the `RetrievedPassage` objects that ground this claim.
   * At least one entry is required — constructing this with `[]` is a
   * TypeScript compile error (NonEmptyArray<string> = [string, ...string[]]).
   */
  supportingPassageIds: NonEmptyArray<string>;
  /** Model confidence score (0–1). */
  confidence: number;
}

// ---------------------------------------------------------------------------
// citation package — output type
// ---------------------------------------------------------------------------

/**
 * A formatted citation string emitted by the `citation` package.
 *
 * The citation package is the ONLY package permitted to produce these values.
 * The `CitationString` branded type prevents other packages from constructing
 * raw strings that pose as citations — they must go through `citation`'s
 * formatting API.
 *
 * Import restriction (enforced by the `citation-boundary` CI job):
 *   Import `CitationString` only from `editor-integration`'s designated
 *   citation-render call site. All other import sites fail the build.
 */
export type CitationString = string & { readonly __brand: 'CitationString' };

/** Supported citation format styles. */
export type CitationStyle = 'APA' | 'MLA' | 'Chicago' | 'IEEE';

/**
 * The output record the `citation` package returns per source.
 * Keys are `SourceRecord.id` values.
 */
export interface CitationOutput {
  /** The source this citation record is for. */
  sourceId: string;
  /** Branded, formatted citation string — only constructible by `citation`. */
  formatted: CitationString;
  /** Style used to format this citation. */
  style: CitationStyle;
}

// ---------------------------------------------------------------------------
// Claim↔Evidence Ledger — v2 grounding contract (#a493ec1c)
// ---------------------------------------------------------------------------

/**
 * The verification lifecycle of an EvidenceBinding.
 *
 * Legal transitions (enforced by verificationStateMachine.ts):
 *   unverified → machine_verified
 *   unverified → contradicted
 *   machine_verified → human_confirmed
 *   machine_verified → contradicted
 *   human_confirmed  — terminal (no further transitions)
 *   contradicted     — terminal (no further transitions)
 *
 * Reverification is idempotent: applying the same event to the same state
 * always yields the same output state.
 */
export type VerificationStatus =
  | 'unverified'
  | 'machine_verified'
  | 'human_confirmed'
  | 'contradicted';

/**
 * An atomic claim node that lives in the document model.
 *
 * Produced by the synthesis package and referenced by stable `id`.
 * Agents MUST NOT insert prose into a document without first creating a
 * ClaimNode and registering ≥1 EvidenceBinding through GroundingPort.
 *
 * Schema version is explicit so consumers can gate on it; bump when fields
 * are added or semantics change.
 */
export interface ClaimNode {
  /** Stable UUID for this claim within the document. */
  id: string;
  /**
   * Opaque reference to the document position this claim occupies.
   * Format is editor-substrate-specific (e.g. ProseMirror pos, Slate path).
   * Shuri/Hawkeye own the mapping from spanRef to rendered position.
   */
  spanRef: string;
  /**
   * Identity of the agent that produced this claim (e.g. 'banner-rag',
   * 'clara-synthesis', 'user-manual'). Used in audit trails.
   */
  agentOrigin: string;
  /** ISO-8601 timestamp of initial claim creation. */
  createdAt: string;
  /** Schema version — must be '1' for this release. */
  schemaVersion: '1';
}

/**
 * A binding between a ClaimNode and an evidence span in a real source.
 *
 * Every ClaimNode in a published document MUST carry ≥1 EvidenceBinding.
 * GroundingPort.bind() enforces this at the API level; the CI contract test
 * enforces it statically.
 *
 * For legacy citations backfilled during migration, rawLegacyCitationText
 * preserves the original string so no provenance is lost even when a source
 * span cannot be recovered.
 */
export interface EvidenceBinding {
  /** ID of the ClaimNode this binding grounds. */
  claimId: string;
  /** ID of the SourceRecord the evidence span is drawn from. */
  sourceId: string;
  /**
   * Character-offset span [start, end) within SourceRecord.text.
   * Both values are required; a zero-length span [n, n) is invalid.
   */
  evidenceSpan: [start: number, end: number];
  /** Retrieval confidence score returned by the embedding model (0–1). */
  retrievalConfidence: number;
  /** Current verification lifecycle state. */
  verificationStatus: VerificationStatus;
  /**
   * Preserved verbatim text of the original legacy citation, present only
   * on bindings created by the dual-write migration path. Retained so that
   * bindings whose source span cannot be recovered are not silently dropped.
   */
  rawLegacyCitationText?: string;
  /** Schema version — must be '1' for this release. */
  schemaVersion: '1';
}

// ---------------------------------------------------------------------------
// editor-integration package — consumed types (presentation only)
// ---------------------------------------------------------------------------

/**
 * The input shape the `editor-integration` package receives to render an
 * inline suggestion. It owns no retrieval or citation-formatting logic —
 * only presentation (accept/reject state, AI-vs-source-grounded visual).
 */
export interface EditorSuggestion {
  /** The draft claim to render as an inline suggestion. */
  claim: DraftClaim;
  /**
   * Pre-formatted citations from the `citation` package.
   * editor-integration MUST NOT format citations itself.
   */
  citations: CitationOutput[];
  /** Whether the suggestion has been accepted, rejected, or is still pending. */
  state: 'pending' | 'accepted' | 'rejected';
}
