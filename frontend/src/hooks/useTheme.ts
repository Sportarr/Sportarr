import { useEffect, useState } from 'react';
import { useUISettings } from './useUISettings';

export type ThemeChoice = 'auto' | 'light' | 'dark';

const STORAGE_KEY = 'sportarr.theme';

function systemPrefersLight(): boolean {
  return typeof window !== 'undefined'
    && window.matchMedia?.('(prefers-color-scheme: light)').matches === true;
}

function resolve(choice: ThemeChoice): 'light' | 'dark' {
  if (choice === 'auto') return systemPrefersLight() ? 'light' : 'dark';
  return choice;
}

export function storedThemeChoice(): ThemeChoice {
  try {
    const saved = localStorage.getItem(STORAGE_KEY);
    if (saved === 'light' || saved === 'dark' || saved === 'auto') return saved;
  } catch {
    // Private browsing can refuse storage.
  }
  return 'dark';
}

/**
 * Paints a theme. Only the saved server choice is remembered: previewing a
 * selection the user has not saved must not become what the next page load
 * paints before the settings arrive.
 */
export function applyTheme(choice: ThemeChoice, remember = false): void {
  document.documentElement.dataset.theme = resolve(choice);
  if (!remember) return;
  try {
    localStorage.setItem(STORAGE_KEY, choice);
  } catch {
    // Losing the hint only costs a flash on the next load.
  }
}

/**
 * Applies the saved theme and keeps it in step with the system when the
 * user picked auto. The settings request is slower than the first paint, so
 * nothing is applied until it answers: the inline script in index.html has
 * already painted the remembered choice, and overwriting that with a
 * default would flash the wrong theme at everyone who is not on it.
 */
export function useResolvedTheme(): 'light' | 'dark' {
  const { theme } = useUISettings();
  const [resolved, setResolved] = useState<'light' | 'dark'>(
    () => (typeof document !== 'undefined'
      && document.documentElement.dataset.theme === 'light') ? 'light' : 'dark');

  useEffect(() => {
    if (!theme) return;
    applyTheme(theme, true);
    setResolved(resolve(theme));
  }, [theme]);

  useEffect(() => {
    if (theme !== 'auto' || typeof window === 'undefined' || !window.matchMedia) return;

    const media = window.matchMedia('(prefers-color-scheme: light)');
    const onChange = () => {
      applyTheme('auto', true);
      setResolved(resolve('auto'));
    };
    media.addEventListener('change', onChange);
    return () => media.removeEventListener('change', onChange);
  }, [theme]);

  return resolved;
}
