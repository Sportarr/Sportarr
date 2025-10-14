import { useState, useEffect } from 'react';

/**
 * Custom hook for managing settings via the /api/settings endpoint
 * Settings are stored as JSON in specific fields (HostSettings, UISettings, etc.)
 */
export function useSettings<T>(settingsKey: keyof AppSettings, defaultValue: T): [T, (value: T) => Promise<void>, boolean] {
  const [settings, setSettings] = useState<T>(defaultValue);
  const [loading, setLoading] = useState(true);

  // Load settings on mount
  useEffect(() => {
    fetchSettings();
  }, []);

  const fetchSettings = async () => {
    try {
      const response = await fetch('/api/settings');
      if (response.ok) {
        const data: AppSettings = await response.json();
        const settingsJson = data[settingsKey];
        if (settingsJson && typeof settingsJson === 'string') {
          const parsed = JSON.parse(settingsJson);
          setSettings(parsed);
        }
      }
    } catch (error) {
      console.error(`Failed to fetch ${String(settingsKey)}:`, error);
    } finally {
      setLoading(false);
    }
  };

  const saveSettings = async (value: T) => {
    try {
      // First fetch current settings
      const response = await fetch('/api/settings');
      if (!response.ok) throw new Error('Failed to fetch current settings');

      const currentSettings: AppSettings = await response.json();

      // Update only the specific settings key
      const updatedSettings = {
        ...currentSettings,
        [settingsKey]: JSON.stringify(value),
        lastModified: new Date().toISOString()
      };

      // Save back to API
      const saveResponse = await fetch('/api/settings', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(updatedSettings),
      });

      if (saveResponse.ok) {
        setSettings(value);
      } else {
        throw new Error('Failed to save settings');
      }
    } catch (error) {
      console.error(`Failed to save ${String(settingsKey)}:`, error);
      throw error;
    }
  };

  return [settings, saveSettings, loading];
}

// Type definition matching backend AppSettings model
interface AppSettings {
  id: number;
  hostSettings: string;
  securitySettings: string;
  proxySettings: string;
  loggingSettings: string;
  analyticsSettings: string;
  backupSettings: string;
  updateSettings: string;
  uiSettings: string;
  mediaManagementSettings: string;
  lastModified: string;
}
