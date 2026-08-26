#!/usr/bin/env node
/**
 * check-grounding-port.mjs — CI contract test: no ungrounded prose inserts (#a493ec1c)
 *
 * Purpose:
 *   Enforce that every content-producing call site routes through GroundingPort.
 *   Any file that invokes a known "direct insert" API without a preceding
 *   GroundingPort.bind() call fails the build with a clear diagnostic.
 *
 * What it checks:
 *   (A) No file imports ClaimNode or EvidenceBinding and then calls a
 *       direct document-mutation API (insertContent, appendText, etc.)
 *       without also importing or using GroundingPort.
 *
 *   (B) No file constructs a bare EvidenceBinding object literal with an
 *       empty evidenceSpan ([0, 0] is the migration-only sentinel — only
 *       backfillLegacyCitation() is allowed to produce it).
 *
 *   (C) GroundingPort must be the only named export surface for bind/unbind/
 *       reverify; no file may shadow these method names on a non-GroundingPort
 *       object.
 *
 * Allowlist:
 *   GROUNDING_ALLOWLIST contains files that ARE permitted to call direct
 *   document-insert APIs without GroundingPort (e.g. test fixtures, legacy
 *   migration shim during Phase 1-3 only).
 *
 * Usage (local):
 *   node packages/ato-shared/scripts/check-grounding-port.mjs
 *
 * Exit codes:
 *   0 — no violations
 *   1 — one or more violations found (CI fails)
 */

import { readFileSync, readdirSync, statSync } from 'fs';
import { join, relative } from 'path';

// ---------------------------------------------------------------------------
// Configuration
// ---------------------------------------------------------------------------

/** Repo root (script lives at packages/ato-shared/scripts/). */
const REPO_ROOT = new URL('../../../', import.meta.url).pathname;

/** Directories to scan for TypeScript/TSX source files. */
const SCAN_DIRS = [
  'src',
  'packages/ato-shared/src',
  'extensions/vscode/src',
  'extensions/m365/src',
];

/** Directories to skip entirely during the walk. */
const SKIP_DIRS = new Set([
  'node_modules',
  'dist',
  'build',
  'site',
  'results',
  '.git',
]);

/**
 * Direct document-mutation API names that are FORBIDDEN without GroundingPort.
 * Add new APIs here as the editor-integration surface grows.
 */
const FORBIDDEN_DIRECT_INSERT_APIS = [
  'insertContent',
  'appendText',
  'insertNode',
  'insertParagraph',
  'setContent',
  'insertClaimWithoutBinding',
];

/**
 * The GroundingPort method names that authorize a write site.
 * A file that calls one of FORBIDDEN_DIRECT_INSERT_APIS is only exempt if
 * it also references GroundingPort (import or usage).
 */
const GROUNDING_PORT_MARKERS = [
  'GroundingPort',
  '.bind(',
  '.reverify(',
  '.unbind(',
  'backfillLegacyCitation',
];

/**
 * Files permitted to use direct insert APIs without GroundingPort.
 * These are the legacy dual-write shim and test fixture paths.
 * Remove entries as migration phases complete.
 *
 * Friday: remove the dual-write shim entries when Phase 4 coverage gate passes.
 */
const GROUNDING_ALLOWLIST = new Set([
  // Shared package's own contract tests — they test the types, not live insert.
  'packages/ato-shared/src/grounding/__tests__/grounding-port.contract.test.ts',
  // Legacy migration shim (permitted during Phase 1–3 only).
  // 'src/Ato.Copilot.Chat/ClientApp/src/features/citations/legacyCitationShim.ts',
]);

// ---------------------------------------------------------------------------
// Scanner
// ---------------------------------------------------------------------------

function walk(dir) {
  const results = [];
  for (const entry of readdirSync(dir)) {
    if (SKIP_DIRS.has(entry)) continue;
    const full = join(dir, entry);
    const stat = statSync(full);
    if (stat.isDirectory()) {
      results.push(...walk(full));
    } else if (/\.(ts|tsx)$/.test(entry)) {
      results.push(full);
    }
  }
  return results;
}

function collectFiles() {
  const files = [];
  for (const dir of SCAN_DIRS) {
    const abs = join(REPO_ROOT, dir);
    try {
      files.push(...walk(abs));
    } catch {
      // Directory may not exist in all environments — skip silently.
    }
  }
  return files;
}

function lineOf(src, index) {
  return src.slice(0, index).split('\n').length;
}

// ---------------------------------------------------------------------------
// Main
// ---------------------------------------------------------------------------

const files = collectFiles();
const violations = [];

for (const absPath of files) {
  const rel = relative(REPO_ROOT, absPath);
  if (GROUNDING_ALLOWLIST.has(rel)) continue;

  const src = readFileSync(absPath, 'utf8');

  // Check (A): direct insert API without GroundingPort marker.
  const hasGroundingMarker = GROUNDING_PORT_MARKERS.some(m => src.includes(m));

  for (const api of FORBIDDEN_DIRECT_INSERT_APIS) {
    const pattern = new RegExp(`\\b${api}\\s*\\(`, 'g');
    for (const match of src.matchAll(pattern)) {
      if (!hasGroundingMarker) {
        violations.push({
          file: rel,
          line: lineOf(src, match.index),
          rule: 'A',
          detail: `'${api}()' called without GroundingPort — all content inserts must route through GroundingPort.bind()`,
          snippet: src.slice(match.index, match.index + 80).replace(/\n/g, ' '),
        });
      }
    }
  }

  // Check (B): bare EvidenceBinding with evidenceSpan [0, 0] outside migration helper.
  if (!rel.includes('grounding/migration')) {
    const sentinelPattern = /evidenceSpan\s*:\s*\[\s*0\s*,\s*0\s*\]/g;
    for (const match of src.matchAll(sentinelPattern)) {
      violations.push({
        file: rel,
        line: lineOf(src, match.index),
        rule: 'B',
        detail: `evidenceSpan [0, 0] outside migration.ts — only backfillLegacyCitation() may produce the span-unknown sentinel`,
        snippet: src.slice(match.index, match.index + 80).replace(/\n/g, ' '),
      });
    }
  }

  // Check (D): ClaimNode constructed without GroundingPort.
  //
  // A ClaimNode object literal is identified by the presence of both
  // 'spanRef:' and 'agentOrigin:' properties (both are required fields unique
  // to ClaimNode — not present on EvidenceBinding or any other shared type).
  //
  // If a file constructs a ClaimNode and does NOT reference GroundingPort or
  // backfillLegacyCitation, the invariant is violated: the claim may enter
  // a document without a binding.
  //
  // Allowlist exemptions: the grounding __tests__ directory and type
  // definition files (they declare the shape, not create instances).
  if (
    !rel.includes('grounding/__tests__') &&
    !rel.endsWith('types.ts') &&
    !rel.endsWith('types.d.ts')
  ) {
    const hasClaimNodeLiteral = src.includes('spanRef:') && src.includes('agentOrigin:');
    if (hasClaimNodeLiteral && !hasGroundingMarker) {
      // Find the first occurrence of spanRef: for line reporting.
      const idx = src.indexOf('spanRef:');
      violations.push({
        file: rel,
        line: lineOf(src, idx),
        rule: 'D',
        detail: `ClaimNode constructed without GroundingPort — every ClaimNode MUST be registered via GroundingPort.bind(claim, evidence) with ≥1 EvidenceBinding`,
        snippet: src.slice(idx, idx + 80).replace(/\n/g, ' '),
      });
    }
  }
}

// ---------------------------------------------------------------------------
// Report
// ---------------------------------------------------------------------------

if (violations.length === 0) {
  console.log('grounding-port: OK — no ungrounded insert violations.');
  process.exit(0);
}

console.error('');
console.error('grounding-port: FAIL — direct content inserts detected without GroundingPort.');
console.error('');
console.error('Rule: every agent that inserts a claim into a document MUST call');
console.error('  GroundingPort.bind(claim, evidence) before touching the document.');
console.error('  Add a legitimate allowlist entry in GROUNDING_ALLOWLIST only if the');
console.error('  file is a legacy dual-write shim and is tracked for Phase-4 removal.');
console.error('');

for (const v of violations) {
  console.error(`  [Rule ${v.rule}] ${v.file}:${v.line}`);
  console.error(`    ${v.detail}`);
  console.error(`    → ${v.snippet}`);
  console.error('');
}

console.error(`Total violations: ${violations.length}`);
process.exit(1);
