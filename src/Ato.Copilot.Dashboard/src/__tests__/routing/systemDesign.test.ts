import { describe, expect, it } from 'vitest';
import { SYSTEM_SCREEN_GROUPS, isSystemScreenActive } from '../../features/systems/systemScreenRoutes';

describe('System design navigation contract', () => {
  it('adds only the seventh consolidated definition tab and keeps its deep link active', () => {
    // Arrange
    const definition = SYSTEM_SCREEN_GROUPS.find(group => group.label === 'System definition')!;
    // Act
    const labels = definition.items.map(item => item.label);
    // Assert
    expect(labels).toEqual(['Mission', 'Users', 'Environment & hosting', 'Data',
      'Inventory & boundary', 'Ports & interconnections', 'System design']);
    expect(definition.items[6]!.path).toBe('profile/SystemDesign');
    expect(isSystemScreenActive(definition.items[6]!.path, '/systems/a/profile/SystemDesign', '', '/systems/a')).toBe(true);
    expect(SYSTEM_SCREEN_GROUPS.some(group => group.label === 'System design')).toBe(false);
  });
});
