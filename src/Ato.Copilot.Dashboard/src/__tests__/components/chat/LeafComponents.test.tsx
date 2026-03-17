import { render, screen, fireEvent } from '@testing-library/react';
import { describe, it, expect, vi } from 'vitest';
import ChatBubble from '../../../components/chat/ChatBubble';
import ChatInput from '../../../components/chat/ChatInput';
import ChatHeader from '../../../components/chat/ChatHeader';
import ProgressSteps from '../../../components/chat/ProgressSteps';
import ErrorMessage from '../../../components/chat/ErrorMessage';
import type { Message, SseProgressEvent, ErrorDetail } from '../../../types/chat';

describe('ChatBubble', () => {
  const baseMessage: Message = {
    id: '1',
    role: 'user',
    content: 'Hello world',
    status: 'complete',
    timestamp: new Date().toISOString(),
  };

  it('renders user message right-aligned with blue background', () => {
    const { container } = render(<ChatBubble message={baseMessage} />);
    const bubble = container.querySelector('.justify-end');
    expect(bubble).toBeDefined();
    expect(screen.getByText('Hello world')).toBeDefined();
  });

  it('renders assistant message left-aligned with white background', () => {
    const msg: Message = { ...baseMessage, role: 'assistant', content: 'Hi there' };
    const { container } = render(<ChatBubble message={msg} />);
    const bubble = container.querySelector('.justify-start');
    expect(bubble).toBeDefined();
    expect(screen.getByText('Hi there')).toBeDefined();
  });

  it('shows status indicator for sending state', () => {
    const msg: Message = { ...baseMessage, status: 'sending' };
    const { container } = render(<ChatBubble message={msg} />);
    const spinner = container.querySelector('.animate-spin');
    expect(spinner).toBeDefined();
  });

  it('shows error icon for error state', () => {
    const msg: Message = { ...baseMessage, status: 'error' };
    const { container } = render(<ChatBubble message={msg} />);
    const errorIcon = container.querySelector('.text-red-500');
    expect(errorIcon).toBeDefined();
  });
});

describe('ChatInput', () => {
  it('calls onSend when Enter is pressed', () => {
    const onSend = vi.fn();
    const onCancel = vi.fn();
    render(<ChatInput onSend={onSend} onCancel={onCancel} isProcessing={false} disabled={false} />);
    const textarea = screen.getByRole('textbox');
    fireEvent.change(textarea, { target: { value: 'Hello' } });
    fireEvent.keyDown(textarea, { key: 'Enter', shiftKey: false });
    expect(onSend).toHaveBeenCalledWith('Hello');
  });

  it('does not send on Shift+Enter', () => {
    const onSend = vi.fn();
    render(<ChatInput onSend={onSend} onCancel={vi.fn()} isProcessing={false} disabled={false} />);
    const textarea = screen.getByRole('textbox');
    fireEvent.change(textarea, { target: { value: 'Hello' } });
    fireEvent.keyDown(textarea, { key: 'Enter', shiftKey: true });
    expect(onSend).not.toHaveBeenCalled();
  });

  it('shows cancel button when processing', () => {
    render(<ChatInput onSend={vi.fn()} onCancel={vi.fn()} isProcessing={true} disabled={false} />);
    expect(screen.getByTitle('Cancel')).toBeDefined();
  });

  it('disables send when empty', () => {
    render(<ChatInput onSend={vi.fn()} onCancel={vi.fn()} isProcessing={false} disabled={false} />);
    const sendBtn = screen.getByTitle('Send');
    expect(sendBtn.hasAttribute('disabled')).toBe(true);
  });
});

describe('ChatHeader', () => {
  it('renders title and conversation count', () => {
    render(<ChatHeader title="ATO Copilot" onClose={vi.fn()} onNewConversation={vi.fn()} conversationCount={3} />);
    expect(screen.getByText('ATO Copilot')).toBeDefined();
    expect(screen.getByText('3')).toBeDefined();
  });

  it('calls onClose on close button click', () => {
    const onClose = vi.fn();
    render(<ChatHeader title="ATO Copilot" onClose={onClose} onNewConversation={vi.fn()} conversationCount={0} />);
    fireEvent.click(screen.getByTitle('Close'));
    expect(onClose).toHaveBeenCalledOnce();
  });

  it('calls onNewConversation on new button click', () => {
    const onNew = vi.fn();
    render(<ChatHeader title="ATO Copilot" onClose={vi.fn()} onNewConversation={onNew} conversationCount={0} />);
    fireEvent.click(screen.getByTitle('New conversation'));
    expect(onNew).toHaveBeenCalledOnce();
  });
});

describe('ProgressSteps', () => {
  const steps: SseProgressEvent[] = [
    { step: 'Routing', detail: 'Selecting agent', timestamp: new Date().toISOString() },
    { step: 'Processing', detail: 'Running query', timestamp: new Date().toISOString() },
  ];

  it('renders all steps', () => {
    render(<ProgressSteps steps={steps} />);
    expect(screen.getByText('Routing')).toBeDefined();
    expect(screen.getByText('Processing')).toBeDefined();
  });

  it('shows spinner for latest step', () => {
    const { container } = render(<ProgressSteps steps={steps} />);
    const spinners = container.querySelectorAll('.animate-spin');
    expect(spinners.length).toBe(1);
  });

  it('shows check for completed steps', () => {
    const { container } = render(<ProgressSteps steps={steps} />);
    const checks = container.querySelectorAll('.text-green-500');
    expect(checks.length).toBe(1);
  });

  it('renders nothing for empty steps', () => {
    const { container } = render(<ProgressSteps steps={[]} />);
    expect(container.innerHTML).toBe('');
  });
});

describe('ErrorMessage', () => {
  const error: ErrorDetail = {
    errorCode: 'E001',
    message: 'Something went wrong',
    suggestion: 'Try again later',
  };

  it('renders error message and suggestion', () => {
    render(<ErrorMessage error={error} />);
    expect(screen.getByText('Something went wrong')).toBeDefined();
    expect(screen.getByText('Try again later')).toBeDefined();
    expect(screen.getByText('Code: E001')).toBeDefined();
  });

  it('renders retry button when onRetry provided', () => {
    const onRetry = vi.fn();
    render(<ErrorMessage error={error} onRetry={onRetry} />);
    const retryBtn = screen.getByText('Retry');
    fireEvent.click(retryBtn);
    expect(onRetry).toHaveBeenCalledOnce();
  });

  it('does not render retry button when onRetry not provided', () => {
    render(<ErrorMessage error={error} />);
    expect(screen.queryByText('Retry')).toBeNull();
  });
});
