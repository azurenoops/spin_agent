interface AsyncErrorStateProps {
  title: string;
  onRetry: () => void;
  message?: string;
}

export default function AsyncErrorState({
  title,
  onRetry,
  message = 'Check your connection and try again.',
}: AsyncErrorStateProps) {
  return (
    <div role="alert" className="rounded-lg border border-red-200 bg-red-50 p-8 text-center">
      <p className="text-sm font-medium text-red-800">{title}</p>
      <p className="mt-1 text-sm text-red-700">{message}</p>
      <button
        type="button"
        onClick={onRetry}
        className="mt-3 text-sm font-medium text-red-700 underline hover:text-red-900"
      >
        Retry
      </button>
    </div>
  );
}