import { render, screen, fireEvent } from '@testing-library/react';
import { describe, it, expect, vi } from 'vitest';
import ConversationList from '../../../components/chat/ConversationList';
import WelcomeMessage from '../../../components/chat/WelcomeMessage';
import type { Conversation } from '../../../types/chat';

describe('ConversationList', () => {
  const conversations: Conversation[] = [
    { id: '1', title: 'First conversation', messages: [], createdAt: new Date().toISOString(), updatedAt: new Date().toISOString() },
    { id: '2', title: 'Second conversation', messages: [], createdAt: new Date().toISOString(), updatedAt: new Date(Date.now() - 86400000).toISOString() },
  ];

  it('renders conversation titles', () => {
    render(<ConversationList conversations={conversations} activeId={null} onSelect={vi.fn()} onDelete={vi.fn()} onNew={vi.fn()} />);
    expect(screen.getByText('First conversation')).toBeDefined();
    expect(screen.getByText('Second conversation')).toBeDefined();
  });

  it('shows relative timestamps', () => {
    render(<ConversationList conversations={conversations} activeId={null} onSelect={vi.fn()} onDelete={vi.fn()} onNew={vi.fn()} />);
    expect(screen.getByText('Today')).toBeDefined();
    expect(screen.getByText('Yesterday')).toBeDefined();
  });

  it('highlights active conversation', () => {
    const { container } = render(<ConversationList conversations={conversations} activeId="1" onSelect={vi.fn()} onDelete={vi.fn()} onNew={vi.fn()} />);
    const activeButton = container.querySelector('.bg-indigo-50');
    expect(activeButton).toBeDefined();
  });

  it('calls onSelect when clicking a conversation', () => {
    const onSelect = vi.fn();
    render(<ConversationList conversations={conversations} activeId={null} onSelect={onSelect} onDelete={vi.fn()} onNew={vi.fn()} />);
    fireEvent.click(screen.getByText('First conversation'));
    expect(onSelect).toHaveBeenCalledWith('1');
  });

  it('calls onDelete when clicking delete', () => {
    const onDelete = vi.fn();
    render(<ConversationList conversations={conversations} activeId={null} onSelect={vi.fn()} onDelete={onDelete} onNew={vi.fn()} />);
    const deleteButtons = screen.getAllByTitle('Delete');
    fireEvent.click(deleteButtons[0]!);
    expect(onDelete).toHaveBeenCalledWith('1');
  });
});

describe('WelcomeMessage', () => {
  it('renders branding and example questions', () => {
    render(<WelcomeMessage onSendExample={vi.fn()} />);
    expect(screen.getByText('ATO Copilot')).toBeDefined();
    // Portfolio/no-system context shows generic suggestions
    expect(screen.getByText('Portfolio overview')).toBeDefined();
  });

  it('sends example question on click', () => {
    const onSend = vi.fn();
    render(<WelcomeMessage onSendExample={onSend} />);
    const btn = screen.getByText('Portfolio overview').closest('button')!;
    fireEvent.click(btn);
    expect(onSend).toHaveBeenCalledWith('Show me the portfolio compliance overview and systems that need attention');
  });

  it('renders phase-aware suggestions for system detail', () => {
    render(<WelcomeMessage context={{ page: 'system-detail', systemId: '123', rmfPhase: 'Assess' }} onSendExample={vi.fn()} />);
    // Should show phase badge
    expect(screen.getByText('Assess')).toBeDefined();
    // Should show "Recommended next steps" heading
    expect(screen.getByText('Recommended next steps')).toBeDefined();
  });
});
