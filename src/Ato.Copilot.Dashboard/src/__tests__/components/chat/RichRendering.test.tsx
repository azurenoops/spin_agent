import { render, screen, fireEvent } from '@testing-library/react';
import { describe, it, expect, vi } from 'vitest';
import ToolEvidence from '../../../components/chat/ToolEvidence';
import SuggestionCards from '../../../components/chat/SuggestionCards';
import type { ToolExecution, SuggestedAction } from '../../../types/chat';

describe('ToolEvidence', () => {
  const tools: ToolExecution[] = [
    { toolName: 'GetCompliance', success: true, executionTimeMs: 150, result: '{"status": "ok"}' },
    { toolName: 'CheckSTIG', success: false, executionTimeMs: 300 },
  ];

  it('renders tool count', () => {
    render(<ToolEvidence tools={tools} />);
    expect(screen.getByText('2 tools executed')).toBeDefined();
  });

  it('expands to show tools on click', () => {
    render(<ToolEvidence tools={tools} />);
    fireEvent.click(screen.getByText('2 tools executed'));
    expect(screen.getByText('GetCompliance')).toBeDefined();
    expect(screen.getByText('CheckSTIG')).toBeDefined();
    expect(screen.getByText('150ms')).toBeDefined();
    expect(screen.getByText('300ms')).toBeDefined();
  });

  it('shows success/failure icons', () => {
    const { container } = render(<ToolEvidence tools={tools} />);
    fireEvent.click(screen.getByText('2 tools executed'));
    const greenIcons = container.querySelectorAll('.text-green-500');
    const redIcons = container.querySelectorAll('.text-red-500');
    expect(greenIcons.length).toBe(1);
    expect(redIcons.length).toBe(1);
  });

  it('renders nothing for empty tools', () => {
    const { container } = render(<ToolEvidence tools={[]} />);
    expect(container.innerHTML).toBe('');
  });
});

describe('SuggestionCards', () => {
  const suggestions: SuggestedAction[] = [
    { label: 'Show details', prompt: 'Show compliance details' },
    { label: 'Export report', prompt: 'Export compliance report', icon: '📄' },
  ];

  it('renders suggestion chips', () => {
    render(<SuggestionCards suggestions={suggestions} onSelect={vi.fn()} disabled={false} />);
    expect(screen.getByText('Show details')).toBeDefined();
    expect(screen.getByText('Export report')).toBeDefined();
  });

  it('calls onSelect with prompt on click', () => {
    const onSelect = vi.fn();
    render(<SuggestionCards suggestions={suggestions} onSelect={onSelect} disabled={false} />);
    fireEvent.click(screen.getByText('Show details'));
    expect(onSelect).toHaveBeenCalledWith('Show compliance details');
  });

  it('disables buttons when disabled', () => {
    render(<SuggestionCards suggestions={suggestions} onSelect={vi.fn()} disabled={true} />);
    const buttons = screen.getAllByRole('button');
    buttons.forEach((btn) => {
      expect(btn.hasAttribute('disabled')).toBe(true);
    });
  });

  it('renders nothing for empty suggestions', () => {
    const { container } = render(<SuggestionCards suggestions={[]} onSelect={vi.fn()} disabled={false} />);
    expect(container.innerHTML).toBe('');
  });
});
