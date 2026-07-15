import { useEffect, useState } from 'react';
import { COMPACT_VIEW_BREAKPOINT } from '../utils/designTokens';
import { useUISettings } from './useUISettings';

function getIsWideScreen(breakpoint: number): boolean {
  if (typeof window === 'undefined') {
    return true;
  }

  return window.innerWidth >= breakpoint;
}

export function useIsWideScreen(breakpoint = COMPACT_VIEW_BREAKPOINT): boolean {
  const [wideScreen, setWideScreen] = useState(() => getIsWideScreen(breakpoint));

  useEffect(() => {
    const onResize = () => setWideScreen(getIsWideScreen(breakpoint));
    window.addEventListener('resize', onResize);

    return () => window.removeEventListener('resize', onResize);
  }, [breakpoint]);

  return wideScreen;
}

export function useCompactView(): boolean {
  const { eventViewMode, loading } = useUISettings();
  const wideScreen = useIsWideScreen();
  const phone = !useIsWideScreen(640);

  // Phones never get tables, regardless of the user's view-mode setting - a
  // horizontally clipped table is unusable on a phone. Card views are the
  // mobile layout; the setting still governs tablets and desktop.
  if (phone) {
    return false;
  }

  if (loading) {
    return !wideScreen;
  }

  if (eventViewMode === 'compact') {
    return true;
  }

  if (eventViewMode === 'spacious') {
    return false;
  }

  // Auto mode: wide screens get spacious cards (more space for rich layout),
  // narrow screens get compact tables (dense data in limited space)
  return !wideScreen;
}
