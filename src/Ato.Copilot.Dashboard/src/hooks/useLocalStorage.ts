import { useState, useCallback, useRef, useEffect } from 'react';

function readValue<T>(key: string | null, initialValue: T): T {
  if (key === null) return initialValue;
  try {
    const item = localStorage.getItem(key);
    return item ? JSON.parse(item) as T : initialValue;
  } catch {
    console.warn('Browser storage could not be read; using the initial value.');
    return initialValue;
  }
}

/** Null keys deliberately keep state in memory without reading or writing storage. */
export function useLocalStorage<T>(key: string | null, initialValue: T): [T, (value: T | ((prev: T) => T)) => void] {
  const [stored, setStored] = useState(() => ({ key, value: readValue(key, initialValue) }));
  const currentKey = useRef(key);
  const initial = useRef(initialValue);
  currentKey.current = key;
  initial.current = initialValue;
  const pending = useRef<{ key: string; value: T } | null>(null);
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null);

  const flush = useCallback(() => {
    if (timer.current !== null) clearTimeout(timer.current);
    timer.current = null;
    const entry = pending.current;
    pending.current = null;
    if (!entry) return;
    try {
      localStorage.setItem(entry.key, JSON.stringify(entry.value));
    } catch {
      console.warn('Browser storage write failed; changes are available only in this view.');
    }
  }, []);

  useEffect(() => {
    if (stored.key !== key) {
      setStored(previous => previous.key === key ? previous : { key, value: readValue(key, initial.current) });
    }
  }, [key, stored.key]);

  useEffect(() => () => flush(), [key, flush]);

  const setValue = useCallback((value: T | ((previous: T) => T)) => {
    if (currentKey.current !== key) return;
    if (pending.current && pending.current.key !== key) flush();
    setStored(previous => {
      if (currentKey.current !== key) return previous;
      const prior = previous.key === key ? previous.value : readValue(key, initial.current);
      const next = value instanceof Function ? value(prior) : value;
      if (key !== null) {
        if (timer.current !== null) clearTimeout(timer.current);
        pending.current = { key, value: next };
        timer.current = setTimeout(flush, 100);
      }
      return { key, value: next };
    });
  }, [key, flush]);

  return [stored.key === key ? stored.value : initialValue, setValue];
}
