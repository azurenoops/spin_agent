import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter, Link, Route, Routes, useNavigate } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
import { useState, type ReactNode } from 'react';
import UnsavedDesignGuard from '../../features/system-design/UnsavedDesignGuard';

vi.mock('../../features/workspace-operations/SetupDialog', () => ({
  default: ({ children, title }: { children: ReactNode; title: string }) => <div role="dialog" aria-label={title}>{children}</div>,
}));

describe('unsaved design guard', () => {
  it('blocks routing before discarding, lets the user cancel, then explicitly leave', () => {
    // Arrange
    function Editor() {
      const [dirty, setDirty] = useState(false);
      return <><button onClick={() => setDirty(true)}>Edit</button><Link to="/mission">Mission</Link><UnsavedDesignGuard dirty={dirty} /></>;
    }
    render(<MemoryRouter initialEntries={['/design']}><Routes>
      <Route path="/design" element={<Editor />} /><Route path="/mission" element={<h1>Mission page</h1>} />
    </Routes></MemoryRouter>);
    // Act
    fireEvent.click(screen.getByText('Edit'));
    fireEvent.click(screen.getByRole('link', { name: 'Mission' }));
    // Assert
    expect(screen.queryByText('Mission page')).not.toBeInTheDocument();
    expect(screen.getByRole('dialog', { name: 'Unsaved System design changes' })).toBeVisible();
    fireEvent.click(screen.getByRole('button', { name: 'Keep editing' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('link', { name: 'Mission' }));
    fireEvent.click(screen.getByRole('button', { name: 'Discard and leave' }));
    expect(screen.getByText('Mission page')).toBeVisible();
  });
  it('warns before document unload only while dirty', () => {
    // Arrange
    const { rerender } = render(<MemoryRouter><UnsavedDesignGuard dirty /></MemoryRouter>);
    // Act
    const dirty = new Event('beforeunload', { cancelable: true });
    window.dispatchEvent(dirty);
    rerender(<MemoryRouter><UnsavedDesignGuard dirty={false} /></MemoryRouter>);
    const clean = new Event('beforeunload', { cancelable: true });
    window.dispatchEvent(clean);
    // Assert
    expect(dirty.defaultPrevented).toBe(true);
    expect(clean.defaultPrevented).toBe(false);
  });
  it('guards programmatic replace before leaving the editor', () => {
    // Arrange
    function Editor() {
      const navigate = useNavigate();
      return <><button onClick={() => navigate('/mission', { replace: true })}>Switch page</button><UnsavedDesignGuard dirty /></>;
    }
    render(<MemoryRouter initialEntries={['/design']}><Routes>
      <Route path="/design" element={<Editor />} /><Route path="/mission" element={<h1>Mission page</h1>} />
    </Routes></MemoryRouter>);
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Switch page' }));
    // Assert
    expect(screen.queryByText('Mission page')).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Discard and leave' }));
    expect(screen.getByText('Mission page')).toBeVisible();
  });
  it('restores a browser history entry before offering cancellable discard', () => {
    // Arrange
    const original = window.history.state;
    window.history.replaceState({ idx: 2 }, '');
    const go = vi.spyOn(window.history, 'go').mockImplementation(() => undefined);
    render(<MemoryRouter initialEntries={['/design']}><UnsavedDesignGuard dirty /></MemoryRouter>);
    // Act
    fireEvent(window, new PopStateEvent('popstate', { state: { idx: 1 } }));
    fireEvent(window, new PopStateEvent('popstate', { state: { idx: 2 } }));
    // Assert
    expect(go).toHaveBeenCalledWith(1);
    expect(screen.getByRole('dialog', { name: 'Unsaved System design changes' })).toBeVisible();
    fireEvent.click(screen.getByRole('button', { name: 'Keep editing' }));
    fireEvent(window, new PopStateEvent('popstate', { state: { idx: 1 } }));
    fireEvent(window, new PopStateEvent('popstate', { state: { idx: 2 } }));
    fireEvent.click(screen.getByRole('button', { name: 'Discard and leave' }));
    expect(go).toHaveBeenCalledWith(-1);
    go.mockRestore();
    window.history.replaceState(original, '');
  });
});
