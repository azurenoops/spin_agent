import { useState, useCallback, useRef, useEffect } from 'react';

/**
 * Typed localStorage hook with JSON serialization and debounced writes.
 */
export function useLocalStorage<T>(key: string, initialValue: T): [T, (value: T | ((prev: T) => T)) => void] {
  const [storedValue, setStoredValue] = useState<T>(() => {
    try {
      const item = localStorage.getItem(key);
      return item ? (JSON.parse(item) as T) : initialValue;
    } catch {
      return initialValue;
    }
  });

  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  const setValue = useCallback(
    (value: T | ((prev: T) => T)) => {
      setStoredValue((prev) => {
        const nextValue = value instanceof Function ? value(prev) : value;

        // Debounced write to localStorage
        if (debounceRef.current) {
          clearTimeout(debounceRef.current);
        }
        debounceRef.current = setTimeout(() => {
          try {
            localStorage.setItem(key, JSON.stringify(nextValue));
          } catch (error) {
            // QuotaExceededError — fall back silently
            if (error instanceof DOMException && error.name === 'QuotaExceededError') {
              console.warn('localStorage quota exceeded for key:', key);
            }
          }
        }, 100);

        return nextValue;
      });
    },
    [key],
  );

  // Clean up debounce timer on unmount
  useEffect(() => {
    return () => {
      if (debounceRef.current) {
        clearTimeout(debounceRef.current);
      }
    };
  }, []);

  return [storedValue, setValue];
}
