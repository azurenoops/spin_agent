import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, expect, it, vi } from 'vitest';
import SystemSourcePanel from '../../../features/onboarding/SystemSourcePanel';
import SystemSourceReview from '../../../features/onboarding/SystemSourceReview';

const request = vi.hoisted(() => vi.fn());
vi.mock('../../../features/workspace-operations/workspaceRequest', () => ({ workspaceRequest: request }));
const source = {
  sessionId: 'source-a', kind: 'emass' as const, systemId: 'opaque-system', fileName: 'source.xlsx', sha256: 'source-sha',
  sourceRevision: 1, receiptState: 'confirmed', analysisState: 'parsed', reviewState: 'pending', state: 'reviewRequired',
  fields: [{ field: 'name', sourceField: 'system_name', proposedValue: 'Source name', supported: true }], error: null,
};
beforeEach(() => { request.mockReset(); });

it('retains a confirmed receipt and offers separate review without applying fields', async () => {
  // Arrange
  request.mockResolvedValue(source);
  const changed = vi.fn();
  render(<MemoryRouter><SystemSourcePanel tenantId="tenant-a" systemId="opaque-system" kind="emass"
    sources={[]} onChanged={changed} onPendingChange={vi.fn()} /></MemoryRouter>);
  // Act
  fireEvent.change(screen.getByLabelText('Source file'), { target: { files: [new File(['original'], 'source.xlsx')] } });
  fireEvent.click(screen.getByRole('button', { name: 'Retain source for review' }));
  // Assert
  await waitFor(() => expect(changed).toHaveBeenCalledOnce());
  expect(screen.getByText('source-sha')).toBeVisible();
  expect(screen.getByRole('link', { name: 'Review source fields' })).toHaveAttribute('href',
    '/systems/opaque-system/setup?source=emass&receipt=source-a');
  expect(request.mock.calls.some(([config]) => config.url.endsWith('/apply'))).toBe(false);
});

it('keeps current fields by default and requires explicit review confirmation before applying', async () => {
  // Arrange
  request.mockImplementation(async (config: { url: string }) => {
    if (config.url.endsWith('/setup')) return { canManage: true, displayName: 'Retained identity' };
    if (config.url.endsWith('/review-previews')) return { sessionId: source.sessionId, systemId: source.systemId,
      sourceRevision: 1, sourceHash: source.sha256, identityRevision: 'v1', previewHash: 'preview',
      fields: [{ field: 'name', currentValue: 'Retained identity', proposedValue: 'Source name', supported: true }] };
    if (config.url.endsWith('/apply')) return { ...source, reviewState: 'applied', state: 'applied' };
    return source;
  });
  render(<MemoryRouter><SystemSourceReview tenantId="tenant-a" systemId="opaque-system" kind="emass" receiptId="source-a" /></MemoryRouter>);
  await screen.findByText('Retained identity');
  // Act
  fireEvent.click(screen.getByRole('button', { name: 'Review proposed fields' }));
  // Assert
  const apply = await screen.findByRole('button', { name: 'Apply reviewed decisions' });
  expect(apply).toBeDisabled();
  expect(screen.getByLabelText('Decision for name')).toHaveValue('keepCurrent');
  // Act
  fireEvent.click(screen.getByLabelText('I reviewed this source and the exact target fields.'));
  fireEvent.click(apply);
  // Assert
  expect(await screen.findByRole('status')).toHaveTextContent('Source decisions recorded');
  const write = request.mock.calls.map(([config]) => config).find(config => config.url.endsWith('/apply'));
  expect(write.data.decisions).toEqual([{ field: 'name', decision: 'keepCurrent' }]);
});
