import { useEffect, useState } from 'react';

/**
 * Reactively track a CSS media query. For JS-level layout switches that CSS
 * breakpoints alone can't express (e.g. rendering a different component tree
 * on phones, like the TV Guide's compact list).
 */
export function useMediaQuery(query: string): boolean {
  const [matches, setMatches] = useState(() =>
    typeof window !== 'undefined' ? window.matchMedia(query).matches : false
  );

  useEffect(() => {
    const mql = window.matchMedia(query);
    const onChange = (e: MediaQueryListEvent) => setMatches(e.matches);
    setMatches(mql.matches);
    mql.addEventListener('change', onChange);
    return () => mql.removeEventListener('change', onChange);
  }, [query]);

  return matches;
}
