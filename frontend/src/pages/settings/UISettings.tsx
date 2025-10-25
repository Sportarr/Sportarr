import { useState, useEffect } from 'react';
import { PaintBrushIcon, CalendarIcon, ClockIcon, EyeIcon } from '@heroicons/react/24/outline';
import { apiGet, apiPost, apiPut, apiDelete } from '../../utils/api';

interface UISettingsProps {
  showAdvanced: boolean;
}

interface UISettingsData {
  // Calendar
  firstDayOfWeek: string;
  calendarWeekColumnHeader: string;
  // Dates
  shortDateFormat: string;
  longDateFormat: string;
  timeFormat: string;
  showRelativeDates: boolean;
  // Style
  theme: string;
  enableColorImpairedMode: boolean;
  // Language
  uiLanguage: string;
  // Display
  showUnknownOrganizationItems: boolean;
  showEventPath: boolean;
}

export default function UISettings({ showAdvanced }: UISettingsProps) {
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [settings, setSettings] = useState<UISettingsData>({
    // Calendar
    firstDayOfWeek: 'sunday',
    calendarWeekColumnHeader: 'ddd M/D',
    // Dates
    shortDateFormat: 'MMM D YYYY',
    longDateFormat: 'dddd, MMMM D YYYY',
    timeFormat: 'h:mm A',
    showRelativeDates: true,
    // Style
    theme: 'auto',
    enableColorImpairedMode: false,
    // Language
    uiLanguage: 'en',
    // Display
    showUnknownOrganizationItems: false,
    showEventPath: false,
  });

  // Load settings from API on mount
  useEffect(() => {
    loadSettings();
  }, []);

  const loadSettings = async () => {
    try {
      const response = await apiGet('/api/settings');
      if (response.ok) {
        const data = await response.json();
        if (data.uiSettings) {
          const parsed = JSON.parse(data.uiSettings);
          setSettings(parsed);
        }
      }
    } catch (error) {
      console.error('Failed to load UI settings:', error);
    } finally {
      setLoading(false);
    }
  };

  const handleSave = async () => {
    setSaving(true);
    try {
      // First fetch current settings
      const response = await apiGet('/api/settings');
      if (!response.ok) throw new Error('Failed to fetch current settings');

      const currentSettings = await response.json();

      // Update with new UI settings
      const updatedSettings = {
        ...currentSettings,
        uiSettings: JSON.stringify(settings),
      };

      // Save to API
      const saveResponse = await apiPut('/api/settings', updatedSettings);

      if (!saveResponse.ok) {
        console.error('Failed to save UI settings');
      }
    } catch (error) {
      console.error('Failed to save UI settings:', error);
    } finally {
      setSaving(false);
    }
  };

  const updateSetting = <K extends keyof UISettingsData>(key: K, value: UISettingsData[K]) => {
    setSettings(prev => ({ ...prev, [key]: value }));
  };

  if (loading) {
    return (
      <div className="max-w-4xl mx-auto">
        <div className="mb-8">
          <h2 className="text-3xl font-bold text-white mb-2">UI</h2>
          <p className="text-gray-400">User interface preferences and customization</p>
        </div>
        <div className="text-center py-12">
          <p className="text-gray-500">Loading UI settings...</p>
        </div>
      </div>
    );
  }

  return (
    <div className="max-w-4xl mx-auto">
      <div className="mb-8 flex items-center justify-between">
        <div>
          <h2 className="text-3xl font-bold text-white mb-2">UI</h2>
          <p className="text-gray-400">User interface preferences and customization</p>
        </div>
        <button
          onClick={handleSave}
          disabled={saving}
          className="px-6 py-2 bg-red-600 hover:bg-red-700 disabled:opacity-50 disabled:cursor-not-allowed text-white rounded-lg transition-colors"
        >
          {saving ? 'Saving...' : 'Save Changes'}
        </button>
      </div>

      {/* Calendar */}
      <div className="mb-8 bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg p-6">
        <div className="flex items-center mb-4">
          <CalendarIcon className="w-6 h-6 text-red-400 mr-3" />
          <h3 className="text-xl font-semibold text-white">Calendar</h3>
        </div>

        <div className="space-y-4">
          <div>
            <label className="block text-white font-medium mb-2">First Day of Week</label>
            <select
              value={settings.firstDayOfWeek}
              onChange={(e) => updateSetting('firstDayOfWeek', e.target.value)}
              className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
            >
              <option value="sunday">Sunday</option>
              <option value="monday">Monday</option>
            </select>
          </div>

          <div>
            <label className="block text-white font-medium mb-2">Week Column Header</label>
            <input
              type="text"
              value={settings.calendarWeekColumnHeader}
              onChange={(e) => updateSetting('calendarWeekColumnHeader', e.target.value)}
              className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
            />
            <p className="text-xs text-gray-500 mt-1">
              Format for calendar week column headers
            </p>
            <div className="mt-2 p-3 bg-blue-950/30 border border-blue-900/50 rounded-lg">
              <p className="text-sm text-blue-300">
                <strong>Examples:</strong> ddd M/D = Mon 1/1 | MMM D = Jan 1 | ddd D/M = Mon 1/1
              </p>
            </div>
          </div>
        </div>
      </div>

      {/* Dates */}
      <div className="mb-8 bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg p-6">
        <div className="flex items-center mb-4">
          <ClockIcon className="w-6 h-6 text-red-400 mr-3" />
          <h3 className="text-xl font-semibold text-white">Dates</h3>
        </div>

        <div className="space-y-4">
          <div>
            <label className="block text-white font-medium mb-2">Short Date Format</label>
            <select
              value={settings.shortDateFormat}
              onChange={(e) => updateSetting('shortDateFormat', e.target.value)}
              className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
            >
              <option value="MMM D YYYY">Dec 25 2024</option>
              <option value="DD MMM YYYY">25 Dec 2024</option>
              <option value="MM/DD/YYYY">12/25/2024</option>
              <option value="DD/MM/YYYY">25/12/2024</option>
              <option value="YYYY-MM-DD">2024-12-25</option>
            </select>
          </div>

          <div>
            <label className="block text-white font-medium mb-2">Long Date Format</label>
            <select
              value={settings.longDateFormat}
              onChange={(e) => updateSetting('longDateFormat', e.target.value)}
              className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
            >
              <option value="dddd, MMMM D YYYY">Monday, December 25 2024</option>
              <option value="dddd, D MMMM YYYY">Monday, 25 December 2024</option>
              <option value="MMMM D YYYY">December 25 2024</option>
              <option value="D MMMM YYYY">25 December 2024</option>
            </select>
          </div>

          <div>
            <label className="block text-white font-medium mb-2">Time Format</label>
            <select
              value={settings.timeFormat}
              onChange={(e) => updateSetting('timeFormat', e.target.value)}
              className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
            >
              <option value="h:mm A">9:00 PM (12-hour)</option>
              <option value="HH:mm">21:00 (24-hour)</option>
            </select>
          </div>

          <label className="flex items-start space-x-3 cursor-pointer">
            <input
              type="checkbox"
              checked={settings.showRelativeDates}
              onChange={(e) => updateSetting('showRelativeDates', e.target.checked)}
              className="mt-1 w-5 h-5 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
            />
            <div>
              <span className="text-white font-medium">Show Relative Dates</span>
              <p className="text-sm text-gray-400 mt-1">
                Show relative dates (e.g., "Today at 5:30 PM" instead of "Jan 5 2024 at 5:30 PM")
              </p>
            </div>
          </label>
        </div>
      </div>

      {/* Style */}
      <div className="mb-8 bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg p-6">
        <div className="flex items-center mb-4">
          <PaintBrushIcon className="w-6 h-6 text-red-400 mr-3" />
          <h3 className="text-xl font-semibold text-white">Style</h3>
        </div>

        <div className="space-y-4">
          <div>
            <label className="block text-white font-medium mb-2">Theme</label>
            <select
              value={settings.theme}
              onChange={(e) => updateSetting('theme', e.target.value)}
              className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
            >
              <option value="auto">Auto (Follow System)</option>
              <option value="light">Light</option>
              <option value="dark">Dark</option>
            </select>
            <p className="text-xs text-gray-500 mt-1">
              Fightarr will match your system theme in auto mode
            </p>
          </div>

          <label className="flex items-start space-x-3 cursor-pointer">
            <input
              type="checkbox"
              checked={settings.enableColorImpairedMode}
              onChange={(e) => updateSetting('enableColorImpairedMode', e.target.checked)}
              className="mt-1 w-5 h-5 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
            />
            <div>
              <span className="text-white font-medium">Enable Color-Impaired Mode</span>
              <p className="text-sm text-gray-400 mt-1">
                Altered color style to better differentiate status colors for color-impaired users
              </p>
            </div>
          </label>
        </div>
      </div>

      {/* Language */}
      <div className="mb-8 bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg p-6">
        <h3 className="text-xl font-semibold text-white mb-4">Language</h3>

        <div>
          <label className="block text-white font-medium mb-2">UI Language</label>
          <select
            value={settings.uiLanguage}
            onChange={(e) => updateSetting('uiLanguage', e.target.value)}
            className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
          >
            <option value="en">English</option>
            <option value="es">Español (Spanish)</option>
            <option value="pt">Português (Portuguese)</option>
            <option value="ja">日本語 (Japanese)</option>
            <option value="ko">한국어 (Korean)</option>
            <option value="th">ภาษาไทย (Thai)</option>
            <option value="ru">Русский (Russian)</option>
            <option value="de">Deutsch (German)</option>
            <option value="fr">Français (French)</option>
            <option value="zh">中文 (Chinese)</option>
          </select>
          <p className="text-xs text-gray-500 mt-1">
            Language for the Fightarr user interface
          </p>
        </div>
      </div>

      {/* Display */}
      {showAdvanced && (
        <div className="mb-8 bg-gradient-to-br from-gray-900 to-black border border-yellow-900/30 rounded-lg p-6">
          <div className="flex items-center mb-4">
            <EyeIcon className="w-6 h-6 text-yellow-400 mr-3" />
            <h3 className="text-xl font-semibold text-white">
              Display Options
              <span className="ml-2 px-2 py-0.5 bg-yellow-900/30 text-yellow-400 text-xs rounded">
                Advanced
              </span>
            </h3>
          </div>

          <div className="space-y-4">
            <label className="flex items-start space-x-3 cursor-pointer">
              <input
                type="checkbox"
                checked={settings.showUnknownOrganizationItems}
                onChange={(e) => updateSetting('showUnknownOrganizationItems', e.target.checked)}
                className="mt-1 w-5 h-5 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
              />
              <div>
                <span className="text-white font-medium">Show Unknown Organization Items</span>
                <p className="text-sm text-gray-400 mt-1">
                  Display items from unrecognized fight organizations
                </p>
              </div>
            </label>

            <label className="flex items-start space-x-3 cursor-pointer">
              <input
                type="checkbox"
                checked={settings.showEventPath}
                onChange={(e) => updateSetting('showEventPath', e.target.checked)}
                className="mt-1 w-5 h-5 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
              />
              <div>
                <span className="text-white font-medium">Show Event Path</span>
                <p className="text-sm text-gray-400 mt-1">
                  Show the path to event files in the UI
                </p>
              </div>
            </label>
          </div>
        </div>
      )}

      {/* Date/Time Format Reference */}
      {showAdvanced && (
        <div className="mb-8 bg-gradient-to-br from-gray-900 to-black border border-blue-900/30 rounded-lg p-6">
          <h3 className="text-xl font-semibold text-white mb-4">Date/Time Format Tokens</h3>
          <p className="text-sm text-gray-400 mb-4">
            Reference for custom date and time format patterns
          </p>

          <div className="grid grid-cols-2 gap-6">
            <div>
              <h4 className="text-white font-medium mb-2">Month</h4>
              <div className="space-y-1 text-sm">
                <p className="text-gray-300"><code className="text-green-400">MMMM</code> = December</p>
                <p className="text-gray-300"><code className="text-green-400">MMM</code> = Dec</p>
                <p className="text-gray-300"><code className="text-green-400">MM</code> = 12</p>
                <p className="text-gray-300"><code className="text-green-400">M</code> = 12</p>
              </div>
            </div>

            <div>
              <h4 className="text-white font-medium mb-2">Day</h4>
              <div className="space-y-1 text-sm">
                <p className="text-gray-300"><code className="text-green-400">dddd</code> = Monday</p>
                <p className="text-gray-300"><code className="text-green-400">ddd</code> = Mon</p>
                <p className="text-gray-300"><code className="text-green-400">DD</code> = 25</p>
                <p className="text-gray-300"><code className="text-green-400">D</code> = 25</p>
              </div>
            </div>

            <div>
              <h4 className="text-white font-medium mb-2">Year</h4>
              <div className="space-y-1 text-sm">
                <p className="text-gray-300"><code className="text-green-400">YYYY</code> = 2024</p>
                <p className="text-gray-300"><code className="text-green-400">YY</code> = 24</p>
              </div>
            </div>

            <div>
              <h4 className="text-white font-medium mb-2">Time</h4>
              <div className="space-y-1 text-sm">
                <p className="text-gray-300"><code className="text-green-400">HH</code> = 21 (24-hour)</p>
                <p className="text-gray-300"><code className="text-green-400">H</code> = 21 (24-hour)</p>
                <p className="text-gray-300"><code className="text-green-400">h</code> = 9 (12-hour)</p>
                <p className="text-gray-300"><code className="text-green-400">mm</code> = 30 (minutes)</p>
                <p className="text-gray-300"><code className="text-green-400">A</code> = PM</p>
              </div>
            </div>
          </div>
        </div>
      )}

    </div>
  );
}
