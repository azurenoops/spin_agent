#!/usr/bin/env node
// =============================================================================
// assert-citation-styles-count.js — #1703
//
// CI gate: fail with exit code 1 if citationStyles.ts exports fewer than 2600
// styles.  Run via:
//
//   node scripts/assert-citation-styles-count.js
//
// Or via the npm script:
//
//   npm run check:citation-styles
//
// Parses the TS file with a simple regex — no TypeScript transpilation needed —
// so it runs fast in CI without extra build steps.
// =============================================================================

const fs = require('fs');
const path = require('path');

const MINIMUM = 2600;
const DATA_FILE = path.join(__dirname, '..', 'src', 'data', 'citationStyles.ts');

if (!fs.existsSync(DATA_FILE)) {
  console.error(`[check:citation-styles] ERROR: Data file not found: ${DATA_FILE}`);
  process.exit(1);
}

const source = fs.readFileSync(DATA_FILE, 'utf8');

// Count top-level objects in the exported array by counting { "id": occurrences.
const matches = source.match(/"id":\s*"/g);
const count = matches ? matches.length : 0;

if (count < MINIMUM) {
  console.error(
    `[check:citation-styles] FAIL: only ${count} styles found in citationStyles.ts — minimum is ${MINIMUM}`
  );
  process.exit(1);
}

console.log(`[check:citation-styles] OK: ${count} styles found (minimum ${MINIMUM})`);
