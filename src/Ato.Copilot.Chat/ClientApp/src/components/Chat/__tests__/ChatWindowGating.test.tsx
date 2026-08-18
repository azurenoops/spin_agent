/**
 * ChatWindowGating.test.tsx
 *
 * Tests for GATE-2437 feature-flag gating, aria-live region, toolbar toggle
 * button, and first-run nudge lifecycle.
 *
 * Strategy:
 * - Mock react-markdown (ESM, can't be CJS-transformed by Jest) and its deps.
 * - Mock useChatContext so ChatWindowInner renders without a real ChatProvider
 *   (which requires a live SignalR hub connection).
 * - Test TraceabilityNudge in isolation (no context needed).
 *
 * AAA (Arrange / Act / Assert) is marked on each test.
 */

import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';

// ── Mock ESM deps that Jest can't transform ──────────────────────────────────
jest.mock('react-markdown', () => ({
  __esModule: true,
  default: ({ children }: { children: React.ReactNode }) => <>{children}</>,
}));
jest.mock('remark-gfm', () => ({ __esModule: true, default: () => {} }));
jest.mock('react-syntax-highlighter', () => ({
  __esModule: true,
  Prism: ({ children }: { children: React.ReactNode }) => <pre>{children}</pre>,
}));
jest.mock('react-syntax-highlighter/dist/esm/styles/prism', () => ({
  __esModule: true,
  oneDark: {},
}));

// ── Mock ChatContext so ChatWindowInner doesn't need a live SignalR hub ──────
jest.mock('../../../contexts/ChatContext', () => {
  const ConnectionStatus = { Connected: 'Connected', Disconnected: 'Disconnected', Reconnecting: 'Reconnecting' };
  const MessageRole = { User: 'user', Assistant: 'assistant', System: 'system' };
  const mockState = {
    messages: [],
    conversations: [],
    activeConversationId: 'test-conv-1', // non-null so the chat UI (not the fallback) renders
    isProcessing: false,
    error: null,
    connectionStatus: ConnectionStatus.Disconnected,
    attachments: [],
    searchResults: [],
    isSearching: false,
  };
  return {
    __esModule: true,
    ChatProvider: ({ children }: { children: React.ReactNode }) => <>{children}</>,
    useChatContext: () => ({
      state: mockState,
      dispatch: jest.fn(),
      sendMessage: jest.fn(),
      loadConversations: jest.fn(),
      selectConversation: jest.fn(),
      createConversation: jest.fn(),
      deleteConversation: jest.fn(),
      searchConversations: jest.fn(),
    }),
    ConnectionStatus,
    MessageRole,
  };
});

// ── Mock featureFlags (value mutated per-test group) ─────────────────────────
const featureFlagsMock = { isTraceabilityPanelEnabled: false };
jest.mock('../../../lib/featureFlags', () => featureFlagsMock);

// ── Silence console.error noise from ReactMarkdown type warnings ─────────────
beforeAll(() => jest.spyOn(console, 'error').mockImplementation(() => {}));
afterAll(() => (console.error as jest.Mock).mockRestore?.());

// Import after all mocks are registered.
import { ChatWindowInner, TraceabilityNudge } from '../../ChatWindow';

// ─────────────────────────────────────────────────────────────────────────────
// Group 1 – feature flag OFF
// ─────────────────────────────────────────────────────────────────────────────
describe('ChatWindowInner – feature flag OFF', () => {
  beforeEach(() => { featureFlagsMock.isTraceabilityPanelEnabled = false; });

  it('does NOT render the toolbar toggle button', () => {
    // Arrange / Act
    render(<ChatWindowInner />);
    // Assert
    expect(screen.queryByTestId('traceability-toggle')).toBeNull();
  });

  it('does NOT render the traceability nudge', () => {
    // Arrange / Act
    render(<ChatWindowInner />);
    // Assert
    expect(screen.queryByTestId('traceability-nudge')).toBeNull();
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// Group 2 – feature flag ON
// ─────────────────────────────────────────────────────────────────────────────
describe('ChatWindowInner – feature flag ON', () => {
  beforeEach(() => {
    featureFlagsMock.isTraceabilityPanelEnabled = true;
    localStorage.clear();
  });

  it('renders the toolbar toggle button', () => {
    // Arrange / Act
    render(<ChatWindowInner />);
    // Assert
    expect(screen.getByTestId('traceability-toggle')).toBeInTheDocument();
  });

  it('renders the aria-live panel-live-region with correct attributes', () => {
    // Arrange / Act
    render(<ChatWindowInner />);
    // Assert
    const region = screen.getByTestId('panel-live-region');
    expect(region).toHaveAttribute('aria-live', 'polite');
    expect(region).toHaveAttribute('aria-atomic', 'true');
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// Group 3 – TraceabilityNudge component in isolation
// ─────────────────────────────────────────────────────────────────────────────
describe('TraceabilityNudge', () => {
  it('renders with "View trace" link and dismiss button', () => {
    // Arrange
    const onOpen = jest.fn();
    const onDismiss = jest.fn();
    // Act
    render(<TraceabilityNudge onOpen={onOpen} onDismiss={onDismiss} />);
    // Assert
    expect(screen.getByTestId('traceability-nudge')).toBeInTheDocument();
    expect(screen.getByText(/View trace/)).toBeInTheDocument();
    expect(screen.getByLabelText('Dismiss source traceability nudge')).toBeInTheDocument();
  });

  it('calls onOpen when "View trace →" is clicked', () => {
    // Arrange
    const onOpen = jest.fn();
    render(<TraceabilityNudge onOpen={onOpen} onDismiss={jest.fn()} />);
    // Act
    fireEvent.click(screen.getByText(/View trace/));
    // Assert
    expect(onOpen).toHaveBeenCalledTimes(1);
  });

  it('calls onDismiss when the dismiss button is clicked', () => {
    // Arrange
    const onDismiss = jest.fn();
    render(<TraceabilityNudge onOpen={jest.fn()} onDismiss={onDismiss} />);
    // Act
    fireEvent.click(screen.getByLabelText('Dismiss source traceability nudge'));
    // Assert
    expect(onDismiss).toHaveBeenCalledTimes(1);
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// Group 4 – Nudge localStorage persistence
// ─────────────────────────────────────────────────────────────────────────────
describe('Nudge localStorage persistence', () => {
  const NUDGE_KEY = 'tp_nudge_dismissed';

  beforeEach(() => {
    featureFlagsMock.isTraceabilityPanelEnabled = true;
    localStorage.clear();
  });

  it('nudge is absent when localStorage key is pre-set to "true"', () => {
    // Arrange
    localStorage.setItem(NUDGE_KEY, 'true');
    // Act
    render(<ChatWindowInner />);
    // Assert — nudge must not show even with flag on, if already dismissed
    expect(screen.queryByTestId('traceability-nudge')).toBeNull();
  });
});
