import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, act, screen, fireEvent } from '@testing-library/react';
import IdleWarningModal from '../../features/auth/IdleWarningModal';

beforeEach(() => {
  vi.useFakeTimers();
});

afterEach(() => {
  vi.useRealTimers();
});

function dispatchIdleWarning(secondsUntilSignOut = 60) {
  window.dispatchEvent(
    new CustomEvent('ato:idle-warning', {
      detail: { secondsUntilSignOut },
    }),
  );
}

describe('IdleWarningModal', () => {
  it('renders nothing until ato:idle-warning is dispatched', () => {
    // Arrange + Act
    const { container } = render(<IdleWarningModal />);

    // Assert
    expect(container.firstChild).toBeNull();
  });

  it("renders a modal with a countdown and a 'Stay signed in' button after the warning", () => {
    // Arrange
    render(<IdleWarningModal />);

    // Act
    act(() => {
      dispatchIdleWarning(60);
    });

    // Assert
    const dialog = screen.getByRole('alertdialog');
    expect(dialog).toBeInTheDocument();
    expect(screen.getByText(/60/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /stay signed in/i })).toBeInTheDocument();
  });

  it('decrements the countdown each second', () => {
    // Arrange
    render(<IdleWarningModal />);

    // Act
    act(() => {
      dispatchIdleWarning(60);
    });
    expect(screen.getByText(/60/)).toBeInTheDocument();

    act(() => {
      vi.advanceTimersByTime(1_000);
    });

    // Assert — value should have decremented.
    expect(screen.getByText(/59/)).toBeInTheDocument();
  });

  it("'Stay signed in' button dispatches ato:user-input and closes the modal", () => {
    // Arrange
    render(<IdleWarningModal />);
    act(() => {
      dispatchIdleWarning(60);
    });

    let userInputCount = 0;
    const handler = () => {
      userInputCount += 1;
    };
    window.addEventListener('ato:user-input', handler as EventListener);

    // Act
    act(() => {
      fireEvent.click(screen.getByRole('button', { name: /stay signed in/i }));
    });

    // Assert
    expect(userInputCount).toBe(1);
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();

    window.removeEventListener('ato:user-input', handler as EventListener);
  });

  it('closes the modal when the countdown reaches 0', () => {
    // Arrange
    render(<IdleWarningModal />);
    act(() => {
      dispatchIdleWarning(3);
    });
    expect(screen.getByRole('alertdialog')).toBeInTheDocument();

    // Act — advance past the countdown.
    act(() => {
      vi.advanceTimersByTime(3_000);
    });

    // Assert — the actual sign-out is useIdleTimer's responsibility;
    // the modal just dismisses itself when the countdown ends.
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
  });
});
