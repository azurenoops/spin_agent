/**
 * captionRegistry.test.tsx — Phase 4 (#1458)
 *
 * Covers (pure functions):
 *   - registryInsert adds a new entry and returns wasInserted=true
 *   - registryInsert deduplicates: same figure_id returns existing entry, wasInserted=false
 *   - registryInsert does not mutate the original map
 *   - registryLookup returns the entry for a known id
 *   - registryLookup returns undefined for unknown id (graceful fallback)
 *   - captionsBySource returns only matching entries
 *
 * Covers (React hook via CaptionRegistryProvider):
 *   - register() inserts a caption and it appears in registry
 *   - register() for duplicate figure_id returns the existing entry
 *   - lookup() returns undefined for unregistered id
 *   - bySource() returns captions matching source_id
 *
 * AAA (Arrange / Act / Assert) marked on each test.
 */

import React from 'react';
import { render, screen, act } from '@testing-library/react';
import {
  registryInsert,
  registryLookup,
  captionsBySource,
  CaptionRegistryProvider,
  useCaptionRegistry,
} from '../captionRegistry';
import type { CaptionEntry } from '../../types/provenance';

// ── Pure function tests ───────────────────────────────────────────────────────

describe('registryInsert', () => {
  it('inserts a new entry and returns wasInserted=true', () => {
    // Arrange
    const map = new Map<string, CaptionEntry>();
    const entry = { figure_id: 'fig-1', caption: 'Figure 1', source_id: 'src-001' };
    // Act
    const [next, inserted] = registryInsert(map, entry);
    // Assert
    expect(inserted).toBe(true);
    expect(next.has('fig-1')).toBe(true);
    expect(next.get('fig-1')!.caption).toBe('Figure 1');
  });

  it('deduplicates: returns wasInserted=false for existing figure_id', () => {
    // Arrange
    const map = new Map<string, CaptionEntry>();
    const entry = { figure_id: 'fig-1', caption: 'First caption', source_id: 'src-001' };
    const [map2] = registryInsert(map, entry);
    // Act
    const [map3, inserted] = registryInsert(map2, { ...entry, caption: 'Duplicate' });
    // Assert
    expect(inserted).toBe(false);
    expect(map3.get('fig-1')!.caption).toBe('First caption'); // original preserved
  });

  it('does not mutate the original map', () => {
    // Arrange
    const map = new Map<string, CaptionEntry>();
    // Act
    registryInsert(map, { figure_id: 'fig-1', caption: 'x' });
    // Assert
    expect(map.size).toBe(0);
  });

  it('stamps registered_at as an ISO string', () => {
    // Arrange
    const map = new Map<string, CaptionEntry>();
    // Act
    const [next] = registryInsert(map, { figure_id: 'fig-1', caption: 'c' });
    // Assert
    const at = next.get('fig-1')!.registered_at;
    expect(() => new Date(at).toISOString()).not.toThrow();
  });
});

describe('registryLookup', () => {
  it('returns the entry for a known figure_id', () => {
    // Arrange
    const map = new Map<string, CaptionEntry>();
    const [next] = registryInsert(map, { figure_id: 'fig-2', caption: 'Figure 2' });
    // Act / Assert
    expect(registryLookup(next, 'fig-2')?.caption).toBe('Figure 2');
  });

  it('returns undefined for an unknown figure_id (graceful fallback)', () => {
    // Arrange
    const map = new Map<string, CaptionEntry>();
    // Act / Assert
    expect(registryLookup(map, 'not-there')).toBeUndefined();
  });
});

describe('captionsBySource', () => {
  it('returns only entries matching the source_id', () => {
    // Arrange
    let map = new Map<string, CaptionEntry>();
    [map] = registryInsert(map, { figure_id: 'fig-1', caption: 'A', source_id: 'src-1' });
    [map] = registryInsert(map, { figure_id: 'fig-2', caption: 'B', source_id: 'src-2' });
    [map] = registryInsert(map, { figure_id: 'fig-3', caption: 'C', source_id: 'src-1' });
    // Act
    const results = captionsBySource(map, 'src-1');
    // Assert
    expect(results).toHaveLength(2);
    expect(results.map((e) => e.figure_id).sort()).toEqual(['fig-1', 'fig-3']);
  });
});

// ── React context tests ───────────────────────────────────────────────────────

// Minimal consumer component that exposes registry operations for testing
function TestConsumer({ onMount }: { onMount: (ctx: ReturnType<typeof useCaptionRegistry>) => void }) {
  const ctx = useCaptionRegistry();
  React.useEffect(() => { onMount(ctx); }, []); // eslint-disable-line react-hooks/exhaustive-deps
  return null;
}

function renderWithProvider(onMount: (ctx: ReturnType<typeof useCaptionRegistry>) => void) {
  render(
    <CaptionRegistryProvider>
      <TestConsumer onMount={onMount} />
    </CaptionRegistryProvider>
  );
}

describe('CaptionRegistryProvider + useCaptionRegistry', () => {
  it('register() adds a caption and lookup() finds it', async () => {
    // Arrange
    let registryCtx!: ReturnType<typeof useCaptionRegistry>;
    renderWithProvider((ctx) => { registryCtx = ctx; });

    // Act
    await act(async () => {
      registryCtx.register({ figure_id: 'fig-hook-1', caption: 'Hook Figure', source_id: 'src-h' });
    });

    // Assert
    const entry = registryCtx.lookup('fig-hook-1');
    expect(entry?.caption).toBe('Hook Figure');
  });

  it('register() is idempotent — duplicate returns existing entry', async () => {
    // Arrange
    let registryCtx!: ReturnType<typeof useCaptionRegistry>;
    renderWithProvider((ctx) => { registryCtx = ctx; });

    await act(async () => {
      registryCtx.register({ figure_id: 'fig-dup', caption: 'First' });
    });

    // Act
    let second!: CaptionEntry;
    await act(async () => {
      second = registryCtx.register({ figure_id: 'fig-dup', caption: 'Second' });
    });

    // Assert
    expect(second.caption).toBe('First');
  });

  it('lookup() returns undefined for unregistered id', () => {
    // Arrange
    let registryCtx!: ReturnType<typeof useCaptionRegistry>;
    renderWithProvider((ctx) => { registryCtx = ctx; });
    // Act / Assert
    expect(registryCtx.lookup('never-registered')).toBeUndefined();
  });

  it('bySource() returns captions matching source_id', async () => {
    // Arrange
    let registryCtx!: ReturnType<typeof useCaptionRegistry>;
    renderWithProvider((ctx) => { registryCtx = ctx; });

    await act(async () => {
      registryCtx.register({ figure_id: 'f1', caption: 'A', source_id: 'src-x' });
      registryCtx.register({ figure_id: 'f2', caption: 'B', source_id: 'src-y' });
      registryCtx.register({ figure_id: 'f3', caption: 'C', source_id: 'src-x' });
    });

    // Act
    const results = registryCtx.bySource('src-x');
    // Assert
    expect(results.map((e) => e.figure_id).sort()).toEqual(['f1', 'f3']);
  });
});
