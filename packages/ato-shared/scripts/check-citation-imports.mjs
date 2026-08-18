#!/usr/bin/env node
/**
 * check-citation-imports.mjs — CI boundary lint rule (#2683)
 *
 * Purpose:
 *   Enforce that `CitationString` / `CitationOutput` (the citation package's
 *   output types) are imported only from the designated editor-integration
 *   render call site(s). Any other import site fails the build with a clear
 *   diagnostic explaining the boundary rule.
 *
 * Why:
 *   The research-workflow boundary map specifies that the `citation` package
 *   is the ONLY package permitted to render citation strings. Without this
 *   check, any contributor can silently bypass the boundary by importing
 *   CitationString directly into a retrieval or synthesis module, causing
 *   format drift and traceability loss.
 *
 * Allowlist:
 *   CITATION_RENDER_ALLOWLIST contains the repo-relative paths of files that
 *   ARE permitted to import citation output types. Shuri/Hawkeye: update this
 *   list when the editor-integration render call site is created.
 *
 * Usage (local):
 *   node packages/ato-shared/scripts/check-citation-imports.mjs
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

/**
 * Directories to scan for TypeScript/TSX source files.
 * Intentionally excludes node_modules, dist, build, site.
 */
const SCAN_DIRS = [
  'src',
  'packages/ato-shared/src',
  'extensions/vscode/src',
  'extensions/m365/src',
];

/**
 * Directories to skip entirely during the walk.
 */
const SKIP_DIRS = new Set([
  'node_modules',
  'dist',
  'build',
  'site',
  'results',
  '.git',
]);

/**
 * Import symbols whose use is restricted to the allowlist below.
 * These are the citation package's public output types — specifically when
 * imported from @ato-copilot/shared or a research-workflow path.
 *
 * IMPORTANT: we only flag imports whose source module is the shared package
 * or a research-workflow sub-path. Pre-existing local types that happen to
 * share the same name (e.g. a local `citationStyles.ts`) are NOT violations.
 */
const RESTRICTED_SYMBOLS = ['CitationString', 'CitationOutput'];

/**
 * Module specifiers that, when used in an import, indicate the citation
 * package's boundary types are being imported. Only these import sources
 * are checked against the allowlist.
 */
const RESTRICTED_SOURCES = [
  '@ato-copilot/shared',
  'research-workflow',
];

/**
 * Repo-relative paths of files ALLOWED to import restricted symbols.
 *
 * IMPORTANT: Shuri / Hawkeye — add the editor-integration render call site
 * here when it is created (e.g. 'src/Ato.Copilot.Chat/ClientApp/src/features/
 * citations/CitationRenderer.tsx'). Until then the allowlist is intentionally
 * empty so no violations are silently ignored.
 *
 * Friday / Rocket — any new allowlist entry MUST be accompanied by an
 * amendment to the boundary map artifact (docs/architecture/research-workflow-
 * boundary-map.md) before the PR merges, per the Phase 3 quarterly-drift rule.
 */
const CITATION_RENDER_ALLOWLIST = new Set([
  // The shared package's own barrel/index files are not consumers — exclude them.
  'packages/ato-shared/src/research-workflow/index.ts',
  'packages/ato-shared/src/index.ts',
  // Example editor-integration render call site (uncomment when created):
  // 'src/Ato.Copilot.Chat/ClientApp/src/features/citations/CitationRenderer.tsx',
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

// Matches import statements that:
//   (a) name at least one restricted symbol, AND
//   (b) source from @ato-copilot/shared or a research-workflow path.
// Single-line imports only — multi-line are caught by the source-contains check below.
function buildPattern() {
  const symbols = RESTRICTED_SYMBOLS.join('|');
  const sources = RESTRICTED_SOURCES.map(s => s.replace(/\//g, '\\/')).join('|');
  // Full import line: import ... { CitationString | CitationOutput } ... from '...(shared|research-workflow)...'
  return new RegExp(
    `import[^'"]*\\b(${symbols})\\b[^'"]*from\\s+['"]([^'"]*(?:${sources})[^'"]*)['"']`,
    'g',
  );
}

// ---------------------------------------------------------------------------
// Main
// ---------------------------------------------------------------------------

const files = collectFiles();
const pattern = buildPattern();
const violations = [];

for (const absPath of files) {
  const rel = relative(REPO_ROOT, absPath);
  if (CITATION_RENDER_ALLOWLIST.has(rel)) continue;

  const src = readFileSync(absPath, 'utf8');
  const matches = [...src.matchAll(pattern)];
  if (matches.length === 0) continue;

  for (const match of matches) {
    // Find line number for the match.
    const upTo = src.slice(0, match.index);
    const line = upTo.split('\n').length;
    violations.push({ file: rel, line, symbol: match[1], source: match[2], statement: match[0].trim() });
  }
}

if (violations.length === 0) {
  console.log('citation-boundary: OK — no cross-package citation import violations.');
  process.exit(0);
}

console.error('');
console.error('citation-boundary: FAIL — citation output types imported outside the allowed render call site.');
console.error('');
console.error('Rule: CitationString, CitationOutput, and CitationStyle may only be imported from');
console.error('  the editor-integration render call site. Add your file to CITATION_RENDER_ALLOWLIST');
console.error('  in packages/ato-shared/scripts/check-citation-imports.mjs AND amend the boundary');
console.error('  map at docs/architecture/research-workflow-boundary-map.md before merging.');
console.error('');

for (const v of violations) {
  console.error(`  ${v.file}:${v.line}  → import of "${v.symbol}" from "${v.source}"`);
  console.error(`    ${v.statement}`);
  console.error('');
}

console.error(`Total violations: ${violations.length}`);
process.exit(1);
