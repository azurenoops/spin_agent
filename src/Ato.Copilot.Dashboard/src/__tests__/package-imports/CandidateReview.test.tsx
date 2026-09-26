import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, expectTypeOf, it, vi } from 'vitest';
import { CandidateReview } from '../../features/package-imports/CandidateReview';
import * as api from '../../features/package-imports/api';
import { PackageImportError } from '../../features/package-imports/request';
import { candidate } from './fixtures';
import type { EditPackageCandidate, PackageCandidate } from '../../features/package-imports/types';

vi.mock('../../features/package-imports/api', async importOriginal => ({
  ...await importOriginal<typeof import('../../features/package-imports/api')>(),
  editPackageCandidate: vi.fn(),
}));
vi.mock('../../components/AuthenticatedDownload', () => ({
  default: ({ url, children }: { url: string; children: React.ReactNode }) => <button data-url={url}>{children}</button>,
}));
beforeEach(() => vi.clearAllMocks());
const props = () => ({ packageId: 'package-1', candidate: candidate(), onSaved: vi.fn(), onCancel: vi.fn(), onReload: vi.fn() });

describe('source-backed candidate review', () => {
  it('keeps response component type non-nullable while allowing the amended nullable edit field', () => {
    // Arrange
    type ResponseComponentType = PackageCandidate['componentType'];
    type EditComponentType = EditPackageCandidate['componentType'];
    // Act / Assert
    expectTypeOf<ResponseComponentType>().toEqualTypeOf<string>();
    expectTypeOf<EditComponentType>().toEqualTypeOf<string | null>();
  });

  it.each(['Component', 'Capability', 'ControlMapping', 'Responsibility'])(
    'preserves the 256-character limit and canonical non-null component type for %s',
    async type => {
      // Arrange
      const name = 'I'.repeat(256);
      vi.mocked(api.editPackageCandidate).mockResolvedValue(candidate({ type, name }));
      render(<CandidateReview {...props()} candidate={candidate({ type, name })} />);
      // Act
      fireEvent.click(screen.getByRole('button', { name: 'Save changes' }));
      // Assert
      expect(screen.getByLabelText('Name')).toHaveAttribute('maxlength', '256');
      await waitFor(() => expect(api.editPackageCandidate).toHaveBeenCalledWith('package-1', 'candidate-1',
        expect.objectContaining({ name, componentType: 'Service', reviewAction: 'NeedsReview' })));
    },
  );

  it.each(['Component', 'Capability'])('rejects a 257-character %s name without weakening inventory limits', async type => {
    // Arrange
    const name = 'I'.repeat(257);
    render(<CandidateReview {...props()} candidate={candidate({ type })} />);
    // Act
    fireEvent.change(screen.getByLabelText('Name'), { target: { value: name } });
    fireEvent.click(screen.getByRole('button', { name: 'Save changes' }));
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Candidate name must be at most 256 characters');
    expect(api.editPackageCandidate).not.toHaveBeenCalled();
  });

  it.each([null, '', 'NotAComponent'])('does not submit a missing or invalid inventory component selection (%s)', async value => {
    // Arrange
    render(<CandidateReview {...props()} />);
    // Act
    fireEvent.change(screen.getByLabelText('Component type'), { target: { value } });
    fireEvent.click(screen.getByRole('button', { name: 'Save changes' }));
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Select a valid inventory component type');
    expect(api.editPackageCandidate).not.toHaveBeenCalled();
  });

  it('shows unresolved source dependency identifiers for explicit mapping', () => {
    // Arrange
    const input = props();
    // Act
    render(<CandidateReview {...input} candidate={candidate({ unresolvedDependencies: ['source-component-not-resolved'] })} />);
    // Assert
    expect(screen.getByText('source-component-not-resolved')).toBeInTheDocument();
  });
  it('requires evidence acknowledgement, keeps confidence advisory and only marks reviewed', async () => {
    // Arrange
    const input = props();
    vi.mocked(api.editPackageCandidate).mockResolvedValue(candidate({ reviewState: 'Reviewed' }));
    render(<CandidateReview {...input} />);
    // Act
    expect(screen.getByRole('button', { name: 'Mark reviewed' })).toBeDisabled();
    fireEvent.click(screen.getByRole('checkbox', { name: /reviewed the sources/i }));
    fireEvent.click(screen.getByRole('button', { name: 'Mark reviewed' }));
    // Assert
    await waitFor(() => expect(input.onSaved).toHaveBeenCalledOnce());
    expect(api.editPackageCandidate).toHaveBeenCalledWith('package-1', 'candidate-1',
      expect.objectContaining({ expectedRevision: 1, reviewAction: 'Reviewed', controlDuties: input.candidate.controlDuties }));
    expect(screen.getByText('Provider manages the source account lifecycle.')).toBeInTheDocument();
    expect(screen.getByText(/confidence.*advisory/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Download cited source' })).toHaveAttribute('data-url', '/api/csp/package-imports/package-1/artifacts/artifact-1/content');
  });

  it('saves changes as needing fresh review and resets acknowledgement after editing', async () => {
    // Arrange
    vi.mocked(api.editPackageCandidate).mockResolvedValue(candidate({ name: 'Revised', reviewState: 'NeedsReview' }));
    render(<CandidateReview {...props()} />);
    fireEvent.click(screen.getByRole('checkbox', { name: /reviewed the sources/i }));
    // Act
    fireEvent.change(screen.getByLabelText('Name'), { target: { value: 'Revised' } });
    expect(screen.getByRole('button', { name: 'Mark reviewed' })).toBeDisabled();
    fireEvent.click(screen.getByRole('button', { name: 'Save changes' }));
    // Assert
    await waitFor(() => expect(api.editPackageCandidate).toHaveBeenCalledWith('package-1', 'candidate-1',
      expect.objectContaining({ name: 'Revised', reviewAction: 'NeedsReview' })));
  });

  it('preserves edits on a stale revision conflict and offers explicit reload', async () => {
    // Arrange
    const input = props();
    vi.mocked(api.editPackageCandidate).mockRejectedValue(new PackageImportError('Revision changed. Reload before saving.', 409, 'STALE'));
    render(<CandidateReview {...input} />);
    fireEvent.change(screen.getByLabelText('Name'), { target: { value: 'Retain these edits' } });
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Save changes' }));
    await screen.findByText('Revision changed. Reload before saving.');
    // Assert
    expect(screen.getByLabelText('Name')).toHaveValue('Retain these edits');
    expect(input.onSaved).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole('button', { name: 'Reload current revision' }));
    expect(input.onReload).toHaveBeenCalledOnce();
  });

  it('requires a rejection rationale and prevents duplicate in-flight mutations', async () => {
    // Arrange
    let resolve!: (value: ReturnType<typeof candidate>) => void;
    vi.mocked(api.editPackageCandidate).mockImplementation(() => new Promise(done => { resolve = done; }));
    render(<CandidateReview {...props()} />);
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Reject candidate' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('rationale');
    expect(api.editPackageCandidate).not.toHaveBeenCalled();
    fireEvent.change(screen.getByLabelText('Review rationale'), { target: { value: 'Outside provider scope.' } });
    fireEvent.click(screen.getByRole('button', { name: 'Reject candidate' }));
    fireEvent.click(screen.getByRole('button', { name: 'Reject candidate' }));
    // Assert
    expect(api.editPackageCandidate).toHaveBeenCalledOnce();
    await act(async () => resolve(candidate({ reviewState: 'Rejected' })));
  });

  it('rejects duplicate control identifiers instead of silently overwriting duties', async () => {
    // Arrange
    render(<CandidateReview {...props()} />);
    fireEvent.click(screen.getByRole('button', { name: 'Add control mapping' }));
    // Act
    fireEvent.change(screen.getByLabelText('Control ID 2'), { target: { value: 'ac-2' } });
    fireEvent.change(screen.getByLabelText('Responsibility 2'), { target: { value: 'Shared' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save changes' }));
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Duplicate control ID');
    expect(api.editPackageCandidate).not.toHaveBeenCalled();
  });

  it('submits explicit metadata, mapped dependencies and duplicate decisions without changing source evidence', async () => {
    // Arrange
    const input = props();
    const recordId = 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee';
    vi.mocked(api.editPackageCandidate).mockResolvedValue(candidate({ reviewState: 'NeedsReview' }));
    render(<CandidateReview {...input} candidate={candidate({
      unresolvedDependencies: ['source-component'],
      duplicateMatches: [{ recordId, name: 'Existing component', type: 'Component', published: true }],
    })} />);
    // Act
    fireEvent.change(screen.getByLabelText('Component type'), { target: { value: 'Identity' } });
    fireEvent.change(screen.getByLabelText('Classification'), { target: { value: ' Public ' } });
    fireEvent.change(screen.getByLabelText('Service category'), { target: { value: ' Identity services ' } });
    fireEvent.change(screen.getByLabelText('Description'), { target: { value: ' Human-reviewed component description. ' } });
    fireEvent.click(screen.getByRole('button', { name: 'Remove control mapping 1' }));
    fireEvent.change(screen.getByLabelText('Contributor IDs (one per line)'), { target: { value: recordId } });
    fireEvent.change(screen.getByRole('combobox', { name: 'Duplicate resolution' }), { target: { value: 'ReusePublished' } });
    fireEvent.change(screen.getByLabelText('Review rationale'), { target: { value: ' This exact published component resolves the source dependency. ' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save changes' }));
    // Assert
    await waitFor(() => expect(input.onSaved).toHaveBeenCalledOnce());
    expect(api.editPackageCandidate).toHaveBeenCalledWith('package-1', 'candidate-1', {
      expectedRevision: 1, name: input.candidate.name, componentType: 'Identity', classification: 'Public',
      serviceCategory: 'Identity services', description: 'Human-reviewed component description.', controlDuties: {},
      contributorIds: [recordId], duplicateResolution: 'ReusePublished', reviewAction: 'NeedsReview',
      rationale: 'This exact published component resolves the source dependency.',
    });
    expect(screen.getByText('Provider manages the source account lifecycle.')).toBeInTheDocument();
    expect(screen.getByText('source-component')).toBeInTheDocument();
  });
});
