import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
import TaskEvidencePicker from '../../features/remediation-workspace/TaskEvidencePicker';
import { getEvidenceCatalog } from '../../api/evidenceCatalog';

vi.mock('../../api/evidenceCatalog', async original => ({
  ...await original<typeof import('../../api/evidenceCatalog')>(), getEvidenceCatalog: vi.fn(),
}));
describe('Retained task evidence selection', () => {
  it('selects the named manual artifact rather than a provider summary or automated ID', async () => {
    // Arrange
    vi.mocked(getEvidenceCatalog).mockResolvedValue({
      systemId: 'system-a', totalCount: 1, availableCount: 1, page: 1, pageSize: 20,
      items: [{ id: 'artifact:evidence-a', recordId: 'evidence-a', source: 'Manual', name: 'Timeout retest',
        sourceLabel: 'System evidence', recordedAt: null, category: null, controls: [], linksKnown: true }],
      counts: { all: 1, system: 1, provider: 0, missingLinks: 1 }, sources: [{ source: 'system', state: 'available', message: null }],
      permissions: { canUpload: false, uploadReason: null },
    });
    const select = vi.fn().mockResolvedValue(undefined);
    render(<MemoryRouter><TaskEvidencePicker systemId="system-a" busy={false} onLink={select} /></MemoryRouter>);
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Link Timeout retest' }));
    // Assert
    await waitFor(() => expect(select).toHaveBeenCalledWith('evidence-a'));
    expect(getEvidenceCatalog).toHaveBeenCalledWith('system-a', expect.objectContaining({ view: 'system', source: 'Manual' }), expect.any(AbortSignal));
  });
  it('reports unavailable catalog source rather than no evidence', async () => {
    // Arrange
    vi.mocked(getEvidenceCatalog).mockRejectedValue(new Error('Evidence access revoked.'));
    render(<MemoryRouter><TaskEvidencePicker systemId="system-a" busy={false} onLink={vi.fn()} /></MemoryRouter>);
    // Act / Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Evidence access revoked.');
    expect(screen.queryByText('No matching system artifacts.')).not.toBeInTheDocument();
  });
});
