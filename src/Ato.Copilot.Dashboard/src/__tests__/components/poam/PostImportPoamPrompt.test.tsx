import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import PostImportPoamPrompt from '../../../components/poam/PostImportPoamPrompt';

describe('PostImportPoamPrompt', () => {
  it('shows per-finding errors when bulk creation partially fails', async () => {
    // Arrange
    const onBulkCreate = vi.fn().mockResolvedValue({
      totalSubmitted: 2,
      totalSucceeded: 1,
      totalFailed: 1,
      created: 1,
      skippedDuplicates: 0,
      results: [
        { findingId: 'finding-valid', poamId: 'poam-1', status: 'created' },
        { findingId: 'finding-missing', status: 'error', error: 'Finding not found.' },
      ],
    });

    render(
      <MemoryRouter initialEntries={['/systems/system-1/assessments']}>
        <PostImportPoamPrompt
          systemId="system-1"
          findings={[
            { id: 'finding-valid', controlId: 'AC-2', title: 'Valid finding', severity: 'High', hasActivePoam: false },
            { id: 'finding-missing', controlId: 'IA-2', title: 'Missing finding', severity: 'Medium', hasActivePoam: false },
          ]}
          onBulkCreate={onBulkCreate}
          onClose={vi.fn()}
        />
      </MemoryRouter>,
    );

    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Create 2 POA&M Item(s)' }));

    // Assert
    await waitFor(() => expect(onBulkCreate).toHaveBeenCalledWith({
      findingIds: ['finding-valid', 'finding-missing'],
    }));
    expect(await screen.findByText(/1 failed/)).toBeInTheDocument();
    expect(screen.getByText('finding-missing: Finding not found.')).toBeInTheDocument();
  });
});