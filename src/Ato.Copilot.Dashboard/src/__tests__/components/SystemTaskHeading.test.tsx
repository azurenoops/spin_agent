import type { ReactNode } from 'react';
import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { SystemTaskHeading } from '../../features/systems/SystemTaskPresentation';
vi.mock('../../components/layout/WorkspacePageHeader', () => ({
  default: ({ title, description, actions }: { title: string; description?: string; actions?: ReactNode }) =>
    <header data-testid="shared-workspace-header"><h1>{title}</h1><p>{description}</p>{actions}</header>,
}));
describe('System task heading integration', () => {
  it('delegates title/actions to the common workspace header while retaining domain status', () => {
    // Arrange / Act
    render(<SystemTaskHeading title="Mission & purpose" description="Reviewed mission context"
      status={<span>Draft</span>} action={<button>Save draft</button>} />);
    // Assert
    expect(screen.getByTestId('shared-workspace-header')).toHaveTextContent('Mission & purpose');
    expect(screen.getByRole('button', { name: 'Save draft' })).toBeVisible();
    expect(screen.getByText('Draft')).toBeVisible();
    expect(screen.getAllByRole('banner')).toHaveLength(1);
  });
});
