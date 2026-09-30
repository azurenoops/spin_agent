import { fireEvent, render, screen, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { ComponentProps } from 'react';
import ProfileSectionForm from '../../components/forms/ProfileSectionForm';
import '../helpers/dialog';

type Props = ComponentProps<typeof ProfileSectionForm>;
const data = {
  id: 'data-1', dataTypeName: 'Mission support records', description: 'Operational information',
  sensitivityClassification: 'Legacy classification', source: 'Legacy source',
  destination: 'Legacy destination', applicableRegulations: 'Legacy regulation', sortOrder: 4,
  customSource: 'preserve',
};
function props(overrides: Partial<Props> = {}): Props {
  return {
    sectionType: 'DataTypes', governanceStatus: 'Draft', initialChildItems: [data],
    initialContent: '{"dataOverview":"Recorded handling","highestSensitivityLevel":"CUI","customSource":"retain"}',
    reviewerComments: null, isReadOnly: false, userRole: 'MissionOwner',
    isSubmitting: false, error: null, onSave: vi.fn(), onSubmit: vi.fn(), onWithdraw: vi.fn(),
    ...overrides,
  };
}
function openData() {
  const trigger = screen.getByRole('button', { name: 'Open data type Mission support records' });
  trigger.focus();
  fireEvent.click(trigger);
  return trigger;
}

describe('Data information types editor', () => {
  it('saves system-wide handling context from its dialog with existing rows and unknown fields', () => {
    // Arrange
    const input = props({ contextDialogOpen: true, onContextDialogClose: vi.fn() });
    render(<ProfileSectionForm {...input} />);
    const dialog = screen.getByRole('dialog', { name: 'System-wide information handling context' });
    // Act
    fireEvent.change(within(dialog).getByLabelText('Data Overview'), { target: { value: 'Updated handling overview' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save information context' }));
    // Assert
    expect(input.onSave).toHaveBeenCalledExactlyOnceWith(
      '{"dataOverview":"Updated handling overview","highestSensitivityLevel":"CUI","customSource":"retain"}', [data]);
    expect(document.querySelector('form form')).toBeNull();
  });
  it('cancels only context changes and retains an unsaved data-type edit', () => {
    // Arrange
    const input = props({ onContextDialogClose: vi.fn() });
    const view = render(<ProfileSectionForm {...input} />);
    openData();
    fireEvent.click(screen.getByRole('button', { name: 'Edit data type' }));
    fireEvent.change(screen.getByLabelText('Description'), { target: { value: 'Pending row description' } });
    fireEvent.click(screen.getByRole('button', { name: 'Apply to draft' }));
    view.rerender(<ProfileSectionForm {...input} contextDialogOpen />);
    // Act
    fireEvent.change(screen.getByLabelText('Data Overview'), { target: { value: 'Discard context edit' } });
    fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Cancel' }));
    view.rerender(<ProfileSectionForm {...input} contextDialogOpen={false} />);
    fireEvent.click(screen.getByRole('button', { name: 'Save Draft' }));
    // Assert
    expect(input.onContextDialogClose).toHaveBeenCalled();
    expect(input.onSave).toHaveBeenCalledWith(input.initialContent, [{ ...data, description: 'Pending row description' }]);
  });
  it('retains failed context input and locks pending saves', () => {
    // Arrange
    const input = props({ contextDialogOpen: true });
    const view = render(<ProfileSectionForm {...input} />);
    fireEvent.change(screen.getByLabelText('Data Overview'), { target: { value: 'Pending context' } });
    // Act
    view.rerender(<ProfileSectionForm {...input} isSubmitting />);
    // Assert
    expect(screen.getByLabelText('Data Overview')).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Save information context' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Cancel' })).toBeDisabled();
    // Act
    view.rerender(<ProfileSectionForm {...input} error="Save failed" />);
    // Assert
    expect(screen.getByLabelText('Data Overview')).toHaveValue('Pending context');
    expect(within(screen.getByRole('dialog')).getByRole('alert')).toHaveTextContent('Save failed');
  });
  it.each(['Draft', 'Approved', 'UnderReview', 'NeedsRevision', 'NotStarted'] as const)(
    'shows only five compact columns and section-scoped %s governance', governanceStatus => {
      // Arrange
      render(<ProfileSectionForm {...props({ governanceStatus })} />);
      // Act
      const table = screen.getByRole('table', { name: 'Information types' });
      // Assert
      expect(within(table).getAllByRole('columnheader').map(cell => cell.textContent))
        .toEqual(['Data type', 'Context', 'Sensitivity', 'Review state', 'Open']);
      expect(table).not.toHaveClass('min-w-[720px]');
      expect(within(table).getByText(`Section: ${governanceStatus}`)).toBeVisible();
      expect(within(table).queryByRole('button', { name: /edit|remove|move/i })).not.toBeInTheDocument();
      expect(screen.queryByText('Information handling context')).not.toBeInTheDocument();
      expect(screen.queryByLabelText('Data Overview')).not.toBeInTheDocument();
    },
  );

  it('lets readers open every recorded field without mutation or independent review controls', () => {
    // Arrange
    render(<ProfileSectionForm {...props({ isReadOnly: true, governanceStatus: 'Approved' })} />);
    // Act
    const trigger = openData();
    const dialog = screen.getByRole('dialog', { name: 'Data type: Mission support records' });
    // Assert
    for (const value of [data.dataTypeName, data.description, data.sensitivityClassification,
      data.source, data.destination, data.applicableRegulations]) {
      expect(within(dialog).getByText(value, { exact: true })).toBeVisible();
    }
    expect(within(dialog).getByText('Section: Approved')).toBeVisible();
    expect(within(dialog).getByText(/Data types are reviewed with the section/)).toBeVisible();
    expect(within(dialog).queryByRole('button', { name: /edit|remove|move|approve|review/i })).not.toBeInTheDocument();
    // Act
    fireEvent.click(within(dialog).getByRole('button', { name: 'Close' }));
    // Assert
    expect(trigger).toHaveFocus();
  });

  it('edits custom data names locally, preserves unknown fields and saves only from the footer', () => {
    // Arrange
    const input = props();
    render(<ProfileSectionForm {...input} />);
    // Act
    const trigger = openData();
    fireEvent.click(screen.getByRole('button', { name: 'Edit data type' }));
    const dialog = screen.getByRole('dialog', { name: 'Edit data type' });
    const name = within(dialog).getByRole('combobox', { name: 'Data Type' });
    // Assert
    expect(name.tagName).toBe('INPUT');
    expect(name).toHaveAttribute('maxlength', '200');
    expect(name).toHaveAttribute('list', 'child-dataTypeName-suggestions');
    expect(name).toHaveFocus();
    expect(within(dialog).getByLabelText('Description')).toHaveAttribute('maxlength', '2000');
    // Act
    fireEvent.change(name, { target: { value: 'Mission support records – archive' } });
    fireEvent.change(within(dialog).getByLabelText('Description'), { target: { value: 'Updated context' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Apply to draft' }));
    // Assert
    expect(trigger).toHaveFocus();
    expect(input.onSave).not.toHaveBeenCalled();
    expect(screen.getByText('Unsaved section changes')).toBeVisible();
    expect(screen.getByRole('button', { name: 'Submit for Review' })).toBeDisabled();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Save Draft' }));
    // Assert
    expect(input.onSave).toHaveBeenCalledExactlyOnceWith(input.initialContent, [
      { ...data, dataTypeName: 'Mission support records – archive', description: 'Updated context' },
    ]);
  });

  it('cancels editing back to inspection with focus and discards the staged row edits', () => {
    // Arrange
    const input = props();
    render(<ProfileSectionForm {...input} />);
    // Act
    const trigger = openData();
    fireEvent.click(screen.getByRole('button', { name: 'Edit data type' }));
    fireEvent.change(screen.getByLabelText('Description'), { target: { value: 'Discard me' } });
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    // Assert
    const dialog = screen.getByRole('dialog', { name: 'Data type: Mission support records' });
    expect(within(dialog).getByText('Operational information')).toBeVisible();
    expect(within(dialog).getByRole('button', { name: 'Edit data type' })).toHaveFocus();
    expect(screen.queryByText('Discard me')).not.toBeInTheDocument();
    // Act
    fireEvent.click(within(dialog).getByRole('button', { name: 'Close' }));
    // Assert
    expect(trigger).toHaveFocus();
    expect(input.onSave).not.toHaveBeenCalled();
  });

  it('requires confirmation to remove and restores focus to an available table action', () => {
    // Arrange
    const next = { ...data, id: 'data-2', dataTypeName: 'Contact directory', sortOrder: 5 };
    const input = props({ initialChildItems: [data, next] });
    render(<ProfileSectionForm {...input} />);
    // Act
    openData();
    fireEvent.click(screen.getByRole('button', { name: 'Remove data type' }));
    // Assert
    let dialog = screen.getByRole('dialog', { name: 'Remove data type' });
    expect(within(dialog).getByText(/saved record is unchanged until Save Draft/)).toBeVisible();
    expect(input.onSave).not.toHaveBeenCalled();
    // Act
    fireEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }));
    fireEvent.click(screen.getByRole('button', { name: 'Remove data type' }));
    dialog = screen.getByRole('dialog', { name: 'Remove data type' });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Remove from draft' }));
    // Assert
    expect(screen.queryByRole('button', { name: 'Open data type Mission support records' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Open data type Contact directory' })).toHaveFocus();
    expect(input.onSave).not.toHaveBeenCalled();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Save Draft' }));
    // Assert
    expect(input.onSave).toHaveBeenCalledWith(input.initialContent, [{ ...next, sortOrder: 0 }]);
  });

  it('uses the external Add trigger without a duplicate visual heading or add button', () => {
    // Arrange
    const input = props({ addEntryOpen: false, onAddEntryClose: vi.fn() });
    const { rerender } = render(<><button>Header Add data type</button><ProfileSectionForm {...input} /></>);
    const trigger = screen.getByRole('button', { name: 'Header Add data type' });
    trigger.focus();
    // Act
    rerender(<><button>Header Add data type</button><ProfileSectionForm {...input} addEntryOpen /></>);
    const dialog = screen.getByRole('dialog', { name: 'Add data type' });
    // Assert
    expect(screen.getByRole('table', { name: 'Information types' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Information types' }).parentElement).toHaveClass('sr-only');
    expect(screen.queryByRole('button', { name: 'Add data type' })).not.toBeInTheDocument();
    // Act
    fireEvent.change(within(dialog).getByLabelText('Data Type'), { target: { value: 'Custom records' } });
    fireEvent.change(within(dialog).getByLabelText('Classification'), { target: { value: 'CUI' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Apply to draft' }));
    // Assert
    expect(input.onAddEntryClose).toHaveBeenCalledOnce();
    expect(input.onSave).not.toHaveBeenCalled();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(trigger).toHaveFocus();
    expect(screen.getByRole('button', { name: 'Open data type Custom records' })).toBeVisible();
  });

  it('validates required name, classification and maximum lengths before applying', () => {
    // Arrange
    render(<ProfileSectionForm {...props()} />);
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Add data type' }));
    const apply = () => fireEvent.click(screen.getByRole('button', { name: 'Apply to draft' }));
    apply();
    // Assert
    expect(screen.getByRole('alert')).toHaveTextContent('Data Type is required.');
    // Act
    fireEvent.change(screen.getByLabelText('Data Type'), { target: { value: 'Mission support records' } });
    apply();
    // Assert
    expect(screen.getByRole('alert')).toHaveTextContent('Classification is required.');
    // Act
    fireEvent.change(screen.getByLabelText('Classification'), { target: { value: 'CUI' } });
    fireEvent.change(screen.getByLabelText('Data Type'), { target: { value: 'x'.repeat(201) } });
    apply();
    // Assert
    expect(screen.getByRole('alert')).toHaveTextContent('Data Type must be 200 characters or fewer.');
    // Act
    fireEvent.change(screen.getByLabelText('Data Type'), { target: { value: 'Custom records' } });
    fireEvent.change(screen.getByLabelText('Description'), { target: { value: 'x'.repeat(2001) } });
    apply();
    // Assert
    expect(screen.getByRole('alert')).toHaveTextContent('Description must be 2000 characters or fewer.');
  });

  it('keeps external Add inputs intact through busy locks and blocks dismissal and mutations', () => {
    // Arrange
    const input = props({ addEntryOpen: true, onAddEntryClose: vi.fn() });
    const { rerender } = render(<ProfileSectionForm {...input} />);
    fireEvent.change(screen.getByLabelText('Data Type'), { target: { value: 'Retained draft' } });
    // Act
    rerender(<ProfileSectionForm {...input} isSubmitting />);
    const dialog = screen.getByRole('dialog', { name: 'Add data type' });
    fireEvent(dialog, new Event('cancel', { cancelable: true }));
    fireEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }));
    fireEvent.submit(within(dialog).getByLabelText('Data Type').closest('form')!);
    // Assert
    expect(within(dialog).getByLabelText('Data Type')).toBeDisabled();
    expect(within(dialog).getByRole('button', { name: 'Close dialog' })).toBeDisabled();
    expect(input.onAddEntryClose).not.toHaveBeenCalled();
    expect(input.onSave).not.toHaveBeenCalled();
    // Act
    rerender(<ProfileSectionForm {...input} error="Save conflict" />);
    // Assert
    expect(screen.getByLabelText('Data Type')).toHaveValue('Retained draft');
  });

  it('retains applied changes after a failed save and equivalent refetch', () => {
    // Arrange
    const input = props();
    const { rerender } = render(<ProfileSectionForm {...input} />);
    // Act
    openData();
    fireEvent.click(screen.getByRole('button', { name: 'Edit data type' }));
    fireEvent.change(screen.getByLabelText('Description'), { target: { value: 'Retained context' } });
    fireEvent.click(screen.getByRole('button', { name: 'Apply to draft' }));
    fireEvent.click(screen.getByRole('button', { name: 'Save Draft' }));
    rerender(<ProfileSectionForm {...input} initialChildItems={[{ ...data }]} error="Save conflict" />);
    // Assert
    expect(screen.getByText('Retained context')).toBeVisible();
    expect(screen.getByText('Save conflict')).toBeVisible();
    expect(screen.getByRole('button', { name: 'Submit for Review' })).toBeDisabled();
  });

  it('never presents unknown row review metadata as independent data governance', () => {
    // Arrange
    const withMetadata = { ...data, governanceStatus: 'Approved', revision: 9, canReview: true, approvedSnapshotId: 'snapshot' };
    const input = props({ governanceStatus: 'Draft', initialChildItems: [withMetadata], onReviewUserCategory: vi.fn() });
    render(<ProfileSectionForm {...input} />);
    // Act
    openData();
    const dialog = screen.getByRole('dialog', { name: 'Data type: Mission support records' });
    // Assert
    expect(within(dialog).getByText('Section: Draft')).toBeVisible();
    expect(within(dialog).queryByText(/Approved|Revision:|independently/)).not.toBeInTheDocument();
    expect(within(dialog).queryByRole('button', { name: /approve|review/i })).not.toBeInTheDocument();
    // Act
    fireEvent.click(within(dialog).getByRole('button', { name: 'Remove data type' }));
    // Assert
    expect(screen.getByRole('button', { name: 'Remove from draft' })).toBeEnabled();
    expect(screen.queryByRole('button', { name: 'Request removal' })).not.toBeInTheDocument();
    expect(input.onReviewUserCategory).not.toHaveBeenCalled();
  });

  it('reorders only through inspection, keeps row identity and saves the new order', () => {
    // Arrange
    const next = { ...data, id: 'data-2', dataTypeName: 'Contact directory', sortOrder: 5 };
    const input = props({ initialChildItems: [data, next] });
    render(<ProfileSectionForm {...input} />);
    // Act
    const trigger = openData();
    fireEvent.click(screen.getByRole('button', { name: 'Move down' }));
    // Assert
    expect(screen.getByRole('button', { name: 'Move down' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Move up' })).toBeEnabled();
    expect(input.onSave).not.toHaveBeenCalled();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Close' }));
    fireEvent.click(screen.getByRole('button', { name: 'Save Draft' }));
    // Assert
    expect(trigger).toHaveFocus();
    expect(input.onSave).toHaveBeenCalledExactlyOnceWith(input.initialContent,
      [{ ...next, sortOrder: 0 }, { ...data, sortOrder: 1 }]);
  });

  it('locks inspection and removal during a pending save', () => {
    // Arrange
    const input = props();
    const { rerender } = render(<ProfileSectionForm {...input} />);
    openData();
    // Act
    rerender(<ProfileSectionForm {...input} isSubmitting />);
    // Assert
    for (const label of ['Edit data type', 'Remove data type', 'Move up', 'Move down', 'Close', 'Close dialog']) {
      expect(screen.getByRole('button', { name: label })).toBeDisabled();
    }
    // Act
    rerender(<ProfileSectionForm {...input} />);
    fireEvent.click(screen.getByRole('button', { name: 'Remove data type' }));
    rerender(<ProfileSectionForm {...input} isSubmitting />);
    fireEvent.click(screen.getByRole('button', { name: 'Remove from draft' }));
    fireEvent(screen.getByRole('dialog'), new Event('cancel', { cancelable: true }));
    // Assert
    expect(screen.getByRole('dialog', { name: 'Remove data type' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Cancel' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Remove from draft' })).toBeDisabled();
    expect(input.onSave).not.toHaveBeenCalled();
  });

  it('provides a focus destination when the last entry is removed with external Add', () => {
    // Arrange
    const input = props({ onAddEntryClose: vi.fn() });
    render(<ProfileSectionForm {...input} />);
    // Act
    openData();
    fireEvent.click(screen.getByRole('button', { name: 'Remove data type' }));
    fireEvent.click(screen.getByRole('button', { name: 'Remove from draft' }));
    // Assert
    expect(screen.getByRole('table', { name: 'Information types' })).toHaveFocus();
    expect(screen.getByText(/No information types are recorded/)).toBeVisible();
    expect(input.onSave).not.toHaveBeenCalled();
  });

  it('cancels external Add back to its invoker and supports opening it again', () => {
    // Arrange
    const input = props({ addEntryOpen: false, onAddEntryClose: vi.fn() });
    const view = (open: boolean) => <><button>Header Add data type</button><ProfileSectionForm {...input} addEntryOpen={open} /></>;
    const { rerender } = render(view(false));
    const trigger = screen.getByRole('button', { name: 'Header Add data type' });
    trigger.focus();
    // Act
    rerender(view(true));
    fireEvent.change(screen.getByLabelText('Data Type'), { target: { value: 'Discard custom name' } });
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    rerender(view(false));
    // Assert
    expect(trigger).toHaveFocus();
    expect(input.onAddEntryClose).toHaveBeenCalledOnce();
    // Act
    rerender(view(true));
    // Assert
    expect(screen.getByLabelText('Data Type')).toHaveValue('');
    expect(input.onSave).not.toHaveBeenCalled();
  });
});
