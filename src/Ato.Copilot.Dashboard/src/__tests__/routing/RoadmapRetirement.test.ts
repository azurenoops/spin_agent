import { describe, expect, it } from 'vitest';
import appSource from '../../App.tsx?raw';
import { SYSTEM_NAV_GROUPS } from '../../components/layout/SystemLayout';
import { helpSections } from '../../components/help/helpContent';
import { getIntelligentSuggestions } from '../../components/chat/phasePageSuggestions';

describe('Implementation Roadmap UI retirement', () => {
  it.each(['roadmap', 'implementation-roadmap'])('does not register the %s route', (slug) => {
    // Arrange
    const route = new RegExp(`<Route\\s+path=["']${slug}["']`);

    // Act
    const registered = route.test(appSource);

    // Assert
    expect(registered).toBe(false);
  });

  it('removes the sidebar link without removing other planning links', () => {
    // Arrange
    const items = SYSTEM_NAV_GROUPS.flatMap((group) => group.items);

    // Act
    const roadmapLinks = items.filter((item) => /roadmap/i.test(`${item.path} ${item.label}`));

    // Assert
    expect(roadmapLinks).toEqual([]);
    expect(items.map((item) => item.path)).toEqual(
      expect.arrayContaining(['authorize', 'documents', 'conmon', 'emass/status', 'roles']),
    );
  });

  it('does not advertise the retired page in help', () => {
    // Arrange
    const sections = helpSections;

    // Act
    const roadmapHelp = sections.filter((section) =>
      /roadmap/i.test(`${section.id} ${section.title}`),
    );

    // Assert
    expect(roadmapHelp).toEqual([]);
  });

  it('does not offer roadmap-page suggestions for a stale page context', () => {
    // Arrange
    const context = { page: 'roadmap', systemId: 'system-1' };

    // Act
    const suggestions = getIntelligentSuggestions(context);

    // Assert
    expect(suggestions.map((suggestion) => suggestion.label)).not.toContain('Roadmap progress');
  });
});
