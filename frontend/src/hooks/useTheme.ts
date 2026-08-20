import { useEffect } from 'react';
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

export function applyTheme(choice: ThemeChoice): void {
  document.documentElement.dataset.theme = resolve(choice);
  try {
    localStorage.setItem(STORAGE_KEY, choice);
  } catch {
    // Private browsing can refuse storage. The saved setting still applies
    // on the next load, this only costs the pre-paint hint.
  }
}

export function storedThemeChoice(): ThemeChoice {
  try {
    const saved = localStorage.getItem(STORAGE_KEY);
    if (saved === 'light' || saved === 'dark' || saved === 'auto') return saved;
  } catch {
    // ignore
  }
  return 'dark';
}

/**
 * Applies the saved theme to the document and keeps it in step with the
 * system when the user picked auto. The choice is mirrored into
 * localStorage so a reload paints the right theme before the settings
 * request comes back, instead of flashing the wrong one.
 */
export function useTheme(): void {
  const { theme } = useUISettings();

  useEffect(() => {
    applyTheme(theme);
  }, [theme]);

  useEffect(() => {
    if (theme !== 'auto' || typeof window === 'undefined' || !window.matchMedia) return;

    const media = window.matchMedia('(prefers-color-scheme: light)');
    const onChange = () => applyTheme('auto');
    media.addEventListener('change', onChange);
    return () => media.removeEventListener('change', onChange);
  }, [theme]);
}

/**
 * Renders nothing. Exists so the theme hook runs inside the query provider,
 * since it reads the saved setting through React Query.
 */
export function ThemeController(): null {
  useTheme();
  return null;
}
