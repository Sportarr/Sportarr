import { useState, useEffect } from 'react';
import { apiGet, apiPut } from '../utils/api';

/**
 * Custom hook for managing settings via the /api/settings endpoint
 * Settings are stored as JSON in specific fields (HostSettings, UISettings, etc.)
 */
/**
 * Serialises every settings save in the app. See saveSettings below.
 */
let settingsSaveChain: Promise<unknown> = Promise.resolve();

/**
 * Run a settings save after every save already in flight has finished.
 *
 * Every settings page reads the whole object, swaps in its own block and
 * writes the whole object back, so two pages saving together both start
 * from the same copy and the second silently puts the first block back
 * as it found it. The chain lived inside the hook alone, and the pages
 * that actually write settings never went through it.
 */
export function runSettingsSave<T>(save: () => Promise<T>): Promise<T> {
  const run = settingsSaveChain.then(save, save);
  settingsSaveChain = run.catch(() => { /* a failure must not wedge the chain */ });
  return run;
}

export function useSettings<T>(settingsKey: keyof AppSettings, defaultValue: T): [T, (value: T) => Promise<void>, boolean] {
  const [settings, setSettings] = useState<T>(defaultValue);
  const [loading, setLoading] = useState(true);

  // Load settings on mount
  useEffect(() => {
    fetchSettings();
  }, []);

  const fetchSettings = async () => {
    try {
      const response = await apiGet('/api/settings');
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
    // Saves run one at a time, process-wide. Each one reads every setting,
    // swaps in its own key and writes the whole object back, so two running
    // together both read the same starting point and the second wrote the
    // first key back as it found it. The edit looked saved and was gone. The
    // chain makes each save read what the one before it wrote.
    return runSettingsSave(() => performSave(value));
  };

  const performSave = async (value: T) => {
    try {
      // First fetch current settings
      const response = await apiGet('/api/settings');
      if (!response.ok) throw new Error('Failed to fetch current settings');

      const currentSettings: AppSettings = await response.json();

      // Update only the specific settings key
      const updatedSettings = {
        ...currentSettings,
        [settingsKey]: JSON.stringify(value),
        lastModified: new Date().toISOString()
      };

      // Save back to API
      const saveResponse = await apiPut('/api/settings', updatedSettings);

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
