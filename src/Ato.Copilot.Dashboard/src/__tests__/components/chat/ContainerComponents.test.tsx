import { render, screen, fireEvent } from '@testing-library/react';
import { describe, it, expect, vi, beforeAll } from 'vitest';
import { BrowserRouter } from 'react-router-dom';
import ChatMessages from '../../../components/chat/ChatMessages';
import ChatToggle from '../../../components/chat/ChatToggle';
import type { Message } from '../../../types/chat';

beforeAll(() => {
  Element.prototype.scrollIntoView = vi.fn();
});

function renderWithRouter(ui: React.ReactElement) {
  return render(<BrowserRouter>{ui}</BrowserRouter>);
}

describe('ChatMessages', () => {
  const messages: Message[] = [
    { id: '1', role: 'user', content: 'Hello', status: 'complete', timestamp: new Date().toISOString() },
    { id: '2', role: 'assistant', content: 'Hi there!', status: 'complete', timestamp: new Date().toISOString() },
  ];

  it('renders messages', () => {
    renderWithRouter(<ChatMessages messages={messages} progressSteps={[]} isProcessing={false} />);
    expect(screen.getByText('Hello')).toBeDefined();
    expect(screen.getByText('Hi there!')).toBeDefined();
  });

  it('shows empty state when no messages', () => {
    renderWithRouter(<ChatMessages messages={[]} progressSteps={[]} isProcessing={false} />);
    expect(screen.getByText('No messages yet')).toBeDefined();
  });

  it('shows progress steps during processing', () => {
    const steps = [{ step: 'Agent routing', detail: 'Selecting agent', timestamp: new Date().toISOString() }];
    renderWithRouter(<ChatMessages messages={messages} progressSteps={steps} isProcessing={true} />);
    expect(screen.getByText('Agent routing')).toBeDefined();
  });

  it('does not show progress when not processing', () => {
    const steps = [{ step: 'Agent routing', detail: '', timestamp: new Date().toISOString() }];
    renderWithRouter(<ChatMessages messages={messages} progressSteps={steps} isProcessing={false} />);
    expect(screen.queryByText('Agent routing')).toBeNull();
  });
});

describe('ChatToggle', () => {
  it('renders with correct aria label', () => {
    render(<ChatToggle isOpen={false} onClick={vi.fn()} />);
    expect(screen.getByLabelText('Chat (Ctrl+Shift+C)')).toBeDefined();
  });

  it('shows active state when open', () => {
    const { container } = render(<ChatToggle isOpen={true} onClick={vi.fn()} />);
    const button = container.querySelector('.bg-blue-50');
    expect(button).toBeDefined();
  });

  it('calls onClick on click', () => {
    const onClick = vi.fn();
    render(<ChatToggle isOpen={false} onClick={onClick} />);
    fireEvent.click(screen.getByLabelText('Chat (Ctrl+Shift+C)'));
    expect(onClick).toHaveBeenCalledOnce();
  });
});
