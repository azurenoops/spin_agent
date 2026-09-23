import { describe, expect, it } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation, useNavigate } from 'react-router-dom';
import SystemAliasRedirect from '../../features/workspaces/SystemAliasRedirect';

function LocationView() {
  const location = useLocation();
  const navigate = useNavigate();
  return (
    <>
      <output data-testid="location">{location.pathname}{location.search}{location.hash}</output>
      <button onClick={() => void navigate(-1)}>Back</button>
    </>
  );
}

describe('SystemAliasRedirect', () => {
  it.each([
    ['/systems/a/categorization?view=all#AC-2', '/systems/a/baseline?view=all#AC-2'],
    ['/workspaces/organizations/org-alpha/systems/a/control-inheritance?view=all#AC-2',
      '/workspaces/organizations/org-alpha/systems/a/inheritance?view=all#AC-2'],
  ])('redirects the real alias component from %s without dropping URL state', async (url, expected) => {
    // Arrange
    const aliasPath = url.split('?')[0]!;
    const targetPath = expected.split('?')[0]!;

    // Act
    render(
      <MemoryRouter initialEntries={[url]}>
        <Routes>
          <Route path={aliasPath} element={<SystemAliasRedirect />} />
          <Route path={targetPath} element={<LocationView />} />
        </Routes>
      </MemoryRouter>,
    );

    // Assert
    expect(await screen.findByTestId('location')).toHaveTextContent(expected);
  });

  it('replaces the alias history entry so Back returns to the preceding page', async () => {
    // Arrange
    render(
      <MemoryRouter initialEntries={['/systems', '/systems/a/categorization']} initialIndex={1}>
        <Routes>
          <Route path="/systems/a/categorization" element={<SystemAliasRedirect />} />
          <Route path="/systems/a/baseline" element={<LocationView />} />
          <Route path="/systems" element={<LocationView />} />
        </Routes>
      </MemoryRouter>,
    );
    expect(await screen.findByTestId('location')).toHaveTextContent('/systems/a/baseline');

    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Back' }));

    // Assert
    expect(screen.getByTestId('location')).toHaveTextContent(/^\/systems$/);
  });
});
