import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { NliVerdictBadge } from '../NliVerdictBadge';
import { VerdictClass } from '../../../lib/verdict-store';

describe('NliVerdictBadge', () => {
  const defaultProps = {
    verdict: 'SUPPORTED' as VerdictClass,
    label: 'Supported',
    resultId: 'result-1',
    onOpen: jest.fn(),
  };

  beforeEach(() => jest.clearAllMocks());

  it('renders as a button', () => {
    render(<NliVerdictBadge {...defaultProps} />);
    expect(screen.getByRole('button')).toBeInTheDocument();
  });

  it('calls onOpen with resultId when clicked', () => {
    const onOpen = jest.fn();
    render(<NliVerdictBadge {...defaultProps} onOpen={onOpen} />);
    fireEvent.click(screen.getByRole('button'));
    expect(onOpen).toHaveBeenCalledWith('result-1');
  });

  it('calls onOpen when Enter is pressed', () => {
    const onOpen = jest.fn();
    render(<NliVerdictBadge {...defaultProps} onOpen={onOpen} />);
    fireEvent.keyDown(screen.getByRole('button'), { key: 'Enter' });
    expect(onOpen).toHaveBeenCalledWith('result-1');
  });

  it('calls onOpen when Space is pressed', () => {
    const onOpen = jest.fn();
    render(<NliVerdictBadge {...defaultProps} onOpen={onOpen} />);
    fireEvent.keyDown(screen.getByRole('button'), { key: ' ' });
    expect(onOpen).toHaveBeenCalledWith('result-1');
  });

  it('has tabIndex 0 for keyboard accessibility', () => {
    render(<NliVerdictBadge {...defaultProps} />);
    expect(screen.getByRole('button')).toHaveAttribute('tabindex', '0');
  });

  it('uses label as aria-label', () => {
    render(<NliVerdictBadge {...defaultProps} label="Claim supported" />);
    expect(screen.getByRole('button')).toHaveAttribute('aria-label', 'Claim supported');
  });
});
