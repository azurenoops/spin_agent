import { beforeEach, describe, expect, it, vi } from 'vitest';
import { act, fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { FileFirstImport, PackagePreparation } from '../../features/provider-authorizations/FileFirstImport';
import * as packages from '../../features/package-imports/api';
import { packageStatus, candidate, page } from '../package-imports/fixtures';
import { offering } from './testData';

vi.mock('../../features/package-imports/api', async original => ({ ...await original<typeof packages>(), receivePackage: vi.fn(), getPackageCandidates: vi.fn() }));
vi.mock('../../features/package-imports/PackageUpload', () => ({ PackageUpload: ({ upload }: { upload: (files: File[], key: string) => Promise<void> }) => <button onClick={() => void upload([new File(['test'], 'source.pdf')], 'retained-key')}>Select and upload files</button> }));
vi.mock('../../features/provider-authorizations/OfferingIntake', () => ({ OfferingIntake: ({ existingPackageId, initialOfferingId, suggestedBoundary }: { existingPackageId: string; initialOfferingId?: string; suggestedBoundary?: { scopeStatement: string } }) => <div>Associate {existingPackageId}<span>{suggestedBoundary?.scopeStatement}</span><output aria-label="Selected offering">{initialOfferingId ?? ''}</output></div> }));
function Location() { const location = useLocation(); return <output aria-label="Receipt URL">{location.pathname}{location.search}</output>; }
beforeEach(() => { vi.clearAllMocks(); vi.mocked(packages.receivePackage).mockResolvedValue(packageStatus()); vi.mocked(packages.getPackageCandidates).mockResolvedValue(page([])); });
describe('file-first import', () => {
  it('receives files without requiring offering or boundary metadata', async () => {
    // Arrange
    render(<MemoryRouter><FileFirstImport /></MemoryRouter>);
    // Act
    await act(async () => { fireEvent.click(screen.getByText('Select and upload files')); });
    // Assert
    await vi.waitFor(() => expect(packages.receivePackage).toHaveBeenCalledWith(expect.any(Array), 'retained-key'));
    expect(screen.queryByText(/Associate/)).not.toBeInTheDocument();
  });
  it('does not expose association while analysis is processing', () => {
    // Arrange
    const status = packageStatus({ processingState: 'Processing' });
    // Act
    render(<MemoryRouter><PackagePreparation item={status} /></MemoryRouter>);
    // Assert
    expect(screen.getByText('Analyzing your package')).toBeInTheDocument();
    expect(screen.queryByText(/Associate/)).not.toBeInTheDocument();
    expect(packages.getPackageCandidates).not.toHaveBeenCalled();
  });
  it('provides an explicit manual fallback when analysis has no boundary claims', async () => {
    // Arrange
    render(<MemoryRouter><PackagePreparation item={packageStatus({ processingState: 'ReadyForReview' })} /></MemoryRouter>);
    // Act
    fireEvent.click(await screen.findByText('Enter offering and boundary manually'));
    // Assert
    expect(screen.getByText(/Associate package-1/)).toBeInTheDocument();
  });
});

it('retains the selected offering in the file-first receipt URL without associating on upload', async () => {
  // Arrange
  render(<MemoryRouter><FileFirstImport offering={offering} /><Location /></MemoryRouter>);
  // Act
  await act(async () => { fireEvent.click(screen.getByText('Select and upload files')); });
  // Assert
  expect(screen.getByLabelText('Receipt URL')).toHaveTextContent(`/workspaces/csp/authorizations/offerings/${offering.offeringId}/import?packageId=package-1`);
  expect(packages.receivePackage).toHaveBeenCalledWith(expect.any(Array), 'retained-key');
  expect(screen.queryByText(/Associate/)).not.toBeInTheDocument();
});

it('preserves the scoped offering through manual fallback and resets a choice when its context changes', async () => {
  // Arrange
  const status = packageStatus();
  const view = render(<MemoryRouter><PackagePreparation item={status} initialOfferingId="offering-a" /></MemoryRouter>);
  // Act
  fireEvent.click(await screen.findByRole('button', { name: 'Enter offering and boundary manually' }));
  // Assert
  expect(screen.getByLabelText('Selected offering')).toHaveTextContent('offering-a');
  // Act
  view.rerender(<MemoryRouter><PackagePreparation item={status} initialOfferingId="offering-b" /></MemoryRouter>);
  // Assert
  expect(screen.queryByLabelText('Selected offering')).not.toBeInTheDocument();
  fireEvent.click(await screen.findByRole('button', { name: 'Enter offering and boundary manually' }));
  expect(screen.getByLabelText('Selected offering')).toHaveTextContent('offering-b');
});

it('uses a cited boundary claim only after explicit selection', async () => {
  // Arrange
  const source = candidate({ type: 'BoundaryClaim', claim: { authorizationDecision: null, boundary: { subject: 'Extracted offering', scope: 'Shared services only', environment: null, relationship: 'Included', resourceIds: [], responsibilities: [], decisionReference: null }, assessmentFinding: null, poamItem: null, fieldSources: [], relationships: [], sourceAliases: [], qualifications: [] } });
  vi.mocked(packages.getPackageCandidates).mockResolvedValue(page([source]));
  render(<MemoryRouter><PackagePreparation item={packageStatus({ processingState: 'ReadyForReview' })} initialOfferingId="offering-a" /></MemoryRouter>);
  // Act
  fireEvent.click(await screen.findByText('Use this scope as a starting point'));
  // Assert
  expect(screen.getByText('Shared services only')).toBeInTheDocument();
  expect(screen.getByText(/Associate package-1/)).toBeInTheDocument();
  expect(screen.getByLabelText('Selected offering')).toHaveTextContent('offering-a');
});

it.each(['Excluded', 'Undetermined', 'Provider'])('does not prefill included scope from a %s claim', async relationship => {
  // Arrange
  const source = candidate({ type: 'BoundaryClaim', claim: { authorizationDecision: null, boundary: { subject: 'Extracted offering', scope: 'Outside or unconfirmed scope', environment: null, relationship, resourceIds: [], responsibilities: [], decisionReference: null }, assessmentFinding: null, poamItem: null, fieldSources: [], relationships: [], sourceAliases: [], qualifications: [] } });
  vi.mocked(packages.getPackageCandidates).mockResolvedValue(page([source]));
  render(<MemoryRouter><PackagePreparation item={packageStatus()} /></MemoryRouter>);
  // Act
  const select = await screen.findByRole('button', { name: 'Use this scope as a starting point' });
  fireEvent.click(select);
  // Assert
  expect(select).toBeDisabled();
  expect(screen.getByText(`${relationship} source claim · requires confirmation`)).toBeInTheDocument();
  expect(screen.getByText(/This statement does not explicitly identify included scope/)).toBeInTheDocument();
  expect(screen.queryByText(/Associate package-1/)).not.toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Enter offering and boundary manually' })).toBeEnabled();
});
