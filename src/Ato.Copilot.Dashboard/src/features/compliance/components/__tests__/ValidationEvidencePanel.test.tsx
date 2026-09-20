import { render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

vi.mock('../../api/complianceApi', () => ({
  getControlValidationLinks: vi.fn(),
  addValidationLink: vi.fn(),
  deleteValidationLink: vi.fn(),
}));

import * as validationApi from '../../api/complianceApi';
import ValidationEvidencePanel from '../ValidationEvidencePanel';

const getLinks = validationApi.getControlValidationLinks as ReturnType<typeof vi.fn>;

describe('ValidationEvidencePanel', () => {
  beforeEach(() => vi.clearAllMocks());

  it('renders linked Azure resource with type and automation badges', async () => {
    // Arrange
    getLinks.mockResolvedValue({
      systemId: 'system-1',
      controlId: 'AC-2',
      total: 1,
      links: [{
        id: 'link-1',
        linkType: 'AzureResource',
        linkTarget: '/subscriptions/sub-1/resourceGroups/rg-1/providers/Microsoft.Storage/storageAccounts/store1',
        description: 'Storage encryption configuration',
        addedBy: 'iac-scan',
        addedAt: '2026-06-01T12:00:00Z',
        validatedAt: '2026-06-01T12:00:00Z',
        isAutomated: true,
      }],
    });

    // Act
    render(<ValidationEvidencePanel systemId="system-1" controlId="AC-2" canManage />);

    // Assert
    await waitFor(() => expect(screen.getByText('Storage encryption configuration')).toBeInTheDocument());
    expect(screen.getByText('Azure Resource')).toBeInTheDocument();
    expect(screen.getByText('Auto')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /open validation target/i })).toHaveAttribute(
      'href',
      'https://portal.azure.com/#resource//subscriptions/sub-1/resourceGroups/rg-1/providers/Microsoft.Storage/storageAccounts/store1',
    );
    expect(screen.getByRole('button', { name: /add validation link/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /delete validation link/i })).toBeInTheDocument();
  });

  it('shows the no-evidence warning and hides mutation controls for readers', async () => {
    // Arrange
    getLinks.mockResolvedValue({ systemId: 'system-1', controlId: 'AC-2', total: 0, links: [] });

    // Act
    render(<ValidationEvidencePanel systemId="system-1" controlId="AC-2" canManage={false} />);

    // Assert
    expect(await screen.findByText('No validation links attached to this control.')).toBeInTheDocument();
    expect(screen.getByText('No validation evidence linked. Adding evidence strengthens your ATO package.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /add validation link/i })).not.toBeInTheDocument();
  });

  it('shows a load error instead of the no-evidence warning when the request fails', async () => {
    // Arrange
    getLinks.mockRejectedValue(new Error('Internal Server Error'));

    // Act
    render(<ValidationEvidencePanel systemId="system-1" controlId="AC-2" canManage={false} />);

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Unable to load validation links.');
    expect(screen.queryByText('No validation links attached to this control.')).not.toBeInTheDocument();
  });
});