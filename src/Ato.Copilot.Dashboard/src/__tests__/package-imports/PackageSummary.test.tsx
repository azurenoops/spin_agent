import { render, screen, fireEvent } from '@testing-library/react';
import { expect, it, vi } from 'vitest';
import { PackageSummary } from '../../features/package-imports/PackageSummary';
import { packageStatus } from './fixtures';
it('makes exceptions actionable without hiding excluded content', () => {
  const sources = vi.fn();
  render(<PackageSummary item={packageStatus({ processingState: 'NeedsAttention', coverage: { total: 27, processed: 18, pending: 0, failed: 1, unsupported: 0, unreadable: 0, excluded: 8 } })} onSources={sources} />);
  expect(screen.getByText('1 need attention')).toBeInTheDocument();
  expect(screen.getByText(/8 excluded/)).toBeInTheDocument();
  fireEvent.click(screen.getByRole('button', { name: 'Review source issues' }));
  expect(sources).toHaveBeenCalledOnce();
});
it('does not equate completed analysis with publication', () => {
  render(<PackageSummary item={packageStatus()} onSources={vi.fn()} />);
  expect(screen.getByText(/Review the extracted records/)).toBeInTheDocument();
  expect(screen.getByText('Unpublished')).toBeInTheDocument();
});
