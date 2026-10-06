import { fireEvent, render, screen, within, waitFor } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { ComponentProps } from 'react';
import ProfileSectionForm from '../../components/forms/ProfileSectionForm';
import '../helpers/dialog';
vi.mock('../../features/system-design/UnsavedDesignGuard', () => ({ default: () => null }));

type Props = ComponentProps<typeof ProfileSectionForm>;
const original = { missionStatement: 'Saved mission', businessPurpose: 'Saved purpose', customSource: 'preserve' };
function props(overrides: Partial<Props> = {}): Props {
  return {
    sectionType: 'MissionAndPurpose', governanceStatus: 'Draft',
    initialContent: JSON.stringify(original), reviewerComments: null, isReadOnly: false,
    userRole: 'MissionOwner', isSubmitting: false, error: null,
    onSave: vi.fn(), onSubmit: vi.fn(), onWithdraw: vi.fn(), ...overrides,
  };
}
const user = { id: 'user-1', categoryName: 'Legacy category', description: 'Existing description',
  approximateCount: 4, accessMethod: 'Legacy access', dataSensitivityLevel: 'Legacy sensitivity', sortOrder: 0 };
function editUser() {
  fireEvent.click(screen.getByRole('button', { name: 'Open user category Legacy category' }));
  fireEvent.click(screen.getByRole('button', { name: 'Edit category' }));
}

describe('profile draft editing', () => {
  it('summarizes CUI CIA privacy and retention while preserving source details', () => {
    // Arrange
    const row = { id: 'data-a', dataTypeName: 'Recorded security data', description: 'Recorded security documentation',
      sensitivityClassification: 'CUI', source: 'Collectors', destination: 'Archive', applicableRegulations: 'Recorded regulations', sortOrder: 0,
      cuiCategory: '', confidentialityImpact: 'Moderate', integrityImpact: 'Moderate', availabilityImpact: 'Low',
      privacyApplicability: 'ReviewRequired', retentionRule: '', disposalMethod: '', categorizationReference: '', categorizationRationale: '' };
    const input = props({ sectionType: 'DataTypes', initialContent: '{}', initialChildItems: [row] });
    render(<ProfileSectionForm {...input} />);
    // Assert
    const table = screen.getByRole('table', { name: 'Information types' });
    expect(within(table).getAllByRole('columnheader').map(c => c.textContent)).toEqual([
      'Information type', 'Classification / CUI', 'CIA', 'Privacy & retention', 'Review', 'Open',
    ]);
    expect(within(table).getByText('M / M / L · declared')).toBeVisible();
    expect(screen.getByText(/Information handling documentation is incomplete/)).toBeVisible();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Open data type Recorded security data' }));
    fireEvent.click(screen.getByRole('button', { name: 'Edit data type' }));
    fireEvent.change(screen.getByLabelText('CUI category'), { target: { value: 'Recorded systems information' } });
    fireEvent.change(screen.getByLabelText('Retention rule'), { target: { value: 'Retain for recorded six years' } });
    fireEvent.click(screen.getByRole('button', { name: 'Apply to draft' }));
    fireEvent.click(screen.getByRole('button', { name: 'Save Draft' }));
    // Assert
    expect(input.onSave).toHaveBeenCalledWith('{}', [expect.objectContaining({
      cuiCategory: 'Recorded systems information', retentionRule: 'Retain for recorded six years',
      source: 'Collectors', confidentialityImpact: 'Moderate',
    })]);
  });
  it('summarizes Users identity authentication data and review while capturing missing SSP details', () => {
    // Arrange
    const row = { ...user, identityType: 'WorkloadIdentity', privilegeLevel: 'Privileged', affiliation: 'Internal',
      authenticationMethod: 'Managed identity', responsibleOwner: '', userLocations: 'CONUS',
      permittedEnvironments: '', authorizedDataTypes: 'Recorded inventory metadata', governanceStatus: 'Draft' as const };
    const input = props({ sectionType: 'UsersAndAccess', initialContent: '{}', initialChildItems: [row] });
    render(<ProfileSectionForm {...input} />);
    // Assert
    const table = screen.getByRole('table', { name: 'User categories' });
    expect(within(table).getAllByRole('columnheader').map(c => c.textContent)).toEqual([
      'Category', 'Identity / privilege', 'Access & authentication', 'Data access', 'Review', 'Open',
    ]);
    expect(within(table).getByText(/Workload identity/)).toBeVisible();
    expect(screen.getByText(/has no recorded owner or permitted environment/)).toBeVisible();
    // Act
    editUser();
    fireEvent.change(screen.getByLabelText('Responsible owner'), { target: { value: 'Recorded workload owner' } });
    fireEvent.change(screen.getByLabelText('Permitted environments'), { target: { value: 'Recorded production scope' } });
    fireEvent.click(screen.getByRole('button', { name: 'Apply to draft' }));
    fireEvent.click(screen.getByRole('button', { name: 'Save Draft' }));
    // Assert
    expect(input.onSave).toHaveBeenCalledWith('{}', [expect.objectContaining({ responsibleOwner: 'Recorded workload owner',
      permittedEnvironments: 'Recorded production scope', authenticationMethod: 'Managed identity' })]);
  });
  it('keeps only the mock fields in the System record card in reading order', () => {
    // Arrange
    const identity = (label: string) => <label>{label}<input readOnly value={label} /></label>;
    const input = props({ missionIdentityFields: {
      name: identity('System name'), owner: identity('System owner'), acronym: identity('System acronym'),
      emass: identity('eMASS system ID'), ditpr: identity('DITPR identifier'),
    }, initialContent: JSON.stringify({ ...original, operationalJustification: 'Retained need', businessFunctions: 'Retained functions' }) });
    // Act
    render(<ProfileSectionForm {...input} />);
    const card = screen.getByRole('region', { name: 'System record' });
    // Assert
    expect(Array.from(card.querySelectorAll<HTMLInputElement | HTMLTextAreaElement>('input, textarea'), field => field.labels?.[0]?.textContent)).toEqual([
      'System name', 'System owner', 'System acronym', 'System version / release', 'eMASS system ID',
      'DITPR identifier', 'Responsible organization', 'Program office / division', 'Mission statement', 'Business purpose',
    ]);
    expect(within(card).getByLabelText('Mission statement')).toHaveAttribute('rows', '3');
    expect(within(card).queryByText(/4,000/)).not.toBeInTheDocument();
    expect(within(card).queryByLabelText('Operational Justification')).not.toBeInTheDocument();
    expect(screen.getByText('Additional mission details').closest('details')).not.toHaveAttribute('open');
    expect(screen.getByLabelText('Operational Justification')).toHaveValue('Retained need');
    expect(screen.getByLabelText('Business Functions')).toHaveValue('Retained functions');
    expect(within(card).queryByRole('button')).not.toBeInTheDocument();
  });
  it('groups Mission system-record fields and saves their SSP metadata without dropping existing content', () => {
    // Arrange
    const input = props();
    render(<ProfileSectionForm {...input} />);
    // Act
    fireEvent.change(screen.getByLabelText('System version / release'), { target: { value: 'Reviewed release 4.2' } });
    fireEvent.change(screen.getByLabelText('Responsible organization'), { target: { value: 'Recorded mission organization' } });
    fireEvent.change(screen.getByLabelText('Program office / division'), { target: { value: 'Recorded program office' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save Draft' }));
    // Assert
    expect(JSON.parse(vi.mocked(input.onSave).mock.calls[0]![0])).toEqual({
      ...original, systemVersion: 'Reviewed release 4.2', responsibleOrganization: 'Recorded mission organization',
      programOffice: 'Recorded program office',
    });
    expect(screen.getByLabelText('System version / release').closest('[data-mission-record-fields]')).not.toBeNull();
  });
  it('edits communication context without another interface table and preserves all saved PPS rows', () => {
    // Arrange
    const pps = { id: 'pps-a', portOrRange: '443', protocol: 'TCP', serviceName: 'HTTPS',
      direction: 'Inbound', justification: 'User access', sortOrder: 0 };
    const input = props({ sectionType: 'PortsProtocolsAndServices', initialContent: '{"source":"retained"}',
      initialChildItems: [pps], hideChildItems: true });
    render(<ProfileSectionForm {...input} />);
    // Act
    fireEvent.change(screen.getByLabelText('PPS Overview'), { target: { value: 'Permitted mission communications' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save Draft' }));
    // Assert
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
    expect(input.onSave).toHaveBeenCalledExactlyOnceWith('{"source":"retained","ppsOverview":"Permitted mission communications"}', [pps]);
    expect(screen.getByRole('button', { name: 'Submit for Review' })).toBeDisabled();
  });
  it('keeps system-wide access fields and review actions out of the Users table area', () => {
    // Arrange
    render(<ProfileSectionForm {...props({ sectionType: 'UsersAndAccess', initialContent: '{}',
      initialChildItems: [user], onApprove: vi.fn(), onRequestRevision: vi.fn() })} />);
    // Act
    const table = screen.getByRole('table', { name: 'User categories' });
    // Assert
    expect(table).toBeVisible();
    expect(screen.queryByText('Access context')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Access Overview')).not.toBeInTheDocument();
    expect(screen.queryByText('Authentication Methods')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /access.context/i })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Save Draft' })).toBeEnabled();
  });

  it('saves access context and category drafts atomically from an isolated dialog form', () => {
    // Arrange
    const input = props({ sectionType: 'UsersAndAccess', initialContent: '{"customSource":"preserve"}',
      initialChildItems: [user], contextDialogOpen: true, onContextDialogClose: vi.fn() });
    render(<ProfileSectionForm {...input} />);
    const dialog = screen.getByRole('dialog', { name: 'System-wide access context' });
    // Act
    fireEvent.change(within(dialog).getByLabelText('Access Overview'), { target: { value: 'System access model' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save access context' }));
    // Assert
    expect(input.onSave).toHaveBeenCalledExactlyOnceWith('{"customSource":"preserve","accessOverview":"System access model"}', [user]);
    expect(dialog).toHaveAccessibleDescription('Manage and review the system-wide access model independently of individual user categories. Saving persists the access context and any pending category drafts together. Cancel discards only context edits made in this dialog.');
    expect(input.onContextDialogClose).not.toHaveBeenCalled();
    expect(dialog.querySelector('form form')).toBeNull();
    expect(within(dialog).getByRole('button', { name: 'Submit access context' })).toBeDisabled();
    expect(screen.getAllByRole('button', { name: 'Submit access context' })).toHaveLength(1);
  });

  it('cancels only unsaved context changes while preserving category draft edits', () => {
    // Arrange
    const input = props({ sectionType: 'UsersAndAccess', initialContent: '{"accessOverview":"Recorded context"}',
      initialChildItems: [user], onContextDialogClose: vi.fn() });
    const { rerender } = render(<ProfileSectionForm {...input} />);
    // Act
    editUser();
    fireEvent.change(screen.getByLabelText('Description'), { target: { value: 'Category draft' } });
    fireEvent.click(screen.getByRole('button', { name: 'Apply to draft' }));
    rerender(<ProfileSectionForm {...input} contextDialogOpen />);
    fireEvent.change(screen.getByLabelText('Access Overview'), { target: { value: 'Discard context' } });
    fireEvent.click(screen.getByText('Select options...'));
    fireEvent.click(screen.getByRole('option', { name: 'MFA' }));
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    rerender(<ProfileSectionForm {...input} contextDialogOpen={false} />);
    fireEvent.click(screen.getByRole('button', { name: 'Save Draft' }));
    // Assert
    expect(input.onContextDialogClose).toHaveBeenCalledOnce();
    expect(input.onSave).toHaveBeenCalledWith('{"accessOverview":"Recorded context"}', [{ ...user, description: 'Category draft' }]);
    // Act
    rerender(<ProfileSectionForm {...input} contextDialogOpen />);
    // Assert
    expect(screen.getByLabelText('Access Overview')).toHaveValue('Recorded context');
    expect(screen.getByText('Select options...')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Submit access context' })).toBeDisabled();
  });

  it('locks context inputs and dismissal during saves and retains failures inside the dialog', () => {
    // Arrange
    const input = props({ sectionType: 'UsersAndAccess', initialContent: '{}',
      contextDialogOpen: true, onContextDialogClose: vi.fn() });
    const { rerender } = render(<ProfileSectionForm {...input} />);
    // Act
    fireEvent.change(screen.getByLabelText('Access Overview'), { target: { value: 'Retained draft' } });
    fireEvent.click(screen.getByText('Select options...'));
    rerender(<ProfileSectionForm {...input} isSubmitting />);
    const dialog = screen.getByRole('dialog', { name: 'System-wide access context' });
    fireEvent(dialog, new Event('cancel', { bubbles: false, cancelable: true }));
    fireEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }));
    fireEvent.submit(within(dialog).getByLabelText('Access Overview').closest('form')!);
    // Assert
    expect(screen.getByLabelText('Access Overview')).toBeDisabled();
    expect(screen.getByPlaceholderText('Type to filter...')).toBeDisabled();
    expect(screen.getByRole('option', { name: 'CAC/PIV' })).toBeDisabled();
    expect(within(dialog).getByRole('button', { name: 'Close dialog' })).toBeDisabled();
    expect(input.onSave).not.toHaveBeenCalled();
    expect(input.onContextDialogClose).not.toHaveBeenCalled();
    // Act
    rerender(<ProfileSectionForm {...input} error="Save conflict" />);
    // Assert
    expect(within(dialog).getByRole('alert')).toHaveTextContent('Save conflict');
    expect(screen.getByLabelText('Access Overview')).toHaveValue('Retained draft');
    expect(within(dialog).getByRole('button', { name: 'Save access context' })).toBeEnabled();
  });

  it('does not offer access-context review actions to a role without authority', () => {
    // Arrange
    render(<ProfileSectionForm {...props({ sectionType: 'UsersAndAccess', contextDialogOpen: true,
      effectiveRoles: ['SystemOwner'], governanceStatus: 'UnderReview',
      onApprove: vi.fn(), onRequestRevision: vi.fn() })} />);
    // Act
    const dialog = screen.getByRole('dialog', { name: 'System-wide access context' });
    // Assert
    expect(within(dialog).queryByRole('button', { name: /approve|revision|withdraw|submit/i })).not.toBeInTheDocument();
    expect(within(dialog).getByLabelText('Access Overview')).toBeDisabled();
  });

  it.each([
    ['Submit access context', 'Draft', 'MissionOwner', 'onSubmit'],
    ['Withdraw access context', 'UnderReview', 'MissionOwner', 'onWithdraw'],
    ['Approve access context', 'UnderReview', 'ISSM', 'onApprove'],
    ['Request access-context revision', 'UnderReview', 'ISSM', 'onRequestRevision'],
  ] as const)('delegates %s to the parent without saving or dismissing the context itself', (label, governanceStatus, role, callback) => {
    // Arrange
    const input = props({ sectionType: 'UsersAndAccess', initialContent: '{}', contextDialogOpen: true,
      governanceStatus, effectiveRoles: [role], onApprove: vi.fn(), onRequestRevision: vi.fn(), onContextDialogClose: vi.fn() });
    render(<ProfileSectionForm {...input} />);
    // Act
    fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: label }));
    // Assert
    expect(input[callback]).toHaveBeenCalledOnce();
    expect(input.onSave).not.toHaveBeenCalled();
    expect(input.onContextDialogClose).not.toHaveBeenCalled();
    expect(screen.queryByRole('button', { name: /^(Approve|Request Revision|Withdraw|Submit for Review)$/ })).not.toBeInTheDocument();
  });

  it.each(['Close dialog', 'Escape'] as const)('restores the context values at opening via %s without saving', closeAction => {
    // Arrange
    const input = props({ sectionType: 'UsersAndAccess', initialContent: '{}', onContextDialogClose: vi.fn() });
    const { rerender } = render(<ProfileSectionForm {...input} contextDialogOpen />);
    fireEvent.change(screen.getByLabelText('Access Overview'), { target: { value: 'Earlier draft' } });
    rerender(<ProfileSectionForm {...input} contextDialogOpen={false} />);
    rerender(<ProfileSectionForm {...input} contextDialogOpen />);
    // Act
    fireEvent.change(screen.getByLabelText('Access Overview'), { target: { value: 'Discard latest draft' } });
    if (closeAction === 'Escape') fireEvent(screen.getByRole('dialog'), new Event('cancel', { cancelable: true }));
    else fireEvent.click(screen.getByRole('button', { name: closeAction }));
    rerender(<ProfileSectionForm {...input} contextDialogOpen={false} />);
    fireEvent.click(screen.getByRole('button', { name: 'Save Draft' }));
    // Assert
    expect(input.onContextDialogClose).toHaveBeenCalledOnce();
    expect(input.onSave).toHaveBeenCalledExactlyOnceWith('{"accessOverview":"Earlier draft"}', []);
  });

  it('reviews access context separately without locking unrelated draft categories', () => {
    // Arrange
    const input = props({ sectionType: 'UsersAndAccess', governanceStatus: 'UnderReview',
      initialContent: '{"accessOverview":"Recorded context"}', initialChildItems: [{ ...user, revision: 1, governanceStatus: 'Draft' }],
      effectiveRoles: ['ISSM'], contextDialogOpen: true, onApprove: vi.fn(), onReviewUserCategory: vi.fn() });
    render(<ProfileSectionForm {...input} />);
    // Act
    const dialog = screen.getByRole('dialog', { name: 'System-wide access context' });
    // Assert
    expect(screen.getByLabelText('Access Overview')).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Add user category' })).toBeEnabled();
    // Act
    fireEvent.click(within(dialog).getByRole('button', { name: 'Approve access context' }));
    // Assert
    expect(input.onApprove).toHaveBeenCalledOnce();
    expect(input.onReviewUserCategory).not.toHaveBeenCalled();
    expect(screen.queryByRole('button', { name: 'Approve' })).not.toBeInTheDocument();
  });

  it('keeps a pending removal visible and allows cancelling its draft without dropping the approved source', () => {
    // Arrange
    const row = { ...user, revision: 4, governanceStatus: 'Draft' as const, pendingDeletion: true, approvedSnapshotId: 'approved-a' };
    const input = props({ sectionType: 'UsersAndAccess', initialContent: '{}', initialChildItems: [row] });
    render(<ProfileSectionForm {...input} />);
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Open user category Legacy category' }));
    fireEvent.click(screen.getByRole('button', { name: 'Cancel removal request' }));
    fireEvent.click(screen.getByRole('button', { name: 'Save Draft' }));
    // Assert
    expect(input.onSave).toHaveBeenCalledWith('{}', [{ ...row, pendingDeletion: false }]);
  });

  it('reviews only the selected saved category with its revision, not the whole Users section', async () => {
    // Arrange
    const review = vi.fn().mockResolvedValue(true);
    const input = props({ sectionType: 'UsersAndAccess', initialContent: '{}',
      initialChildItems: [{ ...user, revision: 3, governanceStatus: 'UnderReview', canReview: true }],
      effectiveRoles: ['ISSM'], onReviewUserCategory: review, onApprove: vi.fn() });
    render(<ProfileSectionForm {...input} />);
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Open user category Legacy category' }));
    fireEvent.click(screen.getByRole('button', { name: 'Approve category' }));
    expect(review).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole('button', { name: 'Confirm approval' }));
    // Assert
    await waitFor(() => expect(review).toHaveBeenCalledWith('user-1', { action: 'approve', expectedRevision: 3 }));
    expect(input.onApprove).not.toHaveBeenCalled();
    expect(screen.queryByRole('button', { name: 'Submit for Review' })).not.toBeInTheDocument();
  });

  it('retains individual revision comments on failure', async () => {
    // Arrange
    const review = vi.fn().mockResolvedValue(false);
    const input = props({ sectionType: 'UsersAndAccess', initialContent: '{}',
      initialChildItems: [{ ...user, revision: 2, governanceStatus: 'UnderReview', canReview: true }], onReviewUserCategory: review });
    const { rerender } = render(<ProfileSectionForm {...input} />);
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Open user category Legacy category' }));
    fireEvent.click(screen.getByRole('button', { name: 'Request category revision' }));
    fireEvent.change(screen.getByLabelText('Review comments'), { target: { value: 'Describe privileged access.' } });
    fireEvent.click(screen.getByRole('button', { name: 'Confirm revision request' }));
    // Assert
    await waitFor(() => expect(review).toHaveBeenCalledWith('user-1',
      { action: 'request_revision', expectedRevision: 2, comments: 'Describe privileged access.' }));
    rerender(<ProfileSectionForm {...input} error="Review conflict" />);
    expect(within(screen.getByRole('dialog')).getByRole('alert')).toHaveTextContent('Review conflict');
    expect(screen.getByLabelText('Review comments')).toHaveValue('Describe privileged access.');
  });

  it('blocks individual review while unsaved access-context edits remain', () => {
    // Arrange
    const review = vi.fn();
    const input = props({ sectionType: 'UsersAndAccess', initialContent: '{}',
      initialChildItems: [{ ...user, revision: 2, governanceStatus: 'UnderReview', canReview: true }], onReviewUserCategory: review });
    const { rerender } = render(<ProfileSectionForm {...input} contextDialogOpen />);
    // Act
    fireEvent.change(screen.getByLabelText('Access Overview'), { target: { value: 'Changed access model.' } });
    rerender(<ProfileSectionForm {...input} contextDialogOpen={false} />);
    fireEvent.click(screen.getByRole('button', { name: 'Open user category Legacy category' }));
    // Assert
    expect(screen.getByRole('button', { name: 'Approve category' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Edit category' })).toBeDisabled();
    expect(screen.getByRole('dialog')).toHaveTextContent('Save draft changes before reviewing this category.');
    expect(review).not.toHaveBeenCalled();
  });

  it('opens a named user category for read-only inspection without a mutation', () => {
    // Arrange
    const input = props({ sectionType: 'UsersAndAccess', initialContent: '{}', initialChildItems: [user], isReadOnly: true });
    render(<ProfileSectionForm {...input} />);
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Open user category Legacy category' }));
    // Assert
    const dialog = screen.getByRole('dialog');
    expect(dialog).toHaveTextContent('Existing description');
    expect(dialog).toHaveTextContent('Legacy access');
    expect(within(dialog).queryByRole('button', { name: 'Edit category' })).not.toBeInTheDocument();
    expect(input.onSave).not.toHaveBeenCalled();
  });

  it('accepts named mission populations but rejects fractional user counts', () => {
    // Arrange
    const input = props({ sectionType: 'UsersAndAccess', initialContent: '{}', initialChildItems: [] });
    render(<ProfileSectionForm {...input} />);
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Add user category' }));
    expect(screen.getByLabelText('Category').tagName).toBe('INPUT');
    fireEvent.change(screen.getByLabelText('Category'), { target: { value: 'Mission staff' } });
    fireEvent.change(screen.getByLabelText('Count'), { target: { value: '1.5' } });
    fireEvent.click(screen.getByRole('button', { name: 'Apply to draft' }));
    // Assert
    expect(screen.getByRole('alert')).toHaveTextContent('Count must be a whole number between 0 and 2147483647.');
    expect(input.onSave).not.toHaveBeenCalled();
    // Act
    fireEvent.change(screen.getByLabelText('Count'), { target: { value: '120' } });
    fireEvent.click(screen.getByRole('button', { name: 'Apply to draft' }));
    fireEvent.click(screen.getByRole('button', { name: 'Save Draft' }));
    // Assert
    expect(input.onSave).toHaveBeenCalledWith('{}', [expect.objectContaining({ categoryName: 'Mission staff', approximateCount: 120 })]);
  });

  it.each([undefined, 'system-profile-editor'])('allows an empty scalar draft with formId %s', formId => {
    // Arrange
    const input = props({ initialContent: null, formId });
    render(<>
      {formId && <button type="submit" form={formId}>Header save</button>}
      <ProfileSectionForm {...input} />
    </>);
    // Act
    fireEvent.click(screen.getByRole('button', { name: formId ? 'Header save' : 'Save Draft' }));
    // Assert
    expect(input.onSave).toHaveBeenCalledWith('{}', undefined);
    expect(screen.getByRole('textbox', { name: 'Mission statement' })).not.toBeRequired();
    expect(screen.getByRole('textbox', { name: 'Business purpose' })).not.toBeRequired();
  });

  it('supports an external submit button without a duplicate Save Draft action', () => {
    // Arrange
    const input = props();
    render(<><button type="submit" form="profile-editor">Header save</button>
      <ProfileSectionForm {...input} formId="profile-editor" /></>);
    // Act
    fireEvent.change(screen.getByRole('textbox', { name: 'Mission statement' }), { target: { value: 'Changed mission' } });
    fireEvent.click(screen.getByRole('button', { name: 'Header save' }));
    // Assert
    expect(screen.queryByRole('button', { name: 'Save Draft' })).not.toBeInTheDocument();
    expect(input.onSave).toHaveBeenCalledWith(JSON.stringify({ ...original, missionStatement: 'Changed mission' }), undefined);
  });

  it('blocks stale review submission until canonical saved props arrive and preserves failed edits', () => {
    // Arrange
    const input = props();
    const { rerender } = render(<ProfileSectionForm {...input} />);
    expect(screen.getByRole('button', { name: 'Submit for Review' })).toBeEnabled();
    // Act
    fireEvent.change(screen.getByRole('textbox', { name: 'Mission statement' }), { target: { value: 'Changed mission' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save Draft' }));
    rerender(<ProfileSectionForm {...input} isSubmitting />);
    // Assert
    expect(screen.getByRole('textbox', { name: 'Mission statement' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Submit for Review' })).toBeDisabled();
    // Act
    rerender(<ProfileSectionForm {...input} error="Save failed" />);
    // Assert
    expect(screen.getByRole('textbox', { name: 'Mission statement' })).toHaveValue('Changed mission');
    expect(screen.getByText(/Save draft changes before submitting for review/)).toBeVisible();
    fireEvent.click(screen.getByRole('button', { name: 'Submit for Review' }));
    expect(input.onSubmit).not.toHaveBeenCalled();
    // Act
    rerender(<ProfileSectionForm {...input} initialContent={JSON.stringify({ ...original, missionStatement: 'Canonical mission' })} />);
    // Assert
    expect(screen.getByRole('textbox', { name: 'Mission statement' })).toHaveValue('Canonical mission');
    expect(screen.getByRole('button', { name: 'Submit for Review' })).toBeEnabled();
  });

  it('restores review eligibility when edits are reverted and collapses secondary mission details', () => {
    // Arrange
    render(<ProfileSectionForm {...props()} />);
    // Act
    fireEvent.change(screen.getByRole('textbox', { name: 'Business purpose' }), { target: { value: 'New purpose' } });
    fireEvent.change(screen.getByRole('textbox', { name: 'Business purpose' }), { target: { value: original.businessPurpose } });
    // Assert
    expect(screen.getByRole('button', { name: 'Submit for Review' })).toBeEnabled();
    expect(screen.getByRole('textbox', { name: 'Business purpose' })).toBeVisible();
    expect(screen.getByLabelText('Operational Justification')).not.toBeVisible();
    expect(screen.getByLabelText('Business Functions')).not.toBeVisible();
    expect(screen.getByText('Additional mission details')).toBeVisible();
  });

  it('edits legacy child values in a dialog and only persists through Save Draft', () => {
    // Arrange
    const input = props({ sectionType: 'UsersAndAccess', initialContent: '{}', initialChildItems: [user] });
    render(<ProfileSectionForm {...input} />);
    expect(within(screen.getByRole('table')).queryByRole('combobox')).not.toBeInTheDocument();
    // Act
    editUser();
    const dialog = screen.getByRole('dialog');
    // Assert
    expect(within(dialog).getByLabelText('Category')).toHaveValue('Legacy category');
    expect(within(dialog).getByRole('option', { name: 'Legacy access (previously recorded)' })).toBeInTheDocument();
    expect(document.querySelector('form form')).toBeNull();
    // Act
    fireEvent.change(within(dialog).getByLabelText('Description'), { target: { value: 'Updated description' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Apply to draft' }));
    // Assert
    expect(input.onSave).not.toHaveBeenCalled();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Open user category Legacy category' }));
    expect(screen.getByText('Updated description')).toBeVisible();
    fireEvent.click(screen.getByRole('button', { name: 'Close' }));
    expect(screen.getByText('Save draft changes before reviewing an individual user category.')).toBeVisible();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Save Draft' }));
    // Assert
    expect(input.onSave).toHaveBeenCalledWith('{}', [{ ...user, description: 'Updated description' }]);
  });

  it('validates required child fields and nonnegative counts before applying', () => {
    // Arrange
    const input = props({ sectionType: 'UsersAndAccess', initialContent: '{}', initialChildItems: [] });
    render(<ProfileSectionForm {...input} />);
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Add user category' }));
    const dialog = screen.getByRole('dialog');
    fireEvent.click(within(dialog).getByRole('button', { name: 'Apply to draft' }));
    // Assert
    expect(within(dialog).getByRole('alert')).toHaveTextContent('Category is required');
    // Act
    fireEvent.change(within(dialog).getByLabelText('Category'), { target: { value: 'Application Users' } });
    fireEvent.change(within(dialog).getByLabelText('Count'), { target: { value: '-1' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Apply to draft' }));
    // Assert
    expect(within(dialog).getByRole('alert')).toHaveTextContent('Count must be a finite, nonnegative number');
    expect(input.onSave).not.toHaveBeenCalled();
    // Act
    fireEvent.change(within(dialog).getByLabelText('Count'), { target: { value: '0' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Apply to draft' }));
    fireEvent.click(screen.getByRole('button', { name: 'Save Draft' }));
    // Assert
    expect(input.onSave).toHaveBeenCalledWith('{}', [expect.objectContaining({ categoryName: 'Application Users', approximateCount: 0, sortOrder: 0 })]);
  });

  it('discards cancelled edits, confirms removal, and preserves ordered source rows', () => {
    // Arrange
    const input = props({ sectionType: 'UsersAndAccess', initialContent: '{}',
      initialChildItems: [user, { ...user, id: 'user-2', categoryName: 'Second category', sortOrder: 1 }] });
    render(<ProfileSectionForm {...input} />);
    // Act
    editUser();
    fireEvent.change(screen.getByLabelText('Description'), { target: { value: 'Discard me' } });
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    // Assert
    expect(screen.queryByText('Discard me')).not.toBeInTheDocument();
    expect(screen.queryByText('Save draft changes before reviewing an individual user category.')).not.toBeInTheDocument();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Open user category Legacy category' }));
    fireEvent.click(screen.getByRole('button', { name: 'Move down' }));
    fireEvent.click(screen.getByRole('button', { name: /^Close$/ }));
    fireEvent.click(screen.getByRole('button', { name: 'Open user category Second category' }));
    fireEvent.click(screen.getByRole('button', { name: 'Remove category' }));
    // Assert
    expect(screen.getByRole('dialog')).toHaveTextContent('Second category');
    expect(screen.getByRole('table')).toHaveTextContent('Second category');
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Remove from draft' }));
    fireEvent.click(screen.getByRole('button', { name: 'Save Draft' }));
    // Assert
    expect(input.onSave).toHaveBeenCalledWith('{}', [{ ...user, sortOrder: 1 }]);
  });

  it('keeps row review permissions independent from edit permission', () => {
    // Arrange
    const input = props({ sectionType: 'UsersAndAccess', initialChildItems: [{ ...user, governanceStatus: 'UnderReview', revision: 1, canReview: true }],
      governanceStatus: 'UnderReview', isReadOnly: true, effectiveRoles: ['ISSM'],
      onReviewUserCategory: vi.fn().mockResolvedValue(true) });
    render(<ProfileSectionForm {...input} />);
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Open user category Legacy category' }));
    // Assert
    expect(screen.getByRole('button', { name: 'Approve category' })).toBeEnabled();
    expect(screen.queryByRole('button', { name: 'Approve' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Add user category' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Edit row 1' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Save Draft' })).not.toBeInTheDocument();
  });

  it('retains child edits across equivalent source refetches after a failed save', () => {
    // Arrange
    const input = props({ sectionType: 'UsersAndAccess', initialContent: '{}', initialChildItems: [user] });
    const { rerender } = render(<ProfileSectionForm {...input} />);
    // Act
    editUser();
    fireEvent.change(screen.getByLabelText('Description'), { target: { value: 'Unsaved description' } });
    fireEvent.click(screen.getByRole('button', { name: 'Apply to draft' }));
    fireEvent.click(screen.getByRole('button', { name: 'Save Draft' }));
    rerender(<ProfileSectionForm {...input} initialChildItems={[{ ...user }]} error="Save failed" />);
    // Assert
    fireEvent.click(screen.getByRole('button', { name: 'Open user category Legacy category' }));
    expect(screen.getByText('Unsaved description')).toBeVisible();
    fireEvent.click(screen.getByRole('button', { name: 'Close' }));
    expect(screen.getByText('Save draft changes before reviewing an individual user category.')).toBeVisible();
    // Act
    rerender(<ProfileSectionForm {...input} initialChildItems={[{ ...user, description: 'Canonical description' }]} />);
    // Assert
    fireEvent.click(screen.getByRole('button', { name: 'Open user category Legacy category' }));
    expect(screen.getByText('Canonical description')).toBeVisible();
    expect(screen.queryByText('Save draft changes before reviewing an individual user category.')).not.toBeInTheDocument();
  });

  it('locks child editor and actions while writing', () => {
    // Arrange
    const input = props({ sectionType: 'UsersAndAccess', initialContent: '{}', initialChildItems: [user] });
    const { rerender } = render(<ProfileSectionForm {...input} />);
    // Act
    editUser();
    rerender(<ProfileSectionForm {...input} isSubmitting />);
    // Assert
    expect(screen.getByLabelText('Description')).toBeDisabled();
    expect(screen.getByLabelText('Category')).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Apply to draft' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Close dialog' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Add user category' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Open user category Legacy category' })).toBeDisabled();
    // Act
    fireEvent.submit(screen.getByRole('button', { name: 'Apply to draft' }).closest('form')!);
    // Assert
    expect(input.onSave).not.toHaveBeenCalled();
    expect(screen.getByRole('dialog')).toBeInTheDocument();
  });

  it('rejects a nonfinite recorded count instead of applying it as valid draft data', () => {
    // Arrange
    render(<ProfileSectionForm {...props({ sectionType: 'UsersAndAccess', initialContent: '{}',
      initialChildItems: [{ ...user, approximateCount: Number.POSITIVE_INFINITY }] })} />);
    // Act
    editUser();
    fireEvent.click(screen.getByRole('button', { name: 'Apply to draft' }));
    // Assert
    expect(screen.getByRole('alert')).toHaveTextContent('Count must be a finite, nonnegative number');
    expect(screen.getByRole('dialog')).toBeInTheDocument();
  });

  it('rejects invalid numeric input that the browser exposes as an empty value', () => {
    // Arrange
    render(<ProfileSectionForm {...props({ sectionType: 'UsersAndAccess', initialContent: '{}', initialChildItems: [user] })} />);
    editUser();
    const count = screen.getByLabelText('Count');
    Object.defineProperty(count, 'validity', { configurable: true, value: { badInput: true } });
    // Act
    fireEvent.change(count, { target: { value: '' } });
    fireEvent.click(screen.getByRole('button', { name: 'Apply to draft' }));
    // Assert
    expect(screen.getByRole('alert')).toHaveTextContent('Count must be a finite, nonnegative number');
    expect(screen.getByRole('dialog')).toBeInTheDocument();
  });

  it.each([
    ['DataTypes', 'Add data type', 'Data Type', 'dataTypeName', 'source',
      { id: 'data-1', dataTypeName: 'Custom data', sensitivityClassification: 'Custom classification', source: 'Source system',
        destination: 'Legacy destination', applicableRegulations: 'Custom regulation', description: 'Saved description', sortOrder: 0 }],
    ['PortsProtocolsAndServices', 'Add port / service', 'Port/Range', 'portOrRange', 'justification',
      { id: 'pps-1', portOrRange: '10000-10100', protocol: 'Custom protocol', serviceName: 'Custom service', direction: 'Both',
        justification: 'Approved custom ports', sortOrder: 0 }],
    ['LeveragedAuthorizations', 'Add Row', 'Provider', 'providerName', 'coveredControlFamilies',
      { id: 'auth-1', providerName: 'Custom provider', authorizationType: 'Custom authority', authorizationDate: '2026-09-01',
        coveredControlFamilies: 'AC, AU, SC', sortOrder: 0 }],
  ] as const)('preserves %s source fields and validates its add dialog', (sectionType, addLabel, label, key, sourceKey, row) => {
    // Arrange
    const input = props({ sectionType, initialContent: '{}', initialChildItems: [row] as Props['initialChildItems'] });
    render(<ProfileSectionForm {...input} />);
    // Act
    if (sectionType === 'DataTypes') {
      fireEvent.click(screen.getByRole('button', { name: 'Open data type Custom data' }));
      fireEvent.click(screen.getByRole('button', { name: 'Edit data type' }));
    } else fireEvent.click(screen.getByRole('button', { name: 'Edit row 1' }));
    // Assert
    expect(screen.getByLabelText(label)).toHaveValue(row[key as keyof typeof row]);
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Apply to draft' }));
    fireEvent.click(screen.getByRole('button', { name: 'Save Draft' }));
    // Assert
    expect(input.onSave).toHaveBeenCalledWith('{}', [row]);
    expect(vi.mocked(input.onSave).mock.calls[0]![1]![0]![sourceKey]).toBe(row[sourceKey as keyof typeof row]);
    expect(screen.getByRole('button', { name: 'Submit for Review' })).toBeEnabled();
    // Act
    fireEvent.click(screen.getByRole('button', { name: addLabel }));
    fireEvent.click(screen.getByRole('button', { name: 'Apply to draft' }));
    // Assert
    expect(screen.getByRole('alert')).toHaveTextContent(`${label} is required`);
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    // Assert
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(within(screen.getByRole('table')).getAllByRole('row')).toHaveLength(2);
  });

  it.each(['{broken', 'null', '[]'])('reports unreadable saved content (%s) instead of overwriting it', initialContent => {
    // Arrange
    const input = props({ initialContent });
    render(<ProfileSectionForm {...input} />);
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Save Draft' }));
    fireEvent.click(screen.getByRole('button', { name: 'Submit for Review' }));
    // Assert
    expect(screen.getByRole('alert')).toHaveTextContent('Saved section content could not be read');
    expect(input.onSave).not.toHaveBeenCalled();
    expect(input.onSubmit).not.toHaveBeenCalled();
  });
});
