import type { ReactElement } from 'react';
import { vi } from 'vitest';

vi.mock('recharts', async () => {
  const charts = await vi.importActual<typeof import('recharts')>('recharts');
  const { cloneElement } = await import('react');
  return {
    ...charts,
    ResponsiveContainer: ({ children }: { children: ReactElement<{ width: number; height: number }> }) =>
      cloneElement(children, { width: 600, height: 220 }),
  };
});
