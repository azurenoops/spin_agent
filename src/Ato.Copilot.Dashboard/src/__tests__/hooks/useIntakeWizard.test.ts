import { act, renderHook } from '@testing-library/react';
import { expect, it } from 'vitest';
import { useIntakeWizard } from '../../hooks/useIntakeWizard';

it('owns only entry visibility, not a second persisted step or cleanup engine', () => {
  // Arrange
  const { result } = renderHook(() => useIntakeWizard());
  expect(result.current.state.isOpen).toBe(false);
  // Act
  act(() => result.current.open());
  expect(result.current.state.isOpen).toBe(true);
  act(() => result.current.close());
  // Assert
  expect(result.current.state).toEqual({ isOpen: false });
  expect(Object.keys(result.current).sort()).toEqual(['close', 'open', 'state']);
});
