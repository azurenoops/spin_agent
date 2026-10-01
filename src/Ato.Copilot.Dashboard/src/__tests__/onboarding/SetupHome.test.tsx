import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
import SetupHome from '../../features/onboarding/shared/SetupHome';
import { WorkspaceNavigationProvider } from '../../features/workspaces/workspaceNavigation';

function renderHome(props: Partial<React.ComponentProps<typeof SetupHome>> = {}) {
  return render(<MemoryRouter><WorkspaceNavigationProvider workspace={{ kind: 'csp' }}>
    <SetupHome mode="start" workspaceName="Synthetic provider" options={[]} records={[]}
      loading={false} onChangeMode={vi.fn()} {...props} />
  </WorkspaceNavigationProvider></MemoryRouter>);
}

describe('authorized setup entry and server-record resume', () => {
  it('shows only permitted options and keeps links in the current workspace', () => {
    // Arrange / Act
    renderHome({ options: [
      { kind: 'provider', destination: '/onboarding/csp?reentry=admin' },
      { kind: 'organization', destination: '/organizations/new' },
    ] });
    // Assert
    expect(screen.getByRole('heading', { name: 'What are you setting up?' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Set up provider' })).toHaveAttribute('href', '/workspaces/csp/onboarding/csp?reentry=admin');
    expect(screen.getByRole('link', { name: 'Set up organization' })).toHaveAttribute('href', '/workspaces/csp/organizations/new');
    expect(screen.queryByRole('link', { name: 'Set up system' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Save & finish later' })).not.toBeInTheDocument();
  });

  it('resumes named saved records without a create or grant action', () => {
    // Arrange / Act
    renderHome({ mode: 'resume', records: [
      { id: 'org-draft', kind: 'organization', name: 'Maritime draft', detail: 'Administrator enrollment is unfinished.',
        state: 'Needs attention', destination: '/organizations/new?draft=org-draft' },
    ] });
    // Assert
    expect(screen.getByRole('heading', { name: 'Continue your setup' })).toBeInTheDocument();
    expect(screen.getByText('Maritime draft')).toBeInTheDocument();
    expect(screen.getByText('Administrator enrollment is unfinished.')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Continue Maritime draft' })).toHaveAttribute('href', '/workspaces/csp/organizations/new?draft=org-draft');
    expect(screen.queryByText('Setup complete')).not.toBeInTheDocument();
  });

  it('distinguishes loading and failed records from an empty saved setup list', () => {
    // Arrange
    const retry = vi.fn();
    const view = renderHome({ mode: 'resume', loading: true });
    // Assert
    expect(screen.getByRole('status')).toHaveTextContent('Loading saved setup');
    expect(screen.queryByText('No saved setup records.')).not.toBeInTheDocument();
    // Act
    view.unmount();
    renderHome({ mode: 'resume', error: 'Saved setup is unavailable.', onRetry: retry });
    fireEvent.click(screen.getByRole('button', { name: 'Retry loading setup' }));
    // Assert
    expect(screen.getByRole('alert')).toHaveTextContent('Saved setup is unavailable.');
    expect(screen.queryByText('No saved setup records.')).not.toBeInTheDocument();
    expect(retry).toHaveBeenCalledOnce();
  });

  it('shows an honest empty state and supports start/resume navigation', () => {
    // Arrange
    const onChangeMode = vi.fn();
    // Act
    renderHome({ mode: 'resume', onChangeMode });
    fireEvent.click(screen.getByRole('button', { name: '1 Choose a path' }));
    fireEvent.click(screen.getByRole('button', { name: 'Back' }));
    fireEvent.click(screen.getByRole('button', { name: '2 Resume' }));
    // Assert
    expect(screen.getByText('No saved setup records.')).toBeInTheDocument();
    expect(onChangeMode).toHaveBeenCalledWith('start');
    expect(onChangeMode).toHaveBeenCalledWith('resume');
  });

  it('retains saved records alongside an unavailable refresh and explains absent permission', () => {
    // Arrange / Act
    renderHome({ error: 'Refresh unavailable; showing previously loaded records.', records: [
      { id: 'receipt-a', kind: 'provider', name: 'Provider sources', detail: 'Receipt confirmation is unresolved.',
        state: 'Needs attention', destination: '/onboarding/csp?reentry=resume' },
    ] });
    // Assert
    expect(screen.getByRole('link', { name: 'Continue Provider sources' })).toBeInTheDocument();
    expect(screen.getByText('Setup actions could not be verified.')).toBeInTheDocument();
    expect(screen.getByRole('alert')).toHaveTextContent('Refresh unavailable');
  });

  it('offers system setup only when supplied as an authorized option and keeps empty actions explicit', () => {
    // Arrange
    const changeMode = vi.fn();
    const view = renderHome({ options: [{ kind: 'system', destination: '/systems/new' }], onChangeMode: changeMode });
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'View saved setup' }));
    // Assert
    expect(screen.getByRole('link', { name: 'Set up system' })).toHaveAttribute('href', '/workspaces/csp/systems/new');
    expect(changeMode).toHaveBeenCalledWith('resume');
    // Act
    view.unmount();
    renderHome();
    // Assert
    expect(screen.getByText('No setup actions are available in this workspace.')).toBeInTheDocument();
  });

  it('exposes remaining saved records through an explicit paginated action', () => {
    // Arrange
    const more = vi.fn();
    // Act
    renderHome({ mode: 'resume', onLoadMore: more });
    fireEvent.click(screen.getByRole('button', { name: 'Load more saved setup' }));
    // Assert
    expect(more).toHaveBeenCalledOnce();
  });
});
