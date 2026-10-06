import { useContext, useEffect, useRef, useState } from 'react';
import { UNSAFE_NavigationContext, useLocation, type To } from 'react-router-dom';
import SetupDialog from '../workspace-operations/SetupDialog';
import { registerHistoryGuard } from './navigationGuardRegistry';

/** BrowserRouter does not support useBlocker. Intercept its navigator before unmount. */
export default function UnsavedDesignGuard({ dirty, title = 'Unsaved System design changes',
  description = 'Leaving will discard unsaved graph or presentation changes. Your saved revision and approved baseline are unchanged.', inline = false }: {
  dirty: boolean; title?: string; description?: string; inline?: boolean;
}) {
  const { navigator } = useContext(UNSAFE_NavigationContext);
  const { pathname } = useLocation();
  const [pending, setPending] = useState<null | (() => void)>(null);
  const bypass = useRef(false);
  const returnFocus = useRef<HTMLElement | null>(null);
  useEffect(() => {
    if (!dirty) return;
    const push = navigator.push;
    const replace = navigator.replace;
    const rememberFocus = () => {
      returnFocus.current = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    };
    const samePage = (to: To) => {
      if (typeof to !== 'string') return to.pathname === undefined || to.pathname === pathname;
      return to.startsWith('?') || to.startsWith('#') || to.split(/[?#]/)[0] === pathname;
    };
    navigator.push = (...args) => {
      if (bypass.current || samePage(args[0])) return push.apply(navigator, args);
      rememberFocus();
      setPending(() => () => push.apply(navigator, args));
    };
    navigator.replace = (...args) => {
      if (bypass.current || samePage(args[0])) return replace.apply(navigator, args);
      rememberFocus();
      setPending(() => () => replace.apply(navigator, args));
    };
    const unload = (event: BeforeUnloadEvent) => { event.preventDefault(); event.returnValue = ''; };
    const anchor = (event: MouseEvent) => {
      if (event.defaultPrevented || event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
      const link = (event.target as Element).closest?.('a[href]') as HTMLAnchorElement | null;
      if (!link || link.target === '_blank' || link.hasAttribute('download') || link.href === window.location.href) return;
      const target = new URL(link.href);
      if (target.origin === window.location.origin && target.pathname === pathname) return;
      event.preventDefault();
      event.stopImmediatePropagation();
      returnFocus.current = link;
      setPending(() => () => {
        const url = new URL(link.href);
        if (url.origin === window.location.origin) push.call(navigator, `${url.pathname}${url.search}${url.hash}`);
        else window.location.assign(url.href);
      });
    };
    const currentIndex = window.history.state?.idx as number | undefined;
    let restoring = false;
    const pop = (event: PopStateEvent) => {
      if (bypass.current) return;
      const nextIndex = event.state?.idx as number | undefined;
      if (restoring) { restoring = false; event.stopImmediatePropagation(); return; }
      if (currentIndex === undefined || nextIndex === undefined || nextIndex === currentIndex) return;
      event.stopImmediatePropagation();
      restoring = true;
      rememberFocus();
      window.history.go(currentIndex - nextIndex);
      setPending(() => () => window.history.go(nextIndex - currentIndex));
    };
    window.addEventListener('beforeunload', unload);
    document.addEventListener('click', anchor, true);
    const unregister = registerHistoryGuard(pop);
    return () => {
      navigator.push = push;
      navigator.replace = replace;
      window.removeEventListener('beforeunload', unload);
      document.removeEventListener('click', anchor, true);
      unregister();
    };
  }, [dirty, navigator, pathname]);
  if (!pending) return null;
  if (inline) return <section role="alert" aria-label={title} className="mb-4 space-y-3 rounded border border-amber-300 p-3 text-sm">
    <h3 className="font-semibold">{title}</h3>
    <p>{description}</p>
    <div className="flex flex-wrap gap-3">
      <button type="button" className="sd-button" autoFocus onClick={() => {
        setPending(null);
        if (returnFocus.current?.isConnected) returnFocus.current.focus();
      }}>Keep editing</button>
      <button type="button" className="sd-button" onClick={() => { bypass.current = true; pending(); setPending(null); }}>Discard and leave</button>
    </div>
  </section>;
  return <SetupDialog title={title} description={description}
    busy={false} onClose={() => setPending(null)}>
    <div className="flex flex-wrap justify-end gap-3">
      <button type="button" className="sd-button" onClick={() => setPending(null)}>Keep editing</button>
      <button type="button" className="sd-button sd-primary" onClick={() => { bypass.current = true; pending(); setPending(null); }}>Discard and leave</button>
    </div>
  </SetupDialog>;
}
