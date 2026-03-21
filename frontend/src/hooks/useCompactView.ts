import { useEffect, useState } from 'react';
import { useUISettings } from './useUISettings';

const COMPACT_VIEW_BREAKPOINT = 1280;

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

  if (loading) {
    return wideScreen;
  }

  if (eventViewMode === 'compact') {
    return true;
  }

  if (eventViewMode === 'spacious') {
    return false;
  }

  return wideScreen;
}
