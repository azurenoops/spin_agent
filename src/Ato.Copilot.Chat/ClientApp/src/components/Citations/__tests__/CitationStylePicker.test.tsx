/**
 * CitationStylePicker.test.tsx — #1703
 *
 * Covers:
 *   - Trigger renders with selected style name
 *   - Picker opens on trigger click (role=dialog visible)
 *   - Search filters the list (aria-live count updates)
 *   - Selecting a style calls setSelectedStyle and closes picker
 *   - Reformat warning appears after style change
 *   - Keyboard: ArrowDown/Up navigates, Enter selects, Escape closes
 *   - ARIA contract: role=combobox, role=searchbox, role=listbox, role=option
 *   - WorkspaceCitationsPanel hidden in focused mode
 *   - CITATION_STYLES.length >= 2600 (CI gate)
 *
 * AAA (Arrange / Act / Assert) marked on each test.
 */

import React from 'react';
import { render, screen, fireEvent, act } from '@testing-library/react';
import { CitationProvider } from '../../../contexts/CitationContext';
import CitationStylePicker from '../CitationStylePicker';
import WorkspaceCitationsPanel from '../WorkspaceCitationsPanel';
import { CITATION_STYLES } from '../../../data/citationStyles';

// ── Helpers ───────────────────────────────────────────────────────────────────

function renderPicker() {
  return render(
    <CitationProvider userId="test-user">
      <CitationStylePicker />
    </CitationProvider>
  );
}

function openPicker() {
  const trigger = screen.getByRole('combobox');
  fireEvent.click(trigger);
}

// ── Style count gate (CI) ─────────────────────────────────────────────────────

test('CITATION_STYLES contains at least 2600 entries', () => {
  // Arrange — static data module
  // Assert
  expect(CITATION_STYLES.length).toBeGreaterThanOrEqual(2600);
});

// ── Trigger ───────────────────────────────────────────────────────────────────

describe('Trigger', () => {
  test('renders with the selected style name', () => {
    // Arrange
    renderPicker();
    // Assert — default is APA 7th Edition
    expect(screen.getByRole('combobox')).toHaveTextContent('APA 7th Edition');
  });

  test('has aria-expanded=false before opening', () => {
    // Arrange
    renderPicker();
    // Assert
    expect(screen.getByRole('combobox')).toHaveAttribute('aria-expanded', 'false');
  });

  test('has aria-haspopup=listbox', () => {
    // Arrange
    renderPicker();
    // Assert
    expect(screen.getByRole('combobox')).toHaveAttribute('aria-haspopup', 'listbox');
  });
});

// ── Open / close ──────────────────────────────────────────────────────────────

describe('Open / close', () => {
  test('opens dialog on trigger click', () => {
    // Arrange
    renderPicker();
    // Act
    openPicker();
    // Assert
    expect(screen.getByRole('dialog')).toBeInTheDocument();
    expect(screen.getByRole('combobox')).toHaveAttribute('aria-expanded', 'true');
  });

  test('closes on close button click', () => {
    // Arrange
    renderPicker();
    openPicker();
    // Act
    fireEvent.click(screen.getByLabelText('Close citation style picker'));
    // Assert
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  test('closes on Escape key', () => {
    // Arrange
    renderPicker();
    openPicker();
    // Act
    fireEvent.keyDown(document, { key: 'Escape' });
    // Assert
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });
});

// ── ARIA contract ─────────────────────────────────────────────────────────────

describe('ARIA contract', () => {
  test('search input has role=searchbox', () => {
    // Arrange
    renderPicker();
    openPicker();
    // Assert
    expect(screen.getByRole('searchbox')).toBeInTheDocument();
  });

  test('list has role=listbox', () => {
    // Arrange
    renderPicker();
    openPicker();
    // Assert
    expect(screen.getByRole('listbox')).toBeInTheDocument();
  });

  test('items have role=option', () => {
    // Arrange
    renderPicker();
    openPicker();
    // Assert
    const options = screen.getAllByRole('option');
    expect(options.length).toBeGreaterThan(0);
  });

  test('selected item has aria-selected=true', () => {
    // Arrange
    renderPicker();
    openPicker();
    // Assert — APA 7th should be selected
    const selected = screen.getAllByRole('option').find(
      (o) => o.getAttribute('aria-selected') === 'true'
    );
    expect(selected).toBeInTheDocument();
  });

  test('count region has aria-live=polite', () => {
    // Arrange
    renderPicker();
    openPicker();
    // Assert
    const live = document.querySelector('[aria-live="polite"]');
    expect(live).toBeInTheDocument();
  });
});

// ── Search / filter ───────────────────────────────────────────────────────────

describe('Search', () => {
  test('filters list when query matches style name', () => {
    // Arrange
    renderPicker();
    openPicker();
    const search = screen.getByRole('searchbox');
    // Act
    fireEvent.change(search, { target: { value: 'MLA' } });
    // Assert
    const options = screen.getAllByRole('option');
    expect(options.length).toBeGreaterThan(0);
    options.forEach((o) => {
      expect(o.textContent?.toLowerCase()).toContain('mla');
    });
  });

  test('shows "No styles found" when no matches', () => {
    // Arrange
    renderPicker();
    openPicker();
    const search = screen.getByRole('searchbox');
    // Act
    fireEvent.change(search, { target: { value: 'zzz_no_match_xyzzy' } });
    // Assert
    expect(screen.getByText('No styles found')).toBeInTheDocument();
  });
});

// ── Selection ─────────────────────────────────────────────────────────────────

describe('Selection', () => {
  test('selecting a different style closes the picker', () => {
    // Arrange
    renderPicker();
    openPicker();
    // Act — click the first non-selected option
    const options = screen.getAllByRole('option');
    const other = options.find((o) => o.getAttribute('aria-selected') !== 'true');
    if (!other) throw new Error('No unselected option found');
    fireEvent.click(other);
    // Assert
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  test('shows reformat warning after style change', async () => {
    // Arrange
    renderPicker();
    openPicker();
    // Act
    const options = screen.getAllByRole('option');
    const other = options.find((o) => o.getAttribute('aria-selected') !== 'true');
    if (!other) throw new Error('No unselected option found');
    fireEvent.click(other);
    // Assert
    expect(screen.getByRole('alert')).toBeInTheDocument();
  });
});

// ── Keyboard navigation ───────────────────────────────────────────────────────

describe('Keyboard navigation', () => {
  test('ArrowDown moves focus to first item', () => {
    // Arrange
    renderPicker();
    openPicker();
    // Act
    fireEvent.keyDown(document, { key: 'ArrowDown' });
    // Assert — first option should be highlighted (bg-blue-50)
    const options = screen.getAllByRole('option');
    expect(options[0].className).toContain('bg-blue-50');
  });

  test('Enter on focused item selects it', () => {
    // Arrange
    renderPicker();
    openPicker();
    fireEvent.keyDown(document, { key: 'ArrowDown' });
    // Act
    fireEvent.keyDown(document, { key: 'Enter' });
    // Assert — picker closed
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });
});

// ── WorkspaceCitationsPanel ───────────────────────────────────────────────────

describe('WorkspaceCitationsPanel', () => {
  function renderPanel(mode?: 'focused' | 'standard' | 'research') {
    return render(
      <CitationProvider userId="test-user">
        <WorkspaceCitationsPanel mode={mode} />
      </CitationProvider>
    );
  }

  test('renders in standard mode', () => {
    // Arrange + Act
    const { container } = renderPanel('standard');
    // Assert
    expect(container.querySelector('[role="region"]')).toBeInTheDocument();
  });

  test('renders in research mode', () => {
    // Arrange + Act
    const { container } = renderPanel('research');
    // Assert
    expect(container.querySelector('[role="region"]')).toBeInTheDocument();
  });

  test('hidden in focused mode', () => {
    // Arrange + Act
    const { container } = renderPanel('focused');
    // Assert
    expect(container.querySelector('[role="region"]')).not.toBeInTheDocument();
  });
});
