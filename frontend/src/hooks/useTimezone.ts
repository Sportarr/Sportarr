import { useState, useEffect } from 'react';
import { apiGet } from '../utils/api';

interface UISettings {
  timeZone?: string;
  eventViewMode?: string;
  [key: string]: any;
}

/**
 * Hook to get the user's configured timezone and UI preferences from settings
 * Returns the timezone ID (e.g., 'America/New_York') or null for system/local timezone
 */
export function useTimezone(): { timezone: string | null; eventViewMode: string; loading: boolean } {
  const [timezone, setTimezone] = useState<string | null>(null);
  const [eventViewMode, setEventViewMode] = useState<string>('auto');
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    loadSettings();
  }, []);

  const loadSettings = async () => {
    try {
      const response = await apiGet('/api/settings');
      if (response.ok) {
        const data = await response.json();
        if (data.uiSettings) {
          const uiSettings: UISettings = JSON.parse(data.uiSettings);
          setTimezone(uiSettings.timeZone || null);
          setEventViewMode(uiSettings.eventViewMode || 'auto');
        }
      }
    } catch (error) {
      console.error('Failed to load UI settings:', error);
    } finally {
      setLoading(false);
    }
  };

  return { timezone, eventViewMode, loading };
}
