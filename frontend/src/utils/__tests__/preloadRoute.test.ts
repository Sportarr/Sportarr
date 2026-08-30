import { describe, it, expect } from 'vitest';
import appSource from '../../App.tsx?raw';
import layoutSource from '../../components/Layout.tsx?raw';
import { preloadablePaths, preloadRoute } from '../preloadRoute';

/**
 * The preload map repeats paths the router already owns. These tests stop the
 * two drifting apart without anyone noticing, which would quietly turn the
 * head start back off.
 */
describe('preloadRoute', () => {
  const matchAll = (source: string, pattern: RegExp): string[] => {
    const found: string[] = [];
    for (const match of source.matchAll(pattern)) {
      if (match[1]) found.push(match[1]);
    }
    return found;
  };

  const routerPaths = matchAll(appSource, /<Route\s+path="([^"]+)"/g)
    .filter((path) => path !== '*' && path !== '/' && !path.includes(':'))
    .map((path) => (path.startsWith('/') ? path : `/${path}`));

  it('only warms paths the router actually serves', () => {
    expect(routerPaths.length).toBeGreaterThan(0);
    const unknown = preloadablePaths().filter((path) => !routerPaths.includes(path));
    expect(unknown).toEqual([]);
  });

  it('covers every destination the sidebar can reach', () => {
    const navPaths = matchAll(layoutSource, /path:\s*'(\/[^']*)'/g);
    expect(navPaths.length).toBeGreaterThan(0);
    const missing = navPaths.filter((path) => !preloadablePaths().includes(path));
    expect(missing).toEqual([]);
  });

  it('ignores a path it does not know, and an undefined one', () => {
    expect(() => preloadRoute('/nope')).not.toThrow();
    expect(() => preloadRoute(undefined)).not.toThrow();
  });
});
