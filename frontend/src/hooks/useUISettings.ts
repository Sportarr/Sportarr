import { useQuery } from '@tanstack/react-query';
import { apiGet } from '../utils/api';

interface UISettings {
  eventViewMode?: string;
  timeZone?: string;
}

export const UI_SETTINGS_QUERY_KEY = ['ui-settings'] as const;

async function fetchUISettings(): Promise<UISettings | null> {
  try {
    const response = await apiGet('/api/settings');
    if (!response.ok) {
      return null;
    }

    const data = await response.json();
    if (!data.uiSettings) {
      return null;
    }

    return JSON.parse(data.uiSettings) as UISettings;
  } catch {
    return null;
  }
}

export function useUISettings(): {
  eventViewMode: string;
  timezone: string | null;
  loading: boolean;
} {
  const { data, isLoading } = useQuery({
    queryKey: UI_SETTINGS_QUERY_KEY,
    queryFn: fetchUISettings,
    staleTime: 5 * 60 * 1000,
    refetchOnWindowFocus: false,
  });

  return {
    eventViewMode: data?.eventViewMode ?? 'auto',
    timezone: data?.timeZone ?? null,
    loading: isLoading,
  };
}
