import { useEffect, type ReactNode } from 'react';
import { fireEvent, render, screen, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import SystemLayout, { useSystemContext } from '../../components/layout/SystemLayout';

vi.mock('../../components/layout/PageLayout', () => ({
  default: ({ children, leftPanel, sidePanel }: { children: ReactNode; leftPanel: ReactNode; sidePanel: ReactNode }) =>
    <><nav aria-label="System navigation">{leftPanel}</nav><main>{children}</main><aside aria-label="System context">{sidePanel}</aside></>,
}));
vi.mock('../../hooks/usePolling', () => ({ usePolling: (fetch: () => void) => useEffect(() => { void fetch(); }, [fetch]) }));
vi.mock('../../hooks/useSettings', () => ({ useSettings: () => ({ settings: { role: 'ISSM' } }) }));
vi.mock('../../features/workspaces/WorkspaceBoundary', () => ({ useWorkspaceSession: () => null }));
vi.mock('../../components/cards/TodoPanel', () => ({ default: () => <p>Existing system tasks</p> }));
vi.mock('../../api/systemDetail', () => ({
  getSystemDetail: async () => ({ systemId: 'system-a', name: 'Mission Alpha', categorization: null }),
}));
vi.mock('../../api/systemProfile', () => ({ getProfileCompleteness: async () => null }));
vi.mock('../../api/client', () => ({ default: { get: async () => ({ data: { items: [] } }) } }));

function CapabilityContent() {
  const { setPageContext } = useSystemContext();
  useEffect(() => {
    setPageContext?.(<h2>System capability follow-up</h2>);
    return () => setPageContext?.(null);
  }, [setPageContext]);
  return <h1>Applied security capabilities</h1>;
}

describe('system capability layout integration', () => {
  it('keeps breadcrumbs, unified sidebar and existing right-panel tasks alongside capability context', async () => {
    // Arrange / Act
    render(<MemoryRouter initialEntries={['/systems/system-a/security-capabilities']}><Routes>
      <Route path="/systems/:id" element={<SystemLayout />}>
        <Route path="security-capabilities" element={<CapabilityContent />} />
      </Route>
    </Routes></MemoryRouter>);
    // Assert
    await screen.findByRole('heading', { name: 'Applied security capabilities' });
    const navigation = screen.getByRole('navigation', { name: 'System navigation' });
    expect(within(navigation).getByRole('link', { name: 'Security Capabilities' })).toHaveAttribute('href', '/systems/system-a/security-capabilities');
    expect(screen.getByRole('link', { name: 'Mission Alpha' })).toHaveAttribute('href', '/systems/system-a');
    expect(await screen.findByRole('heading', { name: 'System capability follow-up' })).toBeVisible();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'To do' }));
    // Assert
    expect(screen.getByText('Existing system tasks')).toBeVisible();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'System Details' }));
    // Assert
    expect(screen.getByText('System Type')).toBeVisible();
  });
});
