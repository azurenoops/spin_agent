export interface ChatToggleProps {
  isOpen: boolean;
  onClick: () => void;
}

export default function ChatToggle({ isOpen, onClick }: ChatToggleProps) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={`rounded-lg p-2 hover:bg-gray-100 hover:text-gray-700 transition-colors ${
        isOpen ? 'bg-blue-50 text-blue-600' : 'text-gray-500'
      }`}
      aria-label="Chat (Ctrl+Shift+C)"
      title="Chat (Ctrl+Shift+C)"
      aria-expanded={isOpen}
    >
      <svg className="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
        <path
          strokeLinecap="round"
          strokeLinejoin="round"
          d="M7.5 8.25h9m-9 3H12m-9.75 1.51c0 1.6 1.123 2.994 2.707 3.227 1.087.16 2.185.283 3.293.369V21l4.076-4.076a1.526 1.526 0 011.037-.443 48.282 48.282 0 005.68-.494c1.584-.233 2.707-1.626 2.707-3.228V6.741c0-1.602-1.123-2.995-2.707-3.228A48.394 48.394 0 0012 3c-2.392 0-4.744.175-7.043.513C3.373 3.746 2.25 5.14 2.25 6.741v6.018z"
        />
      </svg>
      {isOpen && (
        <span className="absolute -top-0.5 -right-0.5 h-2 w-2 rounded-full bg-blue-500" />
      )}
    </button>
  );
}
