import { useState } from 'react';

export function useColumnVisibility<T extends string>(
  localStorageKey: string,
  defaults: Record<T, boolean>,
  alwaysVisible: T[] = []
): {
  colVisibility: Record<T, boolean>;
  toggleCol: (col: T) => void;
  isVisible: (col: T) => boolean;
} {
  const [colVisibility, setColVisibility] = useState<Record<T, boolean>>(() => {
    if (typeof window === 'undefined') {
      return { ...defaults };
    }

    try {
      const saved = window.localStorage.getItem(localStorageKey);
      if (!saved) {
        return { ...defaults };
      }

      const parsed = JSON.parse(saved) as Partial<Record<T, boolean>>;
      const merged = { ...defaults, ...parsed } as Record<T, boolean>;
      alwaysVisible.forEach((col) => {
        merged[col] = true;
      });
      return merged;
    } catch {
      return { ...defaults };
    }
  });

  const toggleCol = (col: T) => {
    if (alwaysVisible.includes(col)) {
      return;
    }

    setColVisibility((current) => {
      const next = { ...current, [col]: !current[col] };

      try {
        window.localStorage.setItem(localStorageKey, JSON.stringify(next));
      } catch {
        // Ignore storage errors.
      }

      return next;
    });
  };

  return {
    colVisibility,
    toggleCol,
    isVisible: (col: T) => colVisibility[col] ?? true,
  };
}
