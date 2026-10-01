import { useCallback, useState } from 'react';

export function useIntakeWizard() {
  const [isOpen, setOpen] = useState(false);
  const open = useCallback(() => setOpen(true), []);
  const close = useCallback(() => setOpen(false), []);
  return { state: { isOpen }, open, close };
}
