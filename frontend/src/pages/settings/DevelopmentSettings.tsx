import { useState, useEffect, useRef } from 'react';
import { BeakerIcon, ExclamationTriangleIcon } from '@heroicons/react/24/outline';
import { apiGet, apiPut } from '../../utils/api';
import SettingsHeader from '../../components/SettingsHeader';
import { useUnsavedChanges } from '../../hooks/useUnsavedChanges';

interface DevelopmentSettingsData {
  customMetadataApiUrl: string;
}

export default function DevelopmentSettings() {
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [hasUnsavedChanges, setHasUnsavedChanges] = useState(false);
  const [testing, setTesting] = useState(false);
  const [testResult, setTestResult] = useState<{ success: boolean; message: string } | null>(null);
  const initialSettings = useRef<DevelopmentSettingsData | null>(null);
  const { blockNavigation } = useUnsavedChanges(hasUnsavedChanges);
  const [settings, setSettings] = useState<DevelopmentSettingsData>({
    customMetadataApiUrl: '',
  });

  useEffect(() => {
    loadSettings();
  }, []);

  const loadSettings = async () => {
    try {
      const response = await apiGet('/api/settings');
      if (response.ok) {
        const data = await response.json();
        if (data.developmentSettings) {
          const parsed = JSON.parse(data.developmentSettings);
          setSettings(prev => ({ ...prev, ...parsed }));
          initialSettings.current = { ...settings, ...parsed };
          setHasUnsavedChanges(false);
        }
      }
    } catch (error) {
      console.error('Failed to load development settings:', error);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    if (!initialSettings.current) return;
    const hasChanges = JSON.stringify(settings) !== JSON.stringify(initialSettings.current);
    setHasUnsavedChanges(hasChanges);
  }, [settings]);

  const handleSave = async () => {
    setSaving(true);
    try {
      const response = await apiGet('/api/settings');
      if (!response.ok) throw new Error('Failed to fetch current settings');

      const currentSettings = await response.json();

      const updatedSettings = {
        ...currentSettings,
        developmentSettings: JSON.stringify(settings),
      };

      const saveResponse = await apiPut('/api/settings', updatedSettings);

      if (saveResponse.ok) {
        initialSettings.current = settings;
        setHasUnsavedChanges(false);
      } else {
        console.error('Failed to save development settings');
      }
    } catch (error) {
      console.error('Failed to save development settings:', error);
    } finally {
      setSaving(false);
    }
  };

  const handleTestApi = async () => {
    setTesting(true);
    setTestResult(null);

    const urlToTest = settings.customMetadataApiUrl || 'https://sportarr.net/api/v2/json';

    try {
      // Test by fetching a simple endpoint
      const testUrl = `${urlToTest.replace(/\/$/, '')}/all/sports`;
      const response = await fetch(testUrl, {
        method: 'GET',
        headers: {
          'Accept': 'application/json',
        },
      });

      if (response.ok) {
        const data = await response.json();
        // Check for valid response structure:
        // - sportarr.net format: { data: { all: [...] }, _meta: {...} }
        // - TheSportsDB format: { sports: [...] }
        const hasValidData = data && (
          data.sports ||                           // TheSportsDB direct format
          data.list ||                             // Alternative list format
          (data.data && data.data.all) ||          // sportarr.net wrapped format
          (data.data && Array.isArray(data.data))  // Alternative wrapped array
        );
        if (hasValidData) {
          setTestResult({ success: true, message: 'API connection successful!' });
        } else {
          setTestResult({ success: false, message: 'API responded but returned unexpected data format' });
        }
      } else {
        setTestResult({ success: false, message: `API returned status ${response.status}` });
      }
    } catch (error) {
      setTestResult({ success: false, message: `Connection failed: ${error instanceof Error ? error.message : 'Unknown error'}` });
    } finally {
      setTesting(false);
    }
  };

  if (loading) {
    return (
      <div className="max-w-4xl mx-auto">
        <div className="mb-8">
          <h2 className="text-3xl font-bold text-white mb-2">Development</h2>
          <p className="text-gray-400">Developer and testing options</p>
        </div>
        <div className="text-center py-12">
          <p className="text-gray-500">Loading development settings...</p>
        </div>
      </div>
    );
  }

  return (
    <div>
      <SettingsHeader
        title="Development"
        subtitle="Developer and testing options (advanced users only)"
        onSave={handleSave}
        isSaving={saving}
        hasUnsavedChanges={hasUnsavedChanges}
        saveButtonText="Save Changes"
      />

      <div className="max-w-4xl mx-auto px-6">
        {/* Warning Banner */}
        <div className="mb-8 bg-gradient-to-br from-yellow-900/30 to-orange-900/20 border border-yellow-600/50 rounded-lg p-6">
          <div className="flex items-start gap-4">
            <ExclamationTriangleIcon className="w-8 h-8 text-yellow-500 flex-shrink-0" />
            <div>
              <h3 className="text-lg font-semibold text-yellow-400 mb-2">Developer Settings</h3>
              <p className="text-yellow-300/80 text-sm">
                These settings are intended for developers and testers only. Modifying these settings incorrectly
                may cause Sportarr to malfunction. Only change these if you know what you're doing.
              </p>
            </div>
          </div>
        </div>

        {/* Custom Metadata API */}
        <div className="mb-8 bg-gradient-to-br from-gray-900 to-black border border-purple-900/30 rounded-lg p-6">
          <div className="flex items-center mb-4">
            <BeakerIcon className="w-6 h-6 text-purple-400 mr-3" />
            <h3 className="text-xl font-semibold text-white">Custom Metadata API</h3>
          </div>

          <div className="space-y-4">
            <div>
              <label className="block text-white font-medium mb-2">Custom API URL</label>
              <input
                type="url"
                value={settings.customMetadataApiUrl}
                onChange={(e) => setSettings(prev => ({ ...prev, customMetadataApiUrl: e.target.value }))}
                placeholder="https://sportarr.net/api/v2/json"
                className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white placeholder-gray-500 focus:outline-none focus:border-purple-600"
              />
              <p className="text-xs text-gray-500 mt-1">
                Override the metadata API URL for testing. Leave empty to use the default sportarr.net API.
              </p>
            </div>

            <div className="flex items-center gap-4">
              <button
                onClick={handleTestApi}
                disabled={testing}
                className="px-4 py-2 bg-purple-600 hover:bg-purple-700 disabled:bg-purple-600/50 text-white text-sm font-medium rounded-lg transition-colors"
              >
                {testing ? 'Testing...' : 'Test Connection'}
              </button>

              {testResult && (
                <div className={`text-sm ${testResult.success ? 'text-green-400' : 'text-red-400'}`}>
                  {testResult.message}
                </div>
              )}
            </div>

            <div className="mt-4 p-4 bg-gray-800/50 border border-gray-700 rounded-lg">
              <h4 className="text-white font-medium mb-2">API URL Format</h4>
              <p className="text-sm text-gray-400 mb-2">
                The API URL should point to a TheSportsDB-compatible API. The default is:
              </p>
              <code className="block text-sm text-purple-400 bg-gray-900 px-3 py-2 rounded">
                https://sportarr.net/api/v2/json
              </code>
              <p className="text-xs text-gray-500 mt-2">
                Changes take effect immediately - no restart required.
              </p>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
