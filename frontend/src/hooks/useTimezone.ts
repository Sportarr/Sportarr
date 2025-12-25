import { useState, useEffect } from 'react';
import { apiGet } from '../utils/api';

interface UISettings {
  timeZone?: string;
  [key: string]: any;
}

/**
 * Hook to get the user's configured timezone from UI settings
 * Returns the timezone ID (e.g., 'America/New_York') or null for system/local timezone
 */
export function useTimezone(): { timezone: string | null; loading: boolean } {
  const [timezone, setTimezone] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    loadTimezone();
  }, []);

  const loadTimezone = async () => {
    try {
      const response = await apiGet('/api/settings');
      if (response.ok) {
        const data = await response.json();
        if (data.uiSettings) {
          const uiSettings: UISettings = JSON.parse(data.uiSettings);
          const tz = uiSettings.timeZone || null;
          console.log('[useTimezone] Loaded timezone:', tz, 'from uiSettings:', uiSettings);
          setTimezone(tz);
        } else {
          console.log('[useTimezone] No uiSettings found in response');
        }
      } else {
        console.log('[useTimezone] Settings API returned non-ok status:', response.status);
      }
    } catch (error) {
      console.error('Failed to load timezone settings:', error);
    } finally {
      setLoading(false);
    }
  };

  return { timezone, loading };
}
