import { useState } from 'react';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { Field, MutationForm } from '../../features/provider-authorizations/forms';
import { PackageImportError } from '../../features/package-imports/request';
import '../package-imports/crypto';

function Form({ submit, saved }: { submit: (value: string, key: string) => Promise<unknown>; saved: () => void }) {
  const [value, setValue] = useState('Exact original intent');
  return <MutationForm label="Save exact operation" submit={key => submit(value, key)} onSaved={saved}>
    <Field label="Retained input" value={value} onChange={setValue} required />
  </MutationForm>;
}
describe('authorization mutation safety', () => {
  it('prevents concurrent duplicate submits and reports success only after the server responds', async () => {
    // Arrange
    let finish!: (value: unknown) => void;
    const submit = vi.fn(() => new Promise(resolve => { finish = resolve; }));
    const saved = vi.fn();
    render(<Form submit={submit} saved={saved} />);
    // Act
    const button = screen.getByRole('button', { name: 'Save exact operation' });
    fireEvent.click(button); fireEvent.click(button);
    // Assert
    expect(submit).toHaveBeenCalledOnce();
    expect(saved).not.toHaveBeenCalled();
    await act(async () => finish({ persisted: true }));
    expect(saved).toHaveBeenCalledOnce();
  });
  it('retains the same key and intent after uncertain failure even if the parent callback changes', async () => {
    // Arrange
    const submit = vi.fn().mockRejectedValueOnce(new Error('Connection lost')).mockResolvedValue({});
    const replacement = vi.fn().mockResolvedValue({});
    const saved = vi.fn();
    const view = render(<Form submit={submit} saved={saved} />);
    fireEvent.click(screen.getByRole('button', { name: 'Save exact operation' }));
    await screen.findByText(/Outcome uncertain/);
    const originalKey = submit.mock.calls[0]?.[1];
    // Act
    view.rerender(<Form submit={replacement} saved={saved} />);
    expect(screen.getByLabelText('Retained input')).toBeDisabled();
    fireEvent.click(screen.getByRole('button', { name: 'Retry same operation' }));
    // Assert
    await waitFor(() => expect(saved).toHaveBeenCalledOnce());
    expect(submit).toHaveBeenLastCalledWith('Exact original intent', originalKey);
    expect(replacement).not.toHaveBeenCalled();
  });
  it('surfaces a stale conflict without clearing inputs or reporting success', async () => {
    // Arrange
    const submit = vi.fn().mockRejectedValue(new PackageImportError('AUTHORIZATION_CONTEXT_STALE', 409));
    const saved = vi.fn();
    render(<Form submit={submit} saved={saved} />);
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Save exact operation' }));
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('AUTHORIZATION_CONTEXT_STALE');
    expect(screen.getByLabelText('Retained input')).toHaveValue('Exact original intent');
    expect(saved).not.toHaveBeenCalled();
  });
  it('warns before leaving an operation whose durable outcome is unknown', async () => {
    // Arrange
    const submit = vi.fn().mockRejectedValue(new Error('Response lost'));
    render(<Form submit={submit} saved={vi.fn()} />);
    fireEvent.click(screen.getByRole('button', { name: 'Save exact operation' }));
    await screen.findByText(/Outcome uncertain/);
    // Act
    const unload = new Event('beforeunload', { cancelable: true });
    window.dispatchEvent(unload);
    // Assert
    expect(unload.defaultPrevented).toBe(true);
  });
});
