import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { SYSTEM_NAV_GROUPS } from '../../components/layout/SystemLayout';
import SystemAliasRedirect from '../../features/workspaces/SystemAliasRedirect';
import { canonicalizeSystemRoute } from '../../features/workspaces/workspaceRoutes';

describe('system security capability navigation', () => {
  it('replaces two sidebar destinations without removing other system tasks', () => {
    // Arrange
    const items = SYSTEM_NAV_GROUPS.flatMap(group => group.items);
    // Act
    const capabilities = items.filter(item => item.path === 'security-capabilities');
    // Assert
    expect(capabilities).toHaveLength(1);
    expect(capabilities[0]?.label).toBe('Security Capabilities');
    expect(items.some(item => ['components', 'capability-coverage'].includes(item.path))).toBe(false);
    expect(items.map(item => item.path)).toEqual(expect.arrayContaining(['roles', 'boundaries', 'inheritance', 'narratives']));
  });

  it.each([
    ['/systems/a/capabilities?search=monitor#coverage', '/systems/a/security-capabilities?search=monitor#coverage'],
    ['/systems/a/capability-coverage', '/systems/a/security-capabilities'],
    ['/systems/a/components?search=SOC&page=2#selected', '/systems/a/security-capabilities?search=SOC&page=2&view=component#selected'],
    ['/systems/a/components/component-1?source=provider', '/systems/a/security-capabilities?source=provider&view=component&componentId=component-1&componentSource=provider'],
    ['/systems/a/capabilities/capability-1?tab=coverage', '/systems/a/security-capabilities/local/capability-1?tab=coverage'],
    ['/systems/b/capability-coverage?capabilityId=capability-2&source=provider', '/systems/b/security-capabilities/provider/capability-2?capabilityId=capability-2&source=provider'],
    ['/systems/a/components/inventory?search=SOC', '/systems/a/security-capabilities/inventory?search=SOC'],
  ])('preserves legacy system and selected record: %s', (url, expected) => {
    // Arrange
    const original = url;
    // Act
    const result = canonicalizeSystemRoute(original);
    // Assert
    expect(result).toBe(expected);
  });

  it('keeps organization and support scope while redirecting a selected component', async () => {
    // Arrange
    const url = '/workspaces/support/organizations/org-a/systems/system-b/components/person-1?search=SOC#details';
    function Location() {
      const location = useLocation();
      return <output>{location.pathname}{location.search}{location.hash}</output>;
    }
    // Act
    render(<MemoryRouter initialEntries={[url]}><Routes>
      <Route path="/workspaces/support/organizations/:tenant/systems/:id/components/*" element={<SystemAliasRedirect />} />
      <Route path="/workspaces/support/organizations/:tenant/systems/:id/security-capabilities" element={<Location />} />
    </Routes></MemoryRouter>);
    // Assert
    expect(await screen.findByRole('status')).toHaveTextContent(
      '/workspaces/support/organizations/org-a/systems/system-b/security-capabilities?search=SOC&view=component&componentId=person-1&componentSource=local#details',
    );
  });
});
