interface Props {
  monitor: { transportMessage: string | null; error: string | null; retry: () => void };
  onClose?: () => void;
}

export default function ProgressTransportNotice({ monitor, onClose }: Props) {
  return (
    <>
      {monitor.transportMessage && <p role="status" className="text-xs text-gray-600">{monitor.transportMessage}</p>}
      {monitor.error && (
        <div role="alert" className="rounded-md border border-red-200 bg-red-50 p-3 text-sm text-red-700">
          <p>{monitor.error}</p>
          <button type="button" onClick={monitor.retry} className="mt-2 underline">Retry progress</button>
          {onClose && <button type="button" onClick={onClose} className="ml-3 underline">Close progress</button>}
        </div>
      )}
    </>
  );
}
