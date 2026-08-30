import { useEffect, useSyncExternalStore, type MouseEvent } from 'react';
import { useLocation } from 'react-router-dom';

let clicked: string | null = null;
const listeners = new Set<() => void>();

function emit() {
  for (const listener of listeners) listener();
}

function subscribe(listener: () => void) {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}

function getSnapshot() {
  return clicked;
}

/** Record where a navigation is going, before the router arrives. */
export function setNavTarget(path: string) {
  if (clicked === path) return;
  clicked = path;
  emit();
}

/**
 * Record a nav click, but only when this tab is the one that will move.
 * A middle click, or a click held with ctrl, cmd, shift or alt, opens the
 * link somewhere else and leaves this tab where it is. The router produces
 * no new location in that case, so a target set here would never clear.
 */
export function setNavTargetFromClick(event: MouseEvent, path: string) {
  if (event.defaultPrevented) return;
  if (event.button !== 0) return;
  if (event.metaKey || event.altKey || event.ctrlKey || event.shiftKey) return;
  setNavTarget(path);
}

function clearNavTarget() {
  if (clicked === null) return;
  clicked = null;
  emit();
}

/**
 * The path the navigation should highlight now.
 *
 * React Router commits a location change inside a transition, and every page
 * is loaded lazily. The commit therefore waits for the destination chunk and
 * its first render, which can take a moment. A highlight read from
 * location.pathname cannot move until then, so the tab looks slow to answer.
 *
 * This returns the clicked path until the router catches up. The committed
 * location stays the source of truth: it replaces the clicked path as soon as
 * it changes, which also corrects the highlight if a redirect sends the user
 * somewhere else.
 */
export function useNavTarget(): string {
  const location = useLocation();
  const target = useSyncExternalStore(subscribe, getSnapshot, getSnapshot);

  // Every navigation makes a new location object, including one that lands
  // back on the path the user was already on. Watching the object, not the
  // path, means a click that never arrives cannot leave the tab lit.
  useEffect(() => {
    clearNavTarget();
  }, [location]);

  return target ?? location.pathname;
}
