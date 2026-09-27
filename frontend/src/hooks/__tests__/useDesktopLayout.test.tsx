import { beforeEach, expect, it } from 'vitest';
import { renderHook } from '@testing-library/react';
import { useIsDesktopLayout } from '../useCompactView';

beforeEach(() => {
  Object.defineProperty(window, 'innerWidth', { configurable: true, value: 1366 });
  Object.defineProperty(window.navigator, 'maxTouchPoints', { configurable: true, value: 0 });
  Object.defineProperty(window.navigator, 'platform', { configurable: true, value: 'Win32' });
  Object.defineProperty(window.navigator, 'userAgent', { configurable: true, value: 'Mozilla/5.0 (Windows NT 10.0)' });
});

it('uses the compact layout on an iPad Pro in landscape', () => {
  Object.defineProperty(window.navigator, 'maxTouchPoints', { configurable: true, value: 5 });
  Object.defineProperty(window.navigator, 'platform', { configurable: true, value: 'MacIntel' });

  const { result } = renderHook(() => useIsDesktopLayout());

  expect(result.current).toBe(false);
});

it('keeps the desktop layout on a wide non-touch display', () => {
  const { result } = renderHook(() => useIsDesktopLayout());

  expect(result.current).toBe(true);
});

it('keeps the desktop layout on a wide Windows touch screen', () => {
  Object.defineProperty(window.navigator, 'maxTouchPoints', { configurable: true, value: 5 });

  const { result } = renderHook(() => useIsDesktopLayout());

  expect(result.current).toBe(true);
});

it('uses the compact layout when the tablet identifies as an iPad', () => {
  Object.defineProperty(window.navigator, 'userAgent', { configurable: true, value: 'Mozilla/5.0 (iPad; CPU OS 17_0 like Mac OS X)' });

  const { result } = renderHook(() => useIsDesktopLayout());

  expect(result.current).toBe(false);
});
