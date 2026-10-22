import { useEffect, useRef, useState } from 'react';
import {
  confirmResponsibilityDraft, getResponsibilityDraft, plainResponsibilityValues, prepareResponsibilityDraft,
  ResponsibilityDraftError, responsibilityFields, saveResponsibilityDraft,
  type ResponsibilityDraftContext, type ResponsibilityField, type ResponsibilityValue, type ResponsibilityValues,
} from '../api/responsibilityDrafts';

export function useResponsibilityFirstPass(systemId: string, controlId: string, enabled: boolean,
  values: ResponsibilityValues, hydrate: (values: Record<ResponsibilityField, ResponsibilityValue>, force: boolean) => void) {
  const [context, setContext] = useState<ResponsibilityDraftContext | null>(null);
  const [scopeId, setScopeId] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const [generation, setGeneration] = useState(0);
  const callbacks = useRef({ hydrate, values });
  callbacks.current = { hydrate, values };
  const controller = useRef<AbortController | null>(null);
  const pending = useRef(false);
  useEffect(() => {
    if (!enabled) return;
    const read = new AbortController();
    setLoading(true); setError(''); setContext(null);
    getResponsibilityDraft(systemId, controlId, scopeId, read.signal).then(next => {
      if (read.signal.aborted) return;
      setContext(next);
      callbacks.current.hydrate(next.draft?.values ?? next.sourceValues, false);
    }).catch(reason => {
      if (!read.signal.aborted) setError(reason instanceof Error ? reason.message : 'Source context could not be loaded.');
    }).finally(() => { if (!read.signal.aborted) setLoading(false); });
    return () => read.abort();
  }, [systemId, controlId, scopeId, enabled, generation]);
  useEffect(() => () => controller.current?.abort(), []);
  const saved = context?.draft ?? null;
  const matchesSaved = !!saved && responsibilityFields.every(key => saved.values[key].value === values[key].trim());
  async function perform(operation: (signal: AbortSignal) => Promise<void>) {
    if (pending.current) return false;
    pending.current = true; setBusy(true); setError('');
    const write = new AbortController(); controller.current = write;
    try {
      await operation(write.signal);
      return !write.signal.aborted;
    } catch (reason) {
      if (write.signal.aborted) return false;
      if (reason instanceof ResponsibilityDraftError && reason.context) {
        setContext(reason.context);
        callbacks.current.hydrate(reason.context.draft?.values ?? reason.context.sourceValues, false);
      }
      setError(reason instanceof Error ? reason.message : 'The draft operation failed. Your edits are preserved.');
      return false;
    } finally {
      pending.current = false;
      if (!write.signal.aborted) setBusy(false);
    }
  }
  const prepare = (generate = true) => perform(async signal => {
    const next = await prepareResponsibilityDraft(systemId, controlId, scopeId, saved?.revision ?? 0, generate, signal);
    if (signal.aborted) return;
    setContext(next);
    if (!saved) callbacks.current.hydrate(next.draft!.values, false);
  });
  const save = (applySuggestion = false, useSuggestion = false) => perform(async signal => {
    const edited = useSuggestion && saved ? plainResponsibilityValues(saved.suggestion.values) : callbacks.current.values;
    const current = saved ? context! : await prepareResponsibilityDraft(systemId, controlId, scopeId, 0, false, signal);
    const next = await saveResponsibilityDraft(systemId, current.draft!.id, current.draft!.revision, edited, applySuggestion, signal);
    if (signal.aborted) return;
    setContext({ ...current, draft: next });
    if (useSuggestion) callbacks.current.hydrate(next.values, true);
  });
  const confirm = (notes: string) => perform(async signal => {
    if (!saved || !matchesSaved || saved.isStale || saved.status !== 'Proposed')
      throw new Error('Save and review the current draft against current sources before confirming.');
    const accepted = await confirmResponsibilityDraft(systemId, saved, notes, signal);
    if (!signal.aborted) setContext(previous => previous && { ...previous, draft: accepted });
  });
  return { context, scopeId, setScopeId, busy, loading, error, prepare, save, confirm, saved, matchesSaved, values,
    reload: () => setGeneration(value => value + 1),
    canConfirm: !!context?.canPrepare && matchesSaved && !saved?.isStale && saved?.status === 'Proposed' && !busy };
}
