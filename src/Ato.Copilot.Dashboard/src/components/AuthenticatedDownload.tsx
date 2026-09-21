import { useEffect, useRef, useState, type ButtonHTMLAttributes } from 'react';
import { downloadAuthenticatedFile } from '../api/downloads';
import { workspaceErrorMessage } from '../features/workspaces/api';

interface Props extends Omit<ButtonHTMLAttributes<HTMLButtonElement>, 'onClick'> {
  url: string;
  fileName?: string;
}

export default function AuthenticatedDownload(props: Props) {
  return <DownloadButton key={props.url} {...props} />;
}

function DownloadButton({ url, fileName, children, disabled, ...props }: Props) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const request = useRef<AbortController | null>(null);
  useEffect(() => () => { request.current?.abort(); request.current = null; }, [url]);

  const download = async () => {
    const controller = new AbortController();
    request.current?.abort();
    request.current = controller;
    setBusy(true);
    setError(null);
    try {
      await downloadAuthenticatedFile(url, fileName, controller.signal);
    } catch (reason) {
      if (!controller.signal.aborted) setError(workspaceErrorMessage(reason));
    } finally {
      if (request.current === controller) {
        request.current = null;
        setBusy(false);
      }
    }
  };

  return (
    <>
      <button {...props} type="button" disabled={disabled || busy} aria-busy={busy} onClick={() => void download()}>
        {children}
      </button>
      {error && <span role="alert" className="mt-1 block text-xs text-red-700">{error}</span>}
    </>
  );
}
