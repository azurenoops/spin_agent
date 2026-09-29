import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
import ComponentPicker from '../../../components/poam/ComponentPicker';
import { getComponents } from '../../../api/components';
vi.mock('../../../api/components', () => ({ getComponents: vi.fn() }));
describe('POA&M component selection', () => {
  it('uses the explicit system scope and shows failures rather than successful emptiness', async () => {
    // Arrange
    vi.mocked(getComponents).mockRejectedValue(new Error('Component service unavailable'));
    render(<MemoryRouter><ComponentPicker systemId="system-a" selectedIds={[]} onChange={vi.fn()} /></MemoryRouter>);
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Select components...' }));
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Component service unavailable');
    expect(getComponents).toHaveBeenCalledWith('system-a', expect.anything());
    expect(screen.queryByText('No components found')).toBeNull();
  });
});
