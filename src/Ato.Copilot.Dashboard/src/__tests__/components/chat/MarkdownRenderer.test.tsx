import { render, screen } from '@testing-library/react';
import { describe, it, expect } from 'vitest';
import MarkdownRenderer from '../../../components/chat/MarkdownRenderer';

describe('MarkdownRenderer', () => {
  it('renders headings', () => {
    render(<MarkdownRenderer content={'# Hello\n## World'} />);
    expect(screen.getByText('Hello')).toBeDefined();
    expect(screen.getByText('World')).toBeDefined();
  });

  it('renders code blocks with language', () => {
    render(<MarkdownRenderer content={'```json\n{"key": "value"}\n```'} />);
    expect(screen.getByText(/"key"/)).toBeDefined();
  });

  it('renders inline code', () => {
    render(<MarkdownRenderer content="Use `npm install` to install" />);
    expect(screen.getByText('npm install')).toBeDefined();
  });

  it('renders tables with GFM', () => {
    const md = '| Name | Value |\n|------|-------|\n| A | 1 |';
    render(<MarkdownRenderer content={md} />);
    expect(screen.getByText('Name')).toBeDefined();
    expect(screen.getByText('Value')).toBeDefined();
  });

  it('renders links', () => {
    render(<MarkdownRenderer content="[Click here](https://example.com)" />);
    const link = screen.getByText('Click here');
    expect(link).toBeDefined();
    expect(link.closest('a')?.getAttribute('href')).toBe('https://example.com');
  });

  it('renders lists', () => {
    render(<MarkdownRenderer content={'- Item 1\n- Item 2\n- Item 3'} />);
    expect(screen.getByText('Item 1')).toBeDefined();
    expect(screen.getByText('Item 2')).toBeDefined();
    expect(screen.getByText('Item 3')).toBeDefined();
  });
});
